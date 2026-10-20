using System.Globalization;
using System.Text.RegularExpressions;

namespace Pop.Core;

/// 数字列表：多行取每行最后一个数字，单行只允许数字和分隔符。
public static class NumberStats
{
    private static readonly Regex Number = new(@"(?<![\d.])[-+]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?(?!\d)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private const string Separators = " \t,，;；、|+¥$€£%";
    public const int MaxLength = 100_000;

    public sealed record Summary(IReadOnlyList<double> Values, int Decimals)
    {
        public int Count => Values.Count;
        public double Sum => Values.Sum();
        public double Average => Sum / Count;
        public double Median
        {
            get
            {
                var sorted = Values.Order().ToArray();
                var middle = Count / 2;
                // 分开除以二，避免两个大数相加溢出。
                return Count % 2 == 0 ? sorted[middle - 1] / 2 + sorted[middle] / 2 : sorted[middle];
            }
        }
        public IReadOnlyList<ResultLine> Rows =>
        [
            new("合计", Format(Sum, Decimals)),
            new("平均", Format(Average, Math.Min(Decimals + 2, 6))),
            new("中位数", Format(Median, Math.Min(Decimals + 2, 6))),
            new("最大", Format(Values.Max(), Decimals)),
            new("最小", Format(Values.Min(), Decimals)),
            new("个数", Count.ToString(CultureInfo.InvariantCulture)),
        ];
    }

    public static Summary? Parse(string text)
    {
        if (text.Length > MaxLength) return null;
        try
        {
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return null;
            List<string> tokens = [];
            if (lines.Length >= 2)
            {
                foreach (var line in lines)
                {
                    var matches = Number.Matches(line);
                    if (matches.Count > 0) tokens.Add(matches[^1].Value);
                }
                if (tokens.Count < lines.Length * 0.8) return null;
            }
            else
            {
                var matches = Number.Matches(lines[0]);
                tokens.AddRange(matches.Select(m => m.Value));
                if (Number.Replace(lines[0], "").Any(c => !Separators.Contains(c))) return null;
            }
            if (tokens.Count < 2) return null;
            var values = new List<double>();
            foreach (var token in tokens)
            {
                if (!double.TryParse(token.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    || !double.IsFinite(value)) return null;
                values.Add(value);
            }
            if (!double.IsFinite(values.Sum())) return null;
            var decimals = tokens.Max(t => t.Contains('.') ? t.Length - t.IndexOf('.') - 1 : 0);
            return new Summary(values.AsReadOnly(), Math.Min(decimals, 6));
        }
        catch (RegexMatchTimeoutException) { return null; }
    }

    private static string Format(double value, int decimals) =>
        value.ToString(decimals == 0 ? "0" : "0." + new string('#', decimals), CultureInfo.InvariantCulture);
}
