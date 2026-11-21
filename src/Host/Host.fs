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
    let root = Context.Create()
    let plugins = ResizeArray<Plugin>()
    let active = Dictionary<string, PluginHandle>()
    let pages = Dictionary<string, Page>()

    member _.Plugins = plugins |> Seq.toList
    member _.Pages = pages.Values |> Seq.toList
    member _.Enabled id = active.ContainsKey id

    member _.Enable(id) =
        if not (active.ContainsKey id) then
            let plugin = plugins |> Seq.find (fun p -> p.Id = id)
            // Finish registration before publishing any of this plugin's pages.
            let additions = ResizeArray<Page>()
            let resources = Stack<IDisposable>()
            let release () =
                while resources.Count > 0 do resources.Pop().Dispose()
            try
                plugin.Create().Activate {
                    new Registration with
                        member _.Page page =
                            if pages.ContainsKey page.Id || Seq.exists (fun (p: Page) -> p.Id = page.Id) additions then
                                invalidOp $"页面标识重复：{page.Id}"
                            additions.Add page
                        member _.Own resource = resources.Push resource
                }
                let starting = Task.Run(fun () ->
                    let handle = root.Plugin(Action<Context>(fun ctx ->
                        ctx.Effect(Func<obj>(fun () -> box (Action release)), plugin.Id) |> ignore))
                    handle.Await().GetAwaiter().GetResult() |> ignore
                    handle)
                let handle = starting.GetAwaiter().GetResult()
                for page in additions do pages.Add(page.Id, page)
                resources.Push { new IDisposable with
                                    member _.Dispose() =
                                        for page in additions do pages.Remove page.Id |> ignore }
                active.Add(id, handle)
            with _ ->
                release()
                reraise()

    member this.Add(plugin) =
        if plugins |> Seq.exists (fun p -> p.Id = plugin.Id) then invalidOp $"插件标识重复：{plugin.Id}"
        plugins.Add plugin
        if plugin.Core then this.Enable plugin.Id

    member _.Disable(id) =
        if plugins |> Seq.exists (fun p -> p.Id = id && p.Core) then invalidOp "核心插件必须保持启用"
        match active.TryGetValue id with
        | true, handle ->
            Task.Run(fun () -> handle.DisposeAsync().AsTask()).GetAwaiter().GetResult()
            active.Remove id |> ignore
        | _ -> ()

    interface IDisposable with
        member _.Dispose() =
            for handle in active.Values |> Seq.toArray |> Array.rev do Task.Run(fun () -> handle.DisposeAsync().AsTask()).GetAwaiter().GetResult()
            active.Clear()
