namespace MyDebugTools.ApiMonitor

open System
open Avalonia.Controls
open Avalonia.Automation
open Avalonia.Media
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

    let create () =
        let panel = StackPanel(Spacing = 8.)
        let query = input "搜索 SDK 函数"
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
            results.ItemsSource <- WindowsApi.Search(string query.Text) |> Seq.toArray
            results.SelectedIndex <- 0
        query.TextChanged.Add(fun _ -> search())
        search()
        let controls: Control list =
            [ TextBlock(Text = "API 监控", FontSize = 24.); query; results; details; add
              TextBlock(Text = "监控函数"); chosen; remove ]
        controls |> List.iter panel.Children.Add
        ScrollViewer(Content = panel) :> Control

type Plugin() =
    interface IDesktopPlugin with
        member _.Activate reg =
            reg.Page { Id = "api.monitor"; Title = "API 监控"; Create = View.create }
