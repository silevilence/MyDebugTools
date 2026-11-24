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

/// Real collectible plugin that fails after declaring UI and owning live resources.
type PartialActivation() =
    interface IDesktopPlugin with
        member _.Activate registry =
            registry.Page { Id = "partial.page"; Title = "partial"; Create = fun () -> failwith "unused" }
            let subscription = AppDomain.CurrentDomain.ProcessExit.Subscribe(fun _ -> GC.KeepAlive registry)
            let stop = new System.Threading.CancellationTokenSource()
            let worker = Task.Delay(System.Threading.Timeout.Infinite, stop.Token)
            registry.Own { new IDisposable with
                               member _.Dispose() =
                                   subscription.Dispose()
                                   stop.Cancel()
                                   AppContext.SetData("MyDebugTools.test.partial.stopped", worker.IsCanceled)
                                   stop.Dispose() }
            failwith "fixture activation fault"

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

[<Fact>]
let ``two rebuilds rollback failures and queued uninstall release old versions`` () = task {
    let folder = Directory.CreateTempSubdirectory("MyDebugTools-replace-")
    let output = Path.Combine(folder.FullName, "dev")
    let id = "thirdparty.A"
    try
        use host = new Host()
        host.Add { Id = "provider"; Title = ""; Core = true; Create = fun () -> Workbench() }
        let catalog = LocalPlugins(host, Path.Combine(folder.FullName, "formal.json"))
        let approved = TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
        let waiting = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        let mutable pause = false
        let approve _ _ =
            if pause then
                waiting.TrySetResult() |> ignore
                approved.Task
            else Task.FromResult true
        let commands = TemporaryCommands(catalog, (fun run -> run()), approve, (fun _ -> Task.FromResult true), ignore)
        use server = new DeveloperServer(Path.Combine(folder.FullName, "dev.json"), "test", commands.Tools)
        server.SetEnabled true
        use! client = DeveloperServer.connect server.Configuration
        let install () = invoke client "install_temporary" ["directory", box output]
        let check revision =
            Assert.Equal<string list>([$"A v{revision} · 请求 A"], Reload.results host)
            let _, pending = catalog.CheckUnloads()
            Assert.Empty pending
        for revision in ["1"; "2"; "1"] do
            Reload.buildThirdParty output "A" revision
            let! error, message = install()
            Assert.False(error, message)
            check revision
        let original = File.ReadAllText(Path.Combine(output, "plugin.json"))
        let testAssembly = typeof<PartialActivation>.Assembly
        File.Copy(testAssembly.Location, Path.Combine(output, "fixture.dll"))
        let failures = [
            "missing entry", fun m -> { m with EntryType = "Does.Not.Exist" }
            "fixture activation fault", fun m -> { m with Assembly = "fixture.dll"; EntryType = typeof<PartialActivation>.FullName }
        ]
        for why, update in failures do
            manifest output update
            let! failed, reason = install()
            Assert.True(failed, reason)
            Assert.Contains(id, reason)
            Assert.Contains("激活临时版", reason)
            if why <> "missing entry" then
                Assert.Contains(why, reason)
                Assert.Equal(box true, AppContext.GetData("MyDebugTools.test.partial.stopped"))
            Assert.DoesNotContain(host.Pages, fun p -> p.Id = "partial.page")
            check "1"
        File.WriteAllText(Path.Combine(output, "plugin.json"), original)
        pause <- true
        let replacing = install()
        do! waiting.Task.WaitAsync(TimeSpan.FromSeconds 10.)
        let removing = invoke client "uninstall_temporary" ["id", box id]
        approved.SetResult(true)
        let! replaced, detail = replacing
        Assert.False(replaced, detail)
        let! removed, detail = removing
        Assert.False(removed, detail)
        Assert.False(catalog.IsTemporary id)
        Assert.Empty(Reload.results host)
        let _, pending = catalog.CheckUnloads()
        Assert.Empty pending
    finally folder.Delete(true)
}
