module MyDebugTools.Tests.Reload

open System
open System.Diagnostics
open System.IO
open System.Text.Json
open Xunit
open MyDebugTools.Host
open MyDebugTools.Samples

let buildSample name output properties =
    let project = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, $"../../samples/{name}/{name}.fsproj"))
    let start = ProcessStartInfo("dotnet", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true)
    for arg in [ "build"; project; "--no-restore"; "-o"; output ] @ properties do
        start.ArgumentList.Add arg
    use build = Process.Start start
    let log = build.StandardOutput.ReadToEnd()
    build.WaitForExit()
    Assert.True(build.ExitCode = 0, log)

let buildCompany output edition revision =
    buildSample "CompanyTools" output ["-p:SampleEdition=" + edition; "-p:SampleRevision=" + revision]
    let manifest = Path.Combine(output, "plugin.json")
    let original = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText manifest)
    File.WriteAllText(manifest, JsonSerializer.Serialize({ original with Id = "company." + edition }))

let results (host: Host) =
    host.Actions "sample.workbench" |> List.map (fun (_, invoke) -> invoke()) |> List.sort

[<Fact>]
let ``external FSharp plugins rebuild in place and release each old dependency graph`` () =
    let folder = Directory.CreateTempSubdirectory("MyDebugTools-test-")
    let settings = Path.Combine(folder.FullName, "settings.json")
    let directory edition = Path.Combine(folder.FullName, edition)
    try
        use host = new Host()
        host.Add { Id = "provider"; Title = ""; Core = false; Create = fun () -> Workbench() }
        host.Enable "provider"
        let catalog = LocalPlugins(host, settings)
        for edition in ["A"; "B"] do
            buildCompany (directory edition) edition "1"
            catalog.AddDirectory(directory edition)
            host.Enable("company." + edition)
        Assert.Equal<string list>(["A v1 · 请求 A"; "B v1 · 请求 A"], results host)
        for revision in ["2"; "1"] do
            buildCompany (directory "A") "A" revision
            host.Disable "company.A"
            host.Enable "company.A"
            Assert.Equal<string list>([$"A v{revision} · 请求 A"; "B v1 · 请求 A"], results host)
            let _, pending = catalog.CheckUnloads()
            Assert.Empty pending
        host.Disable "company.A"
        host.Disable "company.B"
        let count, pending = catalog.CheckUnloads()
        Assert.Equal(2, count)
        Assert.Empty pending
        use reopened = new Host()
        Assert.Empty(LocalPlugins(reopened, settings).Restore())
        Assert.Equal(2, reopened.Plugins.Length)
    finally
        folder.Delete(true)
