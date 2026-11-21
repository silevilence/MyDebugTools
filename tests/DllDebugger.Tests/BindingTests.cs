using System.Diagnostics;
using MyDebugTools.DllDebugger;
using Xunit;

namespace DllDebugger.Tests;

public class BindingTests
{
    static async Task<string> Run(string directory, string executable, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var p = Process.Start(start)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        string result = await stdout + await stderr;
        Assert.True(p.ExitCode == 0, result);
        return result;
    }

    [Theory]
    [InlineData(BindingFormat.CSharp)]
    [InlineData(BindingFormat.FSharp)]
    [InlineData(BindingFormat.LibraryImport)]
    public async Task CompilesAndRunsSelectedBindings(BindingFormat format)
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var image = DllImage.Open(ExportsTests.Fixture("x64"));
            var store = new SignatureStore(dir.FullName);
            store.Confirm(image, SessionTests.Sig("fill", "i32",
                new("text", "string", Encoding: "utf8", Ownership: "caller"), new("dst", "buffer", "out", Length: 4, Ownership: "caller"),
                new("length", "i32*", "out", Ownership: "caller")));
            store.Confirm(image, SessionTests.Sig("adjust", "i32", new Parameter("r", "Record*", "inout", Ownership: "caller")) with
                { Layouts = SignatureText.Layouts("Record pack=8 tag:u8 size:usize value:i32") });
            var args = """{"text":"猫","r":{"tag":3,"size":6,"value":4}}""";
            var single = BindingGenerator.Generate(image, store, ["fill"], format, args);
            Assert.DoesNotContain("adjust", single.Code);
            var batch = BindingGenerator.Generate(image, store, ["adjust", "fill", "count"], format);
            Assert.Single(batch.Skipped);
            Assert.Contains("count", batch.Skipped[0]);
            Assert.Contains("S_Record", batch.Code);
            Assert.DoesNotContain("Example()", batch.Code);
            var generated = BindingGenerator.Generate(image, store, ["fill", "count", "adjust"], format, args);
            Assert.Single(generated.Skipped);
            bool fs = format == BindingFormat.FSharp;
            string ext = fs ? "fs" : "cs";
            string project = Path.Combine(dir.FullName, "Example." + ext + "proj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>" +
                "<OutputType>Exe</OutputType><AllowUnsafeBlocks>true</AllowUnsafeBlocks></PropertyGroup>" +
                (fs ? "<ItemGroup><Compile Include=\"Bindings.fs\"/><Compile Include=\"Program.fs\"/></ItemGroup>" : "") + "</Project>");
            File.WriteAllText(Path.Combine(dir.FullName, "Bindings." + ext), generated.Code);
            File.WriteAllText(Path.Combine(dir.FullName, "Program." + ext), fs ?
                "open Bindings\nNative_fill_1.Example()\nNative_adjust_2.Example()\n" : "Native_fill_1.Example();\nNative_adjust_2.Example();\n");
            string cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            await Run(dir.FullName, "dotnet", "restore", project, "--source", cache, "-p:NuGetAudit=false");
            await Run(dir.FullName, "dotnet", "build", project, "--no-restore");
            string output = await Run(dir.FullName, "dotnet", Path.Combine("bin", "Debug", "net10.0", "Example.dll"));
            Assert.Contains("231", output);
            Assert.Contains("171", output);
            Assert.Contains("24", output);
            Assert.Contains("13", output);
        }
        finally { dir.Delete(true); }
    }
}
