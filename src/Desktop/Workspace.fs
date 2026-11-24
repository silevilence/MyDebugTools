namespace MyDebugTools.Desktop

open System
open System.Collections.Generic
open Avalonia.Controls
open Dock.Avalonia.Controls
open Dock.Model.Avalonia
open Dock.Model.Avalonia.Controls
open Dock.Model.Core

type Workspace() =
    let factory = Factory()
    let center = DocumentDock(Id = "workspace.documents", IsCollapsable = false)
    let root = RootDock(Id = "workspace.root", IsCollapsable = false)
    let view = DockControl()
    let documents = Dictionary<string, Document>()
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
