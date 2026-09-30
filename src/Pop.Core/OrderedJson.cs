using System.Globalization;
using System.Text;

namespace Pop.Core;

/// 保留键顺序的结构化数据
public abstract record DataValue
{
    private DataValue() { }

    /// 对象：键值对按原来的顺序保存
    public sealed record Object(IReadOnlyList<Pair> Pairs) : DataValue
    {
        public bool Equals(Object? other) => other != null && Pairs.SequenceEqual(other.Pairs);
        public override int GetHashCode() => Pairs.Count;
    }

    public sealed record Array(IReadOnlyList<DataValue> Items) : DataValue
    {
        public bool Equals(Array? other) => other != null && Items.SequenceEqual(other.Items);
        public override int GetHashCode() => Items.Count;
    }

    public sealed record String(string Value) : DataValue;

    /// 数字按原来的写法保存，不丢精度
    public sealed record Number(string Text) : DataValue;

    public sealed record Bool(bool Value) : DataValue;

    public sealed record Null : DataValue
    {
        public static readonly Null Instance = new();
    }

    public sealed record Pair(string Key, DataValue Value);
}

/// 按原来的顺序读写 JSON，键的顺序和数字的写法都不变。
public static class OrderedJson
{
    /// 不是合法的 JSON 时返回 null
    public static DataValue? Parse(string text) => new Parser(text).Document();

    /// 两个空格缩进的 JSON；不是合法的 JSON 时返回 null
    public static string? Format(string text) => Parse(text) is { } value ? Format(value) : null;

    /// 压成一行的 JSON；不是合法的 JSON 时返回 null
    public static string? Compact(string text) => Parse(text) is { } value ? Compact(value) : null;

    /// 结果卡片上的两行：格式化和压缩版。不是合法的 JSON 时没有行。
    public static IReadOnlyList<ResultLine> Rows(string text)
    {
        if (Parse(text) is not { } value) return [];
        var pretty = Format(value);
        var compact = Compact(value);
        var rows = new List<ResultLine> { new("JSON 格式化", pretty) };
        if (compact != pretty) rows.Add(new ResultLine("压缩版", compact));
        return rows;
    }

    /// 两个空格缩进的 JSON
    public static string Format(DataValue value)
    {
        var output = new StringBuilder();
        Write(value, 0, output);
        return output.ToString();
    }

    /// 压成一行的 JSON
    public static string Compact(DataValue value)
    {
        var output = new StringBuilder();
        WriteCompact(value, output);
        return output.ToString();
    }

    private static void WriteCompact(DataValue value, StringBuilder output)
    {
        switch (value)
        {
            case DataValue.Object obj:
                output.Append('{');
                for (var i = 0; i < obj.Pairs.Count; i++)
                {
                    if (i > 0) output.Append(',');
                    output.Append(Quoted(obj.Pairs[i].Key)).Append(':');
                    WriteCompact(obj.Pairs[i].Value, output);
                }
                output.Append('}');
                break;
            case DataValue.Array array:
                output.Append('[');
                for (var i = 0; i < array.Items.Count; i++)
                {
                    if (i > 0) output.Append(',');
                    WriteCompact(array.Items[i], output);
                }
                output.Append(']');
                break;
            default:
                WriteScalar(value, output);
                break;
        }
    }

    private static void Write(DataValue value, int indent, StringBuilder output)
    {
        var pad = new string(' ', indent + 2);
        switch (value)
        {
            case DataValue.Object obj:
                if (obj.Pairs.Count == 0)
                {
                    output.Append("{}");
                    return;
                }
                output.Append("{\n");
                for (var i = 0; i < obj.Pairs.Count; i++)
                {
                    output.Append(pad).Append(Quoted(obj.Pairs[i].Key)).Append(": ");
                    Write(obj.Pairs[i].Value, indent + 2, output);
                    output.Append(i < obj.Pairs.Count - 1 ? ",\n" : "\n");
                }
                output.Append(' ', indent).Append('}');
                break;
            case DataValue.Array array:
                if (array.Items.Count == 0)
                {
                    output.Append("[]");
                    return;
                }
                output.Append("[\n");
                for (var i = 0; i < array.Items.Count; i++)
                {
                    output.Append(pad);
                    Write(array.Items[i], indent + 2, output);
                    output.Append(i < array.Items.Count - 1 ? ",\n" : "\n");
                }
                output.Append(' ', indent).Append(']');
                break;
            default:
                WriteScalar(value, output);
                break;
        }
    }

    private static void WriteScalar(DataValue value, StringBuilder output)
    {
        switch (value)
        {
            case DataValue.String s: output.Append(Quoted(s.Value)); break;
            case DataValue.Number n: output.Append(n.Text); break;
            case DataValue.Bool b: output.Append(b.Value ? "true" : "false"); break;
            default: output.Append("null"); break;
        }
    }

