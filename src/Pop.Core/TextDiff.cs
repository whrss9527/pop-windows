using System.Text;

namespace Pop.Core;

public enum DiffKind { Unchanged, Removed, Added }
public sealed record DiffLine(DiffKind Kind, string Text);
public sealed record TextDiffResult(IReadOnlyList<DiffLine> Lines)
{
    public int Added => Lines.Count(l => l.Kind == DiffKind.Added);
    public int Removed => Lines.Count(l => l.Kind == DiffKind.Removed);
    public string Unified()
    {
        var result = new StringBuilder("--- 原文\n+++ 对照\n");
        foreach (var line in Lines) result.Append(line.Kind == DiffKind.Added ? "+ " : line.Kind == DiffKind.Removed ? "- " : "  ").Append(line.Text).Append('\n');
        return result.ToString();
    }
}

/// 逐行比较。先剥离共同首尾，限制差异区域的动态规划空间，避免大段无关文字耗尽内存。
public static class TextDiff
{
    public const int MaximumCharacters = 2_000_000;
    public static TextDiffResult Compare(string before, string after, bool ignoreOuterWhitespace = false)
    {
        if (before.Length > MaximumCharacters || after.Length > MaximumCharacters) throw new ArgumentException("每段文字最多支持 200 万个字符");
        static string[] Lines(string value) => value.Length == 0 ? [] : value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var a = Lines(before); var b = Lines(after);
        if (a.Length > 20_000 || b.Length > 20_000) throw new ArgumentException("每段文字最多支持 20000 行");
        var comparedA = ignoreOuterWhitespace ? a.Select(line => line.Trim()).ToArray() : a;
        var comparedB = ignoreOuterWhitespace ? b.Select(line => line.Trim()).ToArray() : b;
        bool Same(int x, int y) => string.Equals(comparedA[x], comparedB[y], StringComparison.Ordinal);
        var prefix = 0; var suffix = 0;
        while (prefix < a.Length && prefix < b.Length && Same(prefix, prefix)) prefix++;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && Same(a.Length - suffix - 1, b.Length - suffix - 1)) suffix++;
        var ac = a.Length - prefix - suffix; var bc = b.Length - prefix - suffix;
        var rows = new List<DiffLine>();
        for (var i = 0; i < prefix; i++) rows.Add(new(DiffKind.Unchanged, a[i]));
        if (ac == 0 || bc == 0)
        {
            for (var i = 0; i < ac; i++) rows.Add(new(DiffKind.Removed, a[prefix + i]));
            for (var j = 0; j < bc; j++) rows.Add(new(DiffKind.Added, b[prefix + j]));
        }
        else
        {
            if ((long)(ac + 1) * (bc + 1) > 4_000_000) throw new ArgumentException("两段文字差异过多，请缩小比较范围后重试");
            var lcs = new ushort[ac + 1, bc + 1];
            for (var i = ac - 1; i >= 0; i--)
                for (var j = bc - 1; j >= 0; j--)
                    lcs[i, j] = Same(prefix + i, prefix + j) ? (ushort)(1 + lcs[i + 1, j + 1]) : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            var x = 0; var y = 0;
            while (x < ac || y < bc)
            {
                if (x < ac && y < bc && Same(prefix + x, prefix + y)) { rows.Add(new(DiffKind.Unchanged, a[prefix + x])); x++; y++; }
                else if (x < ac && (y == bc || lcs[x + 1, y] >= lcs[x, y + 1])) rows.Add(new(DiffKind.Removed, a[prefix + x++]));
                else rows.Add(new(DiffKind.Added, b[prefix + y++]));
            }
        }
        for (var i = a.Length - suffix; i < a.Length; i++) rows.Add(new(DiffKind.Unchanged, a[i]));
        return new(rows);
    }
}
