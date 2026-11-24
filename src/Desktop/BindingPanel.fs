module MyDebugTools.Desktop.BindingPanel

open System
open System.IO
open Avalonia.Controls
open Avalonia.Input.Platform
open Avalonia.Platform.Storage
open MyDebugTools.DllDebugger

type Panel(clipboard: IClipboard, storage: IStorageProvider, image: unit -> DllImage option,
           draft: unit -> Signature, arguments: unit -> string, store: SignatureStore) =
    let root = StackPanel(Spacing = 6.)
    let choices = ListBox(Height = 118., SelectionMode = SelectionMode.Multiple)
    let format = ComboBox(ItemsSource = [| "C# DllImport"; "F# DllImport"; "C# LibraryImport" |], SelectedIndex = 0)
    let mode = ComboBox(ItemsSource = [| "当前函数调用示例"; "批量声明（点选下列导出）" |], SelectedIndex = 0)
    let preview = TextBox(IsReadOnly = true, AcceptsReturn = true, MaxHeight = 180.)
    let report = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
    let diagnostics = MyDebugTools.Contracts.DiagnosticPanel()
    let receipt = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
    let mutable valid = false
    let copy = Button(Content = "复制代码", IsEnabled = false)
    let save = Button(Content = "导出文件…", IsEnabled = false)
    let invalidate () =
        if valid || preview.Text <> "" then receipt.Text <- "预览已过期。请更新预览。"
        valid <- false
        copy.IsEnabled <- false
        save.IsEnabled <- false
        diagnostics.Clear()
    let generate () =
        let dll = image() |> Option.defaultWith (fun () -> invalidOp "请先打开 DLL。")
        let example = mode.SelectedIndex = 0
        let names =
            if example then [| store.ConfirmedDraft(dll, draft()).Entry |]
            else choices.SelectedItems |> Seq.cast<Export> |> Seq.map _.Entry |> Seq.toArray
        if names.Length = 0 then invalidOp "请选择要导出的函数。"
        let target =
            match format.SelectedIndex with
            | 1 -> BindingFormat.FSharp
            | 2 -> BindingFormat.LibraryImport
            | _ -> BindingFormat.CSharp
        let result = BindingGenerator.Generate(dll, store, names, target, if example then arguments() else null)
        preview.Text <- result.Code
        let skipped = String.concat "\n" result.Skipped
        let scope = String.concat "、" names
        report.Text <- $"{mode.SelectedItem} · {format.SelectedItem} · {scope} · 成功 {names.Length - result.Skipped.Length}，跳过 {result.Skipped.Length}\n{skipped}"
        if result.Skipped.Length > 0 then diagnostics.Show("DLL 工作台", "生成代码", scope, skipped)
        else diagnostics.Clear()
        valid <- not (String.IsNullOrEmpty result.Code)
        copy.IsEnabled <- valid
        save.IsEnabled <- valid
        receipt.Text <- "预览已更新"
    do
        Avalonia.Automation.AutomationProperties.SetName(preview, "生成代码")
        Avalonia.Automation.AutomationProperties.SetName(format, "代码格式")
        root.Children.Add(TextBlock(Text = "生成互操作代码", FontSize = 18.))
        root.Children.Add format
        root.Children.Add mode
        mode.SelectionChanged.Add(fun _ -> choices.IsVisible <- mode.SelectedIndex = 1)
        choices.IsVisible <- false
        root.Children.Add choices
        let actions = WrapPanel()
        let update = Button(Content = "预览代码")
        update.Click.Add(fun _ ->
            try generate()
            with e ->
                invalidate()
                report.Text <- e.Message
                diagnostics.Show("DLL 工作台", "预览代码", "所选入口", e.Message))
        copy.Click.Add(fun _ ->
            task {
                try
                    do! clipboard.SetTextAsync(preview.Text)
                    receipt.Text <- "已复制代码"
                with e -> receipt.Text <- e.Message
            } |> ignore)
        save.Click.Add(fun _ ->
            task {
                let code = preview.Text
                try
                    let ext = if format.SelectedIndex = 1 then "fs" else "cs"
                    let! file = storage.SaveFilePickerAsync(FilePickerSaveOptions(SuggestedFileName = "Bindings." + ext, DefaultExtension = ext))
                    if not (isNull file) then
                        use! stream = file.OpenWriteAsync()
                        stream.SetLength(0)
                        use writer = new StreamWriter(stream)
                        do! writer.WriteAsync(code)
                        do! writer.FlushAsync()
                        receipt.Text <- $"已保存 · {file.TryGetLocalPath()}"
                with e -> receipt.Text <- e.Message
            } |> ignore)
        for b in [update; copy; save] do actions.Children.Add b
        for selector in [format; mode] do selector.SelectionChanged.Add(fun _ -> invalidate())
        choices.SelectionChanged.Add(fun _ -> invalidate())
        root.Children.Add receipt
        root.Children.Add diagnostics.Control
        root.Children.Add actions
        root.Children.Add report
        root.Children.Add preview
    member _.Control = root
    member _.Invalidate() = invalidate()
    member _.Open(dll: DllImage) =
        invalidate()
        choices.ItemsSource <- dll.Exports
        preview.Text <- ""
        report.Text <- ""
