using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace Pop.Core;

/// 制表符、CSV、Markdown 表格互相转换，第一行作为表头。
public static class TableConverter
{
    public enum Format { Markdown, Csv, Tsv, Json }
    public sealed record Table(IReadOnlyList<IReadOnlyList<string>> Rows, Format Source);
    public const int MaxLength = 500_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static Table? Parse(string text)
    {
        if (text.Length > MaxLength) return null;
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');
        if (!normalized.Contains('\n')) return null;
        var lines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToArray();
        if (lines.Length >= 3 && lines.All(l => l.Contains('|')))
        {
            var separator = MarkdownCells(lines[1]);
            if (separator.Count >= 2 && separator.All(c => c.Trim(':') is { Length: > 0 } d && d.All(ch => ch == '-')))
            {
                var table = Consistent(lines.Where((_, i) => i != 1).Select(MarkdownCells).ToList(), Format.Markdown);
                if (table is not null) return table;
            }
        }
        if (normalized.Contains('\t') && ParseDelimited(normalized, '\t') is { } tsv
            && Consistent(tsv, Format.Tsv) is { } tab) return tab;
        return normalized.Contains(',') && ParseDelimited(normalized, ',') is { } csv ? Consistent(csv, Format.Csv) : null;
    }

    public static IReadOnlyList<ResultLine> Conversions(Table table) =>
        Enum.GetValues<Format>().Where(f => f != table.Source)
            .Select(f => new ResultLine(Title(f), Render(table.Rows, f))).ToArray();

    public static string Render(IReadOnlyList<IReadOnlyList<string>> rows, Format format)
    {
        if (rows.Count == 0) return format == Format.Json ? "[]" : "";
        switch (format)
        {
            case Format.Markdown:
                string Escape(string cell) => cell.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\n", " ");
                string Line(IReadOnlyList<string> row) => "| " + string.Join(" | ", row.Select(Escape)) + " |";
                return string.Join("\n", new[] { Line(rows[0]), "|" + string.Join("|", rows[0].Select(_ => " --- ")) + "|" }
                    .Concat(rows.Skip(1).Select(Line)));
            case Format.Csv:
                string CsvField(string cell) => cell.IndexOfAny([',', '"', '\n']) >= 0 || cell != cell.Trim()
                    ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
                return string.Join("\n", rows.Select(row => string.Join(",", row.Select(CsvField))));
            case Format.Tsv:
                return string.Join("\n", rows.Select(row => string.Join("\t", row.Select(c => c.Replace('\t', ' ').Replace('\n', ' ')))));
            default:
                string JsonString(string value) => JsonSerializer.Serialize(value, JsonOptions);
                return "[\n" + string.Join(",\n", rows.Skip(1).Select(row => "  {" + string.Join(", ", rows[0]
                    .Select((key, i) => JsonString(key) + ": " + JsonString(i < row.Count ? row[i] : ""))) + "}")) + "\n]";
        }
    }

    private static string Title(Format format) => format switch
    { Format.Markdown => "Markdown", Format.Csv => "CSV", Format.Tsv => "制表符分隔", _ => "JSON" };

    private static Table? Consistent(List<IReadOnlyList<string>> rows, Format source) =>
        rows.Count >= 2 && rows[0].Count >= 2 && rows.All(r => r.Count == rows[0].Count) ? new(rows, source) : null;

    private static IReadOnlyList<string> MarkdownCells(string line)
    {
        var body = line.StartsWith('|') ? line[1..] : line;
        if (body.EndsWith('|'))
        {
            var slashes = 0;
            for (var i = body.Length - 2; i >= 0 && body[i] == '\\'; i--) slashes++;
            if (slashes % 2 == 0) body = body[..^1];
        }
        var cells = new List<string>();
        var field = new StringBuilder();
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '\\' && i + 1 < body.Length && body[i + 1] is '|' or '\\') field.Append(body[++i]);
            else if (c == '|') { cells.Add(field.ToString().Trim()); field.Clear(); }
            else field.Append(c);
        }
        cells.Add(field.ToString().Trim());
        return cells;
    }

    /// 引号内允许分隔符、换行和双引号；不接受未闭合或位置错误的引号。
    private static List<IReadOnlyList<string>>? ParseDelimited(string text, char separator)
    {
        List<IReadOnlyList<string>> rows = [];
        List<string> row = [];
        var field = new StringBuilder();
        var quoted = false;
        var closed = false;
        var wasQuoted = false;
        void FinishField() { row.Add(wasQuoted ? field.ToString() : field.ToString().Trim()); field.Clear(); closed = false; wasQuoted = false; }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else field.Append(c);
            }
            else if (c == separator) FinishField();
            else if (c == '\n') { FinishField(); rows.Add(row); row = []; }
            else if (closed) { if (!char.IsWhiteSpace(c)) return null; }
            else if (c == '"')
            {
                if (field.Length != 0) return null;
                quoted = true; wasQuoted = true;
            }
            else field.Append(c);
        }
        if (quoted) return null;
        FinishField(); rows.Add(row);
        return rows;
    }
}
