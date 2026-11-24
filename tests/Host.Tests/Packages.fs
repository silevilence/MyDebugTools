module MyDebugTools.Tests.Packages

open System
open System.IO
open System.IO.Compression
open MyDebugTools.Host
open Xunit

[<Fact>]
let ``install is repeatable and survives reopening the catalog`` () =
    let temp = Directory.CreateTempSubdirectory("MyDebugTools-package-test-")
    try
        let build = Path.Combine(temp.FullName, "build")
        Reload.buildThirdParty build "A" "1"
        let zip = Path.Combine(temp.FullName, "sample.zip")
        ZipFile.CreateFromDirectory(build, zip)
        let hash file = File.ReadAllBytes(file) |> Security.Cryptography.SHA256.HashData |> Convert.ToHexString
        let zipHash = hash zip
        let preview = PluginPackage.preview zip
        let settings = Path.Combine(temp.FullName, "plugins.json")
        use host = new Host()
        let catalog = LocalPlugins(host, settings)
        catalog.Install preview
        catalog.Install preview
        Assert.Single(host.Plugins) |> ignore
        Assert.True(host.Enabled preview.Manifest.Id)
        host.Add { Id = "provider"; Title = "sample"; Core = true; Create = fun () -> MyDebugTools.Samples.Workbench() }
        Assert.Contains("A v1 · 请求 A", Reload.results host)
        let replacement = Path.Combine(temp.FullName, "new.zip")
        Reload.buildThirdParty build "A" "2"
        ZipFile.CreateFromDirectory(build, replacement)
        let updated = PluginPackage.preview replacement
        catalog.Replace(updated, ignore)
        Assert.Equal(updated.Hash, (catalog.Package preview.Manifest.Id).Value.Hash)
        Assert.Contains("A v2 · 请求 A", Reload.results host)
        let invalid = Path.Combine(temp.FullName, "invalid.zip")
        File.Copy(replacement, invalid)
        do
            use archive = ZipFile.Open(invalid, ZipArchiveMode.Update)
            archive.GetEntry(updated.Manifest.Assembly).Delete()
            use writer = new StreamWriter(archive.CreateEntry(updated.Manifest.Assembly).Open())
            writer.Write("invalid")
        Assert.ThrowsAny<Exception>(fun () -> catalog.Replace(PluginPackage.preview invalid, ignore)) |> ignore
        Assert.True(host.Enabled preview.Manifest.Id)
        Assert.Equal(updated.Hash, (catalog.Package preview.Manifest.Id).Value.Hash)
        Assert.Contains("A v2 · 请求 A", Reload.results host)
        use restored = new Host()
        let reopened = LocalPlugins(restored, settings)
        Assert.Empty(reopened.Restore())
        Assert.Equal("外部 ZIP", reopened.Source preview.Manifest.Id)
        let installed = (reopened.Directory preview.Manifest.Id).Value
        let held = File.Open(Path.Combine(installed, "plugin.json"), FileMode.Open, FileAccess.Read, FileShare.Read)
        try
            Assert.ThrowsAny<IOException>(fun () -> reopened.RemoveExternal(preview.Manifest.Id, ignore)) |> ignore
            Assert.True((reopened.Package preview.Manifest.Id).Value.PendingRemoval)
        finally held.Dispose()
        reopened.RemoveExternal(preview.Manifest.Id, ignore)
        Assert.False(Directory.Exists installed)
        Assert.Empty(restored.Plugins)
        let snapshot () = Directory.GetFiles(build, "*", SearchOption.AllDirectories) |> Array.map (fun f -> f, hash f) |> Map.ofArray
        let original = snapshot()
        reopened.AddDirectory build
        reopened.RemoveExternal(preview.Manifest.Id, ignore)
        Assert.True(File.Exists(Path.Combine(build, "plugin.json")))
        Assert.Equal<Map<string, string>>(original, snapshot())
        Assert.Equal(zipHash, hash zip)
        Assert.True(File.Exists zip)
        Assert.Empty(reopened.Restore())
        Assert.Empty(restored.Plugins)
    finally
        temp.Delete(true)

[<Theory>]
[<InlineData("../outside.dll", 0)>]
[<InlineData("link.dll", 1024)>]
[<InlineData("plugin.json", 0)>]
let ``invalid archives fail during preview`` (entry: string, attributes: int) =
    let temp = Directory.CreateTempSubdirectory("MyDebugTools-zip-validation-")
    try
        let zip = Path.Combine(temp.FullName, "invalid.zip")
        do
            use archive = ZipFile.Open(zip, ZipArchiveMode.Create)
            let memberEntry = archive.CreateEntry(entry)
            memberEntry.ExternalAttributes <- attributes
            if entry = "plugin.json" then
                use writer = new StreamWriter(memberEntry.Open())
                writer.Write("""{"Id":"example","Title":"example","Assembly":"absent.dll","EntryType":"Example.Tools"}""")
        Assert.ThrowsAny<Exception>(fun () -> PluginPackage.preview zip |> ignore) |> ignore
        Assert.Single(Directory.GetFileSystemEntries(temp.FullName)) |> ignore
    finally temp.Delete(true)
