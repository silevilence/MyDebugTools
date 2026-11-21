using MyDebugTools.DllDebugger;
using Xunit;

namespace DllDebugger.Tests;

public class DeclarationTests
{
    [Fact]
    public void KeepsFieldOrderLocationsAndUnresolvedParameters()
    {
        string c = "typedef struct { unsigned char tag; size_t size; int value; } Record;\nint __stdcall adjust(Record *r, MISSING x);";
        var parsed = CDeclaration.Parse(c, "adjust");
        var s = parsed.Signature;
        Assert.Equal("stdcall", s.Convention);
        Assert.Equal("i32", s.Result);
        Assert.Equal("Record*", s.Parameters![0].Type);
        Assert.Equal("?", s.Parameters[1].Type);
        Assert.Equal("Record pack=8 tag:u8 size:usize value:i32", SignatureText.FormatLayout(s.Layouts[0]));
        Assert.Equal("C 声明", s.Sources["r.类型"]);
        Assert.Contains(parsed.Notes, note => note.Contains("第 2 行，第 33 列") && note.Contains("MISSING"));
        var fixedSig = s with { Parameters = [s.Parameters[0] with { Direction = "inout", Ownership = "caller" }, new("x", "i32")] };
        fixedSig.Validate();
        Assert.Empty(CDeclaration.Parse("double __cdecl twice(double v);", "").Notes);
    }
}
