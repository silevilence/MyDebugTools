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
        if (Path.GetFileName(image.Path).Equals("kernel32.dll", StringComparison.OrdinalIgnoreCase)
            && entry is "GetTickCount" or "GetCurrentProcessId")
            return new(new Signature(entry, "u32", "winapi", [], [], []).WithSource("内置 API 定义"), false, "内置 API 定义 · 待确认");
        return new(Signature.Unknown(entry), false, "待补齐并确认");
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
