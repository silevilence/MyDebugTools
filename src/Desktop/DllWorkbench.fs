module MyDebugTools.Desktop.DllWorkbench

open System
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Platform.Storage
open MyDebugTools.Contracts
open MyDebugTools.DllDebugger

let private button title run =
    let b = Button(Content = title)
    b.Click.Add(fun _ -> run())
    b

let private row (controls: Control list) =
    let panel = WrapPanel(Orientation = Orientation.Horizontal)
    for c in controls do panel.Children.Add c
    panel

let plugin (window: Window) =
    { new IDesktopPlugin with
        member _.Activate registry =
            registry.Page {
                Id = "dll.debugger"; Title = "DLL 工作台"
                Create = fun () ->
                    let panel = StackPanel(Spacing = 8.)
                    let path = TextBox(PlaceholderText = "DLL 文件路径")
                    let status = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
                    let exports = ListBox(Height = 160.)
                    let mutable image: DllImage option = None
                    let openFile file =
                        try
                            let loaded = DllImage.Open file
                            image <- Some loaded
                            path.Text <- loaded.Path
                            exports.ItemsSource <- loaded.Exports
                            status.Text <- $"{loaded.Architecture} · {loaded.Exports.Count} 项导出"
                        with e -> status.Text <- e.Message
                    panel.Children.Add(TextBlock(Text = "DLL 工作台", FontSize = 24.))
                    panel.Children.Add path
                    panel.Children.Add(row [
                        button "打开 DLL" (fun () -> openFile path.Text)
                        button "浏览文件…" (fun () ->
                            task {
                                let! files = window.StorageProvider.OpenFilePickerAsync(FilePickerOpenOptions(Title = "选择原生 DLL"))
                                if files.Count > 0 then openFile (files[0].TryGetLocalPath())
                            } |> ignore)
                    ])
                    panel.Children.Add status
                    panel.Children.Add(TextBlock(Text = "导出名　序号　相对地址　性质"))
                    panel.Children.Add exports
                    panel.Children.Add(TextBlock(Text = "仅有导出表时，返回类型和参数未知。"))
                    ScrollViewer(Content = panel)
            } }
