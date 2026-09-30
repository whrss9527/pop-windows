using System.Security.Cryptography;

namespace Pop.Core;

/// SHA256SUMS.txt：每行「64 位十六进制校验和  文件名」，文件名前可能带 * 表示二进制模式
public static class Checksums
{
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var space = line.IndexOfAny([' ', '\t']);
            if (space != 64) continue;
            var hash = line[..64];
            if (!hash.All(Uri.IsHexDigit)) continue;
            var name = line[space..].Trim().TrimStart('*');
            if (name.Length > 0) result[name] = hash.ToLowerInvariant();
        }
        return result;
    }

    public static string Sha256(Stream stream) => Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Sha256(stream);
    }

    /// 文件的校验和和 SHA256SUMS.txt 里记的是否一致；清单里没有这个文件也算不一致
    public static bool Matches(IReadOnlyDictionary<string, string> sums, string fileName, string actualHash) =>
        sums.TryGetValue(fileName, out var expected) && string.Equals(expected, actualHash, StringComparison.OrdinalIgnoreCase);
}
