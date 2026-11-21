using MyDebugTools.DllDebugger;
using Xunit;

namespace DllDebugger.Tests;

public class PdbTests
{
    [Fact]
    public void UsesOnlyMatchingLocalTypes()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            string dll = Path.Combine(dir.FullName, "fixture.dll"), pdb = Path.ChangeExtension(dll, ".pdb");
            File.Copy(ExportsTests.Fixture("x64"), dll);
            Assert.Contains("未找到", DllImage.Open(dll).Symbols.Status);
            File.Copy(Path.ChangeExtension(ExportsTests.Fixture("x86"), ".pdb"), pdb);
            Assert.Contains("不匹配", DllImage.Open(dll).Symbols.Status);
            File.Copy(Path.ChangeExtension(ExportsTests.Fixture("x64"), ".pdb"), pdb, true);
            var loaded = DllImage.Open(dll);
            var match = new SignatureStore(dir.FullName).Find(loaded, "add");
            Assert.False(match.Confirmed);
            Assert.Equal("i32", match.Signature.Result);
            Assert.Equal("a", match.Signature.Parameters![0].Name);
            Assert.Equal("i32", match.Signature.Parameters[1].Type);
            Assert.Equal("本地 PDB", match.Signature.Sources["返回类型"]);
            Assert.Contains("缺少", DllImage.Open(Path.Combine(Path.GetDirectoryName(ExportsTests.Fixture("x64"))!, "public.dll")).Symbols.Status);
        }
        finally { dir.Delete(true); }
    }
}
