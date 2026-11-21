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

let browseFile events (target: TextBox) =
    let pick () = task {
        let window = TopLevel.GetTopLevel(target)
        let! files = window.StorageProvider.OpenFilePickerAsync(Platform.Storage.FilePickerOpenOptions(Title = "选择请求文件"))
        if files.Count > 0 then target.Text <- Platform.Storage.StorageProviderExtensions.TryGetLocalPath(files[0])
    }
    button events "选择文件…" (fun () -> pick() |> ignore)

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

type Parameters() =
    let panel = StackPanel(Spacing = 10.)
    let entries = ResizeArray<(unit -> Parameter * Argument) * Events>()
    let clear () =
        for _, handlers in entries do (handlers :> IDisposable).Dispose()
        entries.Clear()

    let add (p: Parameter) =
        let handlers = new Events()
        let input title width text =
            let box = field title
            box.Width <- width
            box.Text <- text
            box
        let name = input "参数名" 130. p.Name
        let actual = input "实参" 160. p.DefaultValue
        let defaultValue = input "默认值" 140. p.DefaultValue
        let mutable previousDefault = p.DefaultValue
        handlers.Watch(defaultValue.GetObservable(TextBox.TextProperty).Subscribe(fun next ->
            if value actual = previousDefault then actual.Text <- next
            previousDefault <- next))
        let typeName = input "类型" 100. p.TypeName
        let description = input "说明" 180. p.Description
        let required = CheckBox(Content = "必填", IsChecked = Nullable p.Required)
        let location = choice "位置" [| "查询"; "路径"; "请求头"; "正文"; "文件" |]
        let browse = browseFile handlers actual
        let toggleBrowse () = browse.IsVisible <- string location.SelectedItem = "文件"
        handlers.Watch(location.SelectionChanged.Subscribe(fun _ -> toggleBrowse()))
        toggleBrowse()
        location.SelectedItem <- p.Location
        let read () =
            let key = (value name).Trim()
            if key = "" then invalidOp "请填写参数名或移除空行"
            let position = string location.SelectedItem
            { Name = key; Location = position; TypeName = value typeName; Required = required.IsChecked.GetValueOrDefault()
              Description = value description; DefaultValue = value defaultValue },
            { Name = key; Location = position; Value = value actual }
        let line = stack [
            row [ location; name; TextBlock(Text = "实参"); actual; browse ]
            Expander(Header = "参数定义", Content = row [
                TextBlock(Text = "默认值"); defaultValue; typeName; required; description ])
        ]
        let entry = read, handlers
        let remove () =
            entries.Remove entry |> ignore
            panel.Children.Remove line |> ignore
            (handlers :> IDisposable).Dispose()
        line.Children.Add(button handlers "移除参数" remove)
        panel.Children.Add line
        entries.Add entry
    member _.Content(events) =
        stack [ TextBlock(Text = "路径参数使用 {参数名} 占位"); panel
                button events "添加参数" (fun () ->
                    add { Name = ""; Location = "查询"; TypeName = "string"; Required = false; Description = ""; DefaultValue = "" }) ]
    member _.Load(items) =
        clear()
        panel.Children.Clear()
        Array.iter add items
    member _.Definitions() = entries |> Seq.map (fun (read, _) -> fst (read())) |> Seq.toArray
    member _.Read() =
        entries |> Seq.map (fun (read, _) ->
            let p, arg = read()
            if p.Required && arg.Value = "" then invalidOp $"请填写必填参数 {p.Name}"
            arg) |> Seq.toArray
    interface IDisposable with member _.Dispose() = clear()
