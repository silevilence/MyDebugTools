namespace MyDebugTools.Host

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Reflection
open System.Security.Cryptography
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open ModelContextProtocol
open ModelContextProtocol.Protocol
open ModelContextProtocol.Server

[<CLIMutable>]
type DeveloperSettings = { Enabled: bool; Port: int; Token: string }

module DeveloperTools =
    let tool name description (action: Delegate) =
        McpServerTool.Create(action, McpServerToolCreateOptions(Name = name, Description = description))

    let documentation version =
        let resource name =
            use input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            use reader = new StreamReader(input)
            reader.ReadToEnd()
        $"宿主版本 {version}\n\n" + resource "PluginDevelopment.md" + "\n\n```fsharp\n" + resource "Plugin.fs" + "\n```"

/// One stateless SDK transport per HTTP request. Tools share the live desktop catalog.
type DeveloperServer(settingsPath: string, version: string, extraTools: McpServerTool list) =
    let mutable settings =
        if File.Exists settingsPath then JsonSerializer.Deserialize<DeveloperSettings>(File.ReadAllText settingsPath)
        else { Enabled = false; Port = 0; Token = Convert.ToHexString(RandomNumberGenerator.GetBytes 32) }
    let mutable listener: (HttpListener * CancellationTokenSource) option = None
    let mutable failure = ""
    let endpoint () = $"http://127.0.0.1:{settings.Port}/mcp/"
    let tools = McpServerPrimitiveCollection<McpServerTool>()
    do
        tools.Add(DeveloperTools.tool "read_documentation" "读取当前宿主版本的插件清单、契约和开发示例"
                    (Func<string>(fun () -> DeveloperTools.documentation version)))
        for tool in extraTools do tools.Add tool

    let save value =
        Directory.CreateDirectory(Path.GetDirectoryName settingsPath) |> ignore
        let file = settingsPath + "." + Guid.NewGuid().ToString("N")
        try
            File.WriteAllText(file, JsonSerializer.Serialize value)
            File.Move(file, settingsPath, true)
            settings <- value
        finally
            if File.Exists file then File.Delete file

    let serve (context: HttpListenerContext) (token: CancellationToken) = task {
        let req = context.Request
        use response = context.Response
        try
            let header name = req.Headers.Get(name: string)
            let origin = header "Origin"
            let protocol = header "MCP-Protocol-Version"
            if not (IPAddress.IsLoopback req.RemoteEndPoint.Address) ||
               (not (isNull origin) && origin <> (Uri(endpoint())).GetLeftPart(UriPartial.Authority)) then
                response.StatusCode <- 403
            elif header "Authorization" <> "Bearer " + settings.Token then response.StatusCode <- 401
            elif req.Url.AbsolutePath <> "/mcp/" then response.StatusCode <- 404
            elif req.HttpMethod <> "POST" then
                response.StatusCode <- 405
                response.Headers.Add("Allow", "POST")
            elif not (isNull protocol || List.contains protocol ["2025-03-26"; "2025-06-18"; "2025-11-25"]) then
                response.StatusCode <- 400
            else
                let! message = JsonSerializer.DeserializeAsync<JsonRpcMessage>(req.InputStream, McpJsonUtilities.DefaultOptions, token)
                match message with
                | :? JsonRpcRequest ->
                    response.ContentType <- "text/event-stream"
                    response.SendChunked <- true
                    let transport = StreamableHttpServerTransport(Stateless = true)
                    let options = McpServerOptions(ServerInfo = Implementation(Name = "MyDebugTools", Version = version),
                                                   ProtocolVersion = "2025-11-25", ToolCollection = tools,
                                                   KnownClientInfo = Implementation(Name = "local", Version = "unknown"),
                                                   KnownClientCapabilities = ClientCapabilities())
                    use! server = Task.FromResult(McpServer.Create(transport, options))
                    let running = server.RunAsync token
                    try
                        let! _ = transport.HandlePostRequestAsync(message, response.OutputStream, token)
                        ()
                    finally
                        transport.DisposeAsync().AsTask().GetAwaiter().GetResult()
                    do! running
                | :? JsonRpcNotification | :? JsonRpcResponse -> response.StatusCode <- 202
                | _ -> response.StatusCode <- 400
        with
        | :? JsonException -> response.StatusCode <- 400
        | :? OperationCanceledException | :? HttpListenerException | :? ObjectDisposedException -> ()
        | _ -> response.StatusCode <- 500
    }

    let start () =
        let http = new HttpListener()
        try
            http.Prefixes.Add(endpoint())
            http.Start()
            let stop = new CancellationTokenSource()
            let token = stop.Token
            listener <- Some (http, stop)
            Task.Run(Func<Task>(fun () -> task {
                try
                    while not token.IsCancellationRequested do
                        let! ctx = http.GetContextAsync()
                        serve ctx token |> ignore
                with
                | :? HttpListenerException | :? ObjectDisposedException when token.IsCancellationRequested -> ()
            })) |> ignore
        with e ->
            http.Close()
            failure <- $"{endpoint()} · {e.Message}"

    let stop () =
        listener |> Option.iter (fun (http, cancel) ->
            cancel.Cancel()
            http.Close()
            cancel.Dispose())
        listener <- None

    do if settings.Enabled then start()

    member _.Running = listener.IsSome
    member _.Enabled = settings.Enabled
    member _.Error = failure
    member _.Configuration =
        JsonSerializer.Serialize({| mcpServers = {| MyDebugTools = {| url = endpoint(); headers = {| Authorization = "Bearer " + settings.Token |} |} |} |})

    member _.SetEnabled enabled =
        let port =
            if enabled && settings.Port = 0 then
                use probe = new TcpListener(IPAddress.Loopback, 0)
                probe.Start()
                (probe.LocalEndpoint :?> IPEndPoint).Port
            else settings.Port
        save { settings with Enabled = enabled; Port = port }
        failure <- ""
        if enabled && listener.IsNone then start()
        elif not enabled then stop()

    interface IDisposable with member _.Dispose() = stop()
