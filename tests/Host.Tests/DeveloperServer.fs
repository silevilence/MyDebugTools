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
open MyDebugTools.Contracts
open MyDebugTools.Samples

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
let ``same ID retains the formal preference and reports one effective version`` () =
    let formal = { Id = "sample"; Source = "formal"; Origin = "外部目录"; Enabled = false
                   Version = "1"; State = "disabled"; EffectiveVersion = None; Reasons = [] }
    let temporary = { formal with Source = "temporary"; Enabled = true; Version = "2"; State = "active"; EffectiveVersion = Some "2" }
    let rows = PluginStatus.effectiveSources (Some formal) (Some temporary)
    Assert.Equal<string list>(["formal"; "temporary"], rows |> List.map _.Source)
    Assert.Equal("superseded", rows.Head.State)
    Assert.False(rows.Head.Enabled)
    Assert.All(rows, fun row -> Assert.Equal(Some "2", row.EffectiveVersion))

[<Fact>]
let ``interface views reflect live declarations without invoking plugin code`` () = task {
    let folder = Directory.CreateTempSubdirectory("MyDebugTools-inspect-")
    try
        use host = new Host()
        host.Add { Id = "workbench"; Title = ""; Core = false; Create = fun () -> Workbench() }
        host.Add { Id = "legacy"; Title = ""; Core = true
                   Create = fun () ->
                       { new IDesktopPlugin with
                            member _.Activate r = r.Page { Id = "empty"; Title = ""; Create = fun () -> failwith "UI only" } } }
        let catalog = LocalPlugins(host, Path.Combine(folder.FullName, "plugins.json"))
        let inspect = Inspection(host, catalog, fun read -> read())
        use server = new DeveloperServer(Path.Combine(folder.FullName, "dev.json"), "sample", inspect.Tools)
        server.SetEnabled true
        use! client = connect server.Configuration
        let query name args = task {
            let! answer = client.CallToolAsync(name, readOnlyDict args)
            let text = (answer.Content[0] :?> TextContentBlock).Text
            Assert.False(answer.IsError.GetValueOrDefault(), text)
            return JsonDocument.Parse(text).RootElement.Clone()
        }
        let! initial = query "list_plugins" []
        Assert.Equal("disabled", initial[0].GetProperty("state").GetString())
        host.Enable "workbench"
        let! pages = query "list_interfaces" []
        let! full = query "list_interface_details" []
        Assert.Equal(2, pages.GetArrayLength())
        for p in full.EnumerateArray() do
            let id = p.GetProperty("id").GetString()
            let! detail = query "get_interface" ["id", box id]
            Assert.Equal(detail.GetRawText(), p.GetRawText())
            if id = "empty" then
                Assert.Equal("未声明", p.GetProperty("description").GetString())
                Assert.Equal(0, p.GetProperty("points").GetArrayLength())
            else
                Assert.Equal(2, p.GetProperty("points").GetArrayLength())
        host.Disable "workbench"
        let! missing = client.CallToolAsync("get_interface", readOnlyDict ["id", box "sample.workbench"])
        Assert.True(missing.IsError.GetValueOrDefault())
    finally
        folder.Delete(true)
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
