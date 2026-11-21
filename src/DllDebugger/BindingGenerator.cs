using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyDebugTools.DllDebugger;

public enum BindingFormat { CSharp, FSharp }
public sealed record GeneratedBindings(string Code, string[] Skipped);
public static class BindingGenerator
{
    public static GeneratedBindings Generate(DllImage image, SignatureStore store, IEnumerable<string> entries,
        BindingFormat format, string? exampleArguments = null)
    {
        bool fs = format == BindingFormat.FSharp;
        var output = new StringBuilder(fs
            ? "module Bindings\n\nopen System\nopen System.Runtime.InteropServices\n"
            : "using System;\nusing System.Runtime.InteropServices;\n");
        var skipped = new List<string>();
        int count = 0;
        foreach (var entry in entries.Distinct())
        {
            try
            {
                var match = store.Find(image, entry);
                if (!match.Confirmed) throw new InvalidDataException(match.Status);
                match.Signature.Validate();
                var export = image.Exports.First(e => e.Entry == entry || "#" + e.Ordinal == entry);
                if (export.Kind is "数据" or "未知") throw new InvalidDataException("无法判定为函数。");
                var generator = new Writer(image, match.Signature, format, count + 1);
                output.Append(generator.Render(exampleArguments));
                count++;
            }
            catch (Exception e) when (e is InvalidDataException or JsonException or FormatException or InvalidOperationException or OverflowException or KeyNotFoundException)
            { skipped.Add(entry + " · " + e.Message); }
        }
        return new(count > 0 ? output.ToString() : "", skipped.ToArray());
    }

    sealed class Writer(DllImage image, Signature s, BindingFormat format, int index)
    {
        bool FS => format == BindingFormat.FSharp;
        readonly StringBuilder code = new();
        void Line(string text) => code.AppendLine(text);
        static string Literal(string text) => JsonSerializer.Serialize(text);
        string TypeName(string t) => t switch
        {
            "i8" => "sbyte", "u8" => "byte", "i16" => FS ? "int16" : "short", "u16" => FS ? "uint16" : "ushort",
            "i32" => "int", "u32" => FS ? "uint32" : "uint", "i64" => FS ? "int64" : "long", "u64" => FS ? "uint64" : "ulong",
            "f32" => FS ? "float32" : "float", "f64" => FS ? "float" : "double", "void" => "void",
            "handle" or "isize" => FS ? "nativeint" : "nint", "usize" => FS ? "unativeint" : "nuint",
            "string" or "buffer" => "byte[]", _ => "S_" + t
        };

        string ParameterDecl(Parameter p)
        {
            string name = "p_" + p.Name;
            string t = TypeName(p.Type.TrimEnd('*'));
            if (p.Type.EndsWith('*'))
                return FS ? $"{t}& {name}" : $"{(p.Direction == "out" ? "out" : "ref")} {t} {name}";
            string attrs = "";
            if (p.Type is "buffer" or "string")
            {
                var markers = new List<string>();
                if (p.Direction != "out") markers.Add("In");
                if (p.Direction != "in") markers.Add("Out");
                if (p.Type == "buffer") markers.Add($"MarshalAs(UnmanagedType.LPArray, SizeConst = {p.Length})");
                attrs = FS ? "[<" + string.Join("; ", markers) + ">] " : "[" + string.Join(", ", markers) + "] ";
            }
            return $"{attrs}{t} {name}";
        }

        void LayoutCode(Layout l)
        {
            if (FS)
            {
                Line($"    [<Struct; StructLayout(LayoutKind.Sequential, Pack = {l.Pack})>]");
                Line($"    type S_{l.Name} =");
                foreach (var f in l.Fields) Line($"        val mutable f_{f.Name}: {TypeName(f.Type)}");
            }
            else
            {
                Line($"    [StructLayout(LayoutKind.Sequential, Pack = {l.Pack})]");
                Line($"    public struct S_{l.Name}");
                Line("    {");
                foreach (var f in l.Fields) Line($"        public {TypeName(f.Type)} f_{f.Name};");
                Line("    }");
            }
        }

        public string Render(string? example)
        {
            string group = "Native_" + Regex.Replace(s.Entry, @"\W", "_") + "_" + index;
            Line("");
            Line(FS ? $"module {group} =" : $"public static class {group}\n{{");
            foreach (var l in s.Layouts) LayoutCode(l);
            string convention = s.Convention == "cdecl" ? "Cdecl" : s.Convention == "winapi" ? "Winapi" : "StdCall";
            string dll = Literal(image.Path), entry = Literal(s.Entry);
            string attr = $"DllImport({dll}, EntryPoint = {entry}, ExactSpelling = true, CallingConvention = CallingConvention.{convention})";
            string args = string.Join(", ", s.Parameters!.Select(ParameterDecl));
            Line(FS ? $"    [<{attr}>]" : $"    [{attr}]");
            Line(FS ? $"    extern {TypeName(s.Result)} Invoke({args})" : $"    public static extern {TypeName(s.Result)} Invoke({args});");
            if (example != null) Example(example);
            if (!FS) Line("}");
            return code.ToString();
        }

