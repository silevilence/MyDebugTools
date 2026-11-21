module MyDebugTools.WebApi.Editor

open System
open Avalonia
open Avalonia.Automation
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media

type Events() =
    let handlers = ResizeArray<IDisposable>()
    member _.Watch(subscription) = handlers.Add subscription
    interface IDisposable with
        member _.Dispose() =
            for handler in handlers do handler.Dispose()
            handlers.Clear()

let named name (control: #Control) =
    AutomationProperties.SetName(control :> Control, name)
    control

let field name = TextBox(PlaceholderText = name) |> named name
let value (field: TextBox) = if isNull field.Text then "" else field.Text
let multiline name =
    let box = field name
    box.AcceptsReturn <- true
    box.TextWrapping <- TextWrapping.Wrap
    box.MinHeight <- 76.
    box

let choice name (items: string array) =
    ComboBox(ItemsSource = items, SelectedIndex = 0) |> named name

let button (events: Events) caption action =
    let control = Button(Content = caption)
    events.Watch(control.Click.Subscribe(fun _ -> action()))
    control

let stack (controls: Control list) =
    let panel = StackPanel(Spacing = 8.)
    for control in controls do panel.Children.Add control
    panel

let row (controls: Control list) =
    let panel = WrapPanel()
    for control in controls do
        control.Margin <- Thickness(0., 0., 6., 4.)
        panel.Children.Add control
    panel

let tab title (content: Control) =
    TabItem(Header = title, Content = ScrollViewer(Content = content, Padding = Thickness(2., 8.)))

type Parameters(events: Events) =
    let panel = StackPanel(Spacing = 6.)
    let entries = ResizeArray<unit -> Argument>()
    let add () =
        let name = field "参数名"
        let actual = field "实参"
        let location = choice "位置" [| "查询"; "路径"; "请求头" |]
        name.Width <- 140.
        actual.Width <- 180.
        let read () = { Name = (value name).Trim(); Location = string location.SelectedItem; Value = value actual }
        let line = row [ location; name; actual ]
        let remove = button events "移除" (fun () -> entries.Remove read |> ignore; panel.Children.Remove line |> ignore)
        line.Children.Add remove
        panel.Children.Add line
        entries.Add read
    member _.Content = stack [ TextBlock(Text = "路径参数使用 {参数名} 占位"); panel; button events "添加参数" add ]
    member _.Read() =
        entries |> Seq.map (fun read -> read()) |> Seq.map (fun arg ->
            if arg.Name = "" then invalidOp "请填写参数名或移除空行"
            arg) |> Seq.toArray
