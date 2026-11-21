namespace MyDebugTools.DllDebugger;

public sealed record MonitorFunction(string Module, Signature Signature, string Source, string? Restriction = null,
    string? ImagePath = null, string? Architecture = null, int Rva = 0, string? Identity = null)
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
        Restriction ?? "可监控",
        "参数：" + (Signature.Parameters == null ? "未知" : Signature.Parameters.Length == 0 ? "无" :
            string.Join("，", Signature.Parameters.Select(p => $"{p.Name} {FieldInfo(p.Type)}"))),
        "返回：" + FieldInfo(Signature.Result)
    });
    public override string ToString() => $"{Module}!{Signature.Entry}　{Level}";

    public static MonitorFunction FromExport(DllImage image, Export export, SignatureStore store)
    {
        var match = store.Find(image, export.Entry);
        bool stale = match.Status.StartsWith("DLL 已换版", StringComparison.Ordinal);
        var sig = stale ? Signature.Unknown(export.Entry) : match.Signature;
        string? reason = export.Kind != "代码" ? "此导出属于" + export.Kind :
            sig.Layouts.Any(l => l.Name == sig.Result || sig.Parameters?.Any(p => p.Type == l.Name) == true)
                ? "暂不支持按值传递结构体的 ABI" : null;
        return new(Path.GetFileName(image.Path), sig, match.Status, reason, image.Path, image.Architecture, export.Rva, image.Identity);
    }
}

public static class WindowsApi
{
    public const string Source = "Windows SDK 10.0.26100.0";
    public static IReadOnlyList<MonitorFunction> All { get; } = Load();
    public static IEnumerable<MonitorFunction> Search(string query) => All.Where(f =>
        (f.Module + "!" + f.Signature.Entry).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase));

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
            result.Add(new(c[0] + ".dll", signature, source, restriction));
        }
        return result;
    }
}
