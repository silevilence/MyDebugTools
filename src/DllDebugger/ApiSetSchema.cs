using System.Reflection.PortableExecutable;

namespace MyDebugTools.DllDebugger;

internal sealed class ApiSetSchema
{
    // A v6 namespace maps a version family to a default host and, in some
    // cases, different hosts for specific importing DLLs.
    private readonly Dictionary<string, Dictionary<string, string>> families = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<ApiSetSchema> Current = new(() => new());

    private ApiSetSchema()
    {
        using var file = File.OpenRead(Path.Combine(Environment.SystemDirectory, "apisetschema.dll"));
        using var pe = new PEReader(file);
        var section = pe.GetSectionData(".apiset");
        var header = section.GetReader();
        if (header.ReadUInt32() != 6) throw new InvalidDataException("本机 API Set 映射版本暂不支持。");
        header.Offset = 12;
        int count = header.ReadInt32(), entries = header.ReadInt32();
        string ReadName(int offset, int length) => section.GetReader(offset, length).ReadUTF16(length);
        for (int i = 0; i < count; i++)
        {
            var entry = section.GetReader(checked(entries + i * 24 + 4), 20);
            int name = entry.ReadInt32();
            entry.ReadInt32(); // Full name length, including the minor version.
            string family = ReadName(name, entry.ReadInt32());
            int values = entry.ReadInt32(), valueCount = entry.ReadInt32();
            var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int j = 0; j < valueCount; j++)
            {
                var value = section.GetReader(checked(values + j * 20 + 4), 16);
                string importer = ReadName(value.ReadInt32(), value.ReadInt32());
                hosts[importer] = ReadName(value.ReadInt32(), value.ReadInt32());
            }
            families[family] = hosts;
        }
    }

    internal static bool IsContract(string name) =>
        name.StartsWith("api-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("ext-", StringComparison.OrdinalIgnoreCase);

    internal static string? Resolve(string contract, string importer)
    {
        string family = Path.GetFileNameWithoutExtension(contract);
        int minorVersion = family.LastIndexOf('-');
        if (minorVersion < 0 || !Current.Value.families.TryGetValue(family[..minorVersion], out var hosts)) return null;
        return hosts.TryGetValue(importer, out var host) ? host : hosts.GetValueOrDefault("");
    }
}
