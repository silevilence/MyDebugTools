using MyDebugTools.DllDebugger;
using Xunit;

public class WindowsApiTests
{
    [Theory]
    [InlineData("GetCurrentProcessId", "完整解码", true, "返回：u32")]
    [InlineData("GetSystemTime", "部分解码", true, "lpSystemTime void*")]
    [InlineData("ExitProcess", "暂不支持", false, "此函数不会返回")]
    public void ShowsCoverageBeforeSelection(string name, string level, bool selectable, string field)
    {
        var entry = WindowsApi.Search(name.ToLowerInvariant()).Single(f => f.Signature.Entry == name);
        Assert.Equal(level, entry.Level);
        Assert.Equal(selectable, entry.Selectable);
        Assert.Contains(field, entry.Details);
        Assert.Contains(WindowsApi.Source, entry.Details);
    }

    [Fact]
    public void IncludesMultipleSdkAreas()
    {
        Assert.True(WindowsApi.All.Count > 1000);
        Assert.Equal(3, WindowsApi.All.Select(f => f.Module).Distinct().Count());
    }
}
