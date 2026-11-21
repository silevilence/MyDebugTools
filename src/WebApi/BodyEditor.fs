namespace MyDebugTools.WebApi

open Avalonia.Controls
open MyDebugTools.Contracts
open MyDebugTools.WebApi.Editor

type BodyEditor(events: Events) =
    let kind = choice "正文类型" [| "空"; "原始文本"; "URL 编码表单"; "multipart"; "二进制文件" |]
    let media = field "内容类型"
    let raw = SyntaxEditor() |> named "请求正文"
    let diagnostic = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
    let format = button events "格式化 JSON" (fun () ->
        match Presentation.formatJson raw.Text with
        | Ok text -> raw.Text <- text; diagnostic.Text <- ""
        | Error problem ->
            raw.Focus() |> ignore
            raw.Select(problem.Offset, min 1 (raw.Text.Length - problem.Offset))
            diagnostic.Text <- problem.Message)
    let syntax () =
        let lang = Presentation.language (value media)
        raw.SetLanguage(lang)
        format.IsVisible <- (lang = ".json")
        diagnostic.Text <- ""
    let fields = stack [ TextBlock(Text = "内容类型"); media; TextBlock(Text = "原文（正文参数使用 {参数名}）")
                         raw; format; diagnostic ]
    let path = field "二进制文件路径"
    let binary = stack [ path; browseFile events path ]
    let instructions = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
    let refresh () =
        fields.IsVisible <- kind.SelectedIndex = 1
        binary.IsVisible <- kind.SelectedIndex = 4
        instructions.IsVisible <- kind.SelectedIndex = 2 || kind.SelectedIndex = 3
        instructions.Text <-
            if kind.SelectedIndex = 2 then "在「参数」中添加字段，位置选「正文」。"
            else "在「参数」中添加字段，文本的位置选「正文」，上传文件的位置选「文件」。"
    do
        media.Text <- "text/plain"
        refresh()
        syntax()
        events.Watch(media.TextChanged.Subscribe(fun _ -> syntax()))
        events.Watch(kind.SelectionChanged.Subscribe(fun _ -> refresh()))
    member _.Content = stack [ kind; fields; instructions; binary ]
    member _.Read() =
        { Kind = string kind.SelectedItem; MediaType = value media
          Text = match kind.SelectedIndex with 1 -> raw.Text | 4 -> value path | _ -> "" }
    member _.Load(body: Body) =
        kind.SelectedItem <- body.Kind
        media.Text <- body.MediaType
        raw.Text <- if body.Kind = "原始文本" then body.Text else ""
        path.Text <- if body.Kind = "二进制文件" then body.Text else ""
