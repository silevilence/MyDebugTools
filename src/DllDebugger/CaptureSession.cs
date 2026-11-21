using System.Diagnostics;
using System.Security.Cryptography;

namespace MyDebugTools.DllDebugger;

public sealed record CapturedCall(long Id, string Function, int Thread, DateTimeOffset Time, string Arguments,
    string Stack, string State = "进行中", string Result = "待返回", long? Microseconds = null)
{
    public string Details => $"{Time:HH:mm:ss.fffffff} · 线程 {Thread} · {Function}\n参数：{Arguments}\n返回：{Result}" +
        $"\n耗时：{(Microseconds is long us ? us + " µs" : State)}\n调用栈：\n" + Stack.Replace(" | ", "\n");
    public override string ToString() => $"#{Id}　{Function}　线程 {Thread}　{State}";
}

public sealed class CaptureSession
{
    readonly object gate = new();
    readonly Process worker;
    readonly List<FileStream> images = [];
    readonly MonitorFunction[] functions;
    readonly List<CapturedCall> records = [];
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly int capacity;
    bool stopping;
    string status = "正在连接";
    public string Target { get; }
    public string Architecture { get; }
    public int Pid { get; private set; }
    public DateTimeOffset? Effective { get; private set; }
    public int Lost { get; private set; }
    public Task Ready => ready.Task;
    public Task Completion { get; }
    public string Status { get { lock (gate) return status; } }
    public CapturedCall[] Records() { lock (gate) return records.ToArray(); }
    public override string ToString() => $"{Target} · PID {Pid} · {Architecture} · {Effective:HH:mm:ss}";

    public static CaptureSession Launch(string exe, string arguments, string arch, IEnumerable<MonitorFunction> selection)
    {
        exe = Path.GetFullPath(exe);
        if (!File.Exists(exe)) throw new FileNotFoundException("找不到目标程序", exe);
        return new("launch", "\"" + exe + "\" " + arguments, Path.GetFileName(exe), arch, selection, 10000);
    }

    public static CaptureSession Attach(int pid, string arch, IEnumerable<MonitorFunction> selection)
    {
        if (pid == Environment.ProcessId) throw new InvalidOperationException("请选择宿主以外的目标进程");
        using var process = Process.GetProcessById(pid);
        return new("attach", pid.ToString(), process.ProcessName, arch, selection, 10000);
    }

