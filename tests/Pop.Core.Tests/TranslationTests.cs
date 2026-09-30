using System.Net;
using System.Text;
using Pop.Core;

namespace Pop.Core.Tests;

public class TranslationTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string Answer = """[{"detectedLanguage":{"language":"en","score":1.0},"translations":[{"text":"你好，世界","to":"zh-Hans"}]}]""";

    private static string Jwt(long exp) =>
        "x." + Convert.ToBase64String(Encoding.UTF8.GetBytes($$"""{"exp":{{exp}}}""")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".y";

    private const string WebPage = """
        <html><script>var _G={IG:"A1B2C3D4E5",EF:{}};var params_AbusePreventionHelper = [1759226400000,"tok-EN_123",3600000];</script>
        <div id="rich_tta" data-iid="translator.5028"></div></html>
        """;

    private static bool Is(HttpRequestMessage request, string url) => request.RequestUri!.AbsoluteUri.StartsWith(url, StringComparison.Ordinal);

    private static HttpResponseMessage Page(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html) };

    [Fact]
    public async Task BingUsesTheWebTranslatorFirst()
    {
        var handler = new FakeHandler((request, _) => Is(request, Translator.BingWebUrl) ? Page(WebPage) : Json(Answer));
        var translator = new Translator(new HttpClient(handler));

        var result = await translator.TranslateAsync("  Hello, world  ", "zh-Hans", TranslationEngine.Bing);
        Assert.Equal("你好，世界", result.Text);
        Assert.Equal("en", result.From);
        Assert.Equal("zh-Hans", result.To);
        Assert.Equal("必应翻译", result.Provider);
        Assert.Equal("web", result.Via);

        var (post, body) = handler.Requests.Last();
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Equal("https://www.bing.com/ttranslatev3?isVertical=1&IG=A1B2C3D4E5&IID=translator.5028.1", post.RequestUri!.AbsoluteUri);
        Assert.Equal("https://www.bing.com/translator", post.Headers.Referrer?.AbsoluteUri);
        Assert.Equal("fromLang=auto-detect&to=zh-Hans&text=Hello%2C+world&token=tok-EN_123&key=1759226400000", body);
        // 都带浏览器标识；网页接口能用时不去碰 Edge 的接口
        Assert.All(handler.Requests, r => Assert.Contains("Edg/", r.Request.Headers.UserAgent.ToString()));
        Assert.DoesNotContain(handler.Requests, r => Is(r.Request, Translator.BingAuthUrl));

        // 会话还没过期：第二次不再打开网页，请求编号加一
        await translator.TranslateAsync("again", "zh-Hans", TranslationEngine.Bing);
        Assert.Single(handler.Requests, r => Is(r.Request, Translator.BingWebUrl));
        Assert.EndsWith("IID=translator.5028.2", handler.Requests.Last().Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task BingWebSessionIsRenewedOnceWhenRejected()
    {
        foreach (var rejection in new Func<HttpResponseMessage>[] { () => Json("""{"statusCode":205}"""), () => Json("{}", HttpStatusCode.Unauthorized) })
        {
            var translateCalls = 0;
            var handler = new FakeHandler((request, _) =>
                Is(request, Translator.BingWebUrl) ? Page(WebPage) : ++translateCalls == 1 ? rejection() : Json(Answer));
            var result = await new Translator(new HttpClient(handler)).TranslateAsync("Hello", "zh-Hans", TranslationEngine.Bing);
            Assert.Equal("你好，世界", result.Text);
            Assert.Equal(2, handler.Requests.Count(r => Is(r.Request, Translator.BingWebUrl)));
        }
    }

    [Fact]
    public async Task BingFallsBackToEdgeAndRemembersIt()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var handler = new FakeHandler((request, _) =>
            Is(request, Translator.BingWebUrl) ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Is(request, Translator.BingAuthUrl) ? Page(Jwt(now.ToUnixTimeSeconds() + 600))
            : Json(Answer));
        var translator = new Translator(new HttpClient(handler), () => now);

        var result = await translator.TranslateAsync("Hello, world", "zh-Hans", TranslationEngine.Bing);
        Assert.Equal("你好，世界", result.Text);
        Assert.Equal("edge", result.Via);
        var (post, body) = handler.Requests.Last();
        Assert.StartsWith(Translator.BingTranslateUrl + "?api-version=3.0&to=zh-Hans", post.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", post.Headers.Authorization?.Scheme);
        Assert.Contains("Edg/", post.Headers.UserAgent.ToString());
        Assert.Equal("""[{"Text":"Hello, world"}]""", body);

        // 下一次先用 Edge 的接口，令牌没过期也不再要
        var before = handler.Requests.Count;
        await translator.TranslateAsync("again", "zh-Hans", TranslationEngine.Bing);
        Assert.Single(handler.Requests.Skip(before));
        Assert.Single(handler.Requests, r => Is(r.Request, Translator.BingAuthUrl));
    }

    [Fact]
    public async Task BingRefreshesTheEdgeTokenOnceWhenRejected()
    {
        var translateCalls = 0;
        var handler = new FakeHandler((request, _) =>
        {
            if (Is(request, Translator.BingWebUrl)) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (Is(request, Translator.BingAuthUrl)) return Page("not-a-jwt");
            return ++translateCalls == 1 ? Json("{}", HttpStatusCode.Unauthorized) : Json(Answer);
        });
        var result = await new Translator(new HttpClient(handler)).TranslateAsync("Hello", "zh-Hans", TranslationEngine.Bing);
        Assert.Equal("你好，世界", result.Text);
        Assert.Equal(2, handler.Requests.Count(r => Is(r.Request, Translator.BingAuthUrl)));
    }

    [Fact]
    public async Task WhenBothBingRoutesFailTheFirstErrorIsShown()
    {
        var handler = new FakeHandler((request, _) => Is(request, Translator.BingAuthUrl)
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Page("<html>captcha</html>"));
        var e = await Assert.ThrowsAsync<TranslationException>(() => new Translator(new HttpClient(handler)).TranslateAsync("Hello", "zh-Hans", TranslationEngine.Bing));
        Assert.Contains("改版", e.Message);
    }

    [Fact]
    public async Task LongTextIsTranslatedInChunks()
    {
        var paragraph = string.Join(" ", Enumerable.Repeat("This is a sentence.", 30));
        var text = string.Join("\n", Enumerable.Repeat(paragraph, 5));
        var handler = new FakeHandler((request, body) => Is(request, Translator.BingWebUrl) ? Page(WebPage)
            : Json($$"""[{"detectedLanguage":{"language":"en"},"translations":[{"text":"段落{{body!.Length}}","to":"zh-Hans"}]}]"""));
        var result = await new Translator(new HttpClient(handler)).TranslateAsync(text, "zh-Hans", TranslationEngine.Bing);
        var posts = handler.Requests.Where(r => r.Request.Method == HttpMethod.Post).ToList();
        Assert.True(posts.Count >= 3);
        Assert.Equal(posts.Count, result.Text.Split('\n').Length);
        Assert.Equal("en", result.From);
    }

    [Fact]
    public void ChunksSplitAtLinesThenSentences()
    {
        Assert.Equal([("short", "")], Translator.Chunks("short", 100));
        var lines = Translator.Chunks("aaaa\nbbbb\ncccc", 10);
        Assert.Equal([("aaaa\nbbbb", "\n"), ("cccc", "")], lines);
        var sentences = Translator.Chunks("第一句话很长。第二句话也很长。第三句", 8);
        Assert.Equal("第一句话很长。", sentences[0].Text);
        Assert.All(sentences, c => Assert.True(c.Text.Length <= 8));
        Assert.Equal("第一句话很长。第二句话也很长。第三句", string.Concat(sentences.Select(c => c.Text + c.Separator)));
        var hard = Translator.Chunks(new string('x', 25), 10);
        Assert.Equal([10, 10, 5], hard.Select(c => c.Text.Length));
    }

    [Fact]
    public void WebSessionIsReadFromThePage()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var session = Translator.ParseWebSession(WebPage, "cn.bing.com", now)!;
        Assert.Equal("cn.bing.com", session.Host);
        Assert.Equal("A1B2C3D4E5", session.Ig);
        Assert.Equal("translator.5028", session.Iid);
        Assert.Equal("1759226400000", session.Key);
        Assert.Equal("tok-EN_123", session.Token);
        Assert.Equal(now.AddMinutes(59), session.Expires);
        Assert.Null(Translator.ParseWebSession("<html></html>", "www.bing.com", now));
    }

    [Fact]
    public async Task AzureSendsKeyAndRegion()
    {
        var handler = new FakeHandler((_, _) => Json(Answer));
        var result = await new Translator(new HttpClient(handler)).TranslateAsync("Hello", "zh-Hans", TranslationEngine.Azure, " key123 ", "eastasia");
        Assert.Equal("Microsoft Translator", result.Provider);
        var request = handler.Requests.Single().Request;
        Assert.StartsWith(Translator.AzureTranslateUrl, request.RequestUri!.AbsoluteUri);
        Assert.Equal("key123", request.Headers.GetValues("Ocp-Apim-Subscription-Key").Single());
        Assert.Equal("eastasia", request.Headers.GetValues("Ocp-Apim-Subscription-Region").Single());
    }

    [Fact]
    public async Task ErrorsBecomeReadableMessages()
    {
        var translator = new Translator(new HttpClient(new FakeHandler((_, _) =>
            Json("""{"error":{"code":401000,"message":"The request is not authorized"}}""", HttpStatusCode.Unauthorized))));
        var e = await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("Hello", "zh-Hans", TranslationEngine.Azure, "bad"));
        Assert.Contains("Key 或区域不对", e.Message);

        e = await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("Hello", "zh-Hans", TranslationEngine.Azure));
        Assert.Contains("还没有填写", e.Message);
        e = await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync(new string('a', Translator.MaxLength + 1), "zh-Hans", TranslationEngine.Bing));
        Assert.Contains("太长", e.Message);

        var offline = new Translator(new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException("no network"))));
        e = await Assert.ThrowsAsync<TranslationException>(() => offline.TranslateAsync("Hello", "zh-Hans", TranslationEngine.Bing));
        Assert.Contains("网络", e.Message);
        Assert.Throws<TranslationException>(() => Translator.Parse("[]", "zh-Hans", "必应翻译"));
    }

    [Fact]
    public void JwtExpiryAndTargets()
    {
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_900_000_000), Translator.JwtExpiry(Jwt(1_900_000_000)));
        Assert.Null(Translator.JwtExpiry("garbage"));

        Assert.Equal("zh-Hans", Languages.TargetFor("Hello world", "auto"));
        Assert.Equal("en", Languages.TargetFor("你好世界", "auto"));
        Assert.Equal("ja", Languages.TargetFor("Hello world", "ja"));
        Assert.Equal("en", Languages.TargetFor("你好世界", "zh-Hant"));
        Assert.Equal("英语", Languages.Name("en"));
        Assert.Equal("xx", Languages.Name("xx"));
    }

    [Fact]
    public void Cards()
    {
        var result = TranslationCards.Result("Hello", new TranslationResult("你好", "en", "zh-Hans", "必应翻译"));
        Assert.Equal("你好", result.Body);
        Assert.Equal("你好", result.PrimaryText);
        Assert.Equal("英语 → 简体中文 · 必应翻译", result.Caption);
        Assert.Equal("Hello", result.Source);
        Assert.StartsWith("https://www.bing.com/translator?from=auto&to=zh-Hans&text=Hello", result.Links![0].Url);
        Assert.True(TranslationCards.Loading("Hello", "zh-Hans").Loading);
        Assert.Null(TranslationCards.Failed("Hello", "zh-Hans", "连不上").Replacement);
    }
}
