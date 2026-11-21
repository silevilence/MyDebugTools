using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyDebugTools.DllDebugger;

public sealed record Parameter(string Name, string Type, string Direction = "in", string? Encoding = null,
    int? Length = null, string? Ownership = null);
public sealed record Field(string Name, string Type);
public sealed record Layout(string Name, int Pack, Field[] Fields);
public sealed record Signature(string Entry, string Result, string Convention, Parameter[]? Parameters,
    Layout[] Layouts, Dictionary<string, string> Sources)
{
    public static Signature Unknown(string entry) => new(entry, "?", "?", null, [], []);

    public Dictionary<string, string> Values()
    {
        var fields = new Dictionary<string, string> { ["返回类型"] = Result, ["调用约定"] = Convention,
            ["参数表"] = Parameters == null ? "?" : string.Join(", ", Parameters.Select(p => p.Name)) };
        foreach (var p in Parameters ?? [])
        {
            fields[p.Name + ".类型"] = p.Type;
            fields[p.Name + ".方向"] = p.Direction;
            if (p.Encoding != null || p.Type == "string") fields[p.Name + ".编码"] = p.Encoding ?? "?";
            if (p.Length != null || p.Type == "buffer") fields[p.Name + ".长度"] = p.Length?.ToString() ?? "?";
            if (IsAddress(p.Type)) fields[p.Name + ".所有权"] = p.Ownership ?? "?";
        }
        foreach (var layout in Layouts) fields["结构体 " + layout.Name] = SignatureText.FormatLayout(layout);
        return fields;
    }

    public static bool IsAddress(string type) => type.EndsWith('*') || type is "buffer" or "string" or "handle";
    public static bool IsScalar(string type) => type is "i8" or "u8" or "i16" or "u16" or "i32" or "u32"
        or "i64" or "u64" or "f32" or "f64" or "handle" or "isize" or "usize";

    public void Validate()
    {
        void Require(bool valid, string message)
        {
            if (!valid) throw new InvalidDataException(message);
        }
        Require(!string.IsNullOrWhiteSpace(Entry), "请输入导出名或 #序号。");
        Require(Convention is "cdecl" or "stdcall" or "winapi", "调用约定需填写 cdecl、stdcall 或 winapi。");
        Require(IsScalar(Result) || Result == "void" || Layouts.Any(s => s.Name == Result), "返回类型未知或暂不支持：" + Result);
        Require(Parameters != null, "参数表未知；无参数请填写 -。");
        var names = new HashSet<string>();
        foreach (var s in Layouts)
        {
            Require(Identifier(s.Name) && names.Add(s.Name) && !IsScalar(s.Name) && s.Name is not ("void" or "buffer" or "string"), "结构体名无效或重复：" + s.Name);
            Require(s.Pack is 1 or 2 or 4 or 8 or 16 && s.Fields.Length > 0, s.Name + "：请填写字段及 pack。");
            var fieldNames = new HashSet<string>();
            foreach (var f in s.Fields)
                Require(Identifier(f.Name) && fieldNames.Add(f.Name) && IsScalar(f.Type), $"{s.Name}.{f.Name}：需要唯一字段名和已知标量类型。");
        }
        names.Clear();
        foreach (var p in Parameters!)
        {
            Require(Identifier(p.Name) && names.Add(p.Name), "参数名无效或重复：" + p.Name);
            var t = p.Type.EndsWith('*') ? p.Type[..^1] : p.Type;
            Require(IsScalar(t) || Layouts.Any(s => s.Name == t) || p.Type is "string" or "buffer", p.Name + "：类型或结构体布局未知。");
            Require(p.Direction is "in" or "out" or "inout", p.Name + "：请指定方向 in、out 或 inout。");
            Require(p.Direction == "in" || p.Type.EndsWith('*') || p.Type == "buffer", p.Name + "：输出参数需要指针或缓冲区。");
            if (p.Type == "buffer") Require(p.Length > 0, p.Name + "：缺少有效的缓冲区字节长度。");
            if (p.Type == "string") Require(p.Encoding is "utf8" or "utf16" or "ascii", p.Name + "：请指定 utf8、utf16 或 ascii。");
            if (IsAddress(p.Type)) Require(!string.IsNullOrWhiteSpace(p.Ownership) && p.Ownership != "?", p.Name + "：请注明所有权。");
            if (p.Type.EndsWith('*') || p.Type is "buffer" or "string")
                Require(p.Ownership == "caller", p.Name + "：临时参数只支持 caller 所有权，供本次调用使用。");
        }
    }

    public static bool Identifier(string value) => Regex.IsMatch(value, @"^[A-Za-z_]\w*$");

    public Signature WithSource(string source)
    {
        var values = Values();
        return this with { Sources = values.Where(p => p.Value != "?").ToDictionary(p => p.Key, _ => source) };
    }
}

public static class SignatureText
{
    public static string FormatParameter(Parameter p) => string.Join(' ', new[] { p.Name, p.Type, p.Direction,
        p.Encoding == null ? null : "encoding=" + p.Encoding, p.Length == null ? null : "length=" + p.Length,
        p.Ownership == null ? null : "owner=" + p.Ownership }.Where(s => s != null));

    public static string FormatLayout(Layout l) => $"{l.Name} pack={l.Pack} " + string.Join(' ', l.Fields.Select(f => f.Name + ":" + f.Type));
    static IEnumerable<string[]> Rows(string text) => text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static Parameter[]? Parameters(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "?") return null;
        if (text.Trim() == "-") return [];
        return Rows(text).Select(r =>
        {
            if (r.Length < 3) throw new FormatException(r[0] + "：每行填写 名称 类型 方向。");
            var options = new Dictionary<string, string>();
            foreach (string cell in r.Skip(3))
            {
                var kv = cell.Split('=', 2);
                if (kv.Length != 2 || kv[0] is not ("length" or "encoding" or "owner"))
                    throw new FormatException(r[0] + "：未知选项 " + cell);
                options.Add(kv[0], kv[1]);
            }
            return new Parameter(r[0], r[1], r[2], options.GetValueOrDefault("encoding"),
                options.TryGetValue("length", out var length) ? int.Parse(length) : null, options.GetValueOrDefault("owner"));
        }).ToArray();
    }

    public static Layout[] Layouts(string text) => Rows(text).Select(r =>
    {
        if (r.Length < 3 || !r[1].StartsWith("pack=")) throw new FormatException(r[0] + "：示例 Point pack=8 x:i32 y:i32");
        return new Layout(r[0], int.Parse(r[1][5..]), r.Skip(2).Select(s =>
        {
            var f = s.Split(':');
            return f.Length == 2 ? new Field(f[0], f[1]) : throw new FormatException("字段需要 名称:类型。");
        }).ToArray());
    }).ToArray();
}
