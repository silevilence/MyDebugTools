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
        let preview = PluginPackage.preview zip
        let settings = Path.Combine(temp.FullName, "plugins.json")
        use host = new Host()
        let catalog = LocalPlugins(host, settings)
        catalog.Install preview
        catalog.Install preview
        Assert.Single(host.Plugins) |> ignore
        Assert.True(host.Enabled preview.Manifest.Id)
        let replacement = Path.Combine(temp.FullName, "new.zip")
        Reload.buildThirdParty build "A" "2"
        ZipFile.CreateFromDirectory(build, replacement)
        let updated = PluginPackage.preview replacement
        catalog.Replace(updated, ignore)
        Assert.Equal(updated.Hash, (catalog.Package preview.Manifest.Id).Value.Hash)
        let invalid = Path.Combine(temp.FullName, "invalid.zip")
        File.WriteAllText(Path.Combine(build, updated.Manifest.Assembly), "invalid")
        ZipFile.CreateFromDirectory(build, invalid)
        Assert.ThrowsAny<Exception>(fun () -> catalog.Replace(PluginPackage.preview invalid, ignore)) |> ignore
        Assert.True(host.Enabled preview.Manifest.Id)
        Assert.Equal(updated.Hash, (catalog.Package preview.Manifest.Id).Value.Hash)
        use restored = new Host()
        let reopened = LocalPlugins(restored, settings)
        Assert.Empty(reopened.Restore())
        Assert.Equal("外部 ZIP", reopened.Source preview.Manifest.Id)
        Assert.True(File.Exists zip)
    finally
        temp.Delete(true)