    /// JSON 的字符串写法：控制字符和 DEL 写成小写的 \uxxxx，其余字符原样保留
    public static string Quoted(string text)
    {
        var output = new StringBuilder(text.Length + 2);
        output.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': output.Append("\\\""); break;
                case '\\': output.Append("\\\\"); break;
                case '\n': output.Append("\\n"); break;
                case '\r': output.Append("\\r"); break;
                case '\t': output.Append("\\t"); break;
                default:
                    if (c < 0x20 || c == 0x7F) output.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else output.Append(c);
                    break;
            }
        }
        return output.Append('"').ToString();
    }

    private sealed class Parser(string text)
    {
        private const int MaxDepth = 512;
        private int index;
        private int depth;

        public DataValue? Document()
        {
            var root = Value();
            if (root == null) return null;
            SkipWhitespace();
            return index == text.Length ? root : null;
        }

        private void SkipWhitespace()
        {
            // 空格、换行、回车、制表符
            while (index < text.Length && text[index] is ' ' or '\n' or '\r' or '\t') index++;
        }

        private DataValue? Value()
        {
            SkipWhitespace();
            if (index >= text.Length || depth >= MaxDepth) return null;
            return text[index] switch
            {
                '{' => Object(),
                '[' => Array(),
                '"' => String() is { } s ? new DataValue.String(s) : null,
                't' => Literal("true", new DataValue.Bool(true)),
                'f' => Literal("false", new DataValue.Bool(false)),
                'n' => Literal("null", DataValue.Null.Instance),
                _ => Number(),
            };
        }

        private DataValue? Object()
        {
            index++;
            depth++;
            try
            {
                var pairs = new List<DataValue.Pair>();
                SkipWhitespace();
                if (index < text.Length && text[index] == '}')
                {
                    index++;
                    return new DataValue.Object(pairs);
                }
                while (true)
                {
                    SkipWhitespace();
                    if (index >= text.Length || text[index] != '"' || String() is not { } key) return null;
                    SkipWhitespace();
                    if (index >= text.Length || text[index] != ':') return null;
                    index++;
                    if (Value() is not { } item) return null;
                    pairs.Add(new DataValue.Pair(key, item));
                    SkipWhitespace();
                    if (index >= text.Length) return null;
                    if (text[index] == ',')
                    {
                        index++;
                    }
                    else if (text[index] == '}')
                    {
                        index++;
                        return new DataValue.Object(pairs);
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            finally
            {
                depth--;
            }
        }

        private DataValue? Array()
        {
            index++;
            depth++;
            try
            {
                var items = new List<DataValue>();
                SkipWhitespace();
                if (index < text.Length && text[index] == ']')
                {
                    index++;
                    return new DataValue.Array(items);
                }
                while (true)
                {
                    if (Value() is not { } item) return null;
                    items.Add(item);
                    SkipWhitespace();
                    if (index >= text.Length) return null;
                    if (text[index] == ',')
                    {
                        index++;
                    }
                    else if (text[index] == ']')
                    {
                        index++;
                        return new DataValue.Array(items);
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            finally
            {
                depth--;
            }
        }

        private DataValue? Literal(string word, DataValue result)
        {
            if (index + word.Length > text.Length || string.CompareOrdinal(text, index, word, 0, word.Length) != 0) return null;
            index += word.Length;
            return result;
        }

        private DataValue? Number()
        {
            var start = index;
            if (index < text.Length && text[index] == '-') index++;
            if (Digits() == 0) return null;
            if (index < text.Length && text[index] == '.')
            {
                index++;
                if (Digits() == 0) return null;
            }
            if (index < text.Length && text[index] is 'e' or 'E')
            {
                index++;
                if (index < text.Length && text[index] is '+' or '-') index++;
                if (Digits() == 0) return null;
            }
            return new DataValue.Number(text[start..index]);
        }

        private int Digits()
        {
            var start = index;
            while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
            return index - start;
        }

        private string? String()
        {
            index++;
            var result = new StringBuilder();
            while (index < text.Length)
            {
                var c = text[index];
                index++;
                switch (c)
                {
                    case '"':
                        return result.ToString();
                    case '\\':
                        if (index >= text.Length) return null;
                        var escape = text[index];
                        index++;
                        switch (escape)
                        {
                            case '"': result.Append('"'); break;
                            case '\\': result.Append('\\'); break;
                            case '/': result.Append('/'); break;
                            case 'b': result.Append('\b'); break;
                            case 'f': result.Append('\f'); break;
                            case 'n': result.Append('\n'); break;
                            case 'r': result.Append('\r'); break;
                            case 't': result.Append('\t'); break;
                            case 'u':
                                if (Hex4() is not { } code) return null;
                                if (code is >= 0xD800 and <= 0xDBFF)
                                {
                                    // 代理对：后面必须紧跟着低位的 \uDC00–\uDFFF
                                    if (index + 1 >= text.Length || text[index] != '\\' || text[index + 1] != 'u') return null;
                                    index += 2;
                                    if (Hex4() is not { } low || low is < 0xDC00 or > 0xDFFF) return null;
                                    result.Append((char)code).Append((char)low);
                                }
                                else
                                {
                                    // 单独的低位代理不是合法字符
                                    if (code is >= 0xDC00 and <= 0xDFFF) return null;
                                    result.Append((char)code);
                                }
                                break;
                            default:
                                return null;
                        }
                        break;
                    default:
                        if (c < 0x20) return null;
                        result.Append(c);
                        break;
                }
            }
            return null;
        }

        private int? Hex4()
        {
            if (index + 4 > text.Length) return null;
            var value = 0;
            for (var i = index; i < index + 4; i++)
            {
                var digit = HexDigit(text[i]);
                if (digit < 0) return null;
                value = value * 16 + digit;
            }
            index += 4;
            return value;
        }

        private static int HexDigit(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };
    }
}
