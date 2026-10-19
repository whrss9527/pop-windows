namespace Pop.Core;

public static partial class Actions
{
    public static readonly PopAction Statistics = new("numberStats", "数字统计", "Calculator24", "sztj shuzi tongji sum average median statistics",
        ContentKind.Text,
        c => NumberStats.Parse(c.Text) is { } stats
            ? ActionResult.ShowCard(new CardContent("数字统计", stats.Rows, Caption: $"共 {stats.Count} 个数")) : null,
        c => NumberStats.Parse(c.Text) is not null,
        Category: Convert, Summary: "一列或一串数字的合计、平均、中位数、最大、最小和个数");

    public static readonly PopAction TableConvert = new("tableConvert", "表格转换", "Table24", "bgzh biaoge zhuanhuan table csv tsv markdown json",
        ContentKind.Text,
        c => TableConverter.Parse(c.Text) is { } table
            ? ActionResult.ShowCard(new CardContent("表格转换", TableConverter.Conversions(table), Monospace: true,
                Caption: $"{table.Rows.Count} 行 × {table.Rows[0].Count} 列（第一行当作表头）")) : null,
        c => TableConverter.Parse(c.Text) is not null,
        Category: Convert, Summary: "CSV、制表符和 Markdown 表格互相转换，也能转成 JSON");

    public static readonly PopAction CaseStyles = new("case", "大小写写法", "TextCaseTitle24", "dxx daxiaoxie camel snake kebab pascal case",
        ContentKind.Text,
        c => Lines("大小写写法", CaseConverter.Conversions(c.Text)),
        c => CaseConverter.Conversions(c.Text).Count > 0,
        Category: TextCategory, Summary: "驼峰、帕斯卡、下划线、短横线、常量写法");

    public static readonly PopAction Codec = new("codec", "编码转换", "Code24", "bm bianma base64 url unicode html encode decode",
        ContentKind.Text,
        c => Lines("编码转换", TextCodec.Conversions(c.Text), monospace: true),
        Category: Developer, Summary: "Base64、URL、Unicode、HTML 实体的编码和解码");

    public static readonly PopAction Hash = new("hash", "哈希", "Fingerprint24", "hx haxi hash md5 sha1 sha256 sha512",
        ContentKind.Text,
        c => Lines("哈希", Digests.Rows(c.Text), monospace: true),
        Category: Developer, Summary: "MD5、SHA-1、SHA-256、SHA-512");

    public static readonly PopAction Random = new("random", "随机生成", "ArrowShuffle24", "sj suiji uuid mm mima password random",
        ContentKind.None,
        _ => Lines("随机生成", RandomGenerator.Rows(), monospace: true),
        Category: Developer, Summary: "UUID、密码、随机数字");

    public static readonly PopAction Extract = new("extract", "提取信息", "TextBulletListSquareSearch20", "tq tiqu lj lianjie yx youxiang dh dianhua ip extract",
        ContentKind.Text,
        c => ActionResult.ShowCard(InfoExtractor.Card(InfoExtractor.Extract(c.Text))),
        c => InfoExtractor.IsWorthExtracting(c.Text),
        Category: TextCategory, Summary: "找出一段文字里的链接、邮箱、电话号码和 IP 地址");

    public static readonly PopAction Cleanup = new("cleanup", "文字整理", "TextClearFormatting24", "wzzl wenzi zhengli hh huanhang kg kongge qj banjiao cleanup",
        ContentKind.Text,
        c => Lines("文字整理", TextCleanup.Conversions(c.Text)),
        c => TextCleanup.IsApplicable(c.Text),
        Category: TextCategory, Summary: "合并换行、去空行和多余空格、中英文之间加空格、全角转半角");

    public static readonly PopAction LineTool = new("lines", "按行处理", "TextBulletListLtr24", "ahcl anhang chuli yh yinhao dh douhao px paixu lines",
        ContentKind.Text,
        c => Lines("按行处理", LineTools.Conversions(c.Text)),
        c => LineTools.IsApplicable(c.Text),
        Category: TextCategory, Summary: "一列值加引号和逗号、转 JSON 数组、加减序号、倒序、打乱");

    public static readonly PopAction Json = new("json", "JSON 格式化", "Braces24", "json gsh geshihua ys yasuo format minify",
        ContentKind.Json,
        c => OrderedJson.Format(c.Text) is { } pretty
            ? ActionResult.ShowCard(new CardContent("JSON 格式化", OrderedJson.Rows(c.Text), Replacement: pretty, Monospace: true))
            : null,
        Category: Developer, Summary: "格式化或压缩 JSON，保持键的顺序");

    /// 文字工具，接在列表后面
    private static IEnumerable<PopAction> TextTools() => [Statistics, TableConvert, Json, CaseStyles, Codec, Cleanup, LineTool, Extract, Hash, Random];

    private static ActionResult? Lines(string title, IReadOnlyList<ResultLine> lines, bool monospace = false) =>
        lines.Count == 0 ? null : ActionResult.ShowCard(new CardContent(title, lines, Monospace: monospace));
}
