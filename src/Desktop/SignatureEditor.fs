module MyDebugTools.Desktop.SignatureEditor

open System
open Avalonia.Controls
open MyDebugTools.DllDebugger

type Editor() =
    let panel = StackPanel(Spacing = 5.)
    let advanced = StackPanel(Spacing = 4.)
    let input label multiline =
        let parent = if multiline then advanced else panel
        parent.Children.Add(TextBlock(Text = label, TextWrapping = Avalonia.Media.TextWrapping.Wrap))
        let box = TextBox(AcceptsReturn = multiline)
        Avalonia.Automation.AutomationProperties.SetName(box, label)
        parent.Children.Add box
        box
    let result = input "返回类型（void、i8/u8 至 i64/u64、f32、f64、isize、usize、handle 或结构体名）" false
    let convention = input "调用约定（cdecl / stdcall / winapi）" false
    let parameters = input "参数，每行「名称 类型 方向」。无参数填 -。例：dst buffer out length=16 owner=caller；text string in encoding=utf8 owner=caller" true
    let layouts = input "结构体，每行一项。例：Point pack=8 x:i32 y:i32。指针类型写作 Point* 或 i32*。" true
    let sources = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
    let mutable original = Signature.Unknown ""
    let changed = Event<unit>()
    let mutable loading = false
    let rows = StackPanel(Spacing = 6.)
    let mutable editingRows = false
    let rebuildRows () =
        rows.Children.Clear()
        let lineFields = ResizeArray<unit -> string>()
        let write () =
            if not loading then
                editingRows <- true
                try parameters.Text <- if lineFields.Count = 0 then "-" else lineFields |> Seq.map (fun read -> read()) |> String.concat "\n"
                finally editingRows <- false
        let addRow (p: Parameter) =
            let fields = WrapPanel()
            let field title initial =
                let box = TextBox(Text = initial, MinWidth = 70.)
                let col = StackPanel(Margin = Avalonia.Thickness(0., 0., 6., 4.))
                col.Children.Add(TextBlock(Text = title))
                col.Children.Add box
                fields.Children.Add col
                Avalonia.Automation.AutomationProperties.SetName(box, title)
                box.PropertyChanged.Add(fun e -> if e.Property = TextBox.TextProperty then write())
                box
            let name = field "参数名" p.Name
            let kind = field "类型" p.Type
            let direction = field "方向" p.Direction
            let encoding = field "编码" p.Encoding
            let length = field "长度" (if p.Length.HasValue then string p.Length.Value else "")
            let owner = field "所有权" p.Ownership
            let required (box: TextBox) = if String.IsNullOrWhiteSpace box.Text then "?" else box.Text
            let optional key (box: TextBox) = if String.IsNullOrWhiteSpace box.Text then [] else [key + "=" + box.Text]
            let read () =
                [ yield! [required name; required kind; required direction]
                  yield! optional "encoding" encoding; yield! optional "length" length; yield! optional "owner" owner ] |> String.concat " "
            lineFields.Add read
            let remove = Button(Content = "移除此参数")
            remove.Click.Add(fun _ ->
                lineFields.Remove read |> ignore
                rows.Children.Remove fields |> ignore
                write())
            fields.Children.Add remove
            rows.Children.Add fields
        try
            let current = SignatureText.Parameters(string parameters.Text)
            if not (isNull current) then for p in current do addRow p
            let add = Button(Content = "添加参数行")
            let none = Button(Content = "设为无参数")
            let controls = WrapPanel()
            controls.Children.Add add
            controls.Children.Add none
            rows.Children.Insert(0, controls)
            add.Click.Add(fun _ -> addRow (Parameter("", "?", "in")); write())
            none.Click.Add(fun _ -> parameters.Text <- "-")
        with _ -> rows.Children.Add(TextBlock(Text = "请在高级文本中补全当前参数行。"))
    do
        panel.Children.Add rows
        panel.Children.Add(Expander(Header = "高级签名文本和结构体字段", Content = advanced))
        parameters.PropertyChanged.Add(fun e -> if e.Property = TextBox.TextProperty && not editingRows then rebuildRows())
        panel.Children.Add sources
        for field in [result; convention; parameters; layouts] do
            field.PropertyChanged.Add(fun e ->
                if e.Property = TextBox.TextProperty && not loading then changed.Trigger())
    member _.Changed = changed.Publish
    member _.Snapshot = original, result.Text, convention.Text, parameters.Text, layouts.Text
    member this.Restore(s, r, c, p, l) =
        this.Load(s)
        loading <- true
        result.Text <- r
        convention.Text <- c
        parameters.Text <- p
        layouts.Text <- l
        loading <- false
    member _.Panel = panel
    member _.Entry = original.Entry
    member _.Load(s: Signature) =
        loading <- true
        original <- s
        result.Text <- s.Result
        convention.Text <- s.Convention
        parameters.Text <-
            match s.Parameters with
            | null -> "?"
            | a when a.Length = 0 -> "-"
            | a -> a |> Array.map SignatureText.FormatParameter |> String.concat "\n"
        layouts.Text <- s.Layouts |> Array.map SignatureText.FormatLayout |> String.concat "\n"
        sources.Text <-
            s.Values() |> Seq.map (fun field ->
                let origin = match s.Sources.TryGetValue field.Key with true, v -> v | _ -> "待补齐"
                $"{field.Key} · {origin}") |> String.concat "　"
        loading <- false
    member _.Read(entry) =
        let s = Signature(entry, result.Text.Trim(), convention.Text.Trim(), SignatureText.Parameters parameters.Text,
                          SignatureText.Layouts layouts.Text, Collections.Generic.Dictionary())
        let before = original.Values()
        for field in s.Values() do
            if field.Value <> "?" then
                s.Sources[field.Key] <-
                    match before.TryGetValue field.Key, original.Sources.TryGetValue field.Key with
                    | (true, v), (true, source) when field.Value = v -> source
                    | _ -> "手工录入"
        s
