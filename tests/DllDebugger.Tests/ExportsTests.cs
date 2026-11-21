using MyDebugTools.DllDebugger;
using Xunit;

namespace DllDebugger.Tests;

public class ExportsTests
{
    [Fact]
    public void MarksUnclassifiedSectionsUnknown()
    {
        var bytes = File.ReadAllBytes(Fixture("x64"));
        int coff = BitConverter.ToInt32(bytes, 0x3c) + 4;
        int firstSection = coff + 20 + BitConverter.ToUInt16(bytes, coff + 16);
        // Clear first section flags. The RVA is still exported but the section no longer identifies code or data.
        Array.Clear(bytes, firstSection + 36, 4);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, bytes);
            Assert.Contains(DllImage.Open(path).Exports, e => e.Entry == "add" && e.Kind == "未知");
        }
        finally { File.Delete(path); }
    }

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
        Assert.Contains(image.Exports, e => e.Entry == "forwarded" && e.Forwarder == "KERNEL32.GetTickCount");
        var rejecting = DllImage.Open(Path.Combine(Path.GetDirectoryName(image.Path)!, "reject.dll"));
        Assert.Contains(rejecting.Exports, e => e.Name == "add");
    }
}
