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
        catch (Exception e) when (e is IOException or ArgumentException or BadImageFormatException or
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
                _ => "?"
            };
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
                result[rva] = new Signature("", TypeName(I32(types, proc + 4)), conv, args, [], []).WithSource("本地 PDB");
            }
        }
        return result;
    }
}
