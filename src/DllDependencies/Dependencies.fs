namespace MyDebugTools.DllDependencies

open System
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open Avalonia.Automation
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Input.Platform
open Avalonia.Platform.Storage
open Avalonia.Threading
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.VirtualDom
open MyDebugTools.Contracts
open MyDebugTools.DllDebugger

module internal View =
    let tree (subscriptions: Subscriptions.Group) (result: DependencyAnalysis) picked =
        let rec populate (parent: TreeViewItem) ancestors (m: DependencyModule) =
            let branch = m :: ancestors
            for edge in m.Imports do
                let reference =
                    if branch |> List.exists (fun ancestor -> obj.ReferenceEquals(ancestor, edge.Target)) then DependencyReference.Cycle
                    elif edge.Reference = DependencyReference.Cycle then DependencyReference.Shared
                    else edge.Reference
                let status = if isNull edge.Target then "未解析" else string reference
                let item = ExpandableTreeItem($"{edge.Name} · {status}", Tag = edge)
                AutomationProperties.SetName(item, Labels.edgeLabelAs reference edge)
                ToolTip.SetTip(item, Labels.edgeLabelAs reference edge)
                if reference <> DependencyReference.Cycle && not (isNull edge.Target) && edge.Target.Imports.Count > 0 then
                    // Populate a branch on its first expansion.
                    item.Items.Add(TreeViewItem()) |> ignore
                    item.PropertyChanged.Subscribe(fun change ->
                        if change.Property = TreeViewItem.IsExpandedProperty && item.IsExpanded then
                            match item.Items[0] with
                            | :? TreeViewItem as first when isNull first.Header ->
                                item.Items.Clear()
                                populate item branch edge.Target
                            | _ -> ()) |> subscriptions.Add
                parent.Items.Add item |> ignore
        let root = ExpandableTreeItem(Labels.moduleLabel result.Root, IsExpanded = true)
        populate root [] result.Root
        let control = TreeView()
        ScrollViewer.SetHorizontalScrollBarVisibility(control, ScrollBarVisibility.Auto)
        control.Items.Add root |> ignore
        control.SelectionChanged.Subscribe(fun _ ->
            match control.SelectedItem with
            | :? TreeViewItem as item ->
                match item.Tag with :? DependencyEdge as edge -> picked (Some edge) | _ -> picked None
            | _ -> ()) |> subscriptions.Add
        let locate chain =
            let mutable parent = root :> TreeViewItem
            for edge in chain do
                parent.IsExpanded <- true
                parent <- parent.Items |> Seq.cast<TreeViewItem> |> Seq.tryFind (fun t -> obj.ReferenceEquals(t.Tag, edge)) |> Option.defaultValue parent
            parent.IsSelected <- true
            parent.BringIntoView() |> ignore
        control :> Control, locate

    let create () =
        let tabSubscriptions = Subscriptions.Group()
        let resultSubscriptions = Subscriptions.Group()
        let layout = DockPanel()
        let header = StackPanel(Spacing = 4.)
        for text in [ "静态分析普通导入和延迟导入。运行时动态加载的模块需在运行现场核对。"
                      "搜索顺序：当前 DLL 所在目录 → 自定义目录（从上到下）→ 目标架构的系统目录。" ] do
            TextBlock.create [ TextBlock.text text; TextBlock.textWrapping Avalonia.Media.TextWrapping.Wrap ]
            |> VirtualDom.create |> header.Children.Add
        let folders = Collections.ObjectModel.ObservableCollection<string>()
        let input = TextBox(PlaceholderText = "搜索目录路径", Width = 240.)
        AutomationProperties.SetName(input, "搜索目录路径")
        let order = ListBox(ItemsSource = folders, MinHeight = 36., MaxHeight = 90.)
        AutomationProperties.SetName(order, "搜索目录顺序")
        let feedback = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false)
        let controls = WrapPanel(Orientation = Orientation.Horizontal)
        let button title run =
            let b = Button(Content = title)
            b.Click.Subscribe(fun _ -> run()) |> tabSubscriptions.Add
            controls.Children.Add b
            b
        let options = ScrollViewer(Content = header, MaxHeight = 215.)
        DockPanel.SetDock(options, Dock.Top)
        layout.Children.Add options
        layout.SizeChanged.Subscribe(fun e -> options.MaxHeight <- max 80. (e.NewSize.Height * 0.50)) |> tabSubscriptions.Add
        let content = ContentControl(Content = TextBlock(Text = "请先在工作台打开 DLL。"))
        layout.Children.Add content
        let mutable pending: CancellationTokenSource option = None
        let mutable current = None
        let modes = ComboBox(ItemsSource = [| "依赖树"; "依赖图" |], SelectedIndex = 0)
        AutomationProperties.SetName(modes, "依赖视图")
        let mutable views: Lazy<Control * (unit -> unit)> array = [||]
        let mutable key: (string * string * bool) option = None
        let diagnosis = DiagnosticPanel()
        let chainBox = TextBox(IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap)
        AutomationProperties.SetName(chainBox, "引用链与命中路径")
        let missing = ComboBox(MaxWidth = 320.)
        AutomationProperties.SetName(missing, "未解析导入")
        let show () =
            if modes.SelectedIndex >= 0 && modes.SelectedIndex < views.Length then
                let page, locate = views[modes.SelectedIndex].Value
                content.Content <- page
                locate()
        modes.SelectionChanged.Subscribe(fun _ -> show()) |> tabSubscriptions.Add
        let cancel () = pending |> Option.iter (fun source -> source.Cancel())
        let openFile path =
            if current <> Some path then key <- None
            current <- Some path
            cancel()
            let source = new CancellationTokenSource()
            pending <- Some source
            resultSubscriptions.Clear()
            views <- [||]
            missing.ItemsSource <- null
            chainBox.Text <- ""
            diagnosis.Clear()
            content.Content <- TextBlock(Text = $"正在分析依赖 · {path}")
            let search = folders |> Seq.toArray
            task {
                try
                    try
                        let! result = Task.Run(fun () -> DependencyAnalysis.Read(path, search, source.Token))
                        if not source.IsCancellationRequested then
                            let chains = Collections.Generic.Dictionary<DependencyEdge, DependencyEdge list>(HashIdentity.Reference)
                            let paths = Collections.Generic.Dictionary<string * string * bool, DependencyEdge>()
                            let parents = Collections.Generic.Dictionary<DependencyEdge, string>(HashIdentity.Reference)
                            let seen = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            let queue = Collections.Generic.Queue<DependencyModule * DependencyEdge list>()
                            queue.Enqueue(result.Root, [])
                            seen.Add(result.Root.Path) |> ignore
                            while queue.Count > 0 do
                                let parent, route = queue.Dequeue()
                                for edge in parent.Imports do
                                    let next = edge :: route
                                    chains[edge] <- next
                                    parents[edge] <- parent.Path
                                    paths[(parent.Path, edge.Name, edge.Delayed)] <- edge
                                    if not (isNull edge.Target) && seen.Add(edge.Target.Path) then queue.Enqueue(edge.Target, next)
                            let mutable selected = key |> Option.bind (fun k -> match paths.TryGetValue k with true, e -> Some e | _ -> None)
                            let pick value =
                                selected <- value
                                match value with
                                | Some (e: DependencyEdge) when isNull e.Target ->
                                    diagnosis.Show("DLL 依赖", "解析导入", IO.Path.GetFileName(parents[e]) + " → " + e.Name, e.Problem)
                                | _ -> diagnosis.Clear()
                                key <- value |> Option.map (fun e -> parents[e], e.Name, e.Delayed)
                                chainBox.Text <- match value with
                                                 | None -> result.Root.Path
                                                 | Some e ->
                                                     let names = List.rev chains[e] |> List.map _.Name
                                                     String.concat " → " (IO.Path.GetFileName(path) :: names) + Environment.NewLine + Labels.edgeLabel e
                            let absent = chains.Keys |> Seq.filter (fun e -> isNull e.Target) |> Seq.toArray
                            missing.ItemsSource <- absent |> Array.map (fun e -> $"{IO.Path.GetFileName parents[e]} → {e.Name}")
                            missing.SelectionChanged.Subscribe(fun _ ->
                                if missing.SelectedIndex >= 0 then pick (Some absent[missing.SelectedIndex]); show()) |> resultSubscriptions.Add
                            views <- [| lazy (let page, locate = tree resultSubscriptions result pick
                                              page, fun () -> locate (selected |> Option.map (fun e -> List.rev chains[e]) |> Option.defaultValue []))
                                        lazy (let page, locate = Graph.create resultSubscriptions result pick
                                              page, fun () -> locate selected) |]
                            pick selected
                            show()
                    with e ->
                        if not source.IsCancellationRequested then content.Content <- TextBlock(Text = e.Message)
                finally
                    if pending = Some source then pending <- None
                    source.Dispose()
            } |> ignore
        let refresh () =
            feedback.IsVisible <- false
            current |> Option.iter openFile
        controls.Children.Add input
        let addFolder () =
            try
                let folder = IO.Path.TrimEndingDirectorySeparator(IO.Path.GetFullPath(input.Text.Trim()))
                if not (IO.Directory.Exists folder) then invalidOp "目录不存在。"
                if folders |> Seq.exists (fun f -> String.Equals(f, folder, StringComparison.OrdinalIgnoreCase)) then
                    invalidOp "此目录已添加。"
                folders.Add folder
                order.SelectedIndex <- folders.Count - 1
                input.Clear()
                refresh()
            with e ->
                feedback.Text <- e.Message
                feedback.IsVisible <- true
        button "添加搜索目录" addFolder |> ignore
        button "浏览搜索目录…" (fun () ->
            task {
                let! picked = (TopLevel.GetTopLevel layout).StorageProvider.OpenFolderPickerAsync(FolderPickerOpenOptions(Title = "依赖搜索目录"))
                if picked.Count > 0 then input.Text <- picked[0].TryGetLocalPath(); addFolder()
            } |> ignore) |> ignore
        header.Children.Add(TextBlock(Text = "自定义搜索目录（从上到下）"))
        header.Children.Add order
        let move delta =
            let i = order.SelectedIndex
            folders.Move(i, i + delta)
            order.SelectedIndex <- i + delta
            refresh()
        let up = button "上移" (fun () -> move -1)
        let down = button "下移" (fun () -> move 1)
        let remove = button "移除目录" (fun () ->
            let i = order.SelectedIndex
            folders.RemoveAt i
            order.SelectedIndex <- min i (folders.Count - 1)
            refresh())
        let updateButtons () =
            let i = order.SelectedIndex
            up.IsEnabled <- i > 0
            down.IsEnabled <- i >= 0 && i < folders.Count - 1
            remove.IsEnabled <- i >= 0
        order.SelectionChanged.Subscribe(fun _ -> updateButtons()) |> tabSubscriptions.Add
        updateButtons()
        header.Children.Add controls
        header.Children.Add feedback
        header.Children.Add modes
        header.Children.Add(TextBlock(Text = "未解析导入 · 选择后定位"))
        header.Children.Add missing
        header.Children.Add chainBox
        let copyChain = Button(Content = "复制引用链")
        copyChain.Click.Subscribe(fun _ ->
            task {
                try
                    do! (TopLevel.GetTopLevel layout).Clipboard.SetTextAsync(chainBox.Text)
                    feedback.Text <- "已复制引用链"
                with e ->
                    chainBox.Focus() |> ignore
                    chainBox.SelectAll()
                    feedback.Text <- $"可手动复制所选引用链。{e.Message}"
                feedback.IsVisible <- true
            } |> ignore) |> tabSubscriptions.Add
        header.Children.Add copyChain
        header.Children.Add diagnosis.Control
        let close () =
            let release () =
                cancel()
                tabSubscriptions.Clear()
                resultSubscriptions.Clear()
                views <- [||]
            if Dispatcher.UIThread.CheckAccess() then release() else Dispatcher.UIThread.Post release
        { Content = layout; OpenFile = openFile; Close = close }

type Plugin() =
    interface IDesktopPlugin with
        member _.Activate registry =
            let outlet = PanelOutlet("DLL 依赖")
            PageDocumentation.describe registry "dll.dependencies.detail" "查看静态依赖解析详情"
            registry.Page { Id = "dll.dependencies.detail"; Title = "依赖详情"; Create = fun () -> outlet.Companion }
            let create () =
                let view = View.create()
                outlet.Set view.Content
                { view with Content = outlet.Inline; Close = fun () ->
                                view.Close()
                                outlet.Set(TextBlock(Text = "请打开 DLL 工作台查看当前文件的依赖。")) }
            registry.DllTab { PageId = "dll.debugger"; PointId = "tabs"; MinVersion = 1; MaxVersion = 1
                              Title = "依赖"; Create = create }
