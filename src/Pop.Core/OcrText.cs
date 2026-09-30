using System.Text;

namespace Pop.Core;

/// 把文字识别返回的一行行单词拼回文字。
/// 系统识别中文、日文时每个字都算一个「词」，词之间不能加空格；英文单词之间照常加空格
public static class OcrText
{
    public static string Join(IEnumerable<IEnumerable<string>> lines) =>
        string.Join("\n", lines.Select(JoinLine).Where(l => l.Length > 0));

    public static string JoinLine(IEnumerable<string> words)
    {
        var sb = new StringBuilder();
        foreach (var raw in words)
        {
            var word = raw.Trim();
            if (word.Length == 0) continue;
            if (sb.Length > 0 && NeedsSpace(sb[^1], word[0])) sb.Append(' ');
            sb.Append(word);
        }
        return sb.ToString();
    }

    /// 两边都是西文字符才加空格；挨着中日韩文字或者全角标点的地方不加
    private static bool NeedsSpace(char before, char after) => !IsWide(before) && !IsWide(after);

    private static bool IsWide(char c) =>
        c is >= '⺀' and <= '鿿'   // 中日韩部首、标点、假名、汉字
          or >= '가' and <= '힯'   // 谚文
          or >= '豈' and <= '﫿'   // 兼容汉字
          or >= '＀' and <= '￯'   // 全角字符
          || char.IsSurrogate(c);          // 扩展区汉字
}
