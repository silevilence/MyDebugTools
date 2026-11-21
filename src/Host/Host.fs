namespace MyDebugTools.Host

open System
open System.Collections.Generic
open System.Threading.Tasks
open CordiSharp
open CordiSharp.Registry
open MyDebugTools.Contracts

type Plugin = {
    Id: string
    Title: string
    Core: bool
    Create: unit -> IDesktopPlugin
}

type Host() =
    let mutable target: DebugTarget option = None
    let root = Context.Create()
    let plugins = ResizeArray<Plugin>()
    let active = Dictionary<string, PluginHandle>()
    let pages = Dictionary<string, Page>()
    let dllTabs = ResizeArray<string * DllTab>()
    let tabsChanged = Event<unit>()

    let monitors = Dictionary<string, MonitorSession>()
    let points = Dictionary<string * string, MenuPoint>()
    let extensions = ResizeArray<string * MenuExtension>()
    let compatible (point: MenuPoint) (action: MenuExtension) =
        point.Version >= action.MinVersion && point.Version <= action.MaxVersion

    member _.Target with get() = target and set value = target <- value
    member _.DllTabs = dllTabs |> Seq.toList
    member _.DllTabsChanged = tabsChanged.Publish

    member _.Monitor id = match monitors.TryGetValue id with true, session -> Some session | _ -> None

    member _.Actions pageId = [
        for _, extension in extensions do
            match points.TryGetValue((pageId, extension.PointId)) with
            | true, point when extension.PageId = pageId && compatible point extension ->
                yield extension.Title, fun () -> extension.Invoke(point.Selection())
            | _ -> ()
    ]

    member _.Mismatches owner = [
        for id, extension in extensions do
            if id = owner then
                match points.TryGetValue((extension.PageId, extension.PointId)) with
                | true, point when not (compatible point extension) ->
                    let required =
                        if extension.MinVersion = extension.MaxVersion then string extension.MinVersion
                        else $"{extension.MinVersion}–{extension.MaxVersion}"
                    yield $"{extension.Title} · {point.PageId}/{point.PointId}：要求版本 {required}，实际版本 {point.Version}"
                | _ -> ()
    ]

    member _.Plugins = plugins |> Seq.toList
    member _.Pages = pages.Values |> Seq.toList
    member _.Enabled id = active.ContainsKey id

    member _.Enable(id, ?activate: Registration -> unit) =
        if not (active.ContainsKey id) then
            let plugin = plugins |> Seq.find (fun p -> p.Id = id)
            // Finish registration before publishing any of this plugin's pages.
            let additions = ResizeArray<Page>()
            let declared = ResizeArray<MenuPoint>()
            let actions = ResizeArray<MenuExtension>()
            let tabs = ResizeArray<DllTab>()
            let mutable monitor = None
            let resources = Stack<IDisposable>()
            let release () =
                let errors = ResizeArray<exn>()
                while resources.Count > 0 do
                    try resources.Pop().Dispose()
                    with e -> errors.Add e
                if errors.Count > 0 then raise (AggregateException(errors))
            try
                let register = defaultArg activate (fun reg -> plugin.Create().Activate reg)
                register {
                    new Registration with
                        member _.CurrentTarget() = target
                        member _.DllTab tab = tabs.Add tab
                        member _.Page page =
                            if pages.ContainsKey page.Id || Seq.exists (fun (p: Page) -> p.Id = page.Id) additions then
                                invalidOp $"页面标识重复：{page.Id}"
                            additions.Add page
                        member _.MenuPoint point =
                            let key = point.PageId, point.PointId
                            if points.ContainsKey key || Seq.exists (fun (p: MenuPoint) -> (p.PageId, p.PointId) = key) declared then
                                invalidOp $"扩展点重复：{point.PageId}/{point.PointId}"
                            declared.Add point
                        member _.MenuAction action = actions.Add action
                        member _.Monitor session = monitor <- Some session
                        member _.Own resource = resources.Push resource
                }
                for point in declared do
                    if additions |> Seq.exists (fun page -> page.Id = point.PageId) |> not then
                        invalidOp $"扩展点缺少所属页面：{point.PageId}"
                let starting = Task.Run(fun () ->
                    let handle = root.Plugin(Action<Context>(fun ctx ->
                        ctx.Effect(Func<obj>(fun () -> box (Action release)), plugin.Id) |> ignore))
                    handle.Await().GetAwaiter().GetResult() |> ignore
                    handle)
                let handle = starting.GetAwaiter().GetResult()
                for page in additions do pages.Add(page.Id, page)
                for point in declared do points.Add((point.PageId, point.PointId), point)
                for action in actions do extensions.Add(id, action)
                for tab in tabs do dllTabs.Add(id, tab)
                monitor |> Option.iter (fun session -> monitors.Add(id, session))
                resources.Push { new IDisposable with
                                    member _.Dispose() =
                                        monitors.Remove id |> ignore
                                        dllTabs.RemoveAll(fun (owner, _) -> owner = id) |> ignore
                                        for point in declared do points.Remove((point.PageId, point.PointId)) |> ignore
                                        extensions.RemoveAll(fun (owner, _) -> owner = id) |> ignore
                                        for page in additions do pages.Remove page.Id |> ignore }
                active.Add(id, handle)
            with _ ->
                release()
                reraise()
            if tabs.Count > 0 then tabsChanged.Trigger()

    member this.Add(plugin) =
        if plugins |> Seq.exists (fun p -> p.Id = plugin.Id) then invalidOp $"插件标识重复：{plugin.Id}"
        plugins.Add plugin
        if plugin.Core then this.Enable plugin.Id

    member this.Disable(id) =
        if this.Monitor id |> Option.bind (fun session -> session.Target()) |> Option.isSome then
            invalidOp "请先结束监控会话"
        if plugins |> Seq.exists (fun p -> p.Id = id && p.Core) then invalidOp "核心插件必须保持启用"
        match active.TryGetValue id with
        | true, handle ->
            let hadTabs = dllTabs.Exists(fun (owner, _) -> owner = id)
            Task.Run(fun () -> handle.DisposeAsync().AsTask()).GetAwaiter().GetResult()
            active.Remove id |> ignore
            if hadTabs then tabsChanged.Trigger()
        | _ -> ()

    interface IDisposable with
        member _.Dispose() =
            for handle in active.Values |> Seq.toArray |> Array.rev do Task.Run(fun () -> handle.DisposeAsync().AsTask()).GetAwaiter().GetResult()
            active.Clear()
