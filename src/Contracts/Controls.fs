namespace MyDebugTools.Contracts

open Avalonia.Automation
open Avalonia.Automation.Peers
open Avalonia.Automation.Provider
open Avalonia.Controls
open Avalonia.Media

// Avalonia caches control types for the life of the app. Keep the types in
// this shared assembly so an individual plugin instance can be collected.
type DrawingSurface(draw: DrawingContext -> unit) =
    inherit Control(IsHitTestVisible = false)
    let mutable render = Some draw
    override _.Render(context) = render |> Option.iter (fun callback -> callback context)
    interface System.IDisposable with member _.Dispose() = render <- None

// The stock tree peer exposes selection. Also expose branch expansion to
// screen readers and to the same Windows automation used by the workbench.
type private BranchPeer(item: TreeViewItem) =
    inherit TreeViewItemAutomationPeer(item)
    interface IExpandCollapseProvider with
        member _.Expand() = item.IsExpanded <- true
        member _.Collapse() = item.IsExpanded <- false
        member _.ShowsMenu = false
        member _.ExpandCollapseState =
            if item.Items.Count = 0 then ExpandCollapseState.LeafNode
            elif item.IsExpanded then ExpandCollapseState.Expanded
            else ExpandCollapseState.Collapsed

type ExpandableTreeItem(title: string) as item =
    inherit TreeViewItem(Header = title)
    do AutomationProperties.SetName(item, title)
    override _.StyleKeyOverride = typeof<TreeViewItem>
    override _.OnCreateAutomationPeer() = BranchPeer(item)

/// A panel has one live control. Docking moves it between the ordinary task tab
/// and the shell document without restarting the underlying operation.
type PanelOutlet(title: string) =
    let inlineHost = ContentControl()
    let companion = ContentControl()
    let hint text = TextBlock(Text = text, TextWrapping = TextWrapping.Wrap) :> Control
    let mutable content = hint $"打开主工具后可在这里查看{title}。"
    let mutable detached = false
    let move () =
        inlineHost.Content <- null
        companion.Content <- null
        if detached then
            inlineHost.Content <- hint $"{title}已在独立面板中打开。"
            companion.Content <- content
        else inlineHost.Content <- content
    do
        companion.AttachedToVisualTree.Add(fun _ -> detached <- true; move())
        companion.DetachedFromVisualTree.Add(fun _ -> detached <- false; move())
        move()
    member _.Inline = inlineHost
    member _.Companion = companion :> Control
    member _.Set(value: Control) = content <- value; move()
