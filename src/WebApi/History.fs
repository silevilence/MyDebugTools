namespace MyDebugTools.WebApi

open System
open System.IO
open System.Net.Http
open System.Text
open System.Xml.Linq
open Avalonia.Controls
open MyDebugTools.WebApi.Editor

type LogEntry = { Id: string; Time: string; Method: string; Url: string; Detail: string; Result: string }

module History =
    let limit = 100
    let private file dir = Path.Combine(dir, "history.xml")
    let private entries = Library.children "entry"
    let private packed name (text: string) = Library.node name (Convert.ToBase64String(Encoding.UTF8.GetBytes(text)))
    let private unpack name el = Encoding.UTF8.GetString(Convert.FromBase64String(Library.get name el))

    let snapshot source (request: HttpRequestMessage) (body: Body) (args: Argument array) =
        let detail = StringBuilder()
        let line (s: string) = detail.AppendLine(s) |> ignore
        line ("来源 · " + source)
        for a in args do line $"{a.Location} · {a.Name} = {a.Value}"
        line ("正文类型 · " + body.Kind)
        if body.Kind = "原始文本" then line (Content.renderText args body.Text)
        if body.Kind = "二进制文件" then line body.Text
        line (string request.Headers)
        if not (isNull request.Content) then line (string request.Content.Headers)
        { Id = Guid.NewGuid().ToString("N"); Time = DateTimeOffset.Now.ToString("O"); Method = string request.Method
          Url = request.RequestUri.AbsoluteUri; Detail = detail.ToString(); Result = "已开始发送，尚无结果" }

    let start dir capacity entry =
        Library.update (file dir) "history" (fun root ->
            root.AddFirst(Library.group "entry" [
                Library.node "id" entry.Id; Library.node "time" entry.Time; Library.node "method" entry.Method
                Library.node "url" entry.Url; packed "detail" entry.Detail; packed "result" entry.Result ])
            // Selection is a snapshot because removal changes the sibling enumeration.
            entries root |> Seq.indexed |> Seq.filter (fun (i, _) -> i >= capacity)
            |> Seq.map snd |> Seq.toArray |> Array.iter (fun el -> el.Remove()))

    let finish dir id result =
        Library.update (file dir) "history" (fun root ->
            entries root |> Seq.tryFind (fun el -> Library.get "id" el = id)
            |> Option.iter (fun el -> el.Element(XName.Get "result").ReplaceWith(packed "result" result)))

    let list dir =
        Library.load (file dir) "history" |> entries |> Seq.map (fun el -> {
            Id = Library.get "id" el; Time = Library.get "time" el; Method = Library.get "method" el
            Url = Library.get "url" el; Detail = unpack "detail" el; Result = unpack "result" el
        }) |> Seq.toArray

    let clear dir = Library.update (file dir) "history" (fun root -> root.RemoveNodes())

type HistoryView(events: Events, dir, report: string -> unit) =
    let items = ListBox(MaxHeight = 170.) |> named "历史记录"
    let details = multiline "日志详情"
    let mutable rows: LogEntry array = [||]
    let select () =
        let i = items.SelectedIndex
        details.Text <-
            if i < 0 || i >= rows.Length then ""
            else
                let e = rows[i]
                String.concat Environment.NewLine [ e.Time; e.Method + " " + e.Url; e.Result; e.Detail ]
    let refresh () =
        try
            rows <- History.list dir
            items.ItemsSource <- rows |> Array.map (fun e -> $"{e.Time.Substring(11, 8)}  {e.Method}  {e.Url}  ·  {e.Result}")
            items.SelectedIndex <- if rows.Length = 0 then -1 else 0
            select()
        with e -> report e.Message
    do
        details.IsReadOnly <- true
        events.Watch(items.SelectionChanged.Subscribe(fun _ -> select()))
    member _.Refresh() = refresh()
    member _.Content =
        stack [ TextBlock(Text = $"最近 {History.limit} 次请求"); items; details
                button events "清空请求日志" (fun () ->
                    try History.clear dir; refresh(); report "请求日志已清空"
                    with e -> report e.Message) ]
