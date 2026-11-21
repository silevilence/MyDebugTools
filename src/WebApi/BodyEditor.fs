namespace MyDebugTools.WebApi

open Avalonia.Controls
open MyDebugTools.WebApi.Editor

type BodyEditor(events: Events) =
    let kind = choice "正文类型" [| "空"; "原始文本" |]
    let media = field "内容类型"
    let raw = multiline "请求正文"
    let fields = stack [ TextBlock(Text = "内容类型"); media; TextBlock(Text = "原文（正文参数使用 {参数名}）"); raw ]
    let refresh () = fields.IsVisible <- kind.SelectedIndex = 1
    do
        media.Text <- "text/plain"
        refresh()
        events.Watch(kind.SelectionChanged.Subscribe(fun _ -> refresh()))
    member _.Content = stack [ kind; fields ]
    member _.Read() = { Kind = string kind.SelectedItem; MediaType = value media; Text = value raw }
    member _.Load(body: Body) =
        kind.SelectedItem <- body.Kind
        media.Text <- body.MediaType
        raw.Text <- body.Text
