module MyDebugTools.Desktop.Shell

open System
open System.Collections.Generic
open System.IO
open Avalonia
open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.VirtualDom
open Avalonia.Layout
open Avalonia.Platform.Storage
open MyDebugTools.Contracts
open MyDebugTools.Host
open MyDebugTools.Samples

let private button (label: string) (run: unit -> unit) =
    Button.create [ Button.content label; Button.onClick (fun _ -> run()) ]

let private targets (window: Window) () =
    let result = TextBlock(Text = "宿主 x64", TextWrapping = Media.TextWrapping.Wrap)
    let show resolve =
        try result.Text <- resolve() |> Targets.environment
        with e -> result.Text <- e.Message
    let pid = TextBox(PlaceholderText = "进程 ID")
    StackPanel.create [
        StackPanel.spacing 12.
        StackPanel.children [
            TextBlock.create [ TextBlock.text "调试目标"; TextBlock.fontSize 24. ]
            button "选择目标 DLL" (fun () ->
                task {
                    let! files = window.StorageProvider.OpenFilePickerAsync(FilePickerOpenOptions(Title = "选择目标 DLL"))
                    if files.Count > 0 then show (fun () -> Targets.forFile (files[0].TryGetLocalPath()))
                } |> ignore)
            ContentControl.create [ ContentControl.content pid ]
            button "选择目标进程" (fun () -> show (fun () -> Targets.forProcess (Int32.Parse pid.Text)))
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
    let actionResult = TextBlock(Margin = Thickness(24., 0., 24., 8.))
    let pageArea = ContentControl(Margin = Thickness 24.)
    let manager = StackPanel(Spacing = 12.)
    let feedback = TextBlock(TextWrapping = Media.TextWrapping.Wrap)
    let mutable current = "core.manager"

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
        actionsMenu.Items.Clear()
        actionResult.Text <- ""
        let actions = host.Actions id
        actionsMenu.IsVisible <- not actions.IsEmpty
        if not actions.IsEmpty then
            let group = MenuItem(Header = "页面操作")
            for title, invoke in actions do
                let item = MenuItem(Header = title)
                item.Click.Add(fun _ ->
                    try actionResult.Text <- invoke()
                    with e -> actionResult.Text <- e.Message)
                group.Items.Add item |> ignore
            actionsMenu.Items.Add group |> ignore
        let page = host.Pages |> List.find (fun p -> p.Id = id)
        match cache.TryGetValue id with
        | true, control -> pageArea.Content <- control
        | _ ->
            let control = page.Create()
            cache.Add(id, control)
            pageArea.Content <- control

    and change id reload =
        task {
            manager.IsEnabled <- false
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
                        if enabled then
                            host.Disable id
                            refresh() // Drop all old page instances before reopening the same IDs.
                        if reload || not enabled then host.Enable id
                        feedback.Text <- ""
                with e -> feedback.Text <- e.GetBaseException().Message
            finally
                refresh()
                manager.IsEnabled <- true
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
        menu.Items.Clear()
        for page in pages do
            let item = MenuItem(Header = page.Title)
            item.Click.Add(fun _ -> navigate page.Id)
            menu.Items.Add item |> ignore
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
                            TextBlock.create [ TextBlock.verticalAlignment VerticalAlignment.Center; TextBlock.text $"{plugin.Title}　{state}" ]
                            if not plugin.Core then
                                let verb = if enabled then "停用" else "启用"
                                button $"{verb} {plugin.Title}" (fun () -> change plugin.Id false)
                                if enabled && (catalog.Directory plugin.Id).IsSome then
                                    button $"重新加载 {plugin.Title}" (fun () -> change plugin.Id true)
                        ]
                    ]
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
        corePage { Id = "core.manager"; Title = "插件管理"; Create = fun () -> ScrollViewer(Content = manager) }
        corePage { Id = "core.targets"; Title = "调试目标"; Create = targets window }
        host.Add { Id = "sample.workbench"; Title = "工作台"; Core = false; Create = fun () -> Workbench() }
        host.Add { Id = "sample.actions"; Title = "工作台扩展"; Core = false; Create = fun () -> WorkbenchActions() }
        try feedback.Text <- catalog.Restore() |> String.concat Environment.NewLine
        with e -> feedback.Text <- e.Message
        let layout = DockPanel()
        DockPanel.SetDock(menu, Dock.Top)
        layout.Children.Add menu
        DockPanel.SetDock(actionsMenu, Dock.Top)
        layout.Children.Add actionsMenu
        DockPanel.SetDock(actionResult, Dock.Bottom)
        layout.Children.Add actionResult
        layout.Children.Add pageArea
        window.Content <- layout
        window.Closed.Add(fun _ ->
            pageArea.Content <- null
            cache.Clear()
            menu.Items.Clear()
            actionsMenu.Items.Clear()
            (host :> IDisposable).Dispose()
            catalog.CheckUnloads() |> ignore)
        refresh()
