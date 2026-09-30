using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pop.Core;

/// 翻译服务
public enum TranslationEngine
{
    /// 必应翻译：免费、不用设置。先用必应翻译网页的接口，不行再用 Edge 浏览器内置翻译的接口
    Bing,
    /// Microsoft Translator（Azure）：用自己的 Key，每月有免费额度
    Azure,
    /// 不在 Pop 里翻译，打开必应翻译网页
    Browser,
}

/// <param name="Via">走的是哪条路（日志里用）：edge 是 Edge 浏览器用的接口，web 是必应翻译网页用的接口</param>
public sealed record TranslationResult(string Text, string From, string To, string Provider, string Via = "");

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
public sealed partial class Translator(HttpClient http, Func<DateTimeOffset>? clock = null)
{
    public const int MaxLength = 5000;
    public const string BingAuthUrl = "https://edge.microsoft.com/translate/auth";
    public const string BingTranslateUrl = "https://api-edge.cognitive.microsofttranslator.com/translate";
    public const string BingWebUrl = "https://www.bing.com/translator";
    public const string AzureTranslateUrl = "https://api.cognitive.microsofttranslator.com/translate";

    /// 必应的接口不认没有浏览器标识的请求
    public const string BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36 Edg/140.0.0.0";

    /// 必应翻译网页一次最多翻译这么多字
    public const int WebMaxLength = 1000;

    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? token;
    private DateTimeOffset tokenExpires;
    private WebSession? web;
    private int webRequests;

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
                return await BingAsync(text, to, ct);
            default:
                throw new TranslationException("当前设置是在浏览器里翻译");
        }
    }

    /// 上一次是 Edge 的接口翻译成功的，下次先用它
    private bool preferEdge;

    /// 必应翻译有两条路：必应翻译网页用的接口，和 Edge 浏览器内置翻译用的接口。
    /// 先用上一次成功的那条，失败了换另一条；都失败的话报先试的那条的错误
    private async Task<TranslationResult> BingAsync(string text, string to, CancellationToken ct)
    {
        var routes = new List<(bool Edge, Func<Task<TranslationResult>> Run)>
        {
            (false, () => WebLongAsync(text, to, ct)),
            (true, () => EdgeAsync(text, to, ct)),
        };
        if (preferEdge) routes.Reverse();
        TranslationException? first = null;
        foreach (var (edge, run) in routes)
        {
            try
            {
                var result = await run();
                preferEdge = edge;
                return result;
            }
            catch (TranslationException e) when (!ct.IsCancellationRequested)
            {
                first ??= e;
            }
        }
        throw first!;
    }

    /// 网页接口一次只能翻译 1000 个字：长文字按段落、句子分成几块依次翻译再拼起来
    private async Task<TranslationResult> WebLongAsync(string text, string to, CancellationToken ct)
    {
        var chunks = Chunks(text, WebMaxLength);
        if (chunks.Count == 1) return await WebAsync(text, to, ct);
        var output = new StringBuilder();
        TranslationResult? head = null;
        // 在句子中间切开的两块，译成用空格分词的语言时中间补一个空格
        var spaced = !to.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && to is not "ja" and not "ko" and not "th";
        foreach (var (chunk, separator) in chunks)
        {
            var part = await WebAsync(chunk, to, ct);
            head ??= part;
            output.Append(part.Text).Append(separator.Length == 0 && spaced ? " " : separator);
        }
        return head! with { Text = output.ToString().TrimEnd() };
    }

    /// 把文字分成不超过 max 个字符的几块：优先在换行处分，其次在句末标点处，实在不行硬切。
    /// 每一块带上它后面原来的分隔（换行），拼回去时保持原来的段落
    public static IReadOnlyList<(string Text, string Separator)> Chunks(string text, int max)
    {
        var result = new List<(string, string)>();
        var rest = text;
        while (rest.Length > max)
        {
            var window = rest[..max];
            var cut = window.LastIndexOf('\n');
            var separator = "\n";
            if (cut < max / 3)
            {
                cut = window.LastIndexOfAny(['。', '！', '？', '.', '!', '?', ';', '；']);
                separator = "";
                if (cut < max / 3) cut = max - 1;
            }
            var chunk = rest[..(cut + 1)];
            result.Add((separator == "\n" ? chunk.TrimEnd('\r', '\n') : chunk, separator));
            rest = rest[(cut + 1)..];
            if (separator == "\n") rest = rest.TrimStart('\r', '\n');
        }
        if (rest.Length > 0) result.Add((rest, ""));
        return result;
    }

    private async Task<TranslationResult> EdgeAsync(string text, string to, CancellationToken ct)
    {
        void Authorize(HttpRequestMessage request, string bearer)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
        }
        var provider = ProviderName(TranslationEngine.Bing);
        try
        {
            var bearer = await TokenAsync(forceRefresh: false, ct);
            return await SendAsync(BingTranslateUrl, text, to, r => Authorize(r, bearer), provider, ct) with { Via = "edge" };
        }
        catch (UnauthorizedException)
        {
            // 令牌过期或者失效了，重新要一个再试一次
            var bearer = await TokenAsync(forceRefresh: true, ct);
            try
            {
                return await SendAsync(BingTranslateUrl, text, to, r => Authorize(r, bearer), provider, ct) with { Via = "edge" };
            }
            catch (UnauthorizedException e)
            {
                throw new TranslationException("必应翻译拒绝了请求，请稍后再试", e);
            }
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
                using var request = new HttpRequestMessage(HttpMethod.Get, BingAuthUrl);
                request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
                using var response = await http.SendAsync(request, ct);
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

    /// 必应翻译网页的会话：网页里的 IG、IID 和防滥用的 key、token，翻译时要一起发回去
    public sealed record WebSession(string Host, string Ig, string Iid, string Key, string Token, DateTimeOffset Expires);

    [GeneratedRegex(@"IG:""([0-9A-Za-z]+)""")]
    private static partial Regex IgPattern();

    [GeneratedRegex(@"data-iid=""([^""]+)""")]
    private static partial Regex IidPattern();

    [GeneratedRegex(@"params_AbusePreventionHelper\s*=\s*\[\s*(\d+)\s*,\s*""([^""]+)""\s*,\s*(\d+)")]
    private static partial Regex AbusePattern();

    /// 从必应翻译网页里读出会话参数；读不出来返回 null
    public static WebSession? ParseWebSession(string html, string host, DateTimeOffset now)
    {
        var ig = IgPattern().Match(html);
        var abuse = AbusePattern().Match(html);
        if (!ig.Success || !abuse.Success) return null;
        var iid = IidPattern().Match(html);
        var lifetime = long.TryParse(abuse.Groups[3].Value, out var ms) && ms > 0 ? TimeSpan.FromMilliseconds(ms) : TimeSpan.FromMinutes(30);
        if (lifetime > TimeSpan.FromHours(1)) lifetime = TimeSpan.FromHours(1);
        return new WebSession(host, ig.Groups[1].Value, iid.Success ? iid.Groups[1].Value : "translator.5028", abuse.Groups[1].Value, abuse.Groups[2].Value, now + lifetime - TimeSpan.FromMinutes(1));
    }

    private async Task<WebSession> WebSessionAsync(bool forceRefresh, CancellationToken ct)
    {
        await tokenLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && web is not null && now() < web.Expires) return web;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, BingWebUrl);
                request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
                request.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
                using var response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) throw new TranslationException($"连不上必应翻译网页（{(int)response.StatusCode}）");
                var html = await response.Content.ReadAsStringAsync(ct);
                // 在中国大陆会跳到 cn.bing.com，之后的请求也发到那里
                var host = response.RequestMessage?.RequestUri?.Host ?? "www.bing.com";
                web = ParseWebSession(html, host, now()) ?? throw new TranslationException("必应翻译网页改版了，读不到翻译参数");
                webRequests = 0;
                return web;
            }
            catch (HttpRequestException e)
            {
                throw new TranslationException("连不上必应翻译，请检查网络", e);
            }
        }
        finally
        {
            tokenLock.Release();
        }
    }

    /// 必应翻译网页用的接口（ttranslatev3），返回的格式和正式接口一样
    private async Task<TranslationResult> WebAsync(string text, string to, CancellationToken ct)
    {
        var provider = ProviderName(TranslationEngine.Bing);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var session = await WebSessionAsync(forceRefresh: attempt > 0, ct);
            var url = $"https://{session.Host}/ttranslatev3?isVertical=1&IG={session.Ig}&IID={session.Iid}.{Interlocked.Increment(ref webRequests)}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
            request.Headers.Referrer = new Uri($"https://{session.Host}/translator");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["fromLang"] = "auto-detect",
                ["to"] = to,
                ["text"] = text,
                ["token"] = session.Token,
                ["key"] = session.Key,
            });
            string json;
            HttpStatusCode status;
            try
            {
                using var response = await http.SendAsync(request, ct);
                json = await response.Content.ReadAsStringAsync(ct);
                status = response.StatusCode;
            }
            catch (HttpRequestException e)
            {
                throw new TranslationException("连不上必应翻译，请检查网络", e);
            }
            if (status == HttpStatusCode.TooManyRequests) throw new TranslationException("必应翻译的请求太频繁了，请稍后再试");
            // 会话过期或者被拒绝时返回的是一个带 statusCode 的对象或者 401：换个会话再试一次
            if ((int)status < 300 && json.TrimStart().StartsWith('[')) return Parse(json, to, provider) with { Via = "web" };
            if ((int)status >= 500) throw new TranslationException($"必应翻译出错了（{(int)status}），请稍后再试");
            web = null;
        }
        throw new TranslationException("必应翻译拒绝了请求，请稍后再试");
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
