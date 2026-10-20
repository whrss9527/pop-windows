using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Pop.Core;

public sealed record DecodedJwt(string Header, string Payload, IReadOnlyList<ResultLine> Claims);

/// 只在本机解码 JWT，不验证签名，也不将令牌发送到外部服务。
public static class JwtDecoder
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static DecodedJwt? Decode(string text)
    {
        text = text.Trim();
        if (text.Length is < 5 or > 131072) return null;
        var parts = text.Split('.');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0
            || parts.Any(p => p.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))) return null;
        try
        {
            string DecodePart(string value)
            {
                if (value.Length % 4 == 1) throw new FormatException();
                var base64 = value.Replace('-', '+').Replace('_', '/');
                return Utf8.GetString(Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '=')));
            }
            using var header = JsonDocument.Parse(DecodePart(parts[0]));
            using var payload = JsonDocument.Parse(DecodePart(parts[1]));
            if (header.RootElement.ValueKind != JsonValueKind.Object || payload.RootElement.ValueKind != JsonValueKind.Object) return null;
            var rows = new List<ResultLine>();
            foreach (var (key, title) in new[] { ("alg", "算法"), ("typ", "类型") })
                if (header.RootElement.TryGetProperty(key, out var value)) rows.Add(new(title, value.ToString()));
            foreach (var (key, title) in new[] { ("iss", "签发者"), ("sub", "主题"), ("aud", "接收者"), ("jti", "令牌 ID") })
                if (payload.RootElement.TryGetProperty(key, out var value)) rows.Add(new(title, value.ToString()));
            foreach (var (key, title) in new[] { ("iat", "签发时间"), ("nbf", "生效时间"), ("exp", "过期时间") })
            {
                if (!payload.RootElement.TryGetProperty(key, out var value)) continue;
                var raw = value.ToString();
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                    try { rows.Add(new(title, DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture))); continue; }
                    catch (ArgumentOutOfRangeException) { }
                rows.Add(new(title, raw));
            }
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            return new(JsonSerializer.Serialize(header.RootElement, options), JsonSerializer.Serialize(payload.RootElement, options), rows);
        }
        catch (Exception e) when (e is FormatException or DecoderFallbackException or JsonException) { return null; }
    }
    public static CardContent? Card(string text)
    {
        if (Decode(text) is not { } token) return null;
        return new("JWT 解码", [new("载荷 JSON", token.Payload), new("头部 JSON", token.Header), .. token.Claims],
            Monospace: true, Caption: "仅解码，未验证签名或令牌有效性", Icon: "Key24");
    }
}
