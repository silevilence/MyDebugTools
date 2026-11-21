namespace MyDebugTools.DllDebugger;

public sealed record MonitorFunction(string Module, Signature Signature, string Source, string? Restriction = null,
    string? ImagePath = null, string? Architecture = null, int Rva = 0, string? Identity = null,
    string? Availability = null, string? Declaration = null)
{
    public bool Selectable => Restriction is null;
    public static bool Integer(string type) => Signature.IsScalar(type) && type is not ("f32" or "f64");
    public static bool Address(string type) => Signature.IsAddress(type) && type != "handle";
    public bool Partial => Signature.Parameters == null || Signature.Parameters.Any(p => !Integer(p.Type))
        || !(Integer(Signature.Result) || Signature.Result == "void");
    public string Level => !Selectable ? "暂不支持" : Partial ? "部分解码" : "完整解码";
    static string FieldInfo(string type) => Address(type) ? type + "（只读地址，内容未知）"
        : Integer(type) || type == "void" ? type : type + "（未知）";
    public string Details => string.Join(Environment.NewLine, new[] {
        $"{Module}!{Signature.Entry} · {Level} · {Source}",
        Restriction ?? Availability ?? "可监控",
        "参数：" + (Signature.Parameters == null ? "未知" : Signature.Parameters.Length == 0 ? "无" :
            string.Join("，", Signature.Parameters.Select(p => $"{p.Name} {FieldInfo(p.Type)}"))),
        "返回：" + FieldInfo(Signature.Result), Declaration
    }.OfType<string>());
    public override string ToString() => $"{Module}!{Signature.Entry}　{Level}";

    public static MonitorFunction FromExport(DllImage image, Export export, SignatureStore store)
    {
        var match = store.Find(image, export.Entry);
        bool stale = match.Status.StartsWith("DLL 已换版", StringComparison.Ordinal);
        var sig = stale ? Signature.Unknown(export.Entry) : match.Signature;
        var sdk = WindowsApi.Find(image.Path, export.Name);
        bool slot(string t) => Signature.IsScalar(t) || Signature.IsAddress(t);
        string? reason = export.Kind != "代码" ? "此导出属于" + export.Kind : null;
        if (reason == null && sdk?.Restriction == "此函数不会返回") reason = sdk.Restriction;
        if (sig.Parameters != null && reason == null)
        {
            if (sig.Convention is not ("cdecl" or "stdcall" or "winapi")) reason = "无法确认参数的调用约定";
            else if (sig.Parameters.Any(p => !slot(p.Type)) || !(slot(sig.Result) || sig.Result == "void"))
                reason = "参数或返回类型的 ABI 布局未知，无法确定寄存器与栈位置";
        }
        return new(Path.GetFileName(image.Path), sig, match.Status, reason, image.Path, image.Architecture, export.Rva, image.Identity);
    }
}

