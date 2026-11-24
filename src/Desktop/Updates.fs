module MyDebugTools.Desktop.Updates

open System
open System.IO
open System.Diagnostics
open System.Net.Http
open System.Threading.Tasks
open MyDebugTools.Host
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.VirtualDom
open NetSparkleUpdater.Downloaders
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

let private download (item: AppCastItem) (token: CancellationToken) = task {
    let expected = Convert.FromBase64String item.DownloadSignature
    if expected.Length <> 64 || item.UpdateSize <= 0L then invalidOp "清单中的安装包签名或大小无效。"
    let dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MyDebugTools-updates"))
    let path = Path.Combine(dir.FullName, Convert.ToHexString(expected) + ".exe")
    let valid file =
        File.Exists file && FileInfo(file).Length = item.UpdateSize &&
        (verifier()).VerifySignatureOfFile(item.DownloadSignature, file) = ValidationResult.Valid
    if not (valid path) then
        let part = Path.Combine(dir.FullName, Guid.NewGuid().ToString("N") + ".part")
        try
            use client = new WebFileDownloader()
            use cancel = token.Register(fun () -> client.CancelDownload())
            let mutable error = None
            client.DownloadFileCompleted.Add(fun e -> error <- Option.ofObj e.Error)
            do! client.DownloadFile(Uri item.DownloadLink, part)
            token.ThrowIfCancellationRequested()
            error |> Option.iter raise
            if not (valid part) then invalidOp "安装包大小或签名不符，请重试。"
            File.Move(part, path, true)
        finally
            File.Delete part
    return path
}

type Panel(window: Window, host: Host, canExit: unit -> bool) =
    let lifetime = new CancellationTokenSource()
    let bar = StackPanel(Spacing = 4., Margin = Thickness(12., 4.), IsVisible = false)
    let status = TextBlock(TextWrapping = Media.TextWrapping.Wrap)
    let notes = TextBlock(TextWrapping = Media.TextWrapping.Wrap)
    let details = ScrollViewer(Content = notes, MaxHeight = 110.)
    let commands = WrapPanel()
    let check = Button(Content = "检查更新")
    let later = Button(Content = "稍后")
    let install = Button(Content = "立即更新", IsVisible = false)
    let mutable pending: AppCastItem option = None
    let mutable checking = false
    let show message =
        bar.IsVisible <- true
        status.Text <- message

    let checkNow manual = task {
        if not checking then
            checking <- true
            check.IsEnabled <- false
            pending <- None
            install.IsVisible <- false
            details.IsVisible <- false
            if manual then show "正在检查更新…"
            try
                try
                    let! result = discover lifetime.Token
                    match result with
                    | Some item ->
                        pending <- Some item
                        install.IsVisible <- true
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

    let active () =
        host.Plugins |> List.choose (fun plugin ->
            host.Monitor plugin.Id |> Option.bind (fun session ->
                session.Target() |> Option.map (fun target -> plugin.Title, target, session)))

    let sameSessions left right =
        List.length left = List.length right &&
        List.forall2 (fun (a,b,s) (c,d,t) -> a = c && b = d && obj.ReferenceEquals(s,t)) left right

    let consent sessions = task {
        if List.isEmpty sessions then return true
        else
            let dialog = Window(Title = "更新前结束采集", Width = 480., SizeToContent = SizeToContent.Height,
                                WindowStartupLocation = WindowStartupLocation.CenterOwner)
            let targets = sessions |> List.map (fun (title, target, _) -> $"{title} · {target}") |> String.concat "\n"
            let choice (label: string) answer = Button.create [ Button.content label; Button.onClick (fun _ -> dialog.Close(answer: bool)) ]
            dialog.Content <- StackPanel.create [
                StackPanel.margin 16.; StackPanel.spacing 8.
                StackPanel.children [
                    TextBlock.create [ TextBlock.text ("以下采集会话仍在运行\n" + targets); TextBlock.textWrapping Media.TextWrapping.Wrap ]
                    choice "继续采集，稍后更新" false
                    choice "结束采集并更新" true
                ]
            ] |> VirtualDom.create
            return! dialog.ShowDialog<bool>(window)
    }

    let apply (item: AppCastItem) = task {
        let token = lifetime.Token
        check.IsEnabled <- false
        commands.IsEnabled <- false
        try
            try
                let before = active()
                let! proceed = consent before
                if proceed then
                    show $"正在准备 {item.Version} 安装包…"
                    let! path = Task.Run<string>(fun () -> download item token)
                    token.ThrowIfCancellationRequested()
                    use locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)
                    let! verified = Task.Run(fun () -> (verifier()).VerifySignatureOfFile(item.DownloadSignature, path))
                    token.ThrowIfCancellationRequested()
                    if verified <> ValidationResult.Valid then invalidOp "安装包签名不符。"
                    let now = active()
                    let! approved = if sameSessions before now then Task.FromResult(true) else consent now
                    if approved then
                        if not (canExit()) then invalidOp "请等待当前插件操作完成，再重试更新。"
                        if not (sameSessions now (active())) then invalidOp "采集会话已变化，请重新确认。"
                        show "正在结束采集并准备安装…"
                        do! now |> List.map (fun (_,_,s) -> s.Stop()) |> Task.WhenAll
                        token.ThrowIfCancellationRequested()
                        if not (canExit()) || not (List.isEmpty (active())) then invalidOp "仍有操作或采集会话，请重试。"
                        let start = ProcessStartInfo(path, UseShellExecute = false)
                        // NSIS requires the unquoted /D directory to be the final argument.
                        start.Arguments <- $"/UPDATEPID={Environment.ProcessId} /D={Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)}"
                        use installer = Process.Start(start)
                        window.Close()
                        if window.IsVisible then
                            if not installer.HasExited then installer.Kill()
                            invalidOp "宿主尚未退出，请稍后重试。"
                    else bar.IsVisible <- false
                else bar.IsVisible <- false
            with e ->
                if not token.IsCancellationRequested then show $"更新未完成 · {e.GetBaseException().Message}"
        finally
            check.IsEnabled <- true
            commands.IsEnabled <- true
    }

    do
        commands.Children.Add install
        install.Click.Add(fun _ -> pending |> Option.iter (fun item -> apply item |> ignore))
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
