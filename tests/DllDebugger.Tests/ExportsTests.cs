using MyDebugTools.DllDebugger;
using Xunit;

namespace DllDebugger.Tests;

public class ExportsTests
{
    internal static string Fixture(string arch) => Path.Combine(AppContext.BaseDirectory, arch, "fixture.dll");

    [Theory]
    [InlineData("x86")]
    [InlineData("x64")]
    public void ReadsNativeExports(string arch)
    {
        var image = DllImage.Open(Fixture(arch));
        Assert.Equal(arch, image.Architecture);
        Assert.Contains(image.Exports, e => e.Name == "add" && e.Ordinal == 3 && e.Rva == 0x1000 && e.Kind == "代码");
        Assert.Contains(image.Exports, e => e.Entry == "#4" && e.Kind == "代码");
        Assert.Contains(image.Exports, e => e.Entry == "data" && e.Kind == "数据");
        Assert.Contains(image.Exports, e => e.Entry == "forwarded" && e.Forwarder!.EndsWith("KERNEL32.GetTickCount"));
    }
}
