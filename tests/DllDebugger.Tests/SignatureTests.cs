using MyDebugTools.DllDebugger;
using Xunit;

namespace DllDebugger.Tests;

public class SignatureTests
{
    [Fact]
    public void ConfirmedSignaturesFollowBytesAndRequireReviewOnReplacement()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string path = Path.Combine(root, "work.dll");
            File.Copy(ExportsTests.Fixture("x86"), path);
            var image = DllImage.Open(path);
            var s = new Signature("add", "i32", "cdecl", SignatureText.Parameters("a i32 in\nb i32 in"), [], []).WithSource("手工录入");
            new SignatureStore(root).Confirm(image, s);
            Assert.True(new SignatureStore(root).Find(image, "add").Confirmed);
            File.Copy(ExportsTests.Fixture("x64"), path, true);
            var next = DllImage.Open(path);
            var match = new SignatureStore(root).Find(next, "add");
            Assert.False(match.Confirmed);
            Assert.Equal(s.Parameters, match.Signature.Parameters);
            Assert.Equal(s.Sources, match.Signature.Sources);
            new SignatureStore(root).Confirm(next, match.Signature);
            Assert.True(new SignatureStore(root).Find(next, "add").Confirmed);
        }
        finally { Directory.Delete(root, true); }
    }
}
