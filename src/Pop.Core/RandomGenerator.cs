using System.Globalization;
using System.Security.Cryptography;

namespace Pop.Core;

/// 随机生成 UUID、密码和数字，用系统的安全随机数
public static class RandomGenerator
{
    // 去掉了容易看错的字符（l、I、O、0、1）
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    public const string Symbols = "!@#$%^&*-_=+?";

    /// 每类字符至少一个。
    public static string Password(int length = 16, bool includeSymbols = true)
    {
        var pools = new List<string> { Lowercase, Uppercase, Digits };
        if (includeSymbols) pools.Add(Symbols);
        var all = string.Concat(pools);
        var characters = new List<char>();
        foreach (var pool in pools) characters.Add(Pick(pool));
        while (characters.Count < Math.Max(length, pools.Count)) characters.Add(Pick(all));
        // 洗牌，免得前几位总是固定的类别
        for (var i = characters.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }
        return new string([.. characters]);
    }

    public static IReadOnlyList<ResultLine> Rows()
    {
        var uuid = Guid.NewGuid().ToString("D").ToUpperInvariant();
        return
        [
            new("UUID", uuid),
            new("UUID 小写", uuid.ToLowerInvariant()),
            new("密码", Password()),
            new("密码 无符号", Password(length: 20, includeSymbols: false)),
            new("6 位数字", RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture)),
        ];
    }

    private static char Pick(string pool) => pool[RandomNumberGenerator.GetInt32(pool.Length)];
}
