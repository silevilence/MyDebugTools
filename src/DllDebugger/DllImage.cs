using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace MyDebugTools.DllDebugger;

public sealed record Export(string? Name, int Ordinal, int Rva, string Kind, string? Forwarder)
{
    public string Entry => Name ?? $"#{Ordinal}";
    public override string ToString() => $"{Entry}　#{Ordinal}　0x{Rva:X8}　{Kind} {Forwarder}".TrimEnd();
}

public sealed record DllImage(string Path, string Identity, string Architecture, IReadOnlyList<Export> Exports)
{
    public LocalSymbols Symbols { get; private init; } = new("", []);
    public static DllImage Open(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        using var stream = File.OpenRead(path);
        var identity = Convert.ToHexString(SHA256.HashData(stream));
        stream.Position = 0;
        using var pe = new PEReader(stream);
        var headers = pe.PEHeaders;
        if (!headers.IsDll || headers.CorHeader != null)
            throw new InvalidDataException("请选择原生 DLL。");
        var arch = headers.CoffHeader.Machine switch
        {
            Machine.I386 => "x86",
            Machine.Amd64 => "x64",
            _ => throw new InvalidDataException($"不支持的架构 {headers.CoffHeader.Machine}")
        };
        var exports = new List<Export>();
        var dir = headers.PEHeader!.ExportTableDirectory;
        if (dir.Size > 0)
        {
            var table = pe.GetSectionData(dir.RelativeVirtualAddress).GetReader(16, 24);
            int first = table.ReadInt32(), count = table.ReadInt32(), named = table.ReadInt32();
            var addresses = pe.GetSectionData(table.ReadInt32()).GetReader(0, checked(count * 4));
            int nameRva = table.ReadInt32(), ordinalRva = table.ReadInt32();
            string ReadName(int rva)
            {
                var reader = pe.GetSectionData(rva).GetReader();
                return reader.ReadUTF8(reader.IndexOf(0) is >= 0 and var length ? length : throw new BadImageFormatException());
            }
            var aliases = new List<(int Index, string Name)>();
            if (named > 0)
            {
                var names = pe.GetSectionData(nameRva).GetReader(0, checked(named * 4));
                var ordinals = pe.GetSectionData(ordinalRva).GetReader(0, checked(named * 2));
                for (int i = 0; i < named; i++)
                    aliases.Add((ordinals.ReadUInt16(), ReadName(names.ReadInt32())));
            }
            var byIndex = aliases.ToLookup(a => a.Index, a => a.Name);
            for (int i = 0; i < count; i++)
            {
                int rva = addresses.ReadInt32();
                if (rva == 0) continue;
                int section = headers.GetContainingSectionIndex(rva);
                var flags = section < 0 ? 0 : headers.SectionHeaders[section].SectionCharacteristics;
                bool forwarded = (long)rva >= dir.RelativeVirtualAddress && (long)rva - dir.RelativeVirtualAddress < dir.Size;
                string kind = forwarded ? "转发" :
                    flags.HasFlag(SectionCharacteristics.ContainsCode | SectionCharacteristics.MemExecute) ? "代码" :
                    !flags.HasFlag(SectionCharacteristics.MemExecute) &&
                    (flags & (SectionCharacteristics.ContainsInitializedData | SectionCharacteristics.ContainsUninitializedData)) != 0 ? "数据" : "未知";
                foreach (var name in byIndex[i].DefaultIfEmpty())
                    exports.Add(new(name, checked(first + i), rva, kind, forwarded ? ReadName(rva) : null));
            }
        }
        return new(path, identity, arch, exports) { Symbols = LocalSymbols.Read(path, pe) };
    }
}
