namespace MyDebugTools.Contracts

open System
open System.Reflection
open System.Text.RegularExpressions
open Avalonia.Automation
open Avalonia.Controls
open Avalonia.Input.Platform
open Avalonia.Media

module Diagnostics =
    let summary page action subject reason =
        let version = Assembly.GetEntryAssembly().GetName().Version
        let clean text =
            let paths = Regex.Replace(text, @"(?i)(?:[A-Z]:\\|\\\\)[^""'\r\n]*\\([^\\\r\n""']+)", "$1")
            let urls = Regex.Replace(paths, @"(?i)\bhttps?://[^\s""']+", fun m ->
                match Uri.TryCreate(m.Value, UriKind.Absolute) with
                | true, uri -> uri.Scheme + "://" + uri.IdnHost
                | _ -> "[地址已省略]")
            Regex.Replace(urls, @"(?im)\b(authorization|proxy-authorization|cookie|set-cookie|password|token|api[-_]?key)\s*[:=].*$", "$1=[已省略]")
        String.concat Environment.NewLine [
            $"MyDebugTools {version}"; $"时间　{DateTimeOffset.Now:O}"; $"页面　{page}"
            $"动作　{action}"; $"对象　{clean subject}"; $"原因　{clean reason}" ]

type DiagnosticPanel() =
    let text = TextBox(IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap)
    let receipt = TextBlock(TextWrapping = TextWrapping.Wrap)
    let copy = Button(Content = "复制诊断信息")
    let body = StackPanel(Spacing = 4.)
    let detail = Expander(Header = "诊断详情", Content = text)
    let mutable report = ""
    do
        AutomationProperties.SetName(text, "诊断摘要")
        body.Children.Add detail
        body.Children.Add copy
        body.Children.Add receipt
        body.IsVisible <- false
        copy.Click.Add(fun _ ->
            task {
                let current = report
                try
                    do! (TopLevel.GetTopLevel(body)).Clipboard.SetTextAsync(current)
                    if current = report then receipt.Text <- "已复制诊断信息"
                with _ ->
                    if current = report then
                        detail.IsExpanded <- true
                        text.Focus() |> ignore
                        text.SelectAll()
                        receipt.Text <- "剪贴板不可写。可手动复制已选中的摘要。"
            } |> ignore)
    member _.Control = body
    member _.Clear() = body.IsVisible <- false
    member _.Show(page, action, subject, reason) =
        report <- Diagnostics.summary page action subject reason
        text.Text <- report
        receipt.Text <- ""
        body.IsVisible <- true
