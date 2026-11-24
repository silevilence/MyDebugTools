namespace MyDebugTools.WebApi

open Avalonia.Controls
open MyDebugTools.WebApi.Editor

type Definitions(events: Events, capture: unit -> Definition, apply: Definition -> unit,
                 values: unit -> string array * string, restoreValues: Definition -> string array * string -> unit,
                 report: string -> unit) =
    let dir = Library.directory()
    let folder = field "目录"
    let title = field "接口名称"
    let folders = ListBox(MaxHeight = 130.) |> named "接口目录"
    let items = ListBox(MaxHeight = 160.) |> named "目录中的接口"
    let mutable saved = [||]
    let mutable visible: Definition array = [||]
    let mutable id = ""
    let sourceLabel = TextBlock(Text = "临时请求")
    let snapshot () = { capture() with Id = id; Folder = value folder; Title = value title }, values()
    let mutable baseline = snapshot()
    let drafts = ResizeArray<Definition * (string array * string)>()
    let draftList = ComboBox() |> named "保留的草稿"
    let confirm = StackPanel(Spacing = 4., IsVisible = false)
    let mutable pending = ignore
    let assignSource d = sourceLabel.Text <- if d.Id = "" then "临时请求" else $"接口 · {d.Folder}/{d.Title}"
    let displayFolder f = if f = "" then "（根目录）" else f
    let mutable paths = [||]
    let selectFolder () =
        let i = folders.SelectedIndex
        visible <- if i < 0 || i >= paths.Length then [||] else saved |> Array.filter (fun d -> d.Folder = paths[i]) |> Array.sortBy _.Title
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
        assignSource d
        baseline <- snapshot()
    let run action () = try action() with e -> report e.Message
    let save () =
        let d = { capture() with Id = id; Title = (value title).Trim(); Folder = (value folder).Trim() }
        let result = Library.save dir d
        id <- result.Id
        assignSource result
        baseline <- snapshot()
        refresh result.Folder
        items.SelectedIndex <- visible |> Array.findIndex (fun x -> x.Id = id)
        report $"已保存 · {result.Title}"
    let replace action =
        if baseline = snapshot() then action()
        else
            pending <- action
            confirm.IsVisible <- true
    let complete keep =
        if keep then
            drafts.Add(snapshot())
            draftList.ItemsSource <- drafts |> Seq.mapi (fun i (d, _) -> $"{i + 1} · {d.Method} {d.Url}") |> Seq.toArray
            draftList.SelectedIndex <- drafts.Count - 1
        confirm.IsVisible <- false
        let action = pending
        pending <- ignore
        action()
    let openSelected () =
        if items.SelectedIndex < 0 then invalidOp "请选择目录中的接口"
        let d = visible[items.SelectedIndex]
        replace (fun () -> openDefinition d; report $"已打开 · {d.Title}")
    do
        confirm.Children.Add(TextBlock(Text = "当前请求有未保存的改动。"))
        confirm.Children.Add(row [button events "取消切换" (fun () -> pending <- ignore; confirm.IsVisible <- false)
                                  button events "保留草稿并继续" (fun () -> complete true)
                                  button events "放弃改动并继续" (fun () -> complete false)])
        events.Watch(folders.SelectionChanged.Subscribe(fun _ -> selectFolder()))
        run (fun () -> refresh "") ()
    member _.Content =
        stack [ TextBlock(Text = "内部目录"); folders; items; button events "打开接口" (run openSelected)
                TextBlock(Text = "保存到目录"); folder; TextBlock(Text = "接口名称"); title
                row [ button events "保存定义" (run save)
                      button events "新建临时请求" (fun () -> replace (fun () -> openDefinition Library.blank; report "临时请求")) ]
                confirm; draftList; button events "返回所选草稿" (fun () ->
                    if draftList.SelectedIndex >= 0 then
                        let d, args = drafts[draftList.SelectedIndex]
                        replace (fun () -> openDefinition d; restoreValues d args; report "已恢复草稿")) ]
    member _.Label = sourceLabel
    member _.Source =
        saved |> Array.tryFind (fun d -> d.Id = id)
        |> Option.map (fun d -> String.concat "/" ([ d.Folder; d.Title ] |> List.filter ((<>) "")))
        |> Option.defaultValue "临时请求"
