using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Pop.Core;

/// 插件不适用于当前输入的原因；匹配成功为 None，热路径不构造说明文字。
public enum PluginMatchFailure { None, Kind, Empty, TooShort, TooLong, InvalidPattern, Pattern, PatternTimeout }

/// 判断选中的内容能不能用某个插件
public static class PluginMatcher
{
    /// 正则匹配最多花这么久，写得不好的正则（回溯爆炸）不会卡住圆盘
    public static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

    private const int CacheLimit = 128;
    private static readonly ConcurrentDictionary<string, Regex?> Cache = new(StringComparer.Ordinal);

    public static bool Matches(PluginManifest manifest, ClassifiedContent content) => Matches(manifest.Match, content);

    /// 内容类型满足其一（没写类型表示随时可用），再满足长度和正则的限制。
    /// Windows 版识别不了的类型（图片、图片文件）永远匹配不上
    public static bool Matches(PluginMatch match, ClassifiedContent content) => Check(match, content) == PluginMatchFailure.None;

    public static PluginMatchFailure Check(PluginMatch match, ClassifiedContent content)
    {
        if (match.Kinds.Count > 0 && (PluginKinds.Flags(match.Kinds) & content.Kinds) == 0) return PluginMatchFailure.Kind;
        return CheckConstraints(match, content);
    }

    /// 放到 PopAction.Requires 上的内容类型：没写类型又没有别的限制时是 None（随时可用）；
    /// 有正则或长度限制、或者只写了 Windows 版识别不了的类型时用 Text，让 PopAction 去问 Accepts
    public static ContentKind RequiredKinds(PluginMatch match)
    {
        var flags = PluginKinds.Flags(match.Kinds);
        if (flags != ContentKind.None) return flags;
        return match.Kinds.Count == 0 && !HasConstraints(match) ? ContentKind.None : ContentKind.Text;
    }

    /// 能不能当正则用
    public static bool IsValidPattern(string pattern) => Compile(pattern) is not null;

    private static bool HasConstraints(PluginMatch match) =>
        match.MinLength is not null || match.MaxLength is not null || !string.IsNullOrEmpty(match.Pattern);

    private static PluginMatchFailure CheckConstraints(PluginMatch match, ClassifiedContent content)
    {
        if (!HasConstraints(match)) return PluginMatchFailure.None;
        // 选中的文字；选中的是文件路径时 Text 就是路径
        var subject = content.Text.Length > 0 ? content.Text : content.Path ?? "";
        if (subject.Length == 0) return PluginMatchFailure.Empty;
        if (match.MinLength is not null || match.MaxLength is not null)
        {
            var remaining = subject.AsSpan();
            var length = 0;
            while (!remaining.IsEmpty)
            {
                length++;
                if (length > match.MaxLength) return PluginMatchFailure.TooLong;
                if (match.MaxLength is null && length >= match.MinLength) break;
                remaining = remaining[StringInfo.GetNextTextElementLength(remaining)..];
            }
            if (length < match.MinLength) return PluginMatchFailure.TooShort;
            if (length > match.MaxLength) return PluginMatchFailure.TooLong;
        }
        if (!string.IsNullOrEmpty(match.Pattern))
        {
            if (Compile(match.Pattern) is not { } regex) return PluginMatchFailure.InvalidPattern;
            try
            {
                if (!regex.IsMatch(subject)) return PluginMatchFailure.Pattern;
            }
            catch (RegexMatchTimeoutException)
            {
                return PluginMatchFailure.PatternTimeout;
            }
        }
        return PluginMatchFailure.None;
    }

    private static Regex? Compile(string pattern)
    {
        if (Cache.TryGetValue(pattern, out var cached)) return cached;
        Regex? regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.CultureInvariant, PatternTimeout);
        }
        catch (ArgumentException)
        {
            regex = null;
        }
        if (Cache.Count >= CacheLimit) Cache.Clear();
        Cache[pattern] = regex;
        return regex;
    }
}
