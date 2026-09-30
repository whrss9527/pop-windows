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

    [Fact]
    public async Task BingFetchesTokenAndTranslates()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var handler = new FakeHandler((request, _) => request.RequestUri!.AbsoluteUri.StartsWith(Translator.BingAuthUrl, StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Jwt(now.ToUnixTimeSeconds() + 600)) }
            : Json(Answer));
        var translator = new Translator(new HttpClient(handler), () => now);

        var result = await translator.TranslateAsync("  Hello, world  ", "zh-Hans", TranslationEngine.Bing);
        Assert.Equal("你好，世界", result.Text);
        Assert.Equal("en", result.From);
        Assert.Equal("zh-Hans", result.To);
        Assert.Equal("必应翻译", result.Provider);

        var (post, body) = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.StartsWith(Translator.BingTranslateUrl + "?api-version=3.0&to=zh-Hans", post.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", post.Headers.Authorization?.Scheme);
        Assert.Equal("""[{"Text":"Hello, world"}]""", body);

        // 令牌还没过期：第二次不再要令牌
        await translator.TranslateAsync("again", "zh-Hans", TranslationEngine.Bing);
        Assert.Single(handler.Requests, r => r.Request.RequestUri!.AbsoluteUri.StartsWith(Translator.BingAuthUrl, StringComparison.Ordinal));
    }

    [Fact]
    public async Task BingRefreshesTheTokenOnceWhenRejected()
    {
        var translateCalls = 0;
        var handler = new FakeHandler((request, _) =>
        {
            if (request.RequestUri!.AbsoluteUri.StartsWith(Translator.BingAuthUrl, StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-a-jwt") };
            return ++translateCalls == 1 ? Json("{}", HttpStatusCode.Unauthorized) : Json(Answer);
        });
        var result = await new Translator(new HttpClient(handler)).TranslateAsync("Hello", "zh-Hans", TranslationEngine.Bing);
        Assert.Equal("你好，世界", result.Text);
        Assert.Equal(2, handler.Requests.Count(r => r.Request.RequestUri!.AbsoluteUri.StartsWith(Translator.BingAuthUrl, StringComparison.Ordinal)));
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
