namespace MyDebugTools.DllDependencies

open System
open System.Threading
open System.Threading.Tasks
open Avalonia.Controls
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
        $"{edge.Name} · {kind}{relation} · {destination}"

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
        let note = TextBlock(Text = "静态分析普通导入和延迟导入。运行时动态加载的模块需在运行现场核对。",
                             TextWrapping = Avalonia.Media.TextWrapping.Wrap)
        DockPanel.SetDock(note, Dock.Top)
        layout.Children.Add note
        let content = ContentControl(Content = TextBlock(Text = "请先在工作台打开 DLL。"))
        layout.Children.Add content
        let mutable pending: CancellationTokenSource option = None
        let cancel () = pending |> Option.iter (fun source -> source.Cancel())
        let openFile path =
            cancel()
            let source = new CancellationTokenSource()
            pending <- Some source
            content.Content <- TextBlock(Text = "正在分析依赖…")
            task {
                try
                    try
                        let! result = Task.Run(fun () -> DependencyAnalysis.Read(path, source.Token))
                        if not source.IsCancellationRequested then content.Content <- tree result
                    with e ->
                        if not source.IsCancellationRequested then content.Content <- TextBlock(Text = e.Message)
                finally
                    if pending = Some source then pending <- None
                    source.Dispose()
            } |> ignore
        { Content = layout; OpenFile = openFile; Close = cancel }

type Plugin() =
    interface IDesktopPlugin with
        member _.Activate registry = registry.DllTab { Title = "依赖"; Create = View.create }
