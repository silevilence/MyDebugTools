module MyDebugTools.Desktop.Program

open System
open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent

type App() =
    inherit Application()
    override this.Initialize() = this.Styles.Add(FluentTheme())
    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as lifetime -> lifetime.MainWindow <- Shell.MainWindow()
        | _ -> ()
        base.OnFrameworkInitializationCompleted()

[<EntryPoint; STAThread>]
let main args =
    AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args)
