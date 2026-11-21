namespace MyDebugTools.WebApi

open Avalonia.Controls
open Avalonia.Media
open MyDebugTools.Contracts
open MyDebugTools.WebApi.Editor

type ResponseView(events: Events) =
    let headers = multiline "响应头"
    let raw = multiline "响应原文"
    let code = SyntaxEditor(IsReadOnly = true) |> named "格式化响应"
    let preview = StackPanel(Spacing = 6.)
    let message = TextBlock(TextWrapping = TextWrapping.Wrap)
    let host = ContentControl()
    let mutable media = ""
    let original = button events "原文" (fun () -> host.Content <- raw; message.Text <- "")
    let formatted = button events "格式化视图" (fun () ->
        let result =
            if media = ".json" then Presentation.formatJson (value raw) |> Result.mapError _.Message
            else Presentation.formatXml (value raw)
        match result with
        | Ok text -> code.SetLanguage(media); code.Text <- text; host.Content <- code; message.Text <- ""
        | Error msg -> host.Content <- raw; message.Text <- msg)
    let html = button events "静态预览" (fun () ->
        preview.Children.Clear()
        for tag, text in Presentation.htmlBlocks (value raw) do
            let block = SelectableTextBlock(Text = text, TextWrapping = TextWrapping.Wrap)
            if tag.StartsWith("h") then
                block.FontSize <- if tag = "h1" then 24. else 18.
                block.FontWeight <- FontWeight.Bold
            if tag = "li" then block.Text <- "• " + text
            if tag = "pre" then block.FontFamily <- FontFamily("Consolas")
            preview.Children.Add block
        host.Content <- preview)
    do
        headers.IsReadOnly <- true
        raw.IsReadOnly <- true
        formatted.IsVisible <- false
        html.IsVisible <- false
        host.Content <- raw
    member _.Content = stack [ TextBlock(Text = "响应头"); headers; row [original; formatted; html]; message; host ]
    member _.Show(reply: Reply) =
        media <- Presentation.language reply.MediaType
        headers.Text <- reply.Headers
        raw.Text <- reply.Raw
        message.Text <- ""
        formatted.IsVisible <- media = ".json" || media = ".xml"
        html.IsVisible <- (media = ".html")
        host.Content <- raw
        code.Text <- ""
        preview.Children.Clear()