    internal CaptureSession(string mode, string target, string label, string arch, IEnumerable<MonitorFunction> selection, int capacity)
    {
        Architecture = arch;
        Target = label;
        this.capacity = capacity;
        if (arch is not ("x86" or "x64")) throw new ArgumentException("目标架构应为 x86 或 x64");
        functions = selection.ToArray();
        if (functions.Length == 0) throw new InvalidOperationException("请先选择监控函数");
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "monitor", arch, "capture.exe")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(target);
        var locked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in functions)
            {
                if (!f.Selectable) throw new InvalidOperationException(f.Restriction);
                if (f.ImagePath == null) throw new InvalidOperationException("此定义尚未绑定到目标架构的 DLL");
                if (f.Architecture != arch) throw new InvalidOperationException("导出与目标程序位数不同：" + f.Module);
                if (!locked.TryGetValue(f.ImagePath, out var hash))
                {
                    var stream = File.OpenRead(f.ImagePath);
                    images.Add(stream);
                    locked.Add(f.ImagePath, hash = Convert.ToHexString(SHA256.HashData(stream)));
                }
                if (hash != f.Identity) throw new InvalidDataException("DLL 已换版，请重新选择：" + f.Module);
                start.ArgumentList.Add(f.ImagePath);
                start.ArgumentList.Add(f.Rva.ToString());
                var widths = (f.Signature.Parameters ?? []).Select(p => p.Type is "i64" or "u64" or "f64" ? '8' : '4');
                start.ArgumentList.Add(string.Concat(widths));
                start.ArgumentList.Add(f.Signature.Result is "i64" or "u64" ? "8" : "4");
            }
            worker = Process.Start(start) ?? throw new InvalidOperationException("无法启动采集器");
        }
        catch { foreach (var image in images) image.Dispose(); throw; }
        Completion = Pump();
    }

    public Task StopAsync()
    {
        lock (gate)
        {
            if (!stopping)
            {
                stopping = true;
                try { worker.StandardInput.Close(); }
                catch (InvalidOperationException) when (worker.HasExited) { }
            }
        }
        return Completion;
    }

    string Decode(string type, string raw)
    {
        if (type == "void") return "void";
        if (!ulong.TryParse(raw, out var n)) return "未知";
        return type switch {
            "i8" => ((sbyte)n).ToString(), "i16" => ((short)n).ToString(), "i32" => ((int)n).ToString(),
            "u8" => ((byte)n).ToString(), "u16" => ((ushort)n).ToString(), "u32" => ((uint)n).ToString(),
            "i64" => ((long)n).ToString(), "u64" => n.ToString(),
            "isize" => Architecture == "x86" ? ((int)n).ToString() : ((long)n).ToString(),
            "usize" => Architecture == "x86" ? ((uint)n).ToString() : n.ToString(),
            "handle" => $"0x{n:X}",
            _ when MonitorFunction.Address(type) => $"0x{n:X}（内容未知）", _ => "未知"
        };
    }

    async Task Pump()
    {
        string? error = null;
        var errors = worker.StandardError.ReadToEndAsync();
        var entries = new Dictionary<long, int>();
        try
        {
            while (await worker.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var c = line.Split('\t');
                lock (gate)
                {
                    if (c[0] == "ready") {
                        Pid = int.Parse(c[1]);
                        Effective = DateTimeOffset.FromFileTime(long.Parse(c[2]));
                        status = "采集中";
                        ready.TrySetResult();
                    } else if (c[0] == "enter") {
                        long id = long.Parse(c[1]);
                        if (records.Count >= capacity) { Lost++; continue; }
                        int function = int.Parse(c[2]);
                        var f = functions[function];
                        var raw = c[4].Split(',');
                        string args = f.Signature.Parameters is not { } ps ? "未知" : ps.Length == 0 ? "无" :
                            string.Join("，", ps.Select((p, i) => p.Name + "=" + Decode(p.Type, raw[i])));
                        entries.Add(id, function);
                        records.Add(new(id, f.Module + "!" + f.Signature.Entry, int.Parse(c[3]),
                            DateTimeOffset.FromFileTime(long.Parse(c[6])), args, c[5]));
                    } else if (c[0] is "return" or "incomplete") {
                        long id = long.Parse(c[1]);
                        if (!entries.Remove(id, out int function)) continue;
                        int index = checked((int)id - 1);
                        records[index] = c[0] == "return" ? records[index] with { State = "已返回",
                            Result = Decode(functions[function].Signature.Result, c[2]), Microseconds = long.Parse(c[3]) } :
                            records[index] with { State = "不完整", Result = "未知" };
                    }
                }
            }
            await worker.WaitForExitAsync().ConfigureAwait(false);
            var detail = (await errors.ConfigureAwait(false)).Trim();
            if (worker.ExitCode != 0) error = detail == "" ? $"采集器退出 {worker.ExitCode}" : detail;
        }
        catch (Exception e) { error = e.Message; }
        finally
        {
            lock (gate)
            {
                stopping = true;
                worker.StandardInput.Close();
                foreach (var id in entries.Keys) {
                    int index = checked((int)id - 1);
                    records[index] = records[index] with { State = "不完整", Result = "未知" };
                }
                status = error ?? "已结束";
                ready.TrySetException(new InvalidOperationException(error ?? "目标在生效前退出"));
            }
            worker.Dispose();
            foreach (var image in images) image.Dispose();
        }
    }
}
