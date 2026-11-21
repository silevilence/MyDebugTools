using MyDebugTools.DllDebugger;
using Xunit;

namespace MyDebugTools.Tests;

public class CaptureTests
{
    static MonitorFunction Add(string arch)
    {
        var dll = DllImage.Open(Path.Combine(AppContext.BaseDirectory, arch, "fixture.dll"));
        return MonitorFunction.FromExport(dll, dll.Exports.Single(e => e.Entry == "add"),
            new SignatureStore(Path.Combine(AppContext.BaseDirectory, "empty-signatures")));
    }

    [Theory]
    [InlineData("x86")]
    [InlineData("x64")]
    public async Task LaunchCapturesOnlySelectedCalls(string arch)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, arch, "monitor-target.exe");
        var session = CaptureSession.Launch(exe, "", arch, [Add(arch)]);
        try {
            await session.Ready.WaitAsync(TimeSpan.FromSeconds(20));
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("已结束", session.Status);
            Assert.True(session.Pid > 0);
            var calls = session.Records();
            Assert.Equal(10, calls.Length);
            for (int i = 0; i < calls.Length; i++) {
                var c = calls[i];
                Assert.Equal("fixture.dll!add", c.Function);
                Assert.Equal($"a={i}，b=100", c.Arguments);
                Assert.Equal((i + 100).ToString(), c.Result);
                Assert.Contains("monitor_target+0x", c.Stack);
                Assert.True(c.Thread > 0 && c.Microseconds > 0 && c.Time >= session.Effective);
            }
        } finally { await session.StopAsync(); }
    }
}
