using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Pop.Core;

/// 翻译服务
public enum TranslationEngine
{
    /// 必应翻译：Edge 浏览器内置翻译用的服务，免费、不用设置
    Bing,
    /// Microsoft Translator（Azure）：用自己的 Key，每月有免费额度
    Azure,
    /// 不在 Pop 里翻译，打开必应翻译网页
    Browser,
}

public sealed record TranslationResult(string Text, string From, string To, string Provider);

public sealed class TranslationException(string message, Exception? inner = null) : Exception(message, inner);

/// 语言代码和中文名
public static class Languages
{
    /// 可以选的目标语言
    public static readonly IReadOnlyList<(string Code, string Name)> Targets =
    [
        ("zh-Hans", "简体中文"), ("zh-Hant", "繁体中文"), ("en", "英语"), ("ja", "日语"), ("ko", "韩语"),
        ("fr", "法语"), ("de", "德语"), ("es", "西班牙语"), ("ru", "俄语"), ("pt", "葡萄牙语"),
        ("it", "意大利语"), ("vi", "越南语"), ("th", "泰语"), ("ar", "阿拉伯语"), ("id", "印尼语"),
    ];

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh"] = "中文", ["zh-Hans"] = "简体中文", ["zh-Hant"] = "繁体中文", ["zh-CN"] = "简体中文", ["zh-TW"] = "繁体中文",
        ["lzh"] = "文言文", ["yue"] = "粤语", ["en"] = "英语", ["ja"] = "日语", ["ko"] = "韩语", ["fr"] = "法语",
        ["de"] = "德语", ["es"] = "西班牙语", ["ru"] = "俄语", ["pt"] = "葡萄牙语", ["it"] = "意大利语",
        ["vi"] = "越南语", ["th"] = "泰语", ["ar"] = "阿拉伯语", ["id"] = "印尼语", ["ms"] = "马来语",
        ["nl"] = "荷兰语", ["pl"] = "波兰语", ["tr"] = "土耳其语", ["uk"] = "乌克兰语", ["sv"] = "瑞典语",
        ["da"] = "丹麦语", ["fi"] = "芬兰语", ["no"] = "挪威语", ["nb"] = "挪威语", ["cs"] = "捷克语",
        ["el"] = "希腊语", ["he"] = "希伯来语", ["hi"] = "印地语", ["hu"] = "匈牙利语", ["ro"] = "罗马尼亚语",
    };

    public static string Name(string code) => Names.TryGetValue(code, out var name) ? name : code;

    /// 目标语言：设置成「自动」时，中文翻成英语，其他翻成简体中文；
    /// 固定了目标语言、而选中的文字本来就是这种语言时，中文和英语互翻
    public static string TargetFor(string text, string setting)
    {
        var chinese = TextActions.ChineseRatio(text) > 0.3;
        if (string.IsNullOrWhiteSpace(setting) || setting.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return chinese ? "en" : "zh-Hans";
        if (chinese && setting.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "en";
        return setting;
    }
}

/// 在 Pop 自己的卡片里翻译
public sealed class Translator(HttpClient http, Func<DateTimeOffset>? clock = null)
{
    public const int MaxLength = 5000;
    public const string BingAuthUrl = "https://edge.microsoft.com/translate/auth";
    public const string BingTranslateUrl = "https://api-edge.cognitive.microsofttranslator.com/translate";
    public const string AzureTranslateUrl = "https://api.cognitive.microsofttranslator.com/translate";

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? token;
    private DateTimeOffset tokenExpires;

    public static string ProviderName(TranslationEngine engine) => engine switch
    {
        TranslationEngine.Azure => "Microsoft Translator",
        _ => "必应翻译",
    };

    public async Task<TranslationResult> TranslateAsync(string text, string to, TranslationEngine engine, string? azureKey = null, string? azureRegion = null, CancellationToken ct = default)
    {
        text = text.Trim();
        if (text.Length == 0) throw new TranslationException("没有要翻译的文字");
        if (text.Length > MaxLength) throw new TranslationException($"文字太长了（超过 {MaxLength} 个字符），请分段翻译");
        switch (engine)
        {
            case TranslationEngine.Azure:
                if (string.IsNullOrWhiteSpace(azureKey)) throw new TranslationException("还没有填写 Microsoft Translator 的 Key：在「设置 → 翻译」里填写，或者换成必应翻译");
                return await SendAsync(AzureTranslateUrl, text, to, request =>
                {
                    request.Headers.Add("Ocp-Apim-Subscription-Key", azureKey.Trim());
                    if (!string.IsNullOrWhiteSpace(azureRegion)) request.Headers.Add("Ocp-Apim-Subscription-Region", azureRegion.Trim());
                }, ProviderName(engine), ct);
            case TranslationEngine.Bing:
                try
                {
                    var bearer = await TokenAsync(forceRefresh: false, ct);
                    return await SendAsync(BingTranslateUrl, text, to, r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer), ProviderName(engine), ct);
                }
                catch (UnauthorizedException)
                {
                    // 令牌过期或者失效了，重新要一个再试一次
                    var bearer = await TokenAsync(forceRefresh: true, ct);
                    try
                    {
                        return await SendAsync(BingTranslateUrl, text, to, r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer), ProviderName(engine), ct);
                    }
                    catch (UnauthorizedException e)
                    {
                        throw new TranslationException("必应翻译拒绝了请求，请稍后再试", e);
                    }
                }
            default:
                throw new TranslationException("当前设置是在浏览器里翻译");
        }
    }

    private sealed class UnauthorizedException : Exception;

    private async Task<string> TokenAsync(bool forceRefresh, CancellationToken ct)
    {
        await tokenLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && token is not null && now() < tokenExpires) return token;
            string fresh;
            try
            {
                using var response = await http.GetAsync(BingAuthUrl, ct);
                if (!response.IsSuccessStatusCode) throw new TranslationException($"连不上必应翻译（{(int)response.StatusCode}）");
                fresh = (await response.Content.ReadAsStringAsync(ct)).Trim();
            }
            catch (HttpRequestException e)
            {
                throw new TranslationException("连不上必应翻译，请检查网络", e);
            }
            if (fresh.Length == 0) throw new TranslationException("必应翻译没有返回令牌");
            token = fresh;
            // 令牌是 JWT，里面有过期时间；读不出来就按 8 分钟算，提前一分钟换新的
            tokenExpires = (JwtExpiry(fresh) ?? now().AddMinutes(9)).AddMinutes(-1);
            return fresh;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    /// JWT 第二段里的 exp（Unix 秒）
    public static DateTimeOffset? JwtExpiry(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private async Task<TranslationResult> SendAsync(string url, string text, string to, Action<HttpRequestMessage> authorize, string provider, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}?api-version=3.0&to={Uri.EscapeDataString(to)}");
        authorize(request);
        var body = JsonSerializer.Serialize(new[] { new Dictionary<string, string> { ["Text"] = text } });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException e)
        {
            throw new TranslationException($"连不上{provider}，请检查网络", e);
        }
        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && provider == ProviderName(TranslationEngine.Bing)) throw new UnauthorizedException();
            if (!response.IsSuccessStatusCode)
            {
                var message = ErrorMessage(json);
                throw new TranslationException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"{provider}拒绝了请求：Key 或区域不对{(message is null ? "" : $"（{message}）")}",
                    HttpStatusCode.TooManyRequests => $"{provider}的请求太频繁了，请稍后再试",
                    _ => $"{provider}出错了（{(int)response.StatusCode}）{(message is null ? "" : $"：{message}")}",
                });
            }
            return Parse(json, to, provider);
        }
    }

    public static TranslationResult Parse(string json, string to, string provider)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var first = doc.RootElement[0];
            var from = first.TryGetProperty("detectedLanguage", out var detected) && detected.TryGetProperty("language", out var lang)
                ? lang.GetString() ?? "" : "";
            var translation = first.GetProperty("translations")[0];
            var text = translation.GetProperty("text").GetString() ?? "";
            var target = translation.TryGetProperty("to", out var t) ? t.GetString() ?? to : to;
            return new TranslationResult(text, from, target, provider);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new TranslationException("翻译服务返回的内容看不懂", e);
        }
    }

    private static string? ErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message) ? message.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// 必应翻译网页（「在浏览器中打开」）
    public static string BrowserUrl(string text, string to) =>
        $"https://www.bing.com/translator?from=auto&to={Uri.EscapeDataString(to)}&text={Uri.EscapeDataString(text.Trim())}";
}

/// 翻译卡片
public static class TranslationCards
{
    public static CardContent Loading(string original, string to) => new(
        "翻译", [], Body: "正在翻译…", Source: Preview(original), Icon: "Translate24", Loading: true,
        Caption: $"翻译成{Languages.Name(to)}");

    public static CardContent Result(string original, TranslationResult result) => new(
        "翻译", [], Body: result.Text, Replacement: result.Text, Source: Preview(original), Icon: "Translate24",
        Caption: $"{(result.From.Length > 0 ? Languages.Name(result.From) : "自动检测")} → {Languages.Name(result.To)} · {result.Provider}",
        Links: [new CardLink("在浏览器中打开", Translator.BrowserUrl(original, result.To))]);

    public static CardContent Failed(string original, string to, string message) => new(
        "翻译", [], Body: message, Source: Preview(original), Icon: "Translate24",
        Links: [new CardLink("在浏览器中打开", Translator.BrowserUrl(original, to))]);

    private static string Preview(string text)
    {
        var flat = string.Join(" ", text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return flat.Length > 160 ? flat[..160] + "…" : flat;
    }
}
