module MyDebugTools.Desktop.Shell

open System
open System.Collections.Generic
open System.IO
open Avalonia
open Avalonia.Automation
open Avalonia.Automation.Peers
open Avalonia.Automation.Provider
open Avalonia.Controls
open Avalonia.Interactivity
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.VirtualDom
open Avalonia.Layout
open Avalonia.Platform.Storage
open MyDebugTools.Contracts
open MyDebugTools.Host
open MyDebugTools.Samples

// Avalonia's stock menu peer exposes toggles only. Expose our navigation and
// submenu operations to keyboard-independent Windows automation clients.
type private SubmenuPeer(item: MenuItem) =
    inherit MenuItemAutomationPeer(item)
    interface IExpandCollapseProvider with
        member _.Expand() = item.Open()
        member _.Collapse() = item.Close()
        member _.ShowsMenu = true
        member _.ExpandCollapseState =
            if item.IsSubMenuOpen then ExpandCollapseState.Expanded else ExpandCollapseState.Collapsed

type private ActionPeer(item: MenuItem) =
    inherit MenuItemAutomationPeer(item)
    interface IInvokeProvider with
        member _.Invoke() =
            if item.IsEnabled then
                item.RaiseEvent(RoutedEventArgs(MenuItem.ClickEvent))
                match item.Parent with
                | :? MenuItem as parent -> parent.Close()
                | _ -> ()

type private ShellMenuItem(title: string) =
    inherit MenuItem(Header = title)
    override _.StyleKeyOverride = typeof<MenuItem>
    override item.OnCreateAutomationPeer() =
        if item.HasSubMenu then SubmenuPeer(item) else ActionPeer(item)

let private button (label: string) (run: unit -> unit) =
    Button.create [ Button.content label; Button.onClick (fun _ -> run()) ]

let private targets (window: Window) (host: Host) openTask () =
    let active = TextBlock(Text = "当前目标 · 尚未选择", TextWrapping = Media.TextWrapping.Wrap)
    let feedback = TextBlock(TextWrapping = Media.TextWrapping.Wrap)
    let diagnostics = DiagnosticPanel()
    let pid = TextBox(PlaceholderText = "进程 ID")
    AutomationProperties.SetName(pid, "进程 ID")
    let file = TextBox(PlaceholderText = "DLL 文件路径")
    AutomationProperties.SetName(file, "目标 DLL 路径")
    let next = Button(IsVisible = false)
    let show action subject source =
        try
            let target, runtime = Targets.inspect (source())
            let name =
                match target.Source with
                | DllFile path -> Path.GetFileName path
                | ProcessId id ->
                    use proc = Diagnostics.Process.GetProcessById id
                    $"{proc.ProcessName} · PID {id}"
            host.Target <- Some target
            active.Text <- $"当前目标 · {name} · 目标 {target.Architecture} · 宿主 {Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}"
            feedback.Text <- if runtime then "所需运行时已安装。" else $"执行需要 .NET 10 {target.Architecture} 运行时。可以继续静态浏览。"
            diagnostics.Clear()
            next.Content <- match target.Source with DllFile _ -> "在 DLL 工作台打开" | ProcessId _ -> "进入监控准备"
            next.IsVisible <- true
        with e ->
            feedback.Text <-
                match e with
                | :? IO.FileNotFoundException | :? IO.DirectoryNotFoundException -> "文件不存在。请检查路径或重新选择 DLL。"
                | :? ComponentModel.Win32Exception as err when err.NativeErrorCode = 87 -> "进程已退出或不存在。请重新选择进程。"
                | :? BadImageFormatException -> "文件不是可识别的 DLL。请重新选择。"
                | _ -> e.Message
            diagnostics.Show("调试目标", action, subject, feedback.Text + Environment.NewLine + e.Message)
            if action = "选择进程" then pid.Focus() |> ignore
    let openFile path =
        show "选择文件" (string path) (fun () ->
            if String.IsNullOrWhiteSpace path then invalidOp "请填写 DLL 文件路径。"
            DllFile(Path.GetFullPath path))
    next.Click.Add(fun _ ->
        match host.Target |> Option.map _.Source with
        | Some (DllFile path) -> openTask ["dll.debugger"] "dll.debugger" "导出" (Some path)
        | Some (ProcessId _) -> openTask ["api.monitor"] "api.monitor" "" None
        | None -> ())
    let panel = StackPanel(Spacing = 8.)
    let addButton label run = panel.Children.Add(button label run |> VirtualDom.create)
    panel.Children.Add(TextBlock(Text = "调试目标", FontSize = 24.))
    panel.Children.Add active
    panel.Children.Add next
    panel.Children.Add(TextBlock(Text = "DLL 文件路径"))
    panel.Children.Add file
    addButton "选择目标 DLL" (fun () -> openFile file.Text)
    addButton "浏览 DLL…" (fun () ->
        task {
            let! files = window.StorageProvider.OpenFilePickerAsync(FilePickerOpenOptions(Title = "选择目标 DLL"))
            if files.Count > 0 then
                file.Text <- files[0].TryGetLocalPath()
                openFile file.Text
        } |> ignore)
    panel.Children.Add(TextBlock(Text = "进程 ID"))
    panel.Children.Add pid
    let selectProcess () =
        show "选择进程" ("PID " + string pid.Text) (fun () ->
            if String.IsNullOrWhiteSpace pid.Text then invalidOp "请输入大于 0 的进程 ID。"
            match Int32.TryParse pid.Text with
            | true, id when id > 0 -> ProcessId id
            | _ -> invalidOp "进程 ID 须为大于 0 的整数。请修改后重试。")
    addButton "选择目标进程" selectProcess
    pid.KeyDown.Add(fun e ->
        if e.Key = Input.Key.Enter then selectProcess(); e.Handled <- true)
    panel.Children.Add feedback
    panel.Children.Add diagnostics.Control
    ScrollViewer(Content = panel) :> Control

