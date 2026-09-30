using Pop.Core;

namespace Pop.Core.Tests;

public class TextTransformsTests
{
    private static string? Value(IReadOnlyList<ResultLine> rows, string label) =>
        rows.FirstOrDefault(r => r.Label == label)?.Value;

    [Fact]
    public void CaseConversion()
    {
        Assert.Equal(["get", "HTTP", "Response"], CaseConverter.Words("getHTTPResponse"));
        Assert.Equal(["snake", "case", "value"], CaseConverter.Words("snake_case value"));
        Assert.Equal(["version2", "Update"], CaseConverter.Words("version2Update"));
        var rows = CaseConverter.Conversions("hello world");
        Assert.Equal("HELLO WORLD", Value(rows, "大写"));
        Assert.Equal("Hello World", Value(rows, "首字母大写"));
        Assert.Equal("helloWorld", Value(rows, "camelCase"));
        Assert.Equal("HelloWorld", Value(rows, "PascalCase"));
        Assert.Equal("hello-world", Value(rows, "kebab-case"));
        Assert.Equal("HELLO_WORLD", Value(rows, "CONSTANT"));
        Assert.Empty(CaseConverter.Conversions("  "));
    }

    [Fact]
    public void Codecs()
    {
        Assert.Equal("UG9wIOS9oOWlvQ==", TextCodec.Base64Encode("Pop 你好"));
        Assert.Equal("Pop 你好", TextCodec.Base64Decode("UG9wIOS9oOWlvQ=="));
        Assert.Equal("hello", TextCodec.Base64Decode("aGVsbG8"));
        Assert.Null(TextCodec.Base64Decode("hi"));
        Assert.Equal("a%20b%26c", TextCodec.UrlEncode("a b&c"));
        Assert.Equal("a b&c", TextCodec.UrlDecode("a%20b%26c"));
        Assert.Null(TextCodec.UrlDecode("plain"));
        Assert.Equal("\\u00e9\\ud83d\\ude00a", TextCodec.UnicodeEscape("é😀a"));
        Assert.Equal("é😀a", TextCodec.UnicodeUnescape("\\u00e9\\ud83d\\ude00a"));
        Assert.Equal("x😀y", TextCodec.UnicodeUnescape("x\\u{1F600}y"));
        Assert.Equal("&lt;a href=&quot;x&quot;&gt;&#39;&amp;&#39;&lt;/a&gt;", TextCodec.HtmlEscape("<a href=\"x\">'&'</a>"));
        Assert.Equal("<b> & 中文 &unknown;", TextCodec.HtmlUnescape("&lt;b&gt; &amp; &#20013;&#x6587; &unknown;"));
        var rows = TextCodec.Conversions("aGVsbG8=");
        Assert.Equal("Base64 解码", rows[0].Label);
        Assert.Equal("hello", rows[0].Value);
    }

    [Fact]
    public void CodecEdgeCases()
    {
        // URL 安全字母表、解出来是控制字符的不算
        Assert.Equal("??>", TextCodec.Base64Decode("Pz8-"));
        Assert.Null(TextCodec.Base64Decode("AAECAw=="));
        // 不完整的 % 转义、不是 UTF-8 的字节
        Assert.Null(TextCodec.UrlDecode("100%"));
        Assert.Null(TextCodec.UrlDecode("%FF"));
        Assert.Equal("a+b c", TextCodec.UrlDecode("a+b%20c"));
        // 落单的代理项换成 U+FFFD
        Assert.Equal("�x", TextCodec.UnicodeUnescape("\\ud83dx"));
        Assert.Null(TextCodec.UnicodeUnescape("no escapes"));
        Assert.Null(TextCodec.HtmlUnescape("a & b"));
        Assert.Equal("…", TextCodec.HtmlUnescape("&hellip;"));
        var plain = TextCodec.Conversions("abc");
        Assert.Equal(["Base64 编码", "URL 编码"], plain.Select(r => r.Label));
    }

    [Fact]
    public void DigestsOfText()
    {
        var rows = Digests.Rows("abc");
        Assert.Equal(["MD5", "SHA-1", "SHA-256", "SHA-512"], rows.Select(r => r.Label));
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", Value(rows, "MD5"));
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", Value(rows, "SHA-1"));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Value(rows, "SHA-256"));
        Assert.StartsWith("ddaf35a193617aba", Value(rows, "SHA-512"));
    }

    [Fact]
    public void Random()
    {
        var password = RandomGenerator.Password();
        Assert.Equal(16, password.Length);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, c => RandomGenerator.Symbols.Contains(c));
        var plain = RandomGenerator.Password(length: 20, includeSymbols: false);
        Assert.Equal(20, plain.Length);
        Assert.All(plain, c => Assert.True(char.IsLetterOrDigit(c)));
        Assert.NotEqual(RandomGenerator.Password(), RandomGenerator.Password());

        var rows = RandomGenerator.Rows();
        Assert.Equal(["UUID", "UUID 小写", "密码", "密码 无符号", "6 位数字"], rows.Select(r => r.Label));
        Assert.Equal(Value(rows, "UUID")!.ToLowerInvariant(), Value(rows, "UUID 小写"));
        Assert.Matches("^[0-9]{6}$", Value(rows, "6 位数字")!);
    }
}
