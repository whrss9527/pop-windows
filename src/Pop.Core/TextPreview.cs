using System.Globalization;
using System.Text;

namespace Pop.Core;

/// 列表和提示中的单行预览：只保存需要显示的前缀，避免为大段剪贴板内容拆分、拼接整个字符串。
public static class TextPreview
{
    public static string SingleLine(string text, int maximumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);
        var capacity = (int)Math.Min(text.Length, (long)maximumLength + 1);
        var prefix = new StringBuilder(capacity);
        var length = 0;
        var contentLength = 0;
        var delimiter = false;
        for (var index = 0; index < text.Length; index++)
        {
            var original = text[index];
            var separator = original is '\r' or '\n' or '\t';
            if (separator && delimiter) continue;
            delimiter = separator;
            var c = separator ? ' ' : original;
            var whitespace = char.IsWhiteSpace(c);
            if (length == 0 && whitespace) continue;
            if (length < capacity) { prefix.Append(c); length++; }
            if (whitespace) continue;
            contentLength = length;
            if (contentLength > maximumLength)
            {
                // 多读一个完整码点，文字元素判定才能认出截断处的 emoji 连接序列。
                if (prefix.Length > 0 && char.IsHighSurrogate(prefix[^1])
                    && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                    prefix.Append(text[index + 1]);
                break;
            }
        }
        if (contentLength <= maximumLength) return prefix.ToString(0, contentLength);
        var value = prefix.ToString();
        // 截断处退回完整文字元素的边界，保留代理对、组合字符和 emoji 序列。
        var boundaries = StringInfo.ParseCombiningCharacters(value);
        var boundary = Array.BinarySearch(boundaries, maximumLength);
        var end = boundary >= 0 ? maximumLength : boundaries[~boundary - 1];
        return value[..end] + "…";
    }
}