type MainWindow() as window =
    inherit Window(Title = "MyDebugTools", Width = 860., Height = 580., MinWidth = 580., MinHeight = 400.)
    let host = new Host()
    let catalog = LocalPlugins(host, Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "MyDebugTools", "plugins.json"))
    let folderInput = TextBox(PlaceholderText = "本地插件编译目录")
    let cache = Dictionary<string, Control>()
    let menu = Menu()
    let actionsMenu = Menu()
    let navigationHandlers = ResizeArray<IDisposable>()
    let actionHandlers = ResizeArray<IDisposable>()
    let clearMenu (menu: Menu) (handlers: ResizeArray<IDisposable>) =
        for handler in handlers do handler.Dispose()
        handlers.Clear()
        menu.Items.Clear()
    let actionResult = TextBlock(Margin = Thickness(24., 0., 24., 8.))
    let workspace = Workspace(window)
    let pageArea = workspace.Control
    let views = ShellMenuItem("视图")
    let toolMenu = ShellMenuItem("工具")
    let manager = StackPanel(Spacing = 12.)
    let feedback = TextBlock(TextWrapping = Media.TextWrapping.Wrap)
    let managerDiagnostic = DiagnosticPanel()
    let filter = TextBox(PlaceholderText = "按插件名称查找")
    let flat = CheckBox(Content = "全部插件（平表）")
    let rows = StackPanel(Spacing = 4.)
    let mutable closeAfterChange = false
    let mutable current = "core.home"
    let dllRequests = Event<string * string option>()
    let home = StackPanel(Spacing = 8.)
    let homeMessage = TextBlock(TextWrapping = Media.TextWrapping.Wrap)
    let mutable opening = false

    let confirmStop target verb = task {
        let dialog = Window(Title = verb + "插件", Width = 460., Height = 180.,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner)
        dialog.Content <- StackPanel.create [
            StackPanel.margin 16.
            StackPanel.spacing 8.
            StackPanel.children [
                TextBlock.create [ TextBlock.text $"正在采集：{target}" ]
                button "继续监控" (fun () -> dialog.Close(false))
                button ("结束采集并" + verb) (fun () -> dialog.Close(true))
            ]
        ] |> VirtualDom.create
        return! dialog.ShowDialog<bool>(window)
    }

    let updateNavigation id =
        current <- id
        clearMenu actionsMenu actionHandlers
        actionResult.Text <- ""
        let actions = host.Actions id
        actionsMenu.IsVisible <- not actions.IsEmpty
        if not actions.IsEmpty then
            let group = ShellMenuItem("页面操作")
            for title, invoke in actions do
                let item = ShellMenuItem(title)
                actionHandlers.Add(item.Click.Subscribe(fun _ ->
                    try actionResult.Text <- invoke()
                    with e -> actionResult.Text <- e.Message))
                group.Items.Add item |> ignore
            actionsMenu.Items.Add group |> ignore
        for item in Seq.append (menu.Items |> Seq.cast<MenuItem>) (toolMenu.Items |> Seq.cast<MenuItem>) do
            item.FontWeight <- if unbox<string> item.Tag = id then Media.FontWeight.Bold else Media.FontWeight.Normal

    let rec navigate id =
        updateNavigation id
        let page = host.Pages |> List.find (fun p -> p.Id = id)
        match cache.TryGetValue id with
        | true, control -> workspace.Show(id, page.Title, control)
        | _ ->
            let control = page.Create()
            cache.Add(id, control)
            workspace.Show(id, page.Title, control)

    and openTask plugins destination tab file =
        if not opening then
            opening <- true
            home.IsEnabled <- false
            task {
                try
                    try
                        for id in plugins do
                            if not (host.Enabled id) then do! catalog.EnableAsync(id, ignore)
                        current <- destination
                        refresh()
                        if destination = "dll.debugger" then dllRequests.Trigger(tab, file)
                        pageArea.Focus() |> ignore
                    with e -> homeMessage.Text <- $"打开失败。请到插件管理检查所需插件。{e.GetBaseException().Message}"
                finally
                    opening <- false
                    home.IsEnabled <- true
            } |> ignore

    and change id reload =
        task {
            manager.IsEnabled <- false
            menu.IsEnabled <- false
            try
                try
                    let enabled = host.Enabled id
                    let! proceed = task {
                        match host.Monitor id with
                        | Some session ->
                            match session.Target() with
                            | Some target ->
                                let! stop = confirmStop target (if reload then "重新加载" else "停用")
                                if stop then do! session.Stop()
                                return stop
                            | None -> return true
                        | None -> return true
                    }
                    if proceed then
                        let detach () =
                            if enabled then
                                host.Disable id
                                refresh() // Drop all old page instances before reopening the same IDs.
                        if reload || not enabled then do! catalog.EnableAsync(id, detach)
                        else detach()
                        feedback.Text <- ""
                with e ->
                    feedback.Text <- e.GetBaseException().Message
                    managerDiagnostic.Show("插件管理", (if reload then "重新加载" else "更改启用状态"), id, feedback.Text)
            finally
                refresh()
                manager.IsEnabled <- true
                menu.IsEnabled <- true
                if closeAfterChange then window.Close()
        } |> ignore

    and addDirectory path =
        try
            catalog.AddDirectory path
            filter.Text <- host.Plugins |> List.find (fun p -> catalog.Directory p.Id = Some(Path.GetFullPath path)) |> _.Title
            managerDiagnostic.Clear()
            folderInput.Text <- ""
            feedback.Text <- ""
        with e ->
            feedback.Text <- $"无法关联目录。请检查路径和 plugin.json。{e.Message}"
            managerDiagnostic.Show("插件管理", "关联开发目录", string path, e.Message)
        refresh()

    and refresh () =
        let pages = host.Pages
        for id in cache.Keys |> Seq.toArray do
            if pages |> List.exists (fun p -> p.Id = id) |> not then
                workspace.Remove id
                cache.Remove id |> ignore
        clearMenu menu navigationHandlers
        toolMenu.Items.Clear()
        let compact = window.ClientSize.Width < 760. || pages.Length > 7
        for page in pages do
            let item = ShellMenuItem(page.Title)
            let pageId = page.Id
            item.Tag <- pageId
            navigationHandlers.Add(item.Click.Subscribe(fun _ -> navigate pageId))
            if compact && not (page.Id.StartsWith("core.")) then toolMenu.Items.Add item |> ignore
            else menu.Items.Add item |> ignore
        if toolMenu.Items.Count > 0 then menu.Items.Add toolMenu |> ignore
        views.Items.Clear()
        for page in pages do
            let item = ShellMenuItem(page.Title, ToggleType = MenuItemToggleType.CheckBox, IsChecked = workspace.IsOpen page.Id)
            navigationHandlers.Add(item.Click.Subscribe(fun _ ->
                if workspace.IsOpen page.Id then workspace.Move(page.Id, "隐藏")
                else navigate page.Id))
            views.Items.Add item |> ignore
        for title, action in ["向左停靠","Left"; "向右停靠","Right"; "向上停靠","Top"; "向下停靠","Bottom"; "合并标签","合并标签"; "浮动","浮动"; "隐藏","隐藏"] do
            let item = ShellMenuItem(title)
            navigationHandlers.Add(item.Click.Subscribe(fun _ -> workspace.Move(current, action)))
            views.Items.Add item |> ignore
        let reset = ShellMenuItem("恢复默认布局")
        navigationHandlers.Add(reset.Click.Subscribe(fun _ -> workspace.Reset()))
        views.Items.Add reset |> ignore
        menu.Items.Add views |> ignore
        home.Children.Clear()
        home.Children.Add(TextBlock(Text = "开始调试", FontSize = 24.))
        let group title description entries =
            home.Children.Add(TextBlock(Text = title, FontSize = 18.))
            home.Children.Add(TextBlock(Text = description, TextWrapping = Media.TextWrapping.Wrap))
            let commands = WrapPanel()
            for label, plugins, page, tab in entries do
                let missing = plugins |> List.filter (host.Enabled >> not)
                let caption = if missing.IsEmpty then $"打开{label}" else $"启用并打开{label}"
                let b = Button(Content = caption, Margin = Thickness(0., 0., 8., 0.))
                if label <> "依赖" then b.Classes.Add("accent")
                b.Click.Add(fun _ -> openTask plugins page tab None)
                commands.Children.Add b
            home.Children.Add commands
        group "查看 DLL 导出或依赖" "用 DLL 工作台浏览文件和函数。依赖页签显示引用关系。" [
            "导出", ["dll.debugger"], "dll.debugger", "导出"
            "依赖", ["dll.debugger"; "dll.dependencies"], "dll.debugger", "依赖" ]
        group "监控程序调用" "用 API 监控选择函数、观察调用记录。" [
            "监控", ["api.monitor"], "api.monitor", "" ]
        group "发送 HTTP 请求" "用 Web API 编辑 URL、发送请求并查看响应。" [
            "请求", ["web.api"], "web.api", "" ]
        home.Children.Add homeMessage
        manager.Children.Clear()
        let addButton (parent: Panel) title run =
            parent.Children.Add(button title run |> VirtualDom.create)
        manager.Children.Add(TextBlock(Text = "插件管理", FontSize = 24.))
        manager.Children.Add(TextBlock(Text = "本地插件编译目录"))
        manager.Children.Add folderInput
        let tools = WrapPanel()
        addButton tools "添加目录" (fun () -> addDirectory folderInput.Text)
        addButton tools "关联开发目录…" (fun () ->
            task {
                let! folders = window.StorageProvider.OpenFolderPickerAsync(FolderPickerOpenOptions(Title = "选择插件编译目录"))
                if folders.Count > 0 then addDirectory (folders[0].TryGetLocalPath())
            } |> ignore)
        addButton tools "加载插件包…" (fun () ->
            task {
                try
                    let! files = window.StorageProvider.OpenFilePickerAsync(FilePickerOpenOptions(Title = "选择本地插件 ZIP"))
                    if files.Count > 0 then
                        let! preview = System.Threading.Tasks.Task.Run(fun () -> PluginPackage.preview (files[0].TryGetLocalPath()))
                        let m = preview.Manifest
                        let version = if String.IsNullOrWhiteSpace m.Version then "未知" else m.Version
                        let existing = host.Plugins |> List.tryFind (fun p -> p.Id = m.Id)
                        let prior = existing |> Option.map (fun p -> p.Title + " · " + catalog.Source p.Id) |> Option.defaultValue "无"
                        match catalog.Package m.Id with
                        | Some old when old.Hash = preview.Hash ->
                            filter.Text <- m.Id
                            feedback.Text <- "此包已安装。"
                        | _ ->
                            let dialog = Window(Title = "插件包预览", Width = 460., SizeToContent = SizeToContent.Height,
                                                WindowStartupLocation = WindowStartupLocation.CenterOwner)
                            let content = StackPanel(Spacing = 8., Margin = Thickness 16.)
                            content.Children.Add(TextBlock(Text = $"{m.Title} · {m.Id} · 版本 {version}\n{preview.Source}\n已有同 ID 插件 · {prior}", TextWrapping = Media.TextWrapping.Wrap))
                            addButton content "取消" (fun () -> dialog.Close(false))
                            addButton content (if (catalog.Package m.Id).IsSome then "替换并启用" else "加载并启用") (fun () -> dialog.Close(true))
                            dialog.Content <- content
                            let! ok = dialog.ShowDialog<bool>(window)
                            if ok then
                                let! proceed = task {
                                    match host.Monitor m.Id with
                                    | Some session when (session.Target()).IsSome ->
                                        let! ok = confirmStop (session.Target() |> Option.defaultValue "") "替换"
                                        if ok then do! session.Stop()
                                        return ok
                                    | _ -> return true
                                }
                                if proceed then
                                    try
                                        if (catalog.Package m.Id).IsSome then catalog.Replace(preview, refresh)
                                        else catalog.Install preview
                                        filter.Text <- m.Id
                                        feedback.Text <- $"已启用 · {m.Title}"
                                    finally refresh()
                with e ->
                    feedback.Text <- $"加载插件包失败。请检查 ZIP 和入口程序集。{e.GetBaseException().Message}"
                    managerDiagnostic.Show("插件管理", "加载插件包", "本地 ZIP", e.GetBaseException().Message)
            } |> ignore)
        manager.Children.Add tools
        manager.Children.Add feedback
        manager.Children.Add managerDiagnostic.Control
        let checks = StackPanel(Spacing = 4.)
        addButton checks "检查卸载" (fun () ->
            let count, pending = catalog.CheckUnloads()
            let waiting = if pending.IsEmpty then "无" else String.concat "、" pending
            feedback.Text <- $"已回收 {count} 个旧版本。仍待回收：{waiting}"
            managerDiagnostic.Show("插件管理", "检查卸载", "旧插件程序集", feedback.Text))
        manager.Children.Add(Expander(Header = "卸载诊断", Content = checks))
        manager.Children.Add(TextBlock(Text = "查找插件"))
        manager.Children.Add filter
        manager.Children.Add flat
        manager.Children.Add rows
        renderRows()
        let destination = current
        for page in pages do
            if not (cache.ContainsKey page.Id) && workspace.NeedsMount page.Id then navigate page.Id
        current <- destination
        navigate (if pages |> List.exists (fun p -> p.Id = current) then current else "core.manager")

    and renderRows () =
        rows.Children.Clear()
        let wide = window.ClientSize.Width >= 760.
        let columns = if wide then "2*,*,*,*,2*" else "2*,*,2*"
        let field (grid: Grid) col (control: Control) =
            Grid.SetColumn(control, col)
            grid.Children.Add control
        let grid () = Grid(ColumnDefinitions = ColumnDefinitions(columns), ColumnSpacing = 8.)
        let heading = grid()
        let titles = if wide then ["名称 / 用途"; "来源"; "版本"; "状态"; "操作"] else ["名称"; "状态"; "操作"]
        titles |> List.iteri (fun i s -> field heading i (TextBlock(Text = s)))
        rows.Children.Add heading
        let query = string filter.Text
        let plugins = host.Plugins
        let relations (p: Plugin) =
            catalog.Relations p.Id |> Option.orElseWith (fun () ->
                match p.Id with
                | "sample.workbench" | "dll.debugger" -> Some ([p.Id], [])
                | "sample.actions" -> Some ([], ["sample.workbench", "选中项与摘要（菜单）"])
                | _ -> None)
        let matches (p: Plugin) =
            p.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
        rows.Children.Add(TextBlock(Text = $"共 {plugins.Length} 个插件"))
        for plugin in plugins do
            let info = relations plugin
            let pages, targets = defaultArg info ([], [])
            let children = [
                for page in pages do
                    for child in plugins do
                        for target, title in relations child |> Option.map snd |> Option.defaultValue [] do
                            if target = page then yield page, child, title ]
            if matches plugin || (flat.IsChecked <> Nullable true && children |> List.exists (fun (_, c, _) -> matches c)) then
                let row = grid()
                let enabled = host.Enabled plugin.Id
                let source = if plugin.Core then "随附核心" else catalog.Source plugin.Id
                let state = if plugin.Core then "固定加载" elif enabled then "已启用" else "已停用"
                let name = if plugin.Id.StartsWith("sample.") then plugin.Title + "（示例）" else plugin.Title
                let path = catalog.Directory plugin.Id |> Option.defaultValue "宿主内置"
                let version = catalog.Version plugin.Id
                let details = TextBox(IsReadOnly = true, TextWrapping = Media.TextWrapping.Wrap,
                                      Text = $"{plugin.Id} · {source} · 版本 {version}\n{path}")
                let detailPanel = StackPanel(Spacing = 4.)
                detailPanel.Children.Add details
                if info.IsNone then detailPanel.Children.Add(TextBlock(Text = "关系待识别"))
                for page in pages do detailPanel.Children.Add(TextBlock(Text = $"提供界面 · {page}"))
                for target, title in targets do detailPanel.Children.Add(TextBlock(Text = $"{title} → {target}", TextWrapping = Media.TextWrapping.Wrap))
                if flat.IsChecked <> Nullable true then
                    for page, child, title in children do
                        addAction detailPanel $"{page} → {child.Title} · {title}" (fun () -> flat.IsChecked <- Nullable true; filter.Text <- child.Id)
                let expanded = query <> "" && not (matches plugin)
                field row 0 (Expander(Header = name, Content = detailPanel, IsExpanded = expanded))
                if wide then
                    field row 1 (TextBlock(Text = source))
                    field row 2 (TextBlock(Text = version))
                field row (if wide then 3 else 1) (TextBlock(Text = state))
                let commands = WrapPanel()
                if not plugin.Core then
                    let verb = if enabled then "停用" else "启用"
                    addAction commands $"{verb} {plugin.Title}" (fun () -> change plugin.Id false)
                    if enabled && (catalog.Directory plugin.Id).IsSome then
                        addAction commands $"重新加载 {plugin.Title}" (fun () -> change plugin.Id true)
                if source.StartsWith("外部") then
                    let package = catalog.Package plugin.Id
                    let verb =
                        match package with
                        | Some item when item.PendingRemoval -> "重试卸载插件"
                        | Some _ -> "卸载插件"
                        | None -> "解除目录关联"
                    addAction commands (verb + "…") (fun () ->
                        task {
                            try
                                let dialog = Window(Title = verb, Width = 460., SizeToContent = SizeToContent.Height,
                                                    WindowStartupLocation = WindowStartupLocation.CenterOwner)
                                let body = StackPanel(Spacing = 8., Margin = Thickness 16.)
                                let scope = if package.IsSome then "清理受管副本" else "保留编译目录"
                                let affected = List.append pages (List.map snd targets) |> String.concat "、"
                                body.Children.Add(TextBlock(Text = $"{plugin.Title} · {plugin.Id}\n界面与扩展 · {affected}\n{scope} · {path}", TextWrapping = Media.TextWrapping.Wrap))
                                addAction body "取消" (fun () -> dialog.Close(false))
                                addAction body ("确认" + verb) (fun () -> dialog.Close(true))
                                dialog.Content <- body
                                let! ok = dialog.ShowDialog<bool>(window)
                                if ok then
                                    let! proceed = task {
                                        match host.Monitor plugin.Id with
                                        | Some session when (session.Target()).IsSome ->
                                            let! stop = confirmStop (session.Target() |> Option.defaultValue "") verb
                                            if stop then do! session.Stop()
                                            return stop
                                        | _ -> return true
                                    }
                                    if proceed then
                                        catalog.RemoveExternal(plugin.Id, refresh)
                                        for page in pages do workspace.Forget page
                                        filter.Text <- ""
                                        feedback.Text <- $"已{verb} · {plugin.Title}"
                            with e ->
                                feedback.Text <- $"{verb}未完成 · {path} · {e.Message}。处理后可重试。"
                                managerDiagnostic.Show("插件管理", verb, plugin.Id, e.Message)
                            refresh()
                        } |> ignore)
                field row (if wide then 4 else 2) commands
                rows.Children.Add row
                for reason in host.Mismatches plugin.Id do rows.Children.Add(TextBlock(Text = reason, TextWrapping = Media.TextWrapping.Wrap))

    and addAction (panel: Panel) label run =
        panel.Children.Add(button label run |> VirtualDom.create)

    do
        let corePage (page: Page) =
            host.Add {
                Id = page.Id; Title = page.Title; Core = true
                Create = fun () -> { new IDesktopPlugin with member _.Activate reg = reg.Page page }
            }
        workspace.Focused.Add(fun id ->
            if current <> id && (host.Pages |> List.exists (fun p -> p.Id = id)) then updateNavigation id)
        views.SubmenuOpened.Add(fun _ ->
            for page, item in Seq.zip host.Pages (views.Items |> Seq.cast<MenuItem>) do item.IsChecked <- workspace.IsOpen page.Id)
        pageArea.Focusable <- true
        corePage { Id = "core.home"; Title = "首页"; Create = fun () -> ScrollViewer(Content = home) }
        corePage { Id = "core.manager"; Title = "插件管理"; Create = fun () -> ScrollViewer(Content = manager) }
        corePage { Id = "core.targets"; Title = "调试目标"; Create = targets window host openTask }
        host.Add { Id = "dll.debugger"; Title = "DLL 工作台"; Core = false; Create = fun () -> DllWorkbench.plugin window host dllRequests.Publish }
        host.Add { Id = "sample.workbench"; Title = "工作台"; Core = false; Create = fun () -> Workbench() }
        host.Add { Id = "sample.actions"; Title = "工作台扩展"; Core = false; Create = fun () -> WorkbenchActions() }
        let startupMessages = [
            try catalog.AddBundled(Path.Combine(AppContext.BaseDirectory, "plugins", "DllDependencies"))
            with e -> yield $"DLL 依赖：{e.Message}"
            try catalog.AddBundled(Path.Combine(AppContext.BaseDirectory, "plugins", "ApiMonitor"))
            with e -> yield $"API 监控：{e.Message}"
            try catalog.AddBundled(Path.Combine(AppContext.BaseDirectory, "plugins", "WebApi"))
            with e -> yield $"Web API：{e.Message}"
            try yield! catalog.Restore()
            with e -> yield e.Message
        ]
        feedback.Text <- String.concat Environment.NewLine startupMessages
        let layout = DockPanel()
        DockPanel.SetDock(menu, Dock.Top)
        layout.Children.Add menu
        DockPanel.SetDock(actionsMenu, Dock.Top)
        layout.Children.Add actionsMenu
        DockPanel.SetDock(actionResult, Dock.Bottom)
        layout.Children.Add actionResult
        let capture = Button(IsVisible = false)
        capture.Click.Add(fun _ -> navigate "api.monitor")
        let monitorState = Avalonia.Threading.DispatcherTimer(Interval = TimeSpan.FromSeconds 1.)
        monitorState.Tick.Add(fun _ ->
            let active = host.Monitor "api.monitor" |> Option.bind (fun session -> session.Target())
            capture.IsVisible <- active.IsSome
            active |> Option.iter (fun s -> capture.Content <- $"查看采集 · {s}"))
        monitorState.Start()
        DockPanel.SetDock(capture, Dock.Bottom)
        layout.Children.Add capture
        layout.Children.Add pageArea
        window.Content <- layout
        window.KeyDown.Add(fun e ->
            if e.Key = Input.Key.Home && e.KeyModifiers = Input.KeyModifiers.Alt then
                navigate "core.home"
                home.Children |> Seq.tryPick (function :? WrapPanel as p when p.Children.Count > 0 -> Some p.Children[0] | _ -> None)
                |> Option.iter (fun c -> c.Focus() |> ignore)
                e.Handled <- true)
        window.Closing.Add(fun e ->
            if not manager.IsEnabled then
                e.Cancel <- true
                closeAfterChange <- true
            elif not e.Cancel then
                try workspace.Save([|for p in host.Plugins do if not p.Core && host.Enabled p.Id then yield p.Id|], current)
                with ex ->
                    feedback.Text <- $"布局未能保存 · {ex.Message}"
                    eprintfn "%s" feedback.Text)
        window.Closed.Add(fun _ ->
            monitorState.Stop()
            for id in cache.Keys do workspace.Remove id
            cache.Clear()
            clearMenu menu navigationHandlers
            clearMenu actionsMenu actionHandlers
            (host :> IDisposable).Dispose()
            catalog.CheckUnloads() |> ignore)
        AutomationProperties.SetName(folderInput, "本地插件编译目录")
        AutomationProperties.SetName(filter, "查找插件")
        flat.IsCheckedChanged.Add(fun _ -> renderRows())
        filter.TextChanged.Add(fun _ -> renderRows())
        window.SizeChanged.Add(fun e ->
            if (e.PreviousSize.Width >= 760.) <> (e.NewSize.Width >= 760.) then refresh())
        try
            let enable id =
                if host.Plugins |> List.exists (fun p -> p.Id = id) then
                    try host.Enable id with e -> feedback.Text <- e.Message
            let lookup id =
                host.Pages |> List.tryFind (fun p -> p.Id = id) |> Option.map (fun page ->
                    let control = page.Create()
                    cache[id] <- control
                    page.Title, control)
            current <- workspace.Restore(enable, lookup) |> Option.defaultValue current
        with e -> feedback.Text <- $"布局无法恢复，已使用默认工作区。{e.Message}"
        refresh()
