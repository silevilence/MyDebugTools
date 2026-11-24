module MyDebugTools.Desktop.DllWorkbench

open System
open Avalonia.Controls
open Avalonia.Automation
open Avalonia.Layout
open Avalonia.Platform.Storage
open MyDebugTools.Contracts
open MyDebugTools.Host
open MyDebugTools.DllDebugger

let private button title run =
    let b = Button(Content = title)
    b.Click.Add(fun _ -> run())
    b

let private row (controls: Control list) =
    let panel = WrapPanel(Orientation = Orientation.Horizontal)
    for c in controls do panel.Children.Add c
    panel

let plugin (window: Window) (host: Host) (requests: IObservable<string * string option>) =
    { new IDesktopPlugin with
        member _.Activate registry =
            let tabPoint = { DllTabPoint.PageId = "dll.debugger"; PointId = "tabs"; Version = 1 }
            registry.DllTabPoint tabPoint
            let mutable session: CallSession option = None
            let restart () =
                session |> Option.iter (fun s -> s.Dispose())
                session <- None
            registry.Own { new IDisposable with member _.Dispose() = restart() }
            let results = PanelOutlet("调用结果")
            registry.Page { Id = "dll.debugger.result"; Title = "调用结果"; Create = fun () -> results.Companion }
            registry.Page {
                Id = "dll.debugger"; Title = "DLL 工作台"
                Create = fun () ->
                    let panel = StackPanel(Spacing = 8.)
                    let header = StackPanel(Spacing = 8.)
                    let tabs = TabControl()
                    let mounted = ResizeArray<DllTab * TabItem * DllTabView>()
                    let path = TextBox(PlaceholderText = "DLL 文件路径")
                    let status = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
                    let exports = ListBox(Height = 160.)
                    let filter = TextBox(PlaceholderText = "按名称或序号查找")
                    AutomationProperties.SetName(filter, "筛选导出")
                    let cells (values: string list) =
                        let grid = Grid(ColumnDefinitions = ColumnDefinitions("2*,*,*,*"), ColumnSpacing = 8.)
                        values |> List.iteri (fun i value ->
                            let label = TextBlock(Text = value)
                            Grid.SetColumn(label, i)
                            grid.Children.Add label)
                        grid
                    exports.ItemTemplate <- Templates.FuncDataTemplate<Export>(fun e _ ->
                        cells [e.Entry; string e.Ordinal; $"0x{e.Rva:X8}"; e.Kind] :> Control)
                    AutomationProperties.SetName(path, "DLL 文件路径")
                    let mutable image: DllImage option = None
                    let editor = SignatureEditor.Editor()
                    let entry = TextBox(PlaceholderText = "导出名或 #序号")
                    let signatureStatus = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
                    let store = SignatureStore(IO.Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "MyDebugTools", "signatures"))
                    let arguments = TextBox(Text = "{}", AcceptsReturn = true)
                    AutomationProperties.SetName(entry, "函数入口")
                    AutomationProperties.SetName(arguments, "调用参数")
                    let bindings = BindingPanel.Panel(window.Clipboard, window.StorageProvider, (fun () -> image),
                                                      (fun () -> editor.Read entry.Text), (fun () -> arguments.Text), store)
                    editor.Changed.Add(fun () -> bindings.Invalidate())
                    for field in [entry; arguments] do
                        field.PropertyChanged.Add(fun e -> if e.Property = TextBox.TextProperty then bindings.Invalidate())
                    let drafts = Collections.Generic.Dictionary<string * string, _>()
                    let mutable activeKey = None
                    let mutable dirty = false
                    editor.Changed.Add(fun () ->
                        dirty <- true
                        signatureStatus.Text <- "已修改 · 待确认")
                    let remember () =
                        if dirty then activeKey |> Option.iter (fun key -> drafts[key] <- editor.Snapshot)
                    let selectEntry () =
                        match image with
                        | None -> ()
                        | Some dll ->
                            try
                                remember()
                                let key = dll.Identity, string entry.Text
                                activeKey <- Some key
                                match drafts.TryGetValue key with
                                | true, snapshot ->
                                    editor.Restore snapshot
                                    dirty <- true
                                    signatureStatus.Text <- "已修改 · 待确认"
                                | _ ->
                                    let found = store.Find(dll, entry.Text)
                                    editor.Load found.Signature
                                    dirty <- false
                                    signatureStatus.Text <- found.Status
                            with e -> signatureStatus.Text <- e.Message
                    entry.PropertyChanged.Add(fun e ->
                        if e.Property = TextBox.TextProperty && editor.Entry <> entry.Text then selectEntry())
                    filter.TextChanged.Add(fun _ ->
                        image |> Option.iter (fun dll ->
                            let query = string filter.Text
                            exports.ItemsSource <- dll.Exports |> Seq.filter (fun e ->
                                e.Entry.Contains(query, StringComparison.OrdinalIgnoreCase) || ("#" + string e.Ordinal).Contains(query)) |> Seq.toArray))
                    exports.SelectionChanged.Add(fun _ ->
                        match exports.SelectedItem with
                        | :? Export as item -> entry.Text <- item.Entry
                        | _ -> ())
                    let openFile file =
                        try
                            let loaded = DllImage.Open file
                            remember()
                            activeKey <- None
                            dirty <- false
                            restart()
                            image <- Some loaded
                            bindings.Open loaded
                            path.Text <- loaded.Path
                            filter.Text <- ""
                            panel.IsVisible <- true
                            exports.ItemsSource <- loaded.Exports
                            entry.Text <- ""
                            editor.Load(Signature.Unknown "")
                            signatureStatus.Text <- "请选择导出或填写入口。"
                            if loaded.Exports.Count > 0 then
                                exports.SelectedIndex <- 0
                                entry.Text <- loaded.Exports[0].Entry
                                selectEntry()
                            status.Text <- $"{loaded.Path} · {loaded.Architecture} · {loaded.Exports.Count} 项导出 · {loaded.Symbols.Status}"
                            for _, _, view in mounted do view.OpenFile loaded.Path
                        with e ->
                            let active = image |> Option.map _.Path |> Option.defaultValue "无"
                            status.Text <- $"打开 {file} 失败 · {e.Message}　当前 DLL · {active}"
                    header.Children.Add(TextBlock(Text = "DLL 工作台", FontSize = 24.))
                    header.Children.Add path
                    header.Children.Add(row [
                        button "打开 DLL" (fun () -> openFile path.Text)
                        button "浏览文件…" (fun () ->
                            task {
                                let! files = window.StorageProvider.OpenFilePickerAsync(FilePickerOpenOptions(Title = "选择原生 DLL"))
                                if files.Count > 0 then openFile (files[0].TryGetLocalPath())
                            } |> ignore)
                    ])
                    header.Children.Add status
                    panel.IsVisible <- false
                    panel.Children.Add(TextBlock(Text = "筛选导出"))
                    panel.Children.Add filter
                    panel.Children.Add(cells ["导出名"; "序号"; "相对地址"; "性质"])
                    panel.Children.Add exports
                    panel.Children.Add(TextBlock(Text = "仅有导出表时，返回类型和参数未知。"))
                    panel.Children.Add entry
                    let declaration = TextBox(AcceptsReturn = true, PlaceholderText = "C 函数原型及相关结构体声明")
                    AutomationProperties.SetName(declaration, "C 声明")
                    panel.Children.Add declaration
                    panel.Children.Add(button "识别 C 声明" (fun () ->
                        try
                            let parsed = CDeclaration.Parse(declaration.Text, entry.Text)
                            entry.Text <- parsed.Signature.Entry
                            bindings.Invalidate()
                            editor.Load parsed.Signature
                            dirty <- true
                            signatureStatus.Text <- String.concat "　" (Array.append [| "来自 C 声明 · 待确认" |] parsed.Notes)
                        with e -> signatureStatus.Text <- e.Message))
                    panel.Children.Add editor.Panel
                    panel.Children.Add(button "确认并保存签名" (fun () ->
                        try
                            let dll = image |> Option.defaultWith (fun () -> invalidOp "请先打开 DLL。")
                            let s = editor.Read(entry.Text)
                            store.Confirm(dll, s)
                            dirty <- false
                            drafts.Remove((dll.Identity, entry.Text)) |> ignore
                            selectEntry()
                        with e -> signatureStatus.Text <- e.Message))
                    panel.Children.Add signatureStatus
                    let callFields = StackPanel(Spacing = 4.)
                    let callContext = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
                    let resultSummary = SelectableTextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
                    let output = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
                    let editArguments () =
                        try
                            let dll = image |> Option.defaultWith (fun () -> invalidOp "请先打开 DLL。")
                            let signature = store.ConfirmedDraft(dll, editor.Read entry.Text)
                            let values = System.Text.Json.Nodes.JsonNode.Parse(arguments.Text).AsObject()
                            callFields.Children.Clear()
                            for param in signature.Parameters do
                                let input = TextBox()
                                AutomationProperties.SetName(input, param.Name)
                                let item = values[param.Name]
                                if not (isNull item) then input.Text <- item.ToJsonString()
                                callFields.Children.Add(TextBlock(Text = $"{param.Name} · {param.Type} · {param.Direction}（JSON 值）"))
                                callFields.Children.Add input
                                input.TextChanged.Add(fun _ ->
                                    try
                                        values[param.Name] <- System.Text.Json.Nodes.JsonNode.Parse(input.Text)
                                        arguments.Text <- values.ToJsonString()
                                        callContext.Text <- ""
                                    with _ -> callContext.Text <- $"{param.Name} 需要有效的 JSON 值。")
                        with e -> callContext.Text <- e.Message
                    panel.Children.Add(button "按已确认签名填写" editArguments)
                    panel.Children.Add callFields
                    let resultBody = StackPanel(Spacing = 4.)
                    resultBody.Children.Add callContext
                    entry.TextChanged.Add(fun _ -> callFields.Children.Clear())
                    panel.Children.Add(TextBlock(Text = "调用参数（JSON 对象，如 {\"a\":2,\"b\":3}）"))
                    panel.Children.Add arguments
                    let invoke () =
                        task {
                            try
                                panel.IsEnabled <- false
                                let dll = image |> Option.defaultWith (fun () -> invalidOp "请先打开 DLL。")
                                let saved = store.ConfirmedDraft(dll, editor.Read entry.Text)
                                let worker =
                                    match session with
                                    | Some active -> active
                                    | None ->
                                        let target = Targets.resolve (DllFile dll.Path)
                                        let active = new CallSession(dll, target.DotNetPath)
                                        session <- Some active
                                        active
                                let actual = arguments.Text
                                callContext.Text <- $"{IO.Path.GetFileName dll.Path} · {saved.Entry} · 实参 {actual}"
                                let! result = worker.Invoke(saved, actual)
                                let outputs = result.Outputs |> Seq.map (fun kv -> $"{kv.Key} = {kv.Value}") |> String.concat "、"
                                resultSummary.Text <- $"返回值 · {result.Result}　输出参数 · {outputs}"
                                output.Text <- result.Display()
                            with e -> resultSummary.Text <- ""; output.Text <- e.Message
                            panel.IsEnabled <- true
                        } |> ignore
                    panel.Children.Add(row [
                        button "调用函数" invoke
                        button "重新开始会话" (fun () ->
                            let dialog = Window(Title = "重新开始会话", Width = 360., SizeToContent = SizeToContent.Height,
                                                WindowStartupLocation = WindowStartupLocation.CenterOwner)
                            let body = StackPanel(Spacing = 8., Margin = Avalonia.Thickness 16.)
                            body.Children.Add(TextBlock(Text = "当前会话的句柄将失效。下次调用会创建新会话。", TextWrapping = Avalonia.Media.TextWrapping.Wrap))
                            body.Children.Add(button "取消" (fun () -> dialog.Close()))
                            body.Children.Add(button "确认重新开始" (fun () -> restart(); output.Text <- "已重新开始会话。"; dialog.Close()))
                            dialog.Content <- body
                            dialog.ShowDialog(window) |> ignore)
                    ])
                    resultBody.Children.Add resultSummary
                    resultBody.Children.Add output
                    results.Set(ScrollViewer(Content = resultBody))
                    panel.Children.Add results.Inline
                    panel.Children.Add bindings.Control
                    let tabItem title (content: Control) =
                        let item = TabItem(Header = title, Content = content)
                        AutomationProperties.SetName(item, title)
                        item
                    tabs.Items.Add(tabItem "导出" (ScrollViewer(Content = panel))) |> ignore
                    tabs.SelectedIndex <- 0
                    let syncTabs () =
                        let wanted = host.DllTabs(tabPoint.PageId, tabPoint.PointId)
                        for tab, item, view in mounted.ToArray() do
                            if wanted |> List.exists (fun t -> obj.ReferenceEquals(t, tab)) |> not then
                                if item.IsSelected then tabs.SelectedIndex <- 0
                                tabs.Items.Remove item |> ignore
                                mounted.Remove((tab, item, view)) |> ignore
                                view.Close()
                        for tab in wanted do
                            if mounted.Exists(fun (t, _, _) -> obj.ReferenceEquals(t, tab)) |> not then
                                let view = tab.Create()
                                let item = tabItem tab.Title view.Content
                                mounted.Add(tab, item, view)
                                tabs.Items.Add item |> ignore
                                image |> Option.iter (fun dll -> view.OpenFile dll.Path)
                    registry.Own(host.DllTabsChanged.Subscribe(fun () -> syncTabs()))
                    registry.Own { new IDisposable with
                                       member _.Dispose() =
                                           for _, _, view in mounted do view.Close() }
                    syncTabs()
                    registry.Own(requests.Subscribe(fun (title, file) ->
                        file |> Option.iter openFile
                        tabs.Items |> Seq.cast<TabItem> |> Seq.tryFind (fun item -> string item.Header = title)
                        |> Option.iter (fun item -> tabs.SelectedItem <- item)))
                    let layout = DockPanel()
                    DockPanel.SetDock(header, Dock.Top)
                    layout.Children.Add header
                    layout.Children.Add tabs
                    layout
            } }
