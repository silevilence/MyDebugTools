module MyDebugTools.Desktop.Shell

open System
open System.Collections.Generic
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
    let cache = Dictionary<string, Control>()
    let menu = Menu()
    let actionsMenu = Menu()
    let actionResult = TextBlock(Margin = Thickness(24., 0., 24., 8.))
    let pageArea = ContentControl(Margin = Thickness 24.)
    let manager = StackPanel(Spacing = 12.)
    let feedback = TextBlock(TextWrapping = Media.TextWrapping.Wrap)
    let mutable current = "core.manager"

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
                TextBlock.create [ TextBlock.text "插件管理"; TextBlock.fontSize 24. ]
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
                                button $"{verb} {plugin.Title}" (fun () ->
                                    try
                                        if enabled then host.Disable plugin.Id else host.Enable plugin.Id
                                        feedback.Text <- ""
                                    with e -> feedback.Text <- e.Message
                                    refresh())
                        ]
                    ]
            ]
        ]
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
        let layout = DockPanel()
        DockPanel.SetDock(menu, Dock.Top)
        layout.Children.Add menu
        DockPanel.SetDock(actionsMenu, Dock.Top)
        layout.Children.Add actionsMenu
        DockPanel.SetDock(actionResult, Dock.Bottom)
        layout.Children.Add actionResult
        layout.Children.Add pageArea
        window.Content <- layout
        window.Closed.Add(fun _ -> (host :> IDisposable).Dispose())
        refresh()
