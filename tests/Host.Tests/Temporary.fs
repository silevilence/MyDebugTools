module MyDebugTools.Tests.Temporary

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open MyDebugTools.Contracts
open MyDebugTools.Host
open MyDebugTools.Samples
open ModelContextProtocol.Client
open ModelContextProtocol.Protocol
open Xunit

let invoke (client: McpClient) name arguments = task {
    let! result = client.CallToolAsync(name, readOnlyDict arguments)
    return result.IsError.GetValueOrDefault(), (result.Content[0] :?> TextContentBlock).Text
}

let manifest directory update =
    let file = Path.Combine(directory, "plugin.json")
    let m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText file)
    File.WriteAllText(file, JsonSerializer.Serialize(update m))

[<Fact>]
let ``temporary installs restore pure disabled and enabled sources through the same MCP client`` () = task {
    let folder = Directory.CreateTempSubdirectory("MyDebugTools-temporary-")
    let dev = Path.Combine(folder.FullName, "dev")
    let installed = Path.Combine(folder.FullName, "formal")
    let settings = Path.Combine(folder.FullName, "paths.json")
    let id = "thirdparty.A"
    try
        use host = new Host()
        host.Add { Id = "provider"; Title = ""; Core = true; Create = fun () -> Workbench() }
        let catalog = LocalPlugins(host, settings)
        let mutable approved = false
        let mutable stopAllowed = true
        let commands = TemporaryCommands(catalog, (fun run -> run()), (fun _ _ -> Task.FromResult approved),
                                          (fun _ -> Task.FromResult stopAllowed), ignore)
        let inspect = Inspection(host, catalog, fun read -> read())
        use server = new DeveloperServer(Path.Combine(folder.FullName, "developer.json"), "test", commands.Tools @ inspect.Tools)
        server.SetEnabled true
        use! client = DeveloperServer.connect server.Configuration
        let install () = invoke client "install_temporary" ["directory", box dev]
        let uninstall () = invoke client "uninstall_temporary" ["id", box id]
        Reload.buildThirdParty dev "A" "2"
        manifest dev (fun m -> { m with Version = "2" })
        let! _, cancelled = install()
        Assert.Contains("已取消", cancelled)
        Assert.False(host.Enabled id)
        approved <- true
        for state in ["pure"; "disabled"; "enabled"] do
            if state = "disabled" then
                Reload.buildThirdParty installed "A" "1"
                manifest installed (fun m -> { m with Version = "1" })
                catalog.AddDirectory installed
            if state = "enabled" then host.Enable id
            let! failed, detail = install()
            Assert.False(failed, detail)
            Assert.Equal<string list>(["A v2 · 请求 A"], Reload.results host)
            let! _, json = invoke client "list_plugins" []
            use rows = JsonDocument.Parse json
            let same = rows.RootElement.EnumerateArray() |> Seq.filter (fun r -> r.GetProperty("id").GetString() = id) |> Seq.toArray
            Assert.Equal((if state = "pure" then 1 else 2), same.Length)
            Assert.Equal("temporary", same[same.Length - 1].GetProperty("source").GetString())
            Assert.Equal((state = "enabled"), Array.contains id catalog.FormalEnabled)
            use reopened = new Host()
            Assert.Empty(LocalPlugins(reopened, settings).Restore())
            Assert.Equal((state <> "pure"), reopened.Plugins |> List.exists (fun p -> p.Id = id))
            stopAllowed <- false
            let! _, continuing = uninstall()
            Assert.Contains("取消", continuing)
            Assert.True(host.Enabled id)
            stopAllowed <- true
            let! error, message = uninstall()
            Assert.False(error, message)
            Assert.Equal((state = "enabled"), host.Enabled id)
            Assert.Equal((state <> "pure"), host.Plugins |> List.exists (fun p -> p.Id = id))
            Assert.True(File.Exists(Path.Combine(dev, "plugin.json")))
        manifest dev (fun m -> { m with EntryType = "Missing" })
        let! bad, why = install()
        Assert.True(bad, why)
        Assert.Contains(id, why)
        Assert.Contains("激活临时版", why)
        Assert.Equal<string list>(["A v1 · 请求 A"], Reload.results host)
        manifest dev (fun m -> { m with Id = "provider" })
        let! core, reason = install()
        Assert.True(core)
        Assert.Contains("核心插件", reason)
        host.Disable id
        let _, pending = catalog.CheckUnloads()
        Assert.Empty pending
    finally
        folder.Delete(true)
}
