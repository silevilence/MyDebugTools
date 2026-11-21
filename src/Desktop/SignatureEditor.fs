module MyDebugTools.Desktop.SignatureEditor

open System
open Avalonia.Controls
open MyDebugTools.DllDebugger

type Editor() =
    let panel = StackPanel(Spacing = 5.)
    let input label multiline =
        panel.Children.Add(TextBlock(Text = label, TextWrapping = Avalonia.Media.TextWrapping.Wrap))
        let box = TextBox(AcceptsReturn = multiline)
        panel.Children.Add box
        box
    let result = input "返回类型（void、i8/u8 至 i64/u64、f32、f64、isize、usize、handle 或结构体名）" false
    let convention = input "调用约定（cdecl / stdcall / winapi）" false
    let parameters = input "参数，每行「名称 类型 方向」。无参数填 -。例：dst buffer out length=16 owner=caller；text string in encoding=utf8 owner=caller" true
    let layouts = input "结构体，每行一项。例：Point pack=8 x:i32 y:i32。指针类型写作 Point* 或 i32*。" true
    let sources = TextBlock(TextWrapping = Avalonia.Media.TextWrapping.Wrap)
    let mutable original = Signature.Unknown ""
    do panel.Children.Add sources
    member _.Panel = panel
    member _.Load(s: Signature) =
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
