namespace Pop.Core;

/// 结果卡片只排版有限预览；完整内容留给复制、替换和分页阅读。
public static class ResultPreview
{
    public const int BodyCharacters = 4000;
    public const int SourceCharacters = 600;
    public const int LineCharacters = 2000;
    public const int LinesPerPage = 12;

    public static string Text(string text, int maximumLength) => text.Length <= maximumLength
        ? text : TextPreview.Truncate(text, maximumLength);

    public static bool SourceNeedsReader(string text) => text.Length > 48 || text.Count(c => c == '\n') >= 2;
    public static bool LineNeedsReader(string text) => text.Length > 400 || text.Count(c => c == '\n') >= 6;

    public static (int Start, int Count) Lines(int count, int page)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        var start = (int)Math.Min((long)page * LinesPerPage, count);
        return (start, Math.Min(LinesPerPage, count - start));
    }
}

public sealed record TextPage(string Text, int Start, int End, bool SplitOversizedElement);

/// 只为当前页分配文字，缓存翻过的页起点，不拆分代理对或遗漏字符。
public sealed class TextPages
{
    private readonly int limit;
    private readonly List<int> starts = [0];
    public string FullText { get; }
    public int Index { get; private set; }
    public TextPage Current { get; private set; }
    public bool HasPrevious => Index > 0;
    public bool HasNext => Current.End < FullText.Length;

    public TextPages(string text, int maximumLength = ResultPreview.BodyCharacters)
    {
        if (maximumLength < 2) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        FullText = text;
        limit = maximumLength;
        Current = Read(0);
    }

    public bool Next()
    {
        if (!HasNext) return false;
        if (starts.Count == Index + 1) starts.Add(Current.End);
        Current = Read(starts[++Index]);
        return true;
    }

    public bool Previous()
    {
        if (!HasPrevious) return false;
        Current = Read(starts[--Index]);
        return true;
    }

    private TextPage Read(int start)
    {
        var remaining = FullText.AsSpan(start);
        if (remaining.Length <= limit) return new(FullText.Substring(start), start, FullText.Length, false);
        var preview = TextPreview.Truncate(remaining, limit);
        var length = preview.Length - 1; // 分页不用预览末尾的省略号。
        if (length > 0)
        {
            // 有合适换行时按行收尾，便于阅读 JSON；仍不跳过换行或重复文字。
            var newline = preview.AsSpan(0, length).LastIndexOf('\n');
            if (newline >= limit * 3L / 4) length = newline + 1;
            return new(preview[..length], start, start + length, false);
        }

        // 单个组合字符组也可能超过一页。此时明确分段，但仍保证推进和代理对完整。
        length = limit;
        if (char.IsHighSurrogate(remaining[length - 1]) && char.IsLowSurrogate(remaining[length])) length--;
        if (remaining[length - 1] == '\r' && remaining[length] == '\n') length--;
        return new(remaining[..length].ToString(), start, start + length, true);
    }
}
