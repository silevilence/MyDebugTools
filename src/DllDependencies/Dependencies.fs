namespace MyDebugTools.DllDependencies

open System
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
open Avalonia.Automation
open Avalonia.Layout
open MyDebugTools.Contracts
open MyDebugTools.DllDebugger

module internal View =
    let moduleLabel (m: DependencyModule) = $"{m.Path} · {m.Architecture}"

    let edgeLabel (edge: DependencyEdge) =
        let kind = if edge.Delayed then "延迟导入" else "普通导入"
        let relation =
            match edge.Reference with
            | DependencyReference.Shared -> " · 共享引用"
            | DependencyReference.Cycle -> " · 循环引用"
            | _ -> ""
        let destination = if isNull edge.Target then edge.Problem else moduleLabel edge.Target
        let contract =
            if not edge.IsApiSet then ""
            elif isNull edge.ApiSetHost then " · API Set 合约"
            else $" · API Set 宿主 {edge.ApiSetHost}"
        $"{edge.Name} · {kind}{contract}{relation} · {destination}"

    let tree (result: DependencyAnalysis) =
        let rec populate (parent: TreeViewItem) (m: DependencyModule) =
            for edge in m.Imports do
                let item = TreeViewItem(Header = edgeLabel edge)
                if edge.Reference = DependencyReference.First && not (isNull edge.Target) && edge.Target.Imports.Count > 0 then
                    // Populate a branch on its first expansion.
                    item.Items.Add(TreeViewItem()) |> ignore
                    item.PropertyChanged.Add(fun change ->
                        if change.Property = TreeViewItem.IsExpandedProperty && item.IsExpanded then
                            match item.Items[0] with
                            | :? TreeViewItem as first when isNull first.Header ->
                                item.Items.Clear()
                                populate item edge.Target
                            | _ -> ())
                parent.Items.Add item |> ignore
        let root = TreeViewItem(Header = moduleLabel result.Root, IsExpanded = true)
        populate root result.Root
        let control = TreeView()
        control.Items.Add root |> ignore
        control

    let create () =
        let layout = DockPanel()
        let header = StackPanel(Spacing = 4.)
        let note = TextBlock(Text = "静态分析普通导入和延迟导入。运行时动态加载的模块需在运行现场核对。",
                             TextWrapping = Avalonia.Media.TextWrapping.Wrap)
        header.Children.Add note
        header.Children.Add(TextBlock(Text = "搜索顺序：当前 DLL 所在目录 → 自定义目录（从上到下）→ 目标架构的系统目录。",
                                      TextWrapping = Avalonia.Media.TextWrapping.Wrap))
        let folders = Collections.ObjectModel.ObservableCollection<string>()
        let input = TextBox(PlaceholderText = "搜索目录路径", Width = 240.)
        AutomationProperties.SetName(input, "搜索目录路径")
        let order = ComboBox(ItemsSource = folders, MinWidth = 180., MaxWidth = 360.)
        AutomationProperties.SetName(order, "搜索目录顺序")
        let feedback = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false)
        let controls = WrapPanel(Orientation = Orientation.Horizontal)
        let button title run =
            let b = Button(Content = title)
            b.Click.Add(fun _ -> run())
            controls.Children.Add b
            b
        DockPanel.SetDock(header, Dock.Top)
        layout.Children.Add header
        let content = ContentControl(Content = TextBlock(Text = "请先在工作台打开 DLL。"))
        layout.Children.Add content
        let mutable pending: CancellationTokenSource option = None
        let mutable current = None
        let cancel () = pending |> Option.iter (fun source -> source.Cancel())
        let openFile path =
            current <- Some path
            cancel()
            let source = new CancellationTokenSource()
            pending <- Some source
            content.Content <- TextBlock(Text = "正在分析依赖…")
            let search = folders |> Seq.toArray
            task {
                try
                    try
                        let! result = Task.Run(fun () -> DependencyAnalysis.Read(path, search, source.Token))
                        if not source.IsCancellationRequested then content.Content <- tree result
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
        order.SelectionChanged.Add(fun _ -> updateButtons())
        updateButtons()
        header.Children.Add controls
        header.Children.Add feedback
        { Content = layout; OpenFile = openFile; Close = cancel }

type Plugin() =
    interface IDesktopPlugin with
        member _.Activate registry = registry.DllTab { Title = "依赖"; Create = View.create }
