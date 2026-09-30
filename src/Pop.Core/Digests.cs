using System.Security.Cryptography;
using System.Text;

namespace Pop.Core;

/// 文字的哈希值（按 UTF-8 计算），十六进制小写
public static class Digests
{
    public static IReadOnlyList<ResultLine> Rows(string text) => Rows(Encoding.UTF8.GetBytes(text));

    public static IReadOnlyList<ResultLine> Rows(byte[] data) =>
    [
        new("MD5", Hex(MD5.HashData(data))),
        new("SHA-1", Hex(SHA1.HashData(data))),
        new("SHA-256", Hex(SHA256.HashData(data))),
        new("SHA-512", Hex(SHA512.HashData(data))),
    ];

    public static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
