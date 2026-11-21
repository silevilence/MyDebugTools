// Run from the repo root with: dotnet fsi --gui- --lib:artifacts/0001/publish tests/Clipboard.fsx <fixture.dll>
#r "Avalonia.Base.dll"
#r "Avalonia.Controls.dll"
#r "Avalonia.Desktop.dll"
#r "Avalonia.Themes.Fluent.dll"
#r "MyDebugTools.dll"
#r "DllDebugger.dll"

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Input
open Avalonia.Input.Platform
open Avalonia.Interactivity
open MyDebugTools.Desktop
open MyDebugTools.DllDebugger

// Use Avalonia's normal text transfer methods with process-local storage.
let clipboard =
    let mutable data: IAsyncDataTransfer = null
    { new IClipboard with
        member _.ClearAsync() = data <- null; Task.CompletedTask
        member _.SetDataAsync(value) = data <- value; Task.CompletedTask
        member _.FlushAsync() = Task.CompletedTask
        member _.TryGetDataAsync() = Task.FromResult(data)
        member _.TryGetInProcessDataAsync() = Task.FromResult(data) }

let fixture = DllImage.Open fsi.CommandLineArgs[1]
let mutable failure: exn option = None
let check () =
    AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting() |> ignore
    let work = Directory.CreateTempSubdirectory("MyDebugTools-clipboard-")
    let window = Window()
    try
        let store = SignatureStore(work.FullName)
        let add = store.Find(fixture, "add").Signature
        let twice = store.Find(fixture, "twice").Signature
        store.Confirm(fixture, add)
        store.Confirm(fixture, twice)
        let panel = BindingPanel.Panel(clipboard, window.StorageProvider, (fun () -> Some fixture),
                                       (fun () -> add), (fun () -> """{"a":5,"b":7}"""), store)
        panel.Open fixture
        window.Content <- panel.Control
        let children = panel.Control.Children
        let combos = children |> Seq.choose (function :? ComboBox as c -> Some c | _ -> None) |> Seq.toArray
        let choices = children |> Seq.pick (function :? ListBox as l -> Some l | _ -> None)
        let actions = children |> Seq.pick (function :? WrapPanel as p -> Some p | _ -> None)
        let copy = actions.Children |> Seq.cast<Button> |> Seq.find (fun b -> string b.Content = "复制代码")
        let preview = children |> Seq.pick (function :? TextBox as t -> Some t | _ -> None)
        let copyAndCheck marker =
            copy.RaiseEvent(RoutedEventArgs(Button.ClickEvent))
            let text = clipboard.TryGetTextAsync().GetAwaiter().GetResult()
            if String.IsNullOrEmpty(text) || text <> preview.Text || not (text.Contains(marker: string)) then
                failwith $"Copied content does not match {marker}"
            text
        for i, marker in Array.indexed [| "DllImport("; "extern int Invoke"; "LibraryImport(" |] do
            combos[0].SelectedIndex <- i
            combos[1].SelectedIndex <- 0
            let single = copyAndCheck marker
            if not (single.Contains("Example()")) then failwith "Missing example"
            combos[1].SelectedIndex <- 1
            choices.SelectedItems.Clear()
            for e in fixture.Exports do
                if e.Entry = "add" || e.Entry = "twice" || e.Entry = "spin" then choices.SelectedItems.Add(e) |> ignore
            let batch = copyAndCheck marker
            if not (batch.Contains("twice")) || batch.Contains("spin") || batch.Contains("Example()") then failwith "Invalid batch"
        printfn "PASS isolated clipboard: single example and mixed batch in all three formats"
    finally
        window.Close()
        work.Delete(true)

let thread = Thread(ThreadStart(fun () -> try check() with e -> failure <- Some e))
thread.SetApartmentState(ApartmentState.STA)
thread.Start()
if not (thread.Join(TimeSpan.FromSeconds 40.)) then Environment.Exit(2)
failure |> Option.iter raise
