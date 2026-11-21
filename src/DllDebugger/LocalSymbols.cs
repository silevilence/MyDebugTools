using System.Reflection.PortableExecutable;
using System.Text;
using static MyDebugTools.DllDebugger.PdbStreams;

namespace MyDebugTools.DllDebugger;

public sealed record LocalSymbols(string Status, Dictionary<int, Signature> ByRva)
{
    internal static LocalSymbols Read(string dll, PEReader pe)
    {
        try
        {
            var entry = pe.ReadDebugDirectory().FirstOrDefault(e => e.Type == DebugDirectoryEntryType.CodeView);
            if (entry.DataSize == 0) return new("DLL 未记录 PDB 身份", []);
            var reference = pe.ReadCodeViewDebugDirectoryData(entry);
            string path = Path.Combine(Path.GetDirectoryName(dll)!, Path.GetFileName(reference.Path));
            if (!File.Exists(path)) path = Path.ChangeExtension(dll, ".pdb");
            if (!File.Exists(path)) return new("未找到同目录 PDB", []);
            var pdb = new PdbStreams(File.ReadAllBytes(path));
            var id = pdb.Stream(1);
            if (new Guid(id.AsSpan(12, 16)) != reference.Guid || I32(id, 8) != reference.Age)
                return new("PDB 与 DLL 不匹配", []);
            var result = ReadFunctions(pdb, pe.PEHeaders);
            return new(result.Count == 0 ? "PDB 匹配，缺少函数类型" : $"本地 PDB 匹配 · {result.Count} 项函数类型", result);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or BadImageFormatException or
            IndexOutOfRangeException or OverflowException or UnauthorizedAccessException)
        { return new("无法读取本地 PDB · " + e.Message, []); }
    }

    static int EndString(byte[] bytes, int start)
    {
        int end = Array.IndexOf(bytes, (byte)0, start);
        return end >= start ? end : throw new InvalidDataException("PDB 字符串不完整。");
    }

