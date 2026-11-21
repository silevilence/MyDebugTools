using System.Diagnostics;
using MyDebugTools.DllDebugger;
using Xunit;

namespace MyDebugTools.Tests;

public class SdkCaptureTests
{
    static MonitorFunction Api(string name) => WindowsApi.All.Single(f => f.Signature.Entry == name);
    [Theory]
    [InlineData("x86")]
    [InlineData("x64")]
    public async Task ShowsConcurrentAndUnfinishedCalls(string arch)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, arch, "interactive.exe");
        using var target = Process.Start(new ProcessStartInfo(exe) { RedirectStandardInput = true,
            RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        CaptureSession? session = null;
        try {
            await target.StandardInput.WriteLineAsync("1");
            Assert.Equal("101", await target.StandardOutput.ReadLineAsync());
            var sleep = Api("SleepEx");
            var time = Api("GetSystemTime");
            Assert.Throws<InvalidOperationException>(() => CaptureSession.Attach(target.Id, arch, [Api("ExitProcess")]));
            var bound = WindowsApi.Bind([time], arch)[0];
            var system = Path.Combine(Path.GetDirectoryName(bound.ImagePath)!, time.Module);
            var shared = new SignatureStore(AppContext.BaseDirectory).Find(DllImage.Open(system), time.Signature.Entry);
            Assert.Equal(time.Signature.Parameters, shared.Signature.Parameters);
            Assert.Contains(time.Source, shared.Status);
            session = CaptureSession.Attach(target.Id, arch, [sleep, time]);
            await session.Ready.WaitAsync(TimeSpan.FromSeconds(20));
            await target.StandardInput.WriteLineAsync("sdk");
            Assert.Equal("done", await target.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            await target.StandardInput.WriteLineAsync("sleep");
            var limit = Stopwatch.StartNew();
            while (!session.Records().Any(c => c.Arguments.Contains("=1200") && c.State == "进行中")
                   && limit.Elapsed < TimeSpan.FromSeconds(4)) await Task.Delay(10);
            Assert.Contains(session.Records(), c => c.Arguments.Contains("=1200") && c.State == "进行中");
            await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("awake", await target.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            var rows = session.Records();
            var paired = rows.Where(c => c.Arguments.Contains("=220，") || c.Arguments.Contains("=180，")).ToArray();
            Assert.Equal(2, paired.Length);
            Assert.Equal(2, paired.Select(c => c.Thread).Distinct().Count());
            Assert.All(paired, c => Assert.Equal("0", c.Result));
            Assert.Contains(rows, c => c.Function.EndsWith("!GetSystemTime") && c.Arguments.Contains("内容未知") && c.Result == "void");
            Assert.Contains(rows, c => c.State == "不完整" && c.Arguments.Contains("=1200"));
        } finally {
            if (session != null) await session.StopAsync();
            target.StandardInput.Close();
            await target.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task CountsCallsBeyondCapacity()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "x64");
        var dll = DllImage.Open(Path.Combine(dir, "fixture.dll"));
        var add = MonitorFunction.FromExport(dll, dll.Exports.Single(e => e.Entry == "add"), new SignatureStore(dir));
        var session = new CaptureSession("launch", "\"" + Path.Combine(dir, "monitor-target.exe") + "\"",
            "sample", "x64", [add], 3);
        try {
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(3, session.Records().Length);
            Assert.Equal(7, session.Lost);
        } finally { await session.StopAsync(); }
    }
}
