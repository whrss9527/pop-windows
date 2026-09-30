namespace Pop.Core;

public static partial class Actions
{
    public static readonly PopAction CaseStyles = new("case", "大小写写法", "", "dxx daxiaoxie camel snake kebab pascal case",
        ContentKind.Text,
        c => Lines("大小写写法", CaseConverter.Conversions(c.Text)),
        c => CaseConverter.Conversions(c.Text).Count > 0);

    public static readonly PopAction Codec = new("codec", "编码转换", "", "bm bianma base64 url unicode html encode decode",
        ContentKind.Text,
        c => Lines("编码转换", TextCodec.Conversions(c.Text)));

    public static readonly PopAction Hash = new("hash", "哈希", "", "hx haxi hash md5 sha1 sha256 sha512",
        ContentKind.Text,
        c => Lines("哈希", Digests.Rows(c.Text)));

    public static readonly PopAction Random = new("random", "随机生成", "", "sj suiji uuid mm mima password random",
        ContentKind.None,
        _ => Lines("随机生成", RandomGenerator.Rows()));

    public static readonly PopAction Extract = new("extract", "提取信息", "", "tq tiqu lj lianjie yx youxiang dh dianhua ip extract",
        ContentKind.Text,
        c => ActionResult.ShowCard(InfoExtractor.Card(InfoExtractor.Extract(c.Text))),
        c => InfoExtractor.IsWorthExtracting(c.Text));

    public static readonly PopAction Cleanup = new("cleanup", "文字整理", "", "wzzl wenzi zhengli hh huanhang kg kongge qj banjiao cleanup",
        ContentKind.Text,
        c => Lines("文字整理", TextCleanup.Conversions(c.Text)),
        c => TextCleanup.IsApplicable(c.Text));

    public static readonly PopAction LineTool = new("lines", "按行处理", "", "ahcl anhang chuli yh yinhao dh douhao px paixu lines",
        ContentKind.Text,
        c => Lines("按行处理", LineTools.Conversions(c.Text)),
        c => LineTools.IsApplicable(c.Text));

    public static readonly PopAction Json = new("json", "JSON 格式化", "", "json gsh geshihua ys yasuo format minify",
        ContentKind.Json,
        c => OrderedJson.Format(c.Text) is { } pretty
            ? ActionResult.ShowCard(new CardContent("JSON 格式化", OrderedJson.Rows(c.Text), Replacement: pretty))
            : null);

    /// 文字工具，接在列表后面
    private static IEnumerable<PopAction> TextTools() => [Json, CaseStyles, Codec, Cleanup, LineTool, Extract, Hash, Random];

    private static ActionResult? Lines(string title, IReadOnlyList<ResultLine> lines) =>
        lines.Count == 0 ? null : ActionResult.ShowCard(new CardContent(title, lines));
}
