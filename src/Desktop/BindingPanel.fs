module MyDebugTools.Desktop.BindingPanel

open System
open System.IO
open Avalonia.Controls
open Avalonia.Input.Platform
open Avalonia.Platform.Storage
open MyDebugTools.DllDebugger

type Panel(window: Window, image: unit -> DllImage option, entry: unit -> string, arguments: unit -> string, store: SignatureStore) =
    let root = StackPanel(Spacing = 6.)
    let choices = ListBox(Height = 118., SelectionMode = SelectionMode.Multiple)
    let format = ComboBox(ItemsSource = [| "C# DllImport"; "F# DllImport"; "C# LibraryImport" |], SelectedIndex = 0)
    let mode = ComboBox(ItemsSource = [| "当前函数调用示例"; "批量声明（点选下列导出）" |], SelectedIndex = 0)
    let preview = TextBox(IsReadOnly = true, AcceptsReturn = true, MaxHeight = 180.)
    let report = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
    let generate () =
        let dll = image() |> Option.defaultWith (fun () -> invalidOp "请先打开 DLL。")
        let example = mode.SelectedIndex = 0
        let names = if example then [| entry() |] else choices.SelectedItems |> Seq.cast<Export> |> Seq.map _.Entry |> Seq.toArray
        if names.Length = 0 then invalidOp "请选择要导出的函数。"
        let target =
            match format.SelectedIndex with
            | 1 -> BindingFormat.FSharp
            | 2 -> BindingFormat.LibraryImport
            | _ -> BindingFormat.CSharp
        let result = BindingGenerator.Generate(dll, store, names, target, if example then arguments() else null)
        preview.Text <- result.Code
        report.Text <- String.concat "\n" result.Skipped
        result.Code
    let action title (run: string -> System.Threading.Tasks.Task<unit>) =
        let b = Button(Content = title)
        b.Click.Add(fun _ ->
            task {
                try
                    let code = generate()
                    if code <> "" then do! run code
                with e -> report.Text <- e.Message
            } |> ignore)
        b
    do
        root.Children.Add(TextBlock(Text = "生成互操作代码", FontSize = 18.))
        root.Children.Add format
        root.Children.Add mode
        mode.SelectionChanged.Add(fun _ -> choices.IsVisible <- mode.SelectedIndex = 1)
        choices.IsVisible <- false
        root.Children.Add choices
        let actions = WrapPanel()
        actions.Children.Add(action "预览代码" (fun _ -> task { return () }))
        actions.Children.Add(action "复制代码" (fun code -> task { do! window.Clipboard.SetTextAsync(code) }))
        actions.Children.Add(action "导出文件…" (fun code -> task {
            let ext = if format.SelectedIndex = 1 then "fs" else "cs"
            let! file = window.StorageProvider.SaveFilePickerAsync(FilePickerSaveOptions(SuggestedFileName = "Bindings." + ext, DefaultExtension = ext))
            if not (isNull file) then
                use! stream = file.OpenWriteAsync()
                stream.SetLength(0)
                use writer = new StreamWriter(stream)
                do! writer.WriteAsync(code)
        }))
        root.Children.Add actions
        root.Children.Add report
        root.Children.Add preview
    member _.Control = root
    member _.Open(dll: DllImage) =
        choices.ItemsSource <- dll.Exports
        preview.Text <- ""
        report.Text <- ""
