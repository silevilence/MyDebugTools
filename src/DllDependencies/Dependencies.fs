namespace MyDebugTools.DllDependencies

open System
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open Avalonia.Automation
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Threading
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.VirtualDom
open MyDebugTools.Contracts
open MyDebugTools.DllDebugger

module internal View =
    let tree (subscriptions: Subscriptions.Group) (result: DependencyAnalysis) =
        let rec populate (parent: TreeViewItem) ancestors (m: DependencyModule) =
            let branch = m :: ancestors
            for edge in m.Imports do
                let reference =
                    if branch |> List.exists (fun ancestor -> obj.ReferenceEquals(ancestor, edge.Target)) then DependencyReference.Cycle
                    elif edge.Reference = DependencyReference.Cycle then DependencyReference.Shared
                    else edge.Reference
                let item = ExpandableTreeItem(Labels.edgeLabelAs reference edge)
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
        control

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
        let order = ComboBox(ItemsSource = folders, MinWidth = 180., MaxWidth = 360.)
        AutomationProperties.SetName(order, "搜索目录顺序")
        let feedback = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false)
        let controls = WrapPanel(Orientation = Orientation.Horizontal)
        let button title run =
            let b = Button(Content = title)
            b.Click.Subscribe(fun _ -> run()) |> tabSubscriptions.Add
            controls.Children.Add b
            b
        DockPanel.SetDock(header, Dock.Top)
        layout.Children.Add header
        let content = ContentControl(Content = TextBlock(Text = "请先在工作台打开 DLL。"))
        layout.Children.Add content
        let mutable pending: CancellationTokenSource option = None
        let mutable current = None
        let modes = ComboBox(ItemsSource = [| "依赖树"; "依赖图" |], SelectedIndex = 0)
        AutomationProperties.SetName(modes, "依赖视图")
        let mutable views: Lazy<Control> array = [||]
        let show () =
            if modes.SelectedIndex >= 0 && modes.SelectedIndex < views.Length then
                content.Content <- views[modes.SelectedIndex].Value
        modes.SelectionChanged.Subscribe(fun _ -> show()) |> tabSubscriptions.Add
        let cancel () = pending |> Option.iter (fun source -> source.Cancel())
        let openFile path =
            current <- Some path
            cancel()
            let source = new CancellationTokenSource()
            pending <- Some source
            resultSubscriptions.Clear()
            views <- [||]
            content.Content <- TextBlock(Text = "正在分析依赖…")
            let search = folders |> Seq.toArray
            task {
                try
                    try
                        let! result = Task.Run(fun () -> DependencyAnalysis.Read(path, search, source.Token))
                        if not source.IsCancellationRequested then
                            views <- [| lazy (tree resultSubscriptions result :> Control)
                                        lazy (Graph.create resultSubscriptions result :> Control) |]
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
        button "添加搜索目录" (fun () ->
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
                feedback.IsVisible <- true) |> ignore
        controls.Children.Add order
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
            registry.DllTab { PageId = "dll.debugger"; PointId = "tabs"; MinVersion = 1; MaxVersion = 1
                              Title = "依赖"; Create = View.create }
