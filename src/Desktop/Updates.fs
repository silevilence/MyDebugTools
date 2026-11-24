module MyDebugTools.Desktop.Updates

open System
open System.Net.Http
open System.Reflection
open System.Text
open System.Threading
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open NetSparkleUpdater
open NetSparkleUpdater.AppCastHandlers
open NetSparkleUpdater.Enums
open NetSparkleUpdater.SignatureVerifiers

let private assembly = Assembly.GetExecutingAssembly()
let private metadata key =
    assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
    |> Seq.tryPick (fun a -> if a.Key = key then Option.ofObj a.Value else None)
    |> Option.defaultValue ""

let current = assembly.GetName().Version.ToString(3)
let private publicKey = metadata "UpdatePublicKey"
let private appcast =
    match metadata "UpdateFeedUrl" with
    | "" -> "https://github.com/silevilence/MyDebugTools/releases/latest/download/appcast.xml"
    | url -> url
let private http = new HttpClient(Timeout = TimeSpan.FromSeconds 20.)
let private verifier () = Ed25519Checker(SecurityMode.Strict, publicKey, null)

let private discover (token: CancellationToken) = task {
    let checker = verifier()
    if not (checker.HasValidKeyInformation()) then invalidOp "当前构建缺少更新公钥。"
    let! data = http.GetByteArrayAsync(appcast, token)
    let! signature = http.GetStringAsync(appcast + ".signature", token)
    if checker.VerifySignature(signature.Trim(), data) <> ValidationResult.Valid then
        invalidOp "更新清单签名无效。"
    let parsed = XMLAppCastGenerator(null).DeserializeAppCast(Encoding.UTF8.GetString data)
    let item = Seq.exactlyOne parsed.Items
    let version = Version.Parse item.Version
    if version.Build < 0 || version.Revision >= 0 || String.IsNullOrWhiteSpace item.Description then
        invalidOp "更新清单缺少正式版本或发布说明。"
    return if version > Version.Parse current then Some item else None
}

type Panel(window: Window) =
    let lifetime = new CancellationTokenSource()
    let bar = StackPanel(Spacing = 4., Margin = Thickness(12., 4.), IsVisible = false)
    let status = TextBlock(TextWrapping = Media.TextWrapping.Wrap)
    let notes = TextBlock(TextWrapping = Media.TextWrapping.Wrap)
    let details = ScrollViewer(Content = notes, MaxHeight = 110.)
    let commands = WrapPanel()
    let check = Button(Content = "检查更新")
    let later = Button(Content = "稍后")
    let mutable checking = false
    let show message =
        bar.IsVisible <- true
        status.Text <- message

    let checkNow manual = task {
        if not checking then
            checking <- true
            check.IsEnabled <- false
            details.IsVisible <- false
            if manual then show "正在检查更新…"
            try
                try
                    let! result = discover lifetime.Token
                    match result with
                    | Some item ->
                        show $"发现新版本 {item.Version}"
                        notes.Text <- item.Description
                        details.IsVisible <- true
                    | None -> if manual then show $"当前已是最新版本 {current}"
                with e ->
                    if manual || not (String.IsNullOrEmpty publicKey) then
                        show $"检查更新失败 · {e.GetBaseException().Message}"
            finally
                checking <- false
                check.IsEnabled <- true
    }

    do
        commands.Children.Add later
        bar.Children.Add status
        bar.Children.Add details
        bar.Children.Add commands
        later.Click.Add(fun _ -> bar.IsVisible <- false)
        check.Click.Add(fun _ -> checkNow true |> ignore)
        window.Opened.Add(fun _ -> checkNow false |> ignore)
        window.Closed.Add(fun _ -> lifetime.Cancel(); lifetime.Dispose())

    member _.Bar = bar
    member _.Check = check
