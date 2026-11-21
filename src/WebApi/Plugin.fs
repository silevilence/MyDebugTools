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
                    let parameters = Parameters(events)
                    let status = TextBlock(TextWrapping = TextWrapping.Wrap)
                    let headers = multiline "响应头"
                    let raw = multiline "响应原文"
                    headers.IsReadOnly <- true
                    raw.IsReadOnly <- true
                    let response = tab "响应" (stack [ TextBlock(Text = "响应头"); headers; TextBlock(Text = "原文"); raw ])
                    let tabs = TabControl()
                    tabs.Items.Add(tab "参数" parameters.Content) |> ignore
                    tabs.Items.Add response |> ignore
                    tabs.SelectedIndex <- 0
                    let mutable current: CancellationTokenSource option = None
                    let sendButton = Button(Content = "发送")
                    let cancel = button events "取消请求" (fun () -> current |> Option.iter (fun c -> c.Cancel()))
                    cancel.IsEnabled <- false
                    let send () = task {
                        try
                            use request = Http.prepare (value methodBox) (value url) (parameters.Read())
                            let seconds =
                                match Int32.TryParse(value timeout) with
                                | true, s when s >= 1 && s <= 86400 -> s
                                | _ -> invalidOp "超时秒数须在 1 至 86400 之间"
                            use stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)
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
