namespace MyDebugTools.WebApi

open Avalonia.Controls
open MyDebugTools.WebApi.Editor

type Definitions(events: Events, capture: unit -> Definition, apply: Definition -> unit, report: string -> unit) =
    let dir = Library.directory()
    let folder = field "目录"
    let title = field "接口名称"
    let folders = ListBox(MaxHeight = 130.) |> named "接口目录"
    let items = ListBox(MaxHeight = 160.) |> named "目录中的接口"
    let mutable saved = [||]
    let mutable visible: Definition array = [||]
    let mutable id = ""
    let displayFolder f = if f = "" then "（根目录）" else f
    let mutable paths = [||]
    let selectFolder () =
        let i = folders.SelectedIndex
        visible <- if i < 0 then [||] else saved |> Array.filter (fun d -> d.Folder = paths[i]) |> Array.sortBy _.Title
        items.ItemsSource <- visible |> Array.map _.Title
    let refresh target =
        saved <- Library.list dir
        paths <- saved |> Array.map _.Folder |> Array.distinct |> Array.sort
        folders.ItemsSource <- Array.map displayFolder paths
        folders.SelectedIndex <- Array.tryFindIndex ((=) target) paths |> Option.defaultValue (if paths.Length = 0 then -1 else 0)
        selectFolder()
    let openDefinition d =
        id <- d.Id
        folder.Text <- d.Folder
        title.Text <- d.Title
        apply d
    let run action () = try action() with e -> report e.Message
    let save () =
        let d = { capture() with Id = id; Title = (value title).Trim(); Folder = (value folder).Trim() }
        let result = Library.save dir d
        id <- result.Id
        refresh result.Folder
        items.SelectedIndex <- visible |> Array.findIndex (fun x -> x.Id = id)
        report $"已保存 · {result.Title}"
    let openSelected () =
        if items.SelectedIndex < 0 then invalidOp "请选择目录中的接口"
        let d = visible[items.SelectedIndex]
        openDefinition d
        report $"已打开 · {d.Title}"
    do
        events.Watch(folders.SelectionChanged.Subscribe(fun _ -> selectFolder()))
        run (fun () -> refresh "") ()
    member _.Content =
        stack [ TextBlock(Text = "内部目录"); folders; items; button events "打开接口" (run openSelected)
                TextBlock(Text = "保存到目录"); folder; title
                row [ button events "保存定义" (run save)
                      button events "新建临时请求" (fun () -> openDefinition Library.blank; report "临时请求") ] ]
    member _.CurrentId = id
