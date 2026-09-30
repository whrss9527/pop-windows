using System.Text.RegularExpressions;

namespace Pop.Core;

/// 插件在圆盘和列表里的图标：Fluent System Icons 的图标名（WPF-UI 的 SymbolRegular 枚举里的名称，比如 Search24）。
/// Pop.Core 不引用 WPF-UI，这里只给出名称，界面按名称（不分大小写）找图标，找不到时用 Default
public static partial class PluginGlyphs
{
    /// 拼图块
    public const string Default = "PuzzlePiece24";

    /// macOS 版插件的 SF Symbol 图标名 → 相近的图标。查的时候会去掉 .fill、.circle 这类后缀
    public static readonly IReadOnlyDictionary<string, string> Symbols = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["puzzlepiece"] = "PuzzlePiece24", ["puzzlepiece.extension"] = "PuzzlePiece24", ["magnifyingglass"] = "Search24",
        ["globe"] = "Globe24", ["network"] = "Globe24", ["book"] = "Book24", ["book.closed"] = "Book24",
        ["character.book.closed"] = "BookLetter24", ["books.vertical"] = "Library24", ["map"] = "Map24", ["mappin"] = "Location24",
        ["mappin.and.ellipse"] = "Location24", ["location"] = "Location24", ["terminal"] = "WindowConsole20",
        ["apple.terminal"] = "WindowConsole20", ["arrow.up.arrow.down"] = "ArrowSort24", ["arrow.left.arrow.right"] = "ArrowSwap24",
        ["list.bullet"] = "TextBulletListLtr24", ["list.number"] = "TextNumberListLtr24", ["text.quote"] = "TextQuote24",
        ["textformat"] = "TextFont24", ["character"] = "TextT24", ["character.bubble"] = "Translate24", ["translate"] = "Translate24",
        ["arrowshape.turn.up.left"] = "ArrowReply24", ["square.stack.3d.up"] = "Stack24", ["shippingbox"] = "Box24",
        ["chevron.left.forwardslash.chevron.right"] = "Code24", ["curlybraces"] = "Braces24", ["link"] = "Link24",
        ["envelope"] = "Mail24", ["doc.on.doc"] = "Copy24", ["doc.on.clipboard"] = "ClipboardPaste24", ["clipboard"] = "Clipboard24",
        ["doc"] = "Document24", ["doc.text"] = "DocumentText24", ["note.text"] = "Note24", ["folder"] = "Folder24",
        ["calendar"] = "Calendar24", ["calendar.badge.clock"] = "CalendarClock24", ["clock"] = "Clock24", ["timer"] = "Timer24",
        ["paintpalette"] = "Color24", ["eyedropper"] = "Eyedropper24", ["photo"] = "Image24", ["photo.on.rectangle"] = "ImageMultiple24",
        ["camera"] = "Camera24", ["qrcode"] = "QrCode24", ["star"] = "Star24", ["heart"] = "Heart24", ["tag"] = "Tag24",
        ["flag"] = "Flag24", ["bookmark"] = "Bookmark24", ["gear"] = "Settings24", ["gearshape"] = "Settings24",
        ["sparkles"] = "Sparkle24", ["wand.and.stars"] = "Wand24", ["brain"] = "BrainCircuit24", ["lightbulb"] = "Lightbulb24",
        ["bubble.left"] = "Chat24", ["bubble.left.and.bubble.right"] = "ChatMultiple24", ["function"] = "Calculator24",
        ["x.squareroot"] = "Calculator24", ["plus.forwardslash.minus"] = "Calculator24", ["number"] = "NumberSymbol24",
        ["square.and.arrow.up"] = "Share24", ["paperplane"] = "Send24", ["trash"] = "Delete24", ["lock"] = "LockClosed24",
        ["lock.open"] = "LockOpen24", ["key"] = "Key24", ["house"] = "Home24", ["play"] = "Play24", ["music.note"] = "MusicNote224",
        ["film"] = "Video24", ["video"] = "Video24", ["pencil"] = "Edit24", ["square.and.pencil"] = "Compose24",
        ["arrow.clockwise"] = "ArrowClockwise24", ["arrow.triangle.2.circlepath"] = "ArrowSync24", ["info"] = "Info24",
        ["exclamationmark.triangle"] = "Warning24", ["questionmark"] = "Question24", ["shuffle"] = "ArrowShuffle24",
        ["scissors"] = "Cut24", ["arrow.down"] = "ArrowDownload24", ["arrow.up"] = "ArrowUpload24", ["cloud"] = "Cloud24",
        ["person"] = "Person24", ["person.2"] = "People24", ["phone"] = "Phone24", ["mic"] = "Mic24", ["cart"] = "Cart24",
        ["paperclip"] = "Attach24", ["pin"] = "Pin24", ["highlighter"] = "Highlight24", ["keyboard"] = "Keyboard24",
        ["checkmark"] = "Checkmark24", ["xmark"] = "Dismiss24", ["plus"] = "Add24", ["minus"] = "Subtract24", ["eye"] = "Eye24",
        ["bell"] = "Alert24", ["sun.max"] = "WeatherSunny24", ["moon"] = "WeatherMoon24", ["ruler"] = "Ruler24",
        ["tablecells"] = "Table24", ["wrench"] = "Wrench24", ["hammer"] = "Wrench24", ["archivebox"] = "Archive24",
        ["desktopcomputer"] = "Desktop24", ["printer"] = "Print24", ["face.smiling"] = "Emoji24", ["doc.text.viewfinder"] = "ScanText24",
        ["viewfinder"] = "Scan24", ["textformat.abc"] = "TextCaseTitle24", ["newspaper"] = "News24", ["shield"] = "Shield24",
    };

    private static readonly string[] SymbolSuffixes = [".fill", ".circle", ".square", ".rectangle", ".badge.plus"];

    public static string Resolve(PluginManifest manifest) => Resolve(manifest.Glyph, manifest.Symbol);

    /// glyph 是 Windows 版的图标名，不写尺寸时用 24（Search 就是 Search24）；
    /// 没写或者写的不是图标名时，按 macOS 的 symbol 找一个相近的，再找不到就用拼图块
    public static string Resolve(string? glyph, string? symbol) => FromGlyph(glyph) ?? FromSymbol(symbol) ?? Default;

    private static string? FromGlyph(string? glyph)
    {
        var name = glyph?.Trim() ?? "";
        if (!IconName().IsMatch(name)) return null;
        return char.IsAsciiDigit(name[^1]) ? name : name + "24";
    }

    private static string? FromSymbol(string? symbol)
    {
        var name = symbol?.Trim() ?? "";
        while (name.Length > 0)
        {
            if (Symbols.TryGetValue(name, out var mapped)) return mapped;
            var suffix = SymbolSuffixes.FirstOrDefault(s => name.EndsWith(s, StringComparison.Ordinal));
            if (suffix is null) break;
            name = name[..^suffix.Length];
        }
        return null;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$")]
    private static partial Regex IconName();
}
