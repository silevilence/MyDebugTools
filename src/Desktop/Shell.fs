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

let private targets (window: Window) (host: Host) () =
    let result = TextBlock(Text = "宿主 x64", TextWrapping = Media.TextWrapping.Wrap)
    let show source =
        try
            let target = Targets.resolve (source())
            host.Target <- Some target
            let arch = if target.Architecture = X64 then "x64" else "x86"
            result.Text <- $"目标 {arch} · {target.DotNetPath}"
        with e ->
            host.Target <- None
            result.Text <- e.Message
    let pid = TextBox(PlaceholderText = "进程 ID")
    StackPanel.create [
        StackPanel.spacing 12.
        StackPanel.children [
            TextBlock.create [ TextBlock.text "调试目标"; TextBlock.fontSize 24. ]
            button "选择目标 DLL" (fun () ->
                task {
                    let! files = window.StorageProvider.OpenFilePickerAsync(FilePickerOpenOptions(Title = "选择目标 DLL"))
                    if files.Count > 0 then show (fun () -> DllFile (files[0].TryGetLocalPath()))
                } |> ignore)
            ContentControl.create [ ContentControl.content pid ]
            button "选择目标进程" (fun () -> show (fun () -> ProcessId (Int32.Parse pid.Text)))
            ContentControl.create [ ContentControl.content result ]
        ]
    ] |> VirtualDom.create

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
    let pageArea = ContentControl(Margin = Thickness 24.)
    let manager = StackPanel(Spacing = 12.)
    let feedback = TextBlock(TextWrapping = Media.TextWrapping.Wrap)
    let mutable closeAfterChange = false
    let mutable current = "core.home"
    let dllRequests = Event<string * string option>()
    let home = StackPanel(Spacing = 8.)
    let homeMessage = TextBlock(TextWrapping = Media.TextWrapping.Wrap)
    let mutable opening = false

    let confirmStop target = task {
        let dialog = Window(Title = "停用监控插件", Width = 460., Height = 180.,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner)
        dialog.Content <- StackPanel.create [
            StackPanel.margin 16.
            StackPanel.spacing 8.
            StackPanel.children [
                TextBlock.create [ TextBlock.text $"正在采集：{target}" ]
                button "继续监控" (fun () -> dialog.Close(false))
                button "结束监控并停用" (fun () -> dialog.Close(true))
            ]
        ] |> VirtualDom.create
        return! dialog.ShowDialog<bool>(window)
    }

    let rec navigate id =
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
        for item in menu.Items |> Seq.cast<MenuItem> do
            item.FontWeight <- if unbox<string> item.Tag = id then Media.FontWeight.Bold else Media.FontWeight.Normal
        let page = host.Pages |> List.find (fun p -> p.Id = id)
        match cache.TryGetValue id with
        | true, control -> pageArea.Content <- control
        | _ ->
            let control = page.Create()
            cache.Add(id, control)
            pageArea.Content <- control

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
                                let! stop = confirmStop target
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
                with e -> feedback.Text <- e.GetBaseException().Message
            finally
                refresh()
                manager.IsEnabled <- true
                menu.IsEnabled <- true
                if closeAfterChange then window.Close()
        } |> ignore

    and addDirectory path =
        try
            catalog.AddDirectory path
            folderInput.Text <- ""
            feedback.Text <- ""
        with e -> feedback.Text <- e.Message
        refresh()

    and refresh () =
        let pages = host.Pages
        for id in cache.Keys |> Seq.toArray do
            if pages |> List.exists (fun p -> p.Id = id) |> not then cache.Remove id |> ignore
        clearMenu menu navigationHandlers
        for page in pages do
            let item = ShellMenuItem(page.Title)
            let pageId = page.Id
            item.Tag <- pageId
            navigationHandlers.Add(item.Click.Subscribe(fun _ -> navigate pageId))
            menu.Items.Add item |> ignore
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
        let view = StackPanel.create [
            StackPanel.spacing 12.
            StackPanel.children [
                StackPanel.create [
                    StackPanel.orientation Orientation.Horizontal
                    StackPanel.spacing 8.
                    StackPanel.children [
                        button "添加目录" (fun () -> addDirectory folderInput.Text)
                        button "浏览目录…" (fun () ->
                            task {
                                let! folders = window.StorageProvider.OpenFolderPickerAsync(FolderPickerOpenOptions(Title = "选择插件编译目录"))
                                if folders.Count > 0 then addDirectory (folders[0].TryGetLocalPath())
                            } |> ignore)
                        button "检查卸载" (fun () ->
                            let count, pending = catalog.CheckUnloads()
                            let waiting = String.concat "、" pending
                            let status = if waiting = "" then "无" else waiting
                            feedback.Text <- $"已回收 {count} 个旧版本。仍待回收：{status}")
                    ]
                ]
                for plugin in host.Plugins do
                    let enabled = host.Enabled plugin.Id
                    let state = if plugin.Core then "核心 · 固定加载" elif enabled then "普通 · 已启用" else "普通 · 已停用"
                    StackPanel.create [
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.spacing 16.
                        StackPanel.children [
                            TextBlock.create [ TextBlock.verticalAlignment VerticalAlignment.Center; TextBlock.text (if plugin.Id.StartsWith("sample.") then $"{plugin.Title}（示例）　{state}" else $"{plugin.Title}　{state}") ]
                            if not plugin.Core then
                                let verb = if enabled then "停用" else "启用"
                                button $"{verb} {plugin.Title}" (fun () -> change plugin.Id false)
                                if enabled && (catalog.Directory plugin.Id).IsSome then
                                    button $"重新加载 {plugin.Title}" (fun () -> change plugin.Id true)
                        ]
                    ]
                    for reason in host.Mismatches plugin.Id do
                        TextBlock.create [ TextBlock.text reason; TextBlock.textWrapping Media.TextWrapping.Wrap ]
            ]
        ]
        manager.Children.Add(TextBlock(Text = "插件管理", FontSize = 24.))
        manager.Children.Add folderInput
        manager.Children.Add(VirtualDom.create view)
        manager.Children.Add feedback
        navigate (if pages |> List.exists (fun p -> p.Id = current) then current else "core.manager")

    do
        let corePage (page: Page) =
            host.Add {
                Id = page.Id; Title = page.Title; Core = true
                Create = fun () -> { new IDesktopPlugin with member _.Activate reg = reg.Page page }
            }
        pageArea.Focusable <- true
        corePage { Id = "core.home"; Title = "首页"; Create = fun () -> ScrollViewer(Content = home) }
        corePage { Id = "core.manager"; Title = "插件管理"; Create = fun () -> ScrollViewer(Content = manager) }
        corePage { Id = "core.targets"; Title = "调试目标"; Create = targets window host }
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
        layout.Children.Add pageArea
        window.Content <- layout
        window.Closing.Add(fun e ->
            if not manager.IsEnabled then
                e.Cancel <- true
                closeAfterChange <- true)
        window.Closed.Add(fun _ ->
            pageArea.Content <- null
            cache.Clear()
            clearMenu menu navigationHandlers
            clearMenu actionsMenu actionHandlers
            (host :> IDisposable).Dispose()
            catalog.CheckUnloads() |> ignore)
        refresh()