public static class WindowsApi
{
    public const string Source = "Windows SDK 10.0.26100.0";
    public static IReadOnlyList<MonitorFunction> All { get; } = Load();
    static readonly Dictionary<(string Module, string Entry), MonitorFunction> ByExport =
        All.ToDictionary(f => (f.Module.ToUpperInvariant(), f.Signature.Entry));
    public static MonitorFunction? Find(string path, string? entry) => entry == null ? null :
        ByExport.GetValueOrDefault((Path.GetFileName(path).ToUpperInvariant(), entry));
    public static IEnumerable<MonitorFunction> Search(string query) => All.Where(f =>
        (f.Module + "!" + f.Signature.Entry).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));

    // Use the same PE resolver for preflight and acquisition. Keep the image cache
    // local to each pass so replacing a system image cannot reuse an old RVA.
    static MonitorFunction Resolve(MonitorFunction f, string arch, Dictionary<string, DllImage> images)
    {
        var folder = Environment.GetFolderPath(arch == "x86" ? Environment.SpecialFolder.SystemX86 : Environment.SpecialFolder.System);
        if (!f.Selectable) throw new InvalidOperationException(f.Restriction);
        if (f.ImagePath != null) return f;
        var module = f.Module;
        var entry = f.Signature.Entry;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (visited.Add(module + "!" + entry))
        {
            if (Path.GetFileName(module) != module) throw new InvalidDataException("无效的系统 DLL 名称");
            var path = Path.Combine(folder, module);
            if (!images.TryGetValue(path, out var dll)) images.Add(path, dll = DllImage.Open(path));
            var export = dll.Exports.FirstOrDefault(e => e.Entry == entry || "#" + e.Ordinal == entry);
            if (export?.Kind == "代码")
                return f with { ImagePath = dll.Path, Architecture = dll.Architecture, Rva = export.Rva, Identity = dll.Identity };
            var forward = export?.Forwarder;
            int split = forward?.LastIndexOf('.') ?? -1;
            if (forward == null || split < 1)
                throw new InvalidOperationException($"本机 {arch} {module} 缺少可监控的入口 {entry}");
            module = forward[..split] + ".dll";
            entry = forward[(split + 1)..];
        }
        throw new InvalidDataException("转发导出形成循环：" + f);
    }

    public static MonitorFunction[] Installed()
    {
        var images = new Dictionary<string, DllImage>(StringComparer.OrdinalIgnoreCase);
        return All.Select(f => {
            if (!f.Selectable) return f;
            List<string> supported = [], reasons = [];
            foreach (var arch in new[] { "x86", "x64" })
            {
                try { Resolve(f, arch, images); supported.Add(arch); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or BadImageFormatException or InvalidDataException)
                { reasons.Add(e.Message); }
            }
            return supported.Count == 0 ? f with { Restriction = string.Join("；", reasons.Distinct()) } :
                f with { Availability = "本机可监控：" + string.Join("、", supported) };
        }).ToArray();
    }

    internal static MonitorFunction[] Bind(IEnumerable<MonitorFunction> selected, string arch)
    {
        var images = new Dictionary<string, DllImage>(StringComparer.OrdinalIgnoreCase);
        var addresses = new HashSet<(string, int)>();
        return selected.Select(f => {
            var bound = Resolve(f, arch, images);
            if (!addresses.Add((bound.ImagePath!.ToUpperInvariant(), bound.Rva)))
                throw new InvalidOperationException("多个名称指向同一入口，请只选其中一个：" + f);
            return bound;
        }).ToArray();
    }

    static string Abi(string native) => native switch
    {
        "void" or "VOID" => "void",
        "INT" or "int" or "LONG" or "long" or "BOOL" or "HRESULT" => "i32",
        "DWORD" or "UINT" or "ULONG" or "COLORREF" => "u32",
        "WORD" or "USHORT" or "ATOM" or "LANGID" => "u16",
        "BYTE" or "BOOLEAN" => "u8",
        "ULONGLONG" or "DWORD64" => "u64",
        "LONGLONG" => "i64",
        "SIZE_T" or "UINT_PTR" or "ULONG_PTR" or "DWORD_PTR" or "WPARAM" => "usize",
        "SSIZE_T" or "LONG_PTR" or "LPARAM" or "LRESULT" or "INT_PTR" => "isize",
        "HANDLE" or "HWND" or "HDC" or "HMODULE" or "HINSTANCE" or "HGDIOBJ" or "HBRUSH"
            or "HFONT" or "HICON" or "HMENU" or "HBITMAP" or "HRGN" or "HMONITOR" => "handle",
        "LPDWORD" or "PULONG" => "u32*",
        "LPBOOL" or "LPINT" => "i32*",
        "LPCWSTR" or "LPWSTR" or "PCWSTR" or "PWSTR" => "u16*",
        "LPCSTR" or "LPSTR" or "PCSTR" or "PSTR" => "i8*",
        _ when native.EndsWith('*') || native is "LPVOID" or "LPCVOID" or "PVOID" or "LPFILETIME"
            or "LPSYSTEMTIME" or "LARGE_INTEGER*" => "void*",
        _ => "?"
    };

    static List<MonitorFunction> Load()
    {
        using var stream = typeof(WindowsApi).Assembly.GetManifestResourceStream("WindowsApi.tsv")!;
        using var reader = new StreamReader(stream);
        List<MonitorFunction> result = [];
        while (reader.ReadLine() is { } line)
        {
            var c = line.Split('\t');
            var source = Source + " · " + c[1];
            var parameters = c[5] == "-" ? [] : c[5].Split(',').Select(item => {
                var fields = item.Split(':');
                return new Parameter(fields[0], Abi(fields[1]), fields[2]);
            }).ToArray();
            var signature = new Signature(c[2], Abi(c[3]), "winapi", parameters, [], []).WithSource(source);
            var restriction = c[4] == "noreturn" ? "此函数不会返回" :
                signature.Result == "?" || parameters.Any(p => p.Type == "?") ? "SDK 类型的 ABI 布局尚未映射" : null;
            var native = c[5] == "-" ? "void" : string.Join(", ", c[5].Split(',').Select(arg => {
                var parts = arg.Split(':');
                return parts[1] + " " + parts[0];
            }));
            result.Add(new(c[0] + ".dll", signature, source, restriction, Declaration: $"{c[3]} WINAPI {c[2]}({native})"));
        }
        return result;
    }
}
