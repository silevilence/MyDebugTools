namespace MyDebugTools.Desktop

open System
open System.Collections.Generic
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Interactivity
open Avalonia.VisualTree
open Dock.Avalonia.Controls
open Dock.Model.Avalonia
open Dock.Model.Avalonia.Controls
open Dock.Model.Core
open Dock.Model.Controls
open System.IO
open System.Text.Json

[<CLIMutable>]
type SavedPane = { Id: string; Kind: string; Active: string; Proportion: float; Children: SavedPane array }
[<CLIMutable>]
type SavedWindow = { X: float; Y: float; Width: float; Height: float; Root: SavedPane }
[<CLIMutable>]
type PanelPosition = { Group: string; Index: int; Side: string; Proportion: float; Bounds: float array }
[<CLIMutable>]
type SavedWorkspace = { Positions: Dictionary<string, PanelPosition>; Enabled: string array; Active: string; Layout: SavedPane; Windows: SavedWindow array }

type Workspace(window: Window) =
    let factory = Factory()
    let mutable center = DocumentDock(Id = "workspace.documents", IsCollapsable = false)
    let mutable root = RootDock(Id = "workspace.root", IsCollapsable = false)
    let view = DockControl()
    let documents = Dictionary<string, Document>()
    let file = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "MyDebugTools", "layout.json")
    let focused = Event<string>()
    let positions = Dictionary<string, PanelPosition>()
    let recent = ResizeArray<string>()
    let template (page: Control) =
        let reference = WeakReference<Control>(page)
        Func<IServiceProvider, obj>(fun _ ->
            let frame = Border(Padding = Avalonia.Thickness(12., 8.))
            frame.AttachedToVisualTree.Add(fun _ ->
                match reference.TryGetTarget() with
                | true, control ->
                    match control.Parent with
                    | :? Border as previous when previous <> frame -> previous.Child <- null
                    | _ -> ()
                    frame.Child <- control
                | _ -> ())
            frame.DetachedFromVisualTree.Add(fun _ -> frame.Child <- null)
            frame)
    let isOpen (doc: Document) =
        match doc.Owner with
        | :? IDock as parent -> parent.VisibleDockables <> null && parent.VisibleDockables.Contains(doc)
        | _ -> false
    let remember (layout: IRootDock) (doc: Document) =
        let group = doc.Owner :?> IDock
        let floating = factory.FindRoot(doc, Func<IRootDock, bool>(fun _ -> true))
        let floatWindow =
            if isNull floating || obj.ReferenceEquals(floating, layout) then None
            elif not (isNull floating.Window) then Some floating.Window
            elif isNull layout.Windows then None
            else layout.Windows |> Seq.tryFind (fun w -> obj.ReferenceEquals(w.Layout, floating))
        let bounds =
            match floatWindow with
            | None -> [||]
            | Some w ->
                w.Save()
                [| w.X; w.Y; w.Width; w.Height |]
        let side =
            match group.Owner with
            | :? ProportionalDock as split ->
                let first = split.VisibleDockables.IndexOf(group) = 0
                if split.Orientation = Orientation.Vertical then (if first then "Top" else "Bottom")
                else (if first then "Left" else "Right")
            | _ -> ""
        positions[doc.Id] <-
            { Group = group.Id; Index = group.VisibleDockables.IndexOf doc; Side = side
              Proportion = if Double.IsFinite group.Proportion then group.Proportion else 0.5
              Bounds = bounds }
    do
        center.VisibleDockables <- factory.CreateList<IDockable>()
        root.VisibleDockables <- factory.CreateList<IDockable>(center)
        root.ActiveDockable <- center
        root.DefaultDockable <- center
        factory.DefaultHostWindowLocator <- Func<IHostWindow>(fun () -> HostWindow())
        factory.InitLayout(root)
        view.Factory <- factory
        view.Layout <- root
        let mutable tabPointer: IPointer option = None
        view.AddHandler(InputElement.PointerPressedEvent, (fun _ (e: PointerPressedEventArgs) ->
            match e.Source with
            | :? Avalonia.Visual as source when source.GetSelfAndVisualAncestors() |> Seq.exists (fun v -> v :? DocumentTabStripItem) ->
                tabPointer <- Some e.Pointer
            | _ -> tabPointer <- None), RoutingStrategies.Tunnel)
        view.AddHandler(InputElement.PointerReleasedEvent, (fun _ _ -> tabPointer <- None), RoutingStrategies.Tunnel)
        window.AddHandler(InputElement.KeyDownEvent, (fun _ (e: KeyEventArgs) ->
            match e.Key, tabPointer with
            | Key.Escape, Some pointer when not (isNull pointer.Captured) ->
                pointer.Capture(null)
                tabPointer <- None
                e.Handled <- true
            | _ -> ()), RoutingStrategies.Tunnel)
        factory.FocusedDockableChanged.Add(fun e ->
            match e.Dockable with
            | :? Document as d when documents.ContainsKey d.Id -> focused.Trigger d.Id
            | _ -> ())
    member _.Save(enabled, active) =
        if root.Windows <> null then
            for floating in root.Windows do floating.Save()
        let rec snapshot (item: IDockable) =
            let kind =
                match item with
                | :? RootDock -> "root"
                | :? DocumentDock -> "tabs"
                | :? ProportionalDock as group -> if group.Orientation = Orientation.Vertical then "vertical" else "horizontal"
                | :? ProportionalDockSplitter -> "divider"
                | :? Document -> "page"
                | _ -> invalidOp $"无法保存布局项 {item.Id}"
            let children, selected =
                match item with
                | :? IDock as group when group.VisibleDockables <> null ->
                    group.VisibleDockables |> Seq.map snapshot |> Seq.toArray,
                    (if isNull group.ActiveDockable then "" else group.ActiveDockable.Id)
                | _ -> [||], ""
            { Kind = kind; Id = item.Id; Active = selected; Children = children
              Proportion = if Double.IsFinite item.Proportion then item.Proportion else -1. }
        let windows = [| if root.Windows <> null then
                             for w in root.Windows do
                                 yield { X = w.X; Y = w.Y; Width = w.Width; Height = w.Height; Root = snapshot w.Layout } |]
        let saved = { Positions = positions; Enabled = enabled; Active = active; Layout = snapshot root; Windows = windows }
        Directory.CreateDirectory(Path.GetDirectoryName file) |> ignore
        let temp = file + "." + Guid.NewGuid().ToString("N")
        try
            File.WriteAllText(temp, JsonSerializer.Serialize(saved))
            File.Move(temp, file, true)
        finally
            if File.Exists temp then File.Delete temp

    member _.Restore(enable: string -> unit, lookup: string -> (string * Control) option) =
        if not (File.Exists file) then None else
        let saved = JsonSerializer.Deserialize<SavedWorkspace>(File.ReadAllText file)
        let rec rebuild (s: SavedPane) : IDockable =
            let item : IDockable =
                match s.Kind with
                | "root" -> RootDock(IsCollapsable = false)
                | "tabs" -> DocumentDock(IsCollapsable = (s.Id <> "workspace.documents"))
                | "vertical" | "horizontal" ->
                    ProportionalDock(Orientation = (if s.Kind = "vertical" then Orientation.Vertical else Orientation.Horizontal))
                | "divider" -> ProportionalDockSplitter()
                | "page" -> Document()
                | _ -> invalidOp "布局项类型无效"
            item.Id <- s.Id
            if s.Proportion >= 0. then item.Proportion <- s.Proportion
            match item with
            | :? IDock as group ->
                let children = Array.map rebuild s.Children
                group.VisibleDockables <- factory.CreateList<IDockable>(children)
                let selected = children |> Array.tryFind (fun child -> child.Id = s.Active) |> Option.orElseWith (fun () -> Array.tryHead children)
                selected |> Option.iter (fun child -> group.ActiveDockable <- child; group.DefaultDockable <- child)
            | _ -> ()
            item
        let restored = rebuild saved.Layout :?> RootDock
        let floats = saved.Windows |> Array.map (fun w ->
            Dock.Model.Avalonia.Core.DockWindow(X = w.X, Y = w.Y, Width = w.Width, Height = w.Height, Layout = (rebuild w.Root :?> IRootDock)) :> IDockWindow)
        restored.Windows <- factory.CreateList<IDockWindow>(floats)
        let rec visit (node: IDockable) = seq {
            yield node
            match node with
            | :? IDock as group when group.VisibleDockables <> null ->
                for child in group.VisibleDockables do yield! visit child
            | _ -> ()
        }
        let items = Seq.append (visit restored) (floats |> Seq.collect (fun w -> visit w.Layout)) |> Seq.toArray
        let main = items |> Array.find (fun d -> d.Id = "workspace.documents") :?> DocumentDock
        if not (isNull saved.Positions) then
            for KeyValue(id, pos) in saved.Positions do positions[id] <- pos
        for id in saved.Enabled do enable id
        factory.InitLayout restored
        for node in items do
            match node with
            | :? Document as doc ->
                match lookup doc.Id with
                | Some (title, control) -> doc.Title <- title; doc.Content <- template control; documents[doc.Id] <- doc
                | None ->
                    remember restored doc
                    factory.RemoveDockable(doc, true)
            | _ -> ()
        root <- restored
        center <- main
        view.Layout <- root
        if root.Windows <> null then
            for w in root.Windows do
                if window.Screens.All |> Seq.exists (fun screen -> screen.WorkingArea.Contains(Avalonia.PixelPoint(int w.X, int w.Y))) |> not then
                    let target = window.Screens.Primary
                    if not (isNull target) then
                        w.X <- float target.WorkingArea.X + 20.
                        w.Y <- float target.WorkingArea.Y + 20.
            root.ShowWindows.Execute(null)
        Some saved.Active

    member this.Reset() =
        let openIds = documents |> Seq.choose (fun kv -> if isOpen kv.Value then Some kv.Key else None) |> Seq.toArray
        for id in openIds do this.Move(id, "合并标签")

    member _.Close() =
        if root.Windows <> null then for floating in root.Windows |> Seq.toArray do floating.Exit()
        for doc in documents.Values do doc.Content <- null
        documents.Clear()
        view.Layout <- null
    member _.Control = view
    member _.Focused = focused.Publish
    member _.IsOpen(id) =
        match documents.TryGetValue id with true, doc -> isOpen doc | _ -> false
    member this.Show(id, title, control: Control) =
        let doc =
            match documents.TryGetValue id with
            | true, existing -> existing
            | _ ->
                let item = Document(Id = id, Title = title, Content = template control)
                documents.Add(id, item)
                item
        recent.Remove id |> ignore
        recent.Add id
        match positions.TryGetValue id with
        | true, pos ->
            positions.Remove id |> ignore
            let destination =
                if pos.Bounds.Length > 0 then null
                else factory.FindDockable(root, Func<IDockable, bool>(fun d -> d.Id = pos.Group))
            match destination with
            | :? DocumentDock as destination ->
                if isOpen doc then factory.RemoveDockable(doc, false)
                factory.InsertDockable(destination, doc, max 0 (min pos.Index destination.VisibleDockables.Count))
            | _ when pos.Bounds.Length = 4 ->
                factory.AddDockable(center, doc)
                factory.FloatDockable doc
                match factory.FindRoot(doc, Func<IRootDock, bool>(fun _ -> true)) with
                | floating when not (isNull floating) && not (isNull floating.Window) ->
                    let w = floating.Window
                    w.X <- pos.Bounds[0]
                    w.Y <- pos.Bounds[1]
                    w.Width <- pos.Bounds[2]
                    w.Height <- pos.Bounds[3]
                | _ -> ()
            | _ when pos.Side <> "" ->
                factory.AddDockable(center, doc)
                this.Move(id, pos.Side)
                match doc.Owner with :? IDock as group -> group.Proportion <- pos.Proportion | _ -> ()
            | _ -> factory.AddDockable(center, doc)
        | _ when not (isOpen doc) -> factory.AddDockable(center, doc)
        | _ -> ()
        factory.SetActiveDockable doc
        factory.SetFocusedDockable(root, doc)
    member _.NeedsMount id = positions.ContainsKey id
    member _.Forget id = positions.Remove id |> ignore
    member _.Move(id, operation) =
        match documents.TryGetValue id with
        | true, doc when isOpen doc ->
            match operation with
            | "浮动" -> factory.FloatDockable doc
            | "隐藏" -> factory.CloseDockable doc
            | "合并标签" ->
                if not (obj.ReferenceEquals(doc.Owner, center)) then
                    factory.RemoveDockable(doc, true)
                    factory.AddDockable(center, doc)
                    factory.SetActiveDockable doc
            | side ->
                let direction = Enum.Parse<DockOperation>(side)
                factory.RemoveDockable(doc, true)
                let group = DocumentDock(Id = Guid.NewGuid().ToString("N"))
                group.VisibleDockables <- factory.CreateList<IDockable>(doc)
                group.ActiveDockable <- doc
                factory.SplitToDock(center, group, direction)
                recent |> Seq.rev |> Seq.choose (fun key ->
                    match documents.TryGetValue key with
                    | true, other when obj.ReferenceEquals(other.Owner, center) && isOpen other -> Some other
                    | _ -> None)
                |> Seq.tryHead |> Option.iter (fun previous -> factory.SetActiveDockable previous)
                factory.SetActiveDockable doc
                factory.SetFocusedDockable(root, doc)
        | _ -> ()
    member _.Remove(id) =
        match documents.TryGetValue id with
        | true, doc ->
            if isOpen doc then
                remember root doc
            doc.Content <- null
            if isOpen doc then factory.RemoveDockable(doc, true)
            documents.Remove id |> ignore
        | _ -> ()