        string ScalarValue(string type, JsonElement value)
        {
            if (type == "handle")
            {
                if (value.ValueKind != JsonValueKind.Null) throw new InvalidDataException("会话句柄只能在创建它的进程中使用。");
                return FS ? "0n" : "0";
            }
            Type scalar = type switch { "isize" => image.Architecture == "x86" ? typeof(int) : typeof(long),
                "usize" => image.Architecture == "x86" ? typeof(uint) : typeof(ulong), _ => Worker.Scalar(type) };
            object? parsed = value.Deserialize(scalar);
            string literal = Convert.ToString(parsed, CultureInfo.InvariantCulture)!;
            if (FS)
                return literal + (type switch { "i8" => "y", "u8" => "uy", "i16" => "s", "u16" => "us", "i64" => "L",
                    "u64" => "UL", "u32" => "u", "isize" => "n", "usize" => "un", "f32" => literal.Contains('.') || literal.Contains('E') ? "f" : ".0f",
                    "f64" when !literal.Contains('.') && !literal.Contains('E') => ".0", _ => "" });
            return type switch { "f32" => literal + "f", "u64" => literal + "UL", "i64" => literal + "L",
                "isize" or "usize" => $"({TypeName(type)})({literal})", _ => literal };
        }

        void Example(string json)
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? throw new FormatException("需要 JSON 对象。");
            Line(FS ? "\n    let Example() =" : "\n    public static void Example()\n    {");
            var callArgs = new List<string>();
            foreach (var p in s.Parameters!)
            {
                string name = "p_" + p.Name, t = p.Type.TrimEnd('*'), type = TypeName(t);
                bool input = p.Direction != "out";
                if (!values.TryGetValue(p.Name, out var v) && input) throw new InvalidDataException("缺少示例参数 " + p.Name);
                string expr;
                var layout = s.Layouts.FirstOrDefault(l => l.Name == t);
                if (p.Type == "string")
                {
                    string enc = p.Encoding switch { "utf8" => "UTF8", "utf16" => "Unicode", _ => "ASCII" };
                    expr = $"System.Text.Encoding.{enc}.GetBytes({Literal(v.GetString() + "\0")})";
                }
                else if (p.Type == "buffer") expr = FS ? $"Array.zeroCreate<byte> {p.Length}" : $"new byte[{p.Length}]";
                else if (!input || layout != null) expr = FS ? $"Unchecked.defaultof<{type}>" : "default";
                else expr = ScalarValue(t, v);
                Line(FS ? $"        let mutable {name}: {type} = {expr}" : $"        {type} {name} = {expr};");
                if (layout != null && input)
                    foreach (var f in layout.Fields)
                        Line($"        {name}.f_{f.Name}" + (FS ? " <- " : " = ") + ScalarValue(f.Type, v.GetProperty(f.Name)) + (FS ? "" : ";"));
                if (p.Type == "buffer" && input)
                {
                    int i = 0;
                    foreach (var item in v.EnumerateArray())
                    {
                        if (i >= p.Length) throw new InvalidDataException(p.Name + " 超过缓冲区长度。");
                        Line($"        {name}[{i++}]" + (FS ? " <- " : " = ") + ScalarValue("u8", item) + (FS ? "" : ";"));
                    }
                }
                string prefix = !p.Type.EndsWith('*') ? "" : FS ? "&" : p.Direction == "out" ? "out " : "ref ";
                callArgs.Add(prefix + name);
            }
            string call = $"Invoke({string.Join(", ", callArgs)})";
            Line("        " + (s.Result == "void" ? call + (FS ? "" : ";") : (FS ? "let result = " : "var result = ") + call + (FS ? "" : ";")));
            void Print(string name, string type)
            {
                if (s.Layouts.FirstOrDefault(l => l.Name == type) is {} l)
                {
                    foreach (var f in l.Fields) Print(name + ".f_" + f.Name, f.Type);
                    return;
                }
                if (FS) Line($"        printfn \"%A\" {name}");
                else Line(type == "buffer" ? $"        Console.WriteLine(string.Join(\",\", {name}));" : $"        Console.WriteLine({name});");
            }
            if (s.Result != "void") Print("result", s.Result);
            foreach (var p in s.Parameters!.Where(p => p.Direction != "in")) Print("p_" + p.Name, p.Type.TrimEnd('*'));
            if (!FS) Line("    }");
        }
    }
}
