namespace MyDebugTools.Contracts

open System
open Avalonia.Automation.Peers
open Avalonia.Automation.Provider
open Avalonia.Controls
open Avalonia.Markup.Xaml.Styling
open AvaloniaEdit
open AvaloniaEdit.Highlighting

// Avalonia caches styled control types. Keep the editor and its accessibility peer
// in the shared UI assembly so a plugin can unload after it has displayed code.
type private EditorPeer(editor: TextEditor) =
    inherit ControlAutomationPeer(editor)
    override _.GetAutomationControlTypeCore() = AutomationControlType.Edit
    interface IValueProvider with
        member _.IsReadOnly = editor.IsReadOnly
        member _.Value = editor.Text
        member _.SetValue(value) =
            if editor.IsReadOnly then invalidOp "只读编辑器"
            editor.Text <- value

type SyntaxEditor() as editor =
    inherit TextEditor(ShowLineNumbers = true, WordWrap = true, MinHeight = 110., MaxHeight = 400.)
    do
        let style = Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml")
        editor.Styles.Add(StyleInclude(style, Source = style))
    override _.StyleKeyOverride = typeof<TextEditor>
    override _.OnCreateAutomationPeer() = EditorPeer(editor)
    member _.SetLanguage(extension: string) =
        editor.SyntaxHighlighting <-
            if extension = "" then null else HighlightingManager.Instance.GetDefinitionByExtension(extension)
