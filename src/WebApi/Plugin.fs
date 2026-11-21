namespace MyDebugTools.WebApi

open System
open System.Threading
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.VirtualDom
open Avalonia.Media
open MyDebugTools.Contracts
open MyDebugTools.WebApi.Editor

type Plugin() =
    interface IDesktopPlugin with
        member _.Activate registry =
            let events = new Events()
            let client = Http.createClient()
            let lifetime = new CancellationTokenSource()
            registry.Own { new IDisposable with
                member _.Dispose() =
                    lifetime.Cancel()
                    (events :> IDisposable).Dispose()
                    client.Dispose()
                    lifetime.Dispose() }

            registry.Page {
                Id = "web.api"; Title = "Web API"
                Create = fun () ->
                    let methodBox = field "HTTP 方法"
                    methodBox.Width <- 112.
                    methodBox.Text <- "GET"
                    let url = field "URL"
                    let timeout = field "超时秒数"
                    timeout.Text <- "30"
                    timeout.Width <- 64.
                    let parameters = new Parameters()
                    registry.Own parameters
                    let body = BodyEditor(events)
                    let status = TextBlock(TextWrapping = TextWrapping.Wrap)
                    let headers = multiline "响应头"
                    let raw = multiline "响应原文"
                    headers.IsReadOnly <- true
                    raw.IsReadOnly <- true
                    let response = tab "响应" (stack [ TextBlock(Text = "响应头"); headers; TextBlock(Text = "原文"); raw ])
                    let tabs = TabControl()
                    let paramsTab = tab "参数" (parameters.Content(events))
                    let capture () =
                        { Library.blank with Method = value methodBox; Url = value url
                                             Parameters = parameters.Definitions(); Body = body.Read() }
                    let apply (d: Definition) =
                        methodBox.Text <- d.Method
                        url.Text <- d.Url
                        parameters.Load d.Parameters
                        body.Load d.Body
                        tabs.SelectedItem <- paramsTab
                    let definitions = Definitions(events, capture, apply, fun msg -> status.Text <- msg)
                    tabs.Items.Add(paramsTab) |> ignore
                    tabs.Items.Add(tab "正文" body.Content) |> ignore
                    tabs.Items.Add(tab "接口定义" definitions.Content) |> ignore
                    tabs.Items.Add response |> ignore
                    let dataDir = Library.directory()
                    let history = HistoryView(events, dataDir, fun msg -> status.Text <- msg)
                    let logTab = tab "请求日志" history.Content
                    tabs.Items.Add(logTab) |> ignore
                    events.Watch(tabs.SelectionChanged.Subscribe(fun e ->
                        if e.Source = tabs && tabs.SelectedItem = logTab then history.Refresh()))
                    tabs.SelectedIndex <- 0
                    let mutable current: CancellationTokenSource option = None
                    let sendButton = Button(Content = "发送")
                    let cancel = button events "取消请求" (fun () -> current |> Option.iter (fun c -> c.Cancel()))
                    cancel.IsEnabled <- false
                    let send () = task {
                        try
                            let args = parameters.Read()
                            let payload = body.Read()
                            use request = Http.prepareBody (value methodBox) (value url) args payload
                            let seconds =
                                match Int32.TryParse(value timeout) with
                                | true, s when s >= 1 && s <= 86400 -> s
                                | _ -> invalidOp "超时秒数须在 1 至 86400 之间"
                            use stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)
                            let entry = History.snapshot definitions.Source request payload args
                            History.start dataDir History.limit entry
                            current <- Some stop
                            sendButton.IsEnabled <- false
                            cancel.IsEnabled <- true
                            status.Text <- "正在发送…"
                            try
                                let! reply = Http.send client request (TimeSpan.FromSeconds(float seconds)) stop.Token
                                status.Text <- $"{reply.Summary} · {reply.ElapsedMs} ms"
                                headers.Text <- reply.Headers
                                raw.Text <- reply.Raw
                                tabs.SelectedItem <- response
                                History.finish dataDir entry.Id status.Text
                            finally
                                current <- None
                                cancel.IsEnabled <- false
                                sendButton.IsEnabled <- true
                        with e -> status.Text <- e.Message
                    }
                    events.Watch(sendButton.Click.Subscribe(fun _ -> send() |> ignore))
                    let toolbar = stack [
                        row [ TextBlock(Text = "方法"); methodBox; TextBlock(Text = "超时（秒）"); timeout; sendButton; cancel ]
                        url; status
                    ]
                    DockPanel.create [
                        DockPanel.children [
                            ContentControl.create [ ContentControl.content toolbar; DockPanel.dock Dock.Top ]
                            ContentControl.create [ ContentControl.content tabs ]
                        ]
                    ] |> VirtualDom.create
            }
