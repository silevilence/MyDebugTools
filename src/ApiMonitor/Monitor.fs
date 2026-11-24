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

    let create (target: unit -> DebugTarget option) () =
        let layout = DockPanel()
        let query = input "搜索 SDK 函数"
        let dll = input "监控 DLL 路径"
        let store = SignatureStore(IO.Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "MyDebugTools", "signatures"))
        let installed = WindowsApi.Installed()
        let mutable source = installed
        let diagnosis = DiagnosticPanel()
        let message = TextBlock(TextWrapping = TextWrapping.Wrap)
        let results = ListBox(Height = 130.)
        let details = TextBlock(TextWrapping = TextWrapping.Wrap)
        AutomationProperties.SetAutomationId(details, "function-preview")
        let chosen = ListBox(Height = 80.)
        AutomationProperties.SetAutomationId(chosen, "chosen-functions")
        let selected = ResizeArray<MonitorFunction>()
        let selectionChanged = Event<unit>()
        let count = TextBlock()
        let sourceName = TextBlock(Text = "函数来源 · Windows SDK")
        let clear = button "清除搜索" (fun () -> query.Text <- "")
        let add = button "加入监控" (fun () ->
            match results.SelectedItem with
            | :? MonitorFunction as f when f.Selectable && not (selected.Contains f) ->
                selected.Add f
                chosen.ItemsSource <- selected.ToArray()
                selectionChanged.Trigger()
            | _ -> ())
        let remove = button "移除选中函数" (fun () ->
            match chosen.SelectedItem with
            | :? MonitorFunction as f ->
                selected.Remove f |> ignore
                chosen.ItemsSource <- selected.ToArray()
                selectionChanged.Trigger()
            | _ -> ())
        let update () =
            count.Text <- $"已选 {selected.Count} 个监控函数"
            remove.IsEnabled <- chosen.SelectedIndex >= 0
            match results.SelectedItem with
            | :? MonitorFunction as f ->
                details.Text <- f.Details
                add.IsEnabled <- f.Selectable && not (selected.Contains f)
                if selected.Contains f then details.Text <- details.Text + " · 已加入"
            | _ ->
                details.Text <- $"{sourceName.Text} · 没有匹配项。可清除搜索或切换来源。"
                add.IsEnabled <- false
        results.SelectionChanged.Add(fun _ -> update())
        chosen.SelectionChanged.Add(fun _ -> update())
        selectionChanged.Publish.Add(fun () -> update())
        let search () =
            results.ItemsSource <- source |> Array.filter (fun f ->
                (f.Module + "!" + f.Signature.Entry).Contains(string query.Text, StringComparison.OrdinalIgnoreCase))
            results.SelectedIndex <- 0
            update()
        query.TextChanged.Add(fun _ -> search())
        search()
        let load = button "读取 DLL 导出" (fun () ->
            try
                let image = DllImage.Open dll.Text
                sourceName.Text <- $"函数来源 · {IO.Path.GetFileName image.Path}"
                source <- image.Exports |> Seq.map (fun e -> MonitorFunction.FromExport(image, e, store)) |> Seq.toArray
                query.Text <- ""
                search()
                message.Text <- image.Path
            with e -> message.Text <- e.Message)
        let sdk = button "使用 SDK 目录" (fun () ->
            sourceName.Text <- "函数来源 · Windows SDK"
            source <- installed
            query.Text <- ""
            search())
        let exe = input "目标 EXE 路径"
        let args = input "程序参数"
        let history = ComboBox()
        let mutable sessions: CaptureSession array = [||]
        let mutable names: string array = [||]
        let selectedSession () = sessions |> Array.tryItem history.SelectedIndex
        let updateHistory () =
            let known = CaptureHistory.Sessions()
            let labels = known |> Array.map string
            if labels <> names then
                let index = history.SelectedIndex
                sessions <- known
                names <- labels
                history.ItemsSource <- labels
                history.SelectedIndex <- if index >= 0 && index < labels.Length then index else labels.Length - 1
        let calls = ListBox(Height = 160.)
        let detail = TextBlock(TextWrapping = TextWrapping.Wrap)
        let state = TextBlock(TextWrapping = TextWrapping.Wrap)
        AutomationProperties.SetAutomationId(state, "capture-status")
        AutomationProperties.SetAutomationId(detail, "call-detail")
        let functionFilter = input "筛选记录函数"
        let threadFilter = input "筛选线程 ID"
        let matches = TextBlock()
        let displayed = Collections.ObjectModel.ObservableCollection<CapturedCall>()
        calls.ItemsSource <- displayed
        let timeline = TabControl()
        let mutable lastView = None
        let showDetail () =
            detail.Text <- match calls.SelectedItem with :? CapturedCall as c -> c.Details | _ -> ""
        let refresh () =
            match selectedSession() with
            | Some s when lastView <> Some (s, s.Revision) ->
                lastView <- Some (s, s.Revision)
                let old = match calls.SelectedItem with :? CapturedCall as c -> Some c.Id | _ -> None
                let rows = s.Records()
                let filtered = rows |> Array.filter (fun c ->
                    c.Function.Contains(string functionFilter.Text, StringComparison.OrdinalIgnoreCase) &&
                    (String.IsNullOrWhiteSpace threadFilter.Text || string c.Thread = threadFilter.Text))
                matches.Text <- $"匹配 {filtered.Length} / 共 {rows.Length} 条记录"
                for i in 0 .. filtered.Length - 1 do
                    if i >= displayed.Count then displayed.Add(filtered[i])
                    elif displayed[i] <> filtered[i] then displayed[i] <- filtered[i]
                while displayed.Count > filtered.Length do displayed.RemoveAt(displayed.Count - 1)
                let index = old |> Option.bind (fun id -> filtered |> Array.tryFindIndex (fun c -> c.Id = id))
                calls.SelectedIndex <- defaultArg index (if filtered.Length = 0 then -1 else 0)
                showDetail()
                let time = if s.Effective.HasValue then s.Effective.Value.ToString("HH:mm:ss.fffffff") else "等待"
                state.Text <- $"{s.Target} · PID {s.Pid} · {s.Architecture} · 生效 {time} · {s.Status} · {rows.Length} 次调用 · 丢失 {s.Lost}"
            | _ -> ()
        let filterChanged () = lastView <- None; refresh()
        functionFilter.TextChanged.Add(fun _ -> filterChanged())
        threadFilter.TextChanged.Add(fun _ -> filterChanged())
        let clearRecords = button "清除记录筛选" (fun () -> functionFilter.Text <- ""; threadFilter.Text <- "")
        calls.SelectionChanged.Add(fun _ -> showDetail())
        history.SelectionChanged.Add(fun _ -> refresh())
        let track action subject beginCapture =
            diagnosis.Clear()
            try
                let s = beginCapture()
                CaptureHistory.Add s
                updateHistory()
                timeline.SelectedIndex <- 1
                history.SelectedIndex <- sessions |> Array.findIndex ((=) s)
                message.Text <- ""
            with e ->
                message.Text <- e.Message + "。请核对目标后重试。"
                diagnosis.Show("API 监控", action, subject, e.Message)
        let architecture = function X86 -> "x86" | X64 -> "x64"
        let start = button "启动并监控" (fun () -> track "启动采集" (string exe.Text) (fun () ->
            CaptureSession.Launch(exe.Text, string args.Text, architecture (Targets.forFile exe.Text), selected)))
        let pid = input "目标进程 ID"
        let mode = ComboBox(ItemsSource = [|"启动程序"; "附加进程"|], SelectedIndex = 0)
        AutomationProperties.SetName(mode, "监控目标模式")
        let mutable previousTarget = None
        layout.AttachedToVisualTree.Add(fun _ ->
            let currentTarget = target()
            if currentTarget <> previousTarget then
                previousTarget <- currentTarget
                match currentTarget |> Option.map _.Source with
                | Some (ProcessId id) -> timeline.SelectedIndex <- 0; mode.SelectedIndex <- 1; pid.Text <- string id
                | _ -> ())
        let attach = button "附加并监控" (fun () -> track "附加采集" ("PID " + string pid.Text) (fun () ->
            let id =
                match Int32.TryParse pid.Text with
                | true, n when n > 0 -> n
                | _ -> invalidOp "请输入大于 0 的进程 ID。"
            CaptureSession.Attach(id, architecture (Targets.forProcess id), selected)))
        let stop = button "停止所选会话" (fun () ->
            match selectedSession() with
            | Some s -> s.StopAsync() |> ignore
            | _ -> ())
        let prepare () =
            let launching = mode.SelectedIndex = 0
            for c in [exe :> Control; args; start] do c.IsVisible <- launching
            for c in [pid :> Control; attach] do c.IsVisible <- not launching
            start.IsEnabled <- selected.Count > 0 && not (String.IsNullOrWhiteSpace exe.Text)
            attach.IsEnabled <- selected.Count > 0 && not (String.IsNullOrWhiteSpace pid.Text)
            stop.IsEnabled <- match selectedSession() with Some s -> not s.Completion.IsCompleted | _ -> false
        mode.SelectionChanged.Add(fun _ -> prepare())
        exe.TextChanged.Add(fun _ -> prepare())
        pid.TextChanged.Add(fun _ -> prepare())
        selectionChanged.Publish.Add(fun () -> prepare())
        prepare()
        updateHistory()
        let timer = DispatcherTimer(Interval = TimeSpan.FromMilliseconds 400.)
        timer.Tick.Add(fun _ -> updateHistory(); refresh(); prepare())
        let setup = StackPanel(Spacing = 8.)
        let inputs: Control list = [sourceName; sdk; dll; load; query; clear; results; details; add; count; chosen; remove
                                    TextBlock(Text = "选择目标"); mode; exe; args; start; pid; attach; message; diagnosis.Control]
        for control in inputs do setup.Children.Add control
        let recording = DockPanel()
        let top = StackPanel(Spacing = 4.)
        for control in [history :> Control; state; stop; matches] do top.Children.Add control
        DockPanel.SetDock(top, Dock.Top)
        recording.Children.Add top
        let body = StackPanel(Spacing = 6.)
        for control in [functionFilter :> Control; threadFilter; clearRecords; calls; detail] do body.Children.Add control
        recording.Children.Add(ScrollViewer(Content = body))
        timeline.Items.Add(TabItem(Header = "准备监控", Content = ScrollViewer(Content = setup))) |> ignore
        timeline.Items.Add(TabItem(Header = "会话记录", Content = recording)) |> ignore
        timeline.SelectedIndex <- if sessions.Length = 0 then 0 else 1
        let title = TextBlock(Text = "API 监控", FontSize = 24.)
        DockPanel.SetDock(title, Dock.Top)
        layout.Children.Add title
        layout.Children.Add timeline
        layout.AttachedToVisualTree.Add(fun _ -> timer.Start())
        layout.DetachedFromVisualTree.Add(fun _ -> timer.Stop())
        layout :> Control

type Plugin() =
    interface IDesktopPlugin with
        member _.Activate reg =
            let active () = CaptureHistory.Sessions() |> Array.filter (fun s -> not s.Completion.IsCompleted)
            let stop () : Task = task {
                let sessions = active()
                do! sessions |> Array.map (fun s -> s.StopAsync()) |> Task.WhenAll
                for s in sessions do
                    if s.Status <> "已结束" then invalidOp $"{s} · {s.Status}"
            }
            reg.Monitor { Target = fun () ->
                              match active() with
                              | [||] -> None
                              | all -> Some (all |> Array.map string |> String.concat "、")
                          Stop = stop }
            reg.Own { new IDisposable with member _.Dispose() = stop().GetAwaiter().GetResult() }
            reg.Page { Id = "api.monitor"; Title = "API 监控"; Create = View.create reg.CurrentTarget }
