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
                    let selectEntry () =
                        match image with
                        | None -> ()
                        | Some dll ->
                            try
                                let found = store.Find(dll, entry.Text)
                                editor.Load found.Signature
                                signatureStatus.Text <- found.Status
                            with e -> signatureStatus.Text <- e.Message
                    entry.TextChanged.Add(fun _ -> if editor.Entry <> entry.Text then selectEntry())
                    exports.SelectionChanged.Add(fun _ ->
                        match exports.SelectedItem with
                        | :? Export as item -> entry.Text <- item.Entry
                        | _ -> ())
                    let openFile file =
                        try
                            let loaded = DllImage.Open file
                            restart()
                            image <- Some loaded
                            bindings.Open loaded
                            path.Text <- loaded.Path
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
                            status.Text <- $"打开失败 · {e.Message}　当前 DLL · {active}"
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
                    panel.Children.Add(TextBlock(Text = "导出名　序号　相对地址　性质"))
                    panel.Children.Add exports
                    panel.Children.Add(TextBlock(Text = "仅有导出表时，返回类型和参数未知。"))
                    panel.Children.Add entry
                    let declaration = TextBox(AcceptsReturn = true, PlaceholderText = "C 函数原型及相关结构体声明")
                    AutomationProperties.SetName(declaration, "C 声明")
                    panel.Children.Add declaration
                    panel.Children.Add(button "识别 C 声明" (fun () ->
                        try
                            let parsed = CDeclaration.Parse(declaration.Text, entry.Text)
                            editor.Load parsed.Signature
                            entry.Text <- parsed.Signature.Entry
                            signatureStatus.Text <- String.concat "　" (Array.append [| "来自 C 声明 · 待确认" |] parsed.Notes)
                        with e -> signatureStatus.Text <- e.Message))
                    panel.Children.Add editor.Panel
                    panel.Children.Add(button "确认并保存签名" (fun () ->
                        try
                            let dll = image |> Option.defaultWith (fun () -> invalidOp "请先打开 DLL。")
                            let s = editor.Read(entry.Text)
                            store.Confirm(dll, s)
                            selectEntry()
                        with e -> signatureStatus.Text <- e.Message))
                    panel.Children.Add signatureStatus
                    let output = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
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
                                let! result = worker.Invoke(saved, arguments.Text)
                                output.Text <- result.Display()
                            with e -> output.Text <- e.Message
                            panel.IsEnabled <- true
                        } |> ignore
                    panel.Children.Add(row [
                        button "调用函数" invoke
                        button "重新开始会话" (fun () -> restart(); output.Text <- "已重新开始会话。")
                    ])
                    panel.Children.Add output
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
