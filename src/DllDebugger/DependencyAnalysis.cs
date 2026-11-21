using System.Reflection.PortableExecutable;

namespace MyDebugTools.DllDebugger;

public enum DependencyReference { First, Shared, Cycle }

public sealed record DependencyEdge(string Name, bool Delayed, DependencyModule? Target,
    DependencyReference Reference, string? Problem);

public sealed class DependencyModule(string path, string architecture)
{
    public string Path { get; } = path;
    public string Architecture { get; } = architecture;
    public IReadOnlyList<DependencyEdge> Imports => Edges;
    internal List<DependencyEdge> Edges { get; } = [];
}

public sealed record DependencyAnalysis(DependencyModule Root, IReadOnlyList<DependencyModule> Modules)
{
    private sealed record Import(string Name, bool Delayed);
    private sealed record Image(string Architecture, List<Import> Imports);

    private static Image ReadImage(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var headers = pe.PEHeaders;
        var header = headers.PEHeader ?? throw new BadImageFormatException("缺少 PE 头。");
        string arch = headers.CoffHeader.Machine switch
        {
            Machine.Amd64 => "x64",
            Machine.I386 => "x86",
            var other => other.ToString()
        };
        var imports = new List<Import>();
        void ReadTable(DirectoryEntry directory, bool delayed)
        {
            if (directory.Size == 0) return;
            var table = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader(0, directory.Size);
            int stride = delayed ? 32 : 20;
            while (table.RemainingBytes >= stride)
            {
                int offset = table.Offset;
                uint flags = table.ReadUInt32();
                table.Offset = offset + (delayed ? 4 : 12);
                uint name = table.ReadUInt32();
                if (name == 0) return;
                int rva = delayed && (flags & 1) == 0
                    ? checked((int)(name - header.ImageBase)) : checked((int)name);
                var reader = pe.GetSectionData(rva).GetReader();
                int length = reader.IndexOf(0);
                if (length <= 0) throw new BadImageFormatException("导入名称无效。");
                imports.Add(new(reader.ReadUTF8(length), delayed));
                table.Offset = offset + stride;
            }
            throw new BadImageFormatException("导入表未终止。");
        }
        ReadTable(header.ImportTableDirectory, false);
        ReadTable(header.DelayImportTableDirectory, true);
        return new(arch, imports);
    }

    public static DependencyAnalysis Read(string file, CancellationToken cancellation = default)
    {
        var path = Path.GetFullPath(file);
        var image = ReadImage(path);
        var root = new DependencyModule(path, image.Architecture);
        var directories = new[] { Path.GetDirectoryName(path)!, Environment.GetFolderPath(
            image.Architecture == "x86" ? Environment.SpecialFolder.SystemX86 : Environment.SpecialFolder.System) };
        var comparer = StringComparer.OrdinalIgnoreCase;
        var modules = new Dictionary<string, DependencyModule>(comparer) { [path] = root };
        var active = new HashSet<string>(comparer) { path };
        var stack = new Stack<(DependencyModule Module, List<Import> Imports, int Index)>();
        stack.Push((root, image.Imports, 0));
        while (stack.TryPop(out var frame))
        {
            cancellation.ThrowIfCancellationRequested();
            if (frame.Index == frame.Imports.Count)
            {
                active.Remove(frame.Module.Path);
                continue;
            }
            var import = frame.Imports[frame.Index];
            stack.Push((frame.Module, frame.Imports, frame.Index + 1));
            DependencyModule? target = null;
            var reference = DependencyReference.First;
            string? problem = "缺失 · 搜索目录中没有此文件";
            if (import.Name == Path.GetFileName(import.Name) && import.Name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            {
                foreach (string directory in directories)
                {
                    string candidate = Path.Combine(directory, import.Name);
                    if (modules.TryGetValue(candidate, out target))
                    {
                        reference = active.Contains(candidate) ? DependencyReference.Cycle : DependencyReference.Shared;
                        break;
                    }
                    if (!File.Exists(candidate)) continue;
                    try
                    {
                        var child = ReadImage(candidate);
                        if (child.Architecture != root.Architecture)
                        {
                            problem = $"架构不匹配 · {candidate} 为 {child.Architecture}，目标为 {root.Architecture}";
                            continue;
                        }
                        target = new(candidate, child.Architecture);
                        modules.Add(candidate, target);
                        active.Add(candidate);
                        stack.Push((target, child.Imports, 0));
                        break;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or
                        BadImageFormatException or ArgumentOutOfRangeException or OverflowException)
                    {
                        problem = $"无法读取 · {candidate} · {e.Message}";
                    }
                }
            }
            else problem = "未解析 · 导入名称含路径或无效字符";
            frame.Module.Edges.Add(new(import.Name, import.Delayed, target, reference, target == null ? problem : null));
        }
        return new(root, modules.Values.ToArray());
    }
}
