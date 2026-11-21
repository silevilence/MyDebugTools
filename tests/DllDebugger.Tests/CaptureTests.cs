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
    public async Task AttachSeparatesParentAndChildAndDetaches(string arch)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, arch, "interactive.exe");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true
        })!;
        async Task<int> Command(string cmd)
        {
            await process.StandardInput.WriteLineAsync(cmd);
            return int.Parse((await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)))!);
        }
        CaptureSession? parent = null, child = null;
        try {
            Assert.Equal(101, await Command("1"));
            parent = CaptureSession.Attach(process.Id, arch, [Add(arch)]);
            await parent.Ready.WaitAsync(TimeSpan.FromSeconds(15));
            int childPid = await Command("spawn");
            Assert.Equal(102, await Command("child 2"));
            Assert.Equal(103, await Command("3"));
            child = CaptureSession.Attach(childPid, arch, [Add(arch)]);
            await child.Ready.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(104, await Command("child 4"));
            Assert.Equal(105, await Command("5"));
            await Task.WhenAll(parent.StopAsync(), child.StopAsync()).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(106, await Command("6"));
            Assert.Equal(107, await Command("child 7"));
            Assert.Equal(new[] { "103", "105" }, parent.Records().Select(c => c.Result));
            Assert.Equal("104", Assert.Single(child.Records()).Result);
            Assert.Equal("已结束", child.Status);
            Assert.Equal("已结束", parent.Status);
        } finally {
            if (child != null) await child.StopAsync();
            if (parent != null) await parent.StopAsync();
            process.StandardInput.Close();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
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
