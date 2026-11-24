module MyDebugTools.Tests.DeveloperServer

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading
open MyDebugTools.Host
open ModelContextProtocol.Client
open ModelContextProtocol.Protocol
open Xunit

let connect (configuration: string) = task {
    use config = JsonDocument.Parse configuration
    let entry = config.RootElement.GetProperty("mcpServers").GetProperty("MyDebugTools")
    let headers = Dictionary<string,string>()
    headers.Add("Authorization", entry.GetProperty("headers").GetProperty("Authorization").GetString())
    let endpoint = Uri(entry.GetProperty("url").GetString())
    let transport = new HttpClientTransport(HttpClientTransportOptions(Endpoint = endpoint, AdditionalHeaders = headers,
                                                                      TransportMode = HttpTransportMode.StreamableHttp))
    return! McpClient.CreateAsync(transport, McpClientOptions(ProtocolVersion = "2025-11-25"))
}

[<Fact>]
let ``local client reads versioned documentation after restart and checks authorization and origin`` () = task {
    let folder = Directory.CreateTempSubdirectory("MyDebugTools-mcp-test-")
    let path = Path.Combine(folder.FullName, "developer.json")
    try
        use first = new DeveloperServer(path, "1.2.3", [])
        Assert.False(first.Enabled)
        first.SetEnabled true
        Assert.True(first.Running, first.Error)
        let config = first.Configuration
        use conflict = new DeveloperServer(path, "1.2.3", [])
        Assert.False(conflict.Running)
        Assert.NotEmpty(conflict.Error)
        (first :> IDisposable).Dispose()
        use second = new DeveloperServer(path, "1.2.3", [])
        Assert.Equal(config, second.Configuration)
        use! client = connect config
        let! reply = client.CallToolAsync("read_documentation")
        let guide = (reply.Content[0] :?> TextContentBlock).Text
        for expected in ["1.2.3"; "plugin.json"; "type Registration"; "install_temporary"; "uninstall_temporary"] do
            Assert.Contains(expected, guide)
        use file = JsonDocument.Parse config
        let entry = file.RootElement.GetProperty("mcpServers").GetProperty("MyDebugTools")
        let url = entry.GetProperty("url").GetString()
        use http = new HttpClient()
        let! unauthorized = http.GetAsync url
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode)
        http.DefaultRequestHeaders.Add("Origin", "https://example.invalid")
        http.DefaultRequestHeaders.Add("Authorization", entry.GetProperty("headers").GetProperty("Authorization").GetString())
        let! forbidden = http.GetAsync url
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode)
        second.SetEnabled false
        Assert.False(second.Running)
        use reopened = new DeveloperServer(path, "1.2.3", [])
        Assert.False(reopened.Enabled)
        Assert.Equal(config, reopened.Configuration)
    finally
        folder.Delete(true)
}
