namespace MyDebugTools.ApiMonitor

open System
open Avalonia.Controls
open Avalonia.Automation
open Avalonia.Media
open Avalonia.Threading
open System.Threading.Tasks
open MyDebugTools.Host
open MyDebugTools.Contracts
open MyDebugTools.DllDebugger

module private View =
    let input name =
        let control = TextBox(PlaceholderText = name)
        AutomationProperties.SetName(control, name)
        control

    let button title handler =
        let control = Button(Content = title)
        control.Click.Add(fun _ -> handler())
        control

    let create (sessions: ResizeArray<CaptureSession>) (reg: Registration) () =
        let panel = StackPanel(Spacing = 8.)
        let query = input "搜索 SDK 函数"
        let dll = input "监控 DLL 路径"
        let store = SignatureStore(IO.Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "MyDebugTools", "signatures"))
        let mutable source = WindowsApi.All |> Seq.toArray
        let message = TextBlock(TextWrapping = TextWrapping.Wrap)
        let results = ListBox(Height = 130.)
        let details = TextBlock(TextWrapping = TextWrapping.Wrap)
        let chosen = ListBox(Height = 80.)
        let selected = ResizeArray<MonitorFunction>()
        let add = button "加入监控" (fun () ->
            match results.SelectedItem with
            | :? MonitorFunction as f when f.Selectable && not (selected.Contains f) ->
                selected.Add f
                chosen.ItemsSource <- selected.ToArray()
            | _ -> ())
        let remove = button "移除选中函数" (fun () ->
            match chosen.SelectedItem with
            | :? MonitorFunction as f ->
                selected.Remove f |> ignore
                chosen.ItemsSource <- selected.ToArray()
            | _ -> ())
        results.SelectionChanged.Add(fun _ ->
            match results.SelectedItem with
            | :? MonitorFunction as f ->
                details.Text <- f.Details
                add.IsEnabled <- f.Selectable
            | _ ->
                details.Text <- ""
                add.IsEnabled <- false)
        let search () =
            results.ItemsSource <- source |> Array.filter (fun f ->
                (f.Module + "!" + f.Signature.Entry).Contains(string query.Text, StringComparison.OrdinalIgnoreCase))
            results.SelectedIndex <- 0
        query.TextChanged.Add(fun _ -> search())
        search()
        let load = button "读取 DLL 导出" (fun () ->
            try
                let image = DllImage.Open dll.Text
                source <- image.Exports |> Seq.map (fun e -> MonitorFunction.FromExport(image, e, store)) |> Seq.toArray
                query.Text <- ""
                search()
                message.Text <- image.Path
            with e -> message.Text <- e.Message)
        let sdk = button "使用 SDK 目录" (fun () ->
            source <- WindowsApi.All |> Seq.toArray
            query.Text <- ""
            search())
        let exe = input "目标 EXE 路径"
        let args = input "程序参数"
        let history = ComboBox()
        let calls = ListBox(Height = 160.)
        let detail = TextBlock(TextWrapping = TextWrapping.Wrap)
        let state = TextBlock(TextWrapping = TextWrapping.Wrap)
        let showDetail () =
            detail.Text <- match calls.SelectedItem with :? CapturedCall as c -> c.Details | _ -> ""
        let refresh () =
            match history.SelectedItem with
            | :? CaptureSession as s ->
                let old = calls.SelectedIndex
                let rows = s.Records()
                calls.ItemsSource <- rows
                calls.SelectedIndex <- if old < 0 && rows.Length > 0 then 0 else old
                showDetail()
                let time = if s.Effective.HasValue then s.Effective.Value.ToString("HH:mm:ss.fffffff") else "等待"
                state.Text <- $"{s.Target} · PID {s.Pid} · {s.Architecture} · 生效 {time} · {s.Status} · {rows.Length} 次调用 · 丢失 {s.Lost}"
            | _ -> ()
        calls.SelectionChanged.Add(fun _ -> showDetail())
        history.SelectionChanged.Add(fun _ -> refresh())
        let start = button "启动并监控" (fun () ->
            try
                let arch = if Targets.forFile exe.Text = X86 then "x86" else "x64"
                let s = CaptureSession.Launch(exe.Text, string args.Text, arch, selected)
                sessions.Add s
                history.ItemsSource <- sessions.ToArray()
                history.SelectedItem <- s
                message.Text <- ""
            with e -> message.Text <- e.Message)
        let stop = button "停止所选会话" (fun () ->
            match history.SelectedItem with
            | :? CaptureSession as s -> s.StopAsync() |> ignore
            | _ -> ())
        history.ItemsSource <- sessions.ToArray()
        history.SelectedIndex <- sessions.Count - 1
        let timer = DispatcherTimer(Interval = TimeSpan.FromMilliseconds 400.)
        timer.Tick.Add(fun _ -> refresh())
        panel.AttachedToVisualTree.Add(fun _ -> timer.Start())
        panel.DetachedFromVisualTree.Add(fun _ -> timer.Stop())
        reg.Own { new IDisposable with member _.Dispose() = timer.Stop() }
        let controls: Control list =
            [ TextBlock(Text = "API 监控", FontSize = 24.); dll; load; sdk; query; results; details; add
              TextBlock(Text = "监控函数"); chosen; remove; exe; args; start; message
              TextBlock(Text = "采集会话"); history; stop; state; calls; detail ]
        controls |> List.iter panel.Children.Add
        ScrollViewer(Content = panel) :> Control

type Plugin() =
    interface IDesktopPlugin with
        member _.Activate reg =
            let sessions = ResizeArray<CaptureSession>()
            let active () = sessions |> Seq.filter (fun s -> not s.Completion.IsCompleted) |> Seq.toArray
            let stop () = active() |> Array.map (fun s -> s.StopAsync()) |> Task.WhenAll
            reg.Monitor { Target = fun () ->
                              match active() with
                              | [||] -> None
                              | all -> Some (all |> Array.map string |> String.concat "、")
                          Stop = stop }
            reg.Own { new IDisposable with member _.Dispose() = stop().GetAwaiter().GetResult() }
            reg.Page { Id = "api.monitor"; Title = "API 监控"; Create = View.create sessions reg }
