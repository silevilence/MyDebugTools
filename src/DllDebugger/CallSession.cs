using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace MyDebugTools.DllDebugger;

public sealed record CallRequest(Signature Signature, Dictionary<string, JsonElement> Arguments);
public sealed record CallResult(JsonElement Result, Dictionary<string, JsonElement> Outputs)
{
    public string Display() => JsonSerializer.Serialize(this);
}
internal sealed record Reply(CallResult? Value, string? Error);

public sealed class CallSession(DllImage image, string dotnetPath) : IDisposable
{
    Process? worker;
    NamedPipeServerStream? pipe;
    StreamReader? reader;
    StreamWriter? writer;
    bool failed;

    async Task Start(CancellationToken token)
    {
        string name = "MyDebugTools-" + Guid.NewGuid().ToString("N");
        pipe = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var info = new ProcessStartInfo(dotnetPath) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { typeof(CallSession).Assembly.Location, name, image.Path, image.Identity })
            info.ArgumentList.Add(arg);
        worker = Process.Start(info) ?? throw new IOException("后台进程启动失败。");
        var connected = pipe.WaitForConnectionAsync(token);
        var stopped = worker.WaitForExitAsync(token);
        if (await Task.WhenAny(connected, stopped) == stopped)
            throw new IOException($"{image.Architecture} 后台进程启动失败，请检查对应的 .NET 10 运行时。");
        await connected;
        reader = new(pipe, Encoding.UTF8, false, leaveOpen: true);
        writer = new(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
    }

    public async Task<CallResult> Invoke(Signature signature, string arguments, int timeoutMs = 5000)
    {
        if (failed) throw new InvalidOperationException("会话已结束，请重新开始。");
        signature.Validate();
        var export = image.Exports.FirstOrDefault(e => e.Entry == signature.Entry || "#" + e.Ordinal == signature.Entry);
        if (export == null || export.Kind is "数据" or "未知")
            throw new InvalidDataException("入口不存在或无法判定为代码：" + signature.Entry);
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments) ?? throw new FormatException("请输入 JSON 对象。");
        var request = JsonSerializer.Serialize(new CallRequest(signature, values));
        using var timeout = new CancellationTokenSource(timeoutMs);
        string? response;
        try
        {
            if (worker == null) await Start(timeout.Token);
            await writer!.WriteLineAsync(request.AsMemory(), timeout.Token);
            response = await reader!.ReadLineAsync(timeout.Token) ?? throw new IOException("后台进程已退出。");
        }
        catch (Exception e)
        {
            Dispose();
            failed = true;
            throw new IOException((timeout.IsCancellationRequested ? "后台调用超时。" : e.Message) + " 请重新开始会话。", e);
        }
        var reply = JsonSerializer.Deserialize<Reply>(response)!;
        return reply.Value ?? throw new InvalidDataException(reply.Error);
    }

    public void Dispose()
    {
        pipe?.Dispose();
        if (worker != null)
        {
            if (!worker.HasExited) worker.Kill();
            worker.Dispose();
            worker = null;
        }
        failed = true;
    }
}
