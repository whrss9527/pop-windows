using System.Globalization;
using System.Text;

namespace Pop.Core;

/// 浮窗搜索接收粘贴的 Unicode 文字，限制长度；退格按可见字符删除。
public static class SearchQuery
{
    public const int MaximumElements = 200;
    public static string Append(string current, string value)
    {
        var clean = new StringBuilder();
        foreach (var rune in value.EnumerateRunes().Take(4096)) clean.Append(Rune.IsControl(rune) ? " " : rune.ToString());
        var text = current + clean;
        var info = new StringInfo(text);
        return info.LengthInTextElements > MaximumElements ? info.SubstringByTextElements(0, MaximumElements) : text;
    }
    public static string Backspace(string value)
    {
        var starts = StringInfo.ParseCombiningCharacters(value);
        return starts.Length == 0 ? "" : value[..starts[^1]];
    }
}
