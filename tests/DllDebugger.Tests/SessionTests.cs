using MyDebugTools.DllDebugger;
using Xunit;

namespace DllDebugger.Tests;

public class SessionTests
{
    internal static CallSession Open(string arch)
    {
        var folder = arch == "x86" ? Environment.SpecialFolder.ProgramFilesX86 : Environment.SpecialFolder.ProgramFiles;
        return new(DllImage.Open(ExportsTests.Fixture(arch)), Path.Combine(Environment.GetFolderPath(folder), "dotnet", "dotnet.exe"));
    }

    internal static Signature Sig(string name, string result, params Parameter[] args) => new(name, result, "cdecl", args, [], []);

    [Theory]
    [InlineData("x86")]
    [InlineData("x64")]
    public async Task ScalarStateAndRecovery(string arch)
    {
        var add = Sig("add", "i32", new("a", "i32"), new("b", "i32"));
        var number = Sig("open_handle", "handle");
        var use = Sig("use_handle", "i32", new Parameter("h", "handle", Ownership: "dll"));
        using var session = Open(arch);
        Assert.Equal(12, (await session.Invoke(add, """{"a":5,"b":7}""")).Result.GetInt32());
        Assert.Equal(42, (await session.Invoke(Sig("#4", "i32"), "{}")).Result.GetInt32());
        Assert.Equal(3.75, (await session.Invoke(Sig("twice", "f64", new Parameter("v", "f64")), """{"v":1.875}""")).Result.GetDouble());
        var h = (await session.Invoke(number, "{}")).Result.GetRawText();
        Assert.Equal(1, (await session.Invoke(Sig("count", "i32"), "{}")).Result.GetInt32());
        Assert.Equal(2, (await session.Invoke(Sig("count", "i32"), "{}")).Result.GetInt32());
        string input = "{\"h\":" + h + "}";
        Assert.Equal(1, (await session.Invoke(use, input)).Result.GetInt32());
        await session.Invoke(use with { Entry = "close_handle", Result = "void" }, input);
        Assert.Equal(0, (await session.Invoke(use, input)).Result.GetInt32());
        await Assert.ThrowsAsync<IOException>(() => session.Invoke(Sig("spin", "void"), "{}", 400));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Invoke(add, "{}"));
        using var restarted = Open(arch);
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.Invoke(use, input));
        Assert.Equal(1, (await restarted.Invoke(Sig("count", "i32"), "{}")).Result.GetInt32());
        var stop = Sig("end_session", "void", new Parameter("code", "u32")) with { Convention = "stdcall" };
        await Assert.ThrowsAsync<IOException>(() => restarted.Invoke(stop, """{"code":0}"""));
        using var afterExit = Open(arch);
        Assert.Equal(3, (await afterExit.Invoke(add, """{"a":1,"b":2}""")).Result.GetInt32());
    }
}
