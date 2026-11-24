namespace MyDebugTools.Desktop

open System
open System.Collections.Generic
open Avalonia.Controls
open Dock.Avalonia.Controls
open Dock.Model.Avalonia
open Dock.Model.Avalonia.Controls
open Dock.Model.Core
open Dock.Model.Controls
open Dock.Model.Avalonia.Json
open Dock.Serializer.SystemTextJson
open System.IO
open System.Text.Json

[<CLIMutable>]
type SavedWorkspace = { Enabled: string array; Active: string; Layout: string }

type Workspace(window: Window) =
    let factory = Factory()
    let mutable center = DocumentDock(Id = "workspace.documents", IsCollapsable = false)
    let mutable root = RootDock(Id = "workspace.root", IsCollapsable = false)
    let view = DockControl()
    let documents = Dictionary<string, Document>()
    let serializer = DockSerializer(AvaloniaModelPolymorphicTypeResolver())
    let file = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "MyDebugTools", "layout.json")
    let focused = Event<string>()
    let isOpen (doc: Document) =
        match doc.Owner with
        | :? IDock as parent -> parent.VisibleDockables <> null && parent.VisibleDockables.Contains(doc)
        | _ -> false
    do
        center.VisibleDockables <- factory.CreateList<IDockable>()
        root.VisibleDockables <- factory.CreateList<IDockable>(center)
        root.ActiveDockable <- center
        root.DefaultDockable <- center
        factory.DefaultHostWindowLocator <- Func<IHostWindow>(fun () -> HostWindow())
        factory.InitLayout(root)
        view.Factory <- factory
        view.Layout <- root
        factory.ActiveDockableChanged.Add(fun e ->
            match e.Dockable with
            | :? Document as d when documents.ContainsKey d.Id -> focused.Trigger d.Id
            | _ -> ())
    member _.Save(enabled, active) =
        if root.Windows <> null then
            for floating in root.Windows do floating.Save()
        let saved = { Enabled = enabled; Active = active; Layout = serializer.Serialize<IRootDock>(root) }
        Directory.CreateDirectory(Path.GetDirectoryName file) |> ignore
        let temp = file + "." + Guid.NewGuid().ToString("N")
        try
            File.WriteAllText(temp, JsonSerializer.Serialize(saved))
            File.Move(temp, file, true)
        finally
            if File.Exists temp then File.Delete temp

    member _.Restore(enable: string -> unit, lookup: string -> Control option) =
        if not (File.Exists file) then None else
        let saved = JsonSerializer.Deserialize<SavedWorkspace>(File.ReadAllText file)
        let restored = serializer.Deserialize<IRootDock>(saved.Layout) :?> RootDock
        let rec visit (node: IDockable) = seq {
            yield node
            match node with
            | :? IDock as group when group.VisibleDockables <> null ->
                for child in group.VisibleDockables do yield! visit child
            | _ -> ()
            match node with
            | :? IRootDock as r when r.Windows <> null ->
                for w in r.Windows do yield! visit w.Layout
            | _ -> ()
        }
        let items = visit restored |> Seq.toArray
        let main = items |> Array.find (fun d -> d.Id = "workspace.documents") :?> DocumentDock
        for id in saved.Enabled do enable id
        factory.InitLayout restored
        for node in items do
            match node with
            | :? Document as doc ->
                match lookup doc.Id with
                | Some control -> doc.Content <- control; documents[doc.Id] <- doc
                | None -> factory.RemoveDockable(doc, true)
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

    member _.Control = view
    member _.Focused = focused.Publish
    member _.IsOpen(id) =
        match documents.TryGetValue id with true, doc -> isOpen doc | _ -> false
    member _.Show(id, title, control: Control) =
        let doc =
            match documents.TryGetValue id with
            | true, existing -> existing
            | _ ->
                let item = Document(Id = id, Title = title, Content = control)
                documents.Add(id, item)
                item
        if not (isOpen doc) then factory.AddDockable(center, doc)
        factory.SetActiveDockable doc
        factory.SetFocusedDockable(root, doc)
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
                factory.SplitToDock(center, doc, direction)
        | _ -> ()
    member _.Remove(id) =
        match documents.TryGetValue id with
        | true, doc ->
            doc.Content <- null
            if isOpen doc then factory.RemoveDockable(doc, true)
            documents.Remove id |> ignore
        | _ -> ()
