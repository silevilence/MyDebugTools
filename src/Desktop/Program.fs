module MyDebugTools.Desktop.Program

open System
open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Themes.Fluent
open Avalonia.Styling
open Avalonia.Platform
open Avalonia.Markup.Xaml.Styling
open Microsoft.Win32

type App() =
    inherit Application()
    override this.Initialize() =
        this.Styles.Add(FluentTheme())
        this.Styles.Add(Dock.Avalonia.Themes.Fluent.DockFluentTheme())
        this.Styles.Add(StyleInclude(Uri("avares://MyDebugTools/"), Source = Uri("avares://MyDebugTools/WorkspaceStyles.axaml")))
    override this.OnFrameworkInitializationCompleted() =
        let followSystem highContrast =
            // In a remote Windows session the cached platform theme can lag behind the user's app preference.
            this.RequestedThemeVariant <-
                if highContrast then ThemeVariant.Default
                else
                    match Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null) with
                    | :? int as light -> if light = 0 then ThemeVariant.Dark else ThemeVariant.Light
                    | _ -> ThemeVariant.Default
        match this.PlatformSettings with
        | null -> followSystem false
        | settings ->
            let update (colors: PlatformColorValues) = followSystem (colors.ContrastPreference = ColorContrastPreference.High)
            update (settings.GetColorValues())
            settings.ColorValuesChanged.Add(update)
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as lifetime ->
            lifetime.ShutdownMode <- Avalonia.Controls.ShutdownMode.OnMainWindowClose
            lifetime.MainWindow <- Shell.MainWindow()
        | _ -> ()
        base.OnFrameworkInitializationCompleted()

[<EntryPoint; STAThread>]
let main args =
    AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args)
