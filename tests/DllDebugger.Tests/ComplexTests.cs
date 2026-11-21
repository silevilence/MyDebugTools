using MyDebugTools.DllDebugger;
using Xunit;

namespace DllDebugger.Tests;

public class ComplexTests
{
    [Theory]
    [InlineData("x86", 12)]
    [InlineData("x64", 24)]
    public async Task ReadsWrittenMemoryInTargetLayout(string arch, int size)
    {
        using var session = SessionTests.Open(arch);
        var s = SessionTests.Sig("fill", "i32",
            new("text", "string", Encoding: "utf8", Ownership: "caller"),
            new("dst", "buffer", "out", Length: 4, Ownership: "caller"),
            new("length", "i32*", "out", Ownership: "caller"));
        var r = await session.Invoke(s, """{"text":"猫"}""");
        Assert.Equal(3, r.Result.GetInt32());
        Assert.Equal("[231,140,171,0]", r.Outputs["dst"].GetRawText());
        Assert.Equal(3, r.Outputs["length"].GetInt32());
        var record = SessionTests.Sig("adjust", "i32", new Parameter("r", "Record*", "inout", Ownership: "caller")) with
            { Layouts = SignatureText.Layouts("Record pack=8 tag:u8 size:usize value:i32") };
        r = await session.Invoke(record, """{"r":{"tag":3,"size":6,"value":4}}""");
        Assert.Equal(size, r.Result.GetInt32());
        Assert.Equal(13, r.Outputs["r"].GetProperty("value").GetInt32());
        Assert.Throws<InvalidDataException>(() => (s with { Parameters = [s.Parameters![1] with { Length = null }] }).Validate());
    }
}