    static Dictionary<int, Signature> ReadFunctions(PdbStreams pdb, PEHeaders headers)
    {
        var result = new Dictionary<int, Signature>();
        byte[] types = pdb.Stream(2), dbi = pdb.Stream(3);
        if (types.Length == 0 || dbi.Length == 0) return result;
        var offsets = new Dictionary<int, int>();
        int index = I32(types, 8);
        for (int pos = I32(types, 4); pos < types.Length; pos = checked(pos + U16(types, pos) + 2)) offsets.Add(index++, pos);
        // CodeView stores a structure's byte size before its zero-terminated name.
        (int Size, int Next) Numeric(int pos) => U16(types, pos) switch
        {
            < 0x8000 and var size => (size, pos + 2),
            0x8002 => (U16(types, pos + 2), pos + 4),
            0x8004 => (I32(types, pos + 2), pos + 6),
            _ => (-1, pos)
        };
        string StructName(int record)
        {
            var (size, name) = Numeric(record + 20);
            return size < 0 ? "?" : Encoding.UTF8.GetString(types, name, EndString(types, name) - name);
        }
        var definitions = new Dictionary<string, int>();
        foreach (int pos in offsets.Values)
            if (U16(types, pos + 2) == 0x1505 && (U16(types, pos + 6) & 0x80) == 0)
                definitions[StructName(pos)] = pos;
        string TypeName(int id, int depth = 0)
        {
            if (depth > 12) return "?";
            if (id < 0x1000)
            {
                string t = (id & 255) switch
                {
                    3 => "void", 0x10 or 0x68 or 0x70 => "i8", 0x20 or 0x69 => "u8",
                    0x11 or 0x72 => "i16", 0x21 or 0x71 or 0x73 => "u16",
                    0x12 or 0x74 => "i32", 0x22 or 0x75 => "u32", 0x13 or 0x76 => "i64",
                    0x23 or 0x77 => "u64", 0x40 => "f32", 0x41 => "f64", _ => "?"
                };
                return (id & 0xF00) == 0 ? t : t == "void" ? "handle" : t + "*";
            }
            if (!offsets.TryGetValue(id, out int pos)) return "?";
            return U16(types, pos + 2) switch
            {
                0x1001 => TypeName(I32(types, pos + 4), depth + 1), // LF_MODIFIER
                0x1002 => TypeName(I32(types, pos + 4), depth + 1) + "*", // LF_POINTER
                0x1505 => StructName(pos), // LF_STRUCTURE, including forward references
                _ => "?"
            };
        }

        Layout? StructLayout(string name)
        {
            if (!definitions.TryGetValue(name, out int record) ||
                !offsets.TryGetValue(I32(types, record + 8), out int list) || U16(types, list + 2) != 0x1203)
                return null;
            var fields = new List<(Field Field, int Offset, int Size)>();
            for (int p = list + 4, end = list + 2 + U16(types, list); p < end;)
            {
                if (U16(types, p) != 0x150d) return null; // Only ordinary LF_MEMBER records.
                string type = TypeName(I32(types, p + 4));
                var (position, start) = Numeric(p + 8);
                int size = type switch { "u8" or "i8" => 1, "u16" or "i16" => 2, "u32" or "i32" or "f32" => 4,
                    "u64" or "i64" or "f64" => 8, "handle" => headers.PEHeader!.Magic == PEMagic.PE32 ? 4 : 8, _ => 0 };
                if (position < 0 || size == 0) return null;
                int stop = EndString(types, start);
                fields.Add((new(Encoding.UTF8.GetString(types, start, stop - start), type), position, size));
                p = (stop + 4) & ~3;
            }
            static int Align(int value, int alignment) => checked((value + alignment - 1) & -alignment);
            foreach (int pack in new[] { 8, 1, 2, 4, 16 })
            {
                int cursor = 0, alignment = 1;
                bool matches = true;
                foreach (var field in fields)
                {
                    int a = Math.Min(pack, field.Size);
                    cursor = Align(cursor, a);
                    if (cursor != field.Offset) { matches = false; break; }
                    cursor += field.Size;
                    alignment = Math.Max(alignment, a);
                }
                if (matches && fields.Count > 0 && Align(cursor, alignment) == Numeric(record + 20).Size)
                    return new(name, pack, fields.Select(f => f.Field).ToArray());
            }
            return null;
        }

        int moduleEnd = checked(64 + I32(dbi, 24));
        for (int m = 64; m < moduleEnd;)
        {
            int stream = U16(dbi, m + 34), size = I32(dbi, m + 36);
            m = checked((EndString(dbi, EndString(dbi, m + 64) + 1) + 4) & ~3);
            if (stream == 0xffff || size <= 4) continue;
            var symbols = pdb.Stream(stream);
            if (size > symbols.Length) throw new InvalidDataException("PDB 符号流不完整。");
            for (int pos = 4; pos < size; pos = checked(pos + 2 + U16(symbols, pos)))
            {
                if (U16(symbols, pos + 2) is not (0x1110 or 0x110f)) continue; // GPROC32 / LPROC32
                if (!offsets.TryGetValue(I32(symbols, pos + 28), out int proc) || U16(types, proc + 2) != 0x1008) continue;
                int section = U16(symbols, pos + 36) - 1;
                if (section < 0 || section >= headers.SectionHeaders.Length) continue;
                int rva = checked(headers.SectionHeaders[section].VirtualAddress + I32(symbols, pos + 32));
                if (!offsets.TryGetValue(I32(types, proc + 12), out int list) || U16(types, list + 2) != 0x1201) continue;
                int count = I32(types, list + 4);
                if (count != U16(types, proc + 10) || count > (U16(types, list) - 6) / 4) continue;
                var names = new List<string>();
                int end = Math.Min(size, I32(symbols, pos + 8));
                for (int n = pos + 2 + U16(symbols, pos); n < end; n = checked(n + 2 + U16(symbols, n)))
                    if (U16(symbols, n + 2) == 0x113e && (U16(symbols, n + 8) & 1) != 0)
                        names.Add(Encoding.UTF8.GetString(symbols, n + 10, EndString(symbols, n + 10) - n - 10));
                var args = new Parameter[count];
                for (int i = 0; i < count; i++)
                {
                    var t = TypeName(I32(types, list + 8 + 4 * i));
                    args[i] = new(names.Count == count ? names[i] : "arg" + (i + 1), t, Signature.IsAddress(t) || t.Contains('?') ? "?" : "in");
                }
                string conv = types[proc + 8] switch { 0 => "cdecl", 7 => "stdcall", _ => "?" };
                string returns = TypeName(I32(types, proc + 4));
                var layouts = args.Select(a => a.Type.TrimEnd('*')).Append(returns).Distinct().Select(StructLayout).OfType<Layout>().ToArray();
                result[rva] = new Signature("", returns, conv, args, layouts, []).WithSource("本地 PDB");
            }
        }
        return result;
    }
}
