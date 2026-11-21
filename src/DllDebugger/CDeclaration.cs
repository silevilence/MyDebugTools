using System.Text.RegularExpressions;

namespace MyDebugTools.DllDebugger;

public sealed record DeclarationResult(Signature Signature, string[] Notes);

public static class CDeclaration
{
    const string Id = @"[A-Za-z_]\w*";
    static readonly Dictionary<string, string> Types = new()
    {
        ["void"]="void", ["char"]="i8", ["signed char"]="i8", ["unsigned char"]="u8",
        ["short"]="i16", ["unsigned short"]="u16", ["int"]="i32", ["unsigned"]="u32", ["unsigned int"]="u32",
        ["long"]="i32", ["unsigned long"]="u32", ["long long"]="i64", ["unsigned long long"]="u64",
        ["int8_t"]="i8", ["uint8_t"]="u8", ["int16_t"]="i16", ["uint16_t"]="u16", ["int32_t"]="i32",
        ["uint32_t"]="u32", ["int64_t"]="i64", ["uint64_t"]="u64", ["size_t"]="usize", ["uintptr_t"]="usize",
        ["intptr_t"]="isize", ["float"]="f32", ["double"]="f64", ["HANDLE"]="handle",
        ["DWORD"]="u32", ["BOOL"]="i32", ["BYTE"]="u8", ["WORD"]="u16", ["wchar_t"]="u16"
    };

    public static DeclarationResult Parse(string source, string selected)
    {
        var notes = new List<string>();
        var layouts = new List<Layout>();
        var types = new Dictionary<string, string>(Types);
        void Note(int offset, string message)
        {
            string prefix = source[..offset];
            notes.Add($"第 {1 + prefix.Count(c => c == '\n')} 行，第 {offset - prefix.LastIndexOf('\n')} 列 · {message}");
        }
        string Mask(Match m) => Regex.Replace(m.Value, @"[^\r\n]", " ");
        var code = Regex.Replace(source, @"//[^\n]*|/\*[\s\S]*?\*/", Mask);
        bool unknownPack = false;
        code = Regex.Replace(code, @"(?m)^[ \t]*#[^\n]*", m =>
        {
            Note(m.Index, "请核对预处理指令 " + m.Value.Trim());
            if (m.Value.Contains("pack")) unknownPack = true;
            return Mask(m);
        });

        string Resolve(string raw, int offset)
        {
            var cleaned = Regex.Replace(raw, @"\b(const|volatile|struct|extern|__cdecl|__stdcall|WINAPI|CALLBACK)\b", " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            int stars = cleaned.Count(c => c == '*');
            string native = cleaned.Replace("*", "").Trim();
            if (stars == 1 && native == "void") return "handle";
            if (stars == 1 && native is "wchar_t" or "char" && Regex.IsMatch(raw, @"\bconst\b")) return "string";
            if (types.TryGetValue(native, out var kind) && stars <= 1) return kind + new string('*', stars);
            var bad = native.Split(' ').FirstOrDefault(w => !types.ContainsKey(w)) ?? native;
            Note(offset + Math.Max(0, raw.IndexOf(bad, StringComparison.Ordinal)), "未知类型或宏 " + native);
            return "?";
        }

        (string Name, string Type) Named(string raw, int offset, string fallback)
        {
            var m = Regex.Match(raw, @"^\s*(?<type>.+?[\s*])(?<name>" + Id + @")\s*$", RegexOptions.Singleline);
            if (m.Success && !types.ContainsKey(raw.Trim()))
                return (m.Groups["name"].Value, Resolve(m.Groups["type"].Value, offset + m.Groups["type"].Index));
            return (fallback, Resolve(raw, offset));
        }

        code = Regex.Replace(code, @"\b(?:typedef\s+)?struct\s*(?<tag>" + Id + @")?\s*\{(?<fields>[^{}]*)\}\s*(?<alias>" + Id + @")?\s*;", m =>
        {
            var tag = m.Groups["tag"].Value;
            var name = m.Groups["alias"].Success ? m.Groups["alias"].Value : tag;
            if (name.Length == 0) { Note(m.Index, "缺少结构体名称"); return Mask(m); }
            var fields = new List<Field>();
            var body = m.Groups["fields"];
            foreach (Match f in Regex.Matches(body.Value, @"([^;]+);"))
            {
                var item = Named(f.Groups[1].Value, body.Index + f.Index, "?");
                fields.Add(new(item.Name, item.Type));
            }
            layouts.Add(new(name, unknownPack ? 0 : 8, fields.ToArray()));
            types[name] = name;
            if (tag != "") types[tag] = name;
            return Mask(m);
        });
        var functions = new List<Signature>();
        foreach (Match m in Regex.Matches(code, @"(?:^|(?<=;))\s*(?<prefix>[^;(){}]+?[\s*])(?<name>" + Id + @")\s*\((?<args>[^();]*)\)\s*;"))
        {
            var prefix = m.Groups["prefix"];
            string convention = Regex.IsMatch(prefix.Value, @"\b(__stdcall|WINAPI|CALLBACK)\b") ? "stdcall" : "cdecl";
            var result = Resolve(prefix.Value, prefix.Index);
            var body = m.Groups["args"];
            var parameters = new List<Parameter>();
            if (body.Value.Trim() != "void")
                foreach (Match a in Regex.Matches(body.Value, @"[^,]+"))
                {
                    var item = Named(a.Value, body.Index + a.Index, "arg" + (parameters.Count + 1));
                    bool pointer = item.Type.EndsWith('*');
                    parameters.Add(new(item.Name, item.Type, pointer && !Regex.IsMatch(a.Value, @"\bconst\b") ? "?" : "in",
                        item.Type == "string" && a.Value.Contains("wchar_t") ? "utf16" : null));
                }
            var s = new Signature(m.Groups["name"].Value, result, convention, body.Value.Trim().Length == 0 ? null : parameters.ToArray(),
                layouts.ToArray(), []).WithSource("C 声明");
            if (!Regex.IsMatch(prefix.Value, @"\b(__cdecl|__stdcall|WINAPI|CALLBACK)\b")) s.Sources["调用约定"] = "C 默认约定 · 请核对";
            functions.Add(s);
        }
        var chosen = functions.FirstOrDefault(s => s.Entry == selected) ?? (functions.Count == 1 ? functions[0] : null)
            ?? throw new FormatException(functions.Count == 0 ? string.Join("　", notes.Append("需要可识别的 C 函数原型。")) : "请先选择这组声明中的函数入口。");
        return new(chosen, notes.ToArray());
    }
}
