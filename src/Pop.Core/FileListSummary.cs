namespace Pop.Core;

/// 文件历史只预览前三条路径；计数时直接扫描原文，不拆分和分配整组文件路径。
public sealed record FileListSummary(int Count, IReadOnlyList<string> FirstPaths)
{
    public static FileListSummary Parse(string text)
    {
        var paths = new List<string>(3);
        var remaining = text.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            var separator = remaining.IndexOf('\n');
            var path = separator < 0 ? remaining : remaining[..separator];
            if (!path.IsEmpty)
            {
                count++;
                if (paths.Count < 3) paths.Add(path.ToString());
            }
            if (separator < 0) break;
            remaining = remaining[(separator + 1)..];
        }
        return new FileListSummary(count, paths);
    }
}
