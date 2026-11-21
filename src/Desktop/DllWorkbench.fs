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
                    let editor = SignatureEditor.Editor()
                    let entry = TextBox(PlaceholderText = "导出名或 #序号")
                    let signatureStatus = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
                    let store = SignatureStore(IO.Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"), "MyDebugTools", "signatures"))
                    let selectEntry () =
                        match image with
                        | None -> ()
                        | Some dll ->
                            try
                                let found = store.Find(dll, entry.Text)
                                editor.Load found.Signature
                                signatureStatus.Text <- found.Status
                            with e -> signatureStatus.Text <- e.Message
                    entry.TextChanged.Add(fun _ -> selectEntry())
                    exports.SelectionChanged.Add(fun _ ->
                        match exports.SelectedItem with
                        | :? Export as item -> entry.Text <- item.Entry
                        | _ -> ())
                    let openFile file =
                        try
                            let loaded = DllImage.Open file
                            image <- Some loaded
                            path.Text <- loaded.Path
                            exports.ItemsSource <- loaded.Exports
                            entry.Text <- ""
                            editor.Load(Signature.Unknown "")
                            signatureStatus.Text <- "请选择导出或填写入口。"
                            if loaded.Exports.Count > 0 then
                                exports.SelectedIndex <- 0
                                entry.Text <- loaded.Exports[0].Entry
                                selectEntry()
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
                    panel.Children.Add entry
                    panel.Children.Add editor.Panel
                    panel.Children.Add(button "确认并保存签名" (fun () ->
                        try
                            let dll = image |> Option.defaultWith (fun () -> invalidOp "请先打开 DLL。")
                            let s = editor.Read(entry.Text)
                            store.Confirm(dll, s)
                            selectEntry()
                        with e -> signatureStatus.Text <- e.Message))
                    panel.Children.Add signatureStatus
                    ScrollViewer(Content = panel)
            } }
