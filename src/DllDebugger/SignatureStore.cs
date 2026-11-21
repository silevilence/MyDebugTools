using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyDebugTools.DllDebugger;

public sealed record SignatureMatch(Signature Signature, bool Confirmed, string Status);

public sealed class SignatureStore(string root)
{
    static string Key(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    string Exact(DllImage image, string entry) => Path.Combine(root, image.Identity, Key(entry) + ".json");
    string Candidate(DllImage image, string entry) => Path.Combine(root, "paths", Key(image.Path.ToUpperInvariant()), Key(entry) + ".json");

    public SignatureMatch Find(DllImage image, string entry)
    {
        foreach (var (path, exact) in new[] { (Exact(image, entry), true), (Candidate(image, entry), false) })
            if (File.Exists(path))
                return new(JsonSerializer.Deserialize<Signature>(File.ReadAllText(path))!, exact,
                    exact ? "已确认 · 同一份 DLL" : "DLL 已换版 · 旧签名待重新确认");
        var export = image.Exports.FirstOrDefault(e => e.Entry == entry || "#" + e.Ordinal == entry);
        if (export != null && image.Symbols.ByRva.TryGetValue(export.Rva, out var pdb))
            return new(pdb with { Entry = entry }, false, "本地 PDB · 待确认");
        var sdk = WindowsApi.Find(image.Path, export?.Name);
        if (sdk != null) return new(sdk.Signature with { Entry = entry }, false, sdk.Source + " · 待确认");
        return new(Signature.Unknown(entry), false, "待补齐并确认");
    }

    public Signature ConfirmedDraft(DllImage image, Signature draft)
    {
        draft.Validate();
        var saved = Find(image, draft.Entry);
        if (!saved.Confirmed || !draft.Values().SequenceEqual(saved.Signature.Values()))
            throw new InvalidDataException("请先确认并保存当前签名。");
        return saved.Signature;
    }

    public void Confirm(DllImage image, Signature signature)
    {
        signature.Validate();
        if (!image.Exports.Any(e => e.Entry == signature.Entry || "#" + e.Ordinal == signature.Entry))
            throw new InvalidDataException("DLL 中没有此入口：" + signature.Entry);
        var json = JsonSerializer.Serialize(signature);
        foreach (var path in new[] { Exact(image, signature.Entry), Candidate(image, signature.Entry) })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, json);
            File.Move(temp, path, true);
        }
    }
}
