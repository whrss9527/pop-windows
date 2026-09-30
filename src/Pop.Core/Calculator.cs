using System.Globalization;
using System.Text;

namespace Pop.Core;

/// 小型算式求值器：支持 + - * / ^、括号、百分号（50% = 0.5）和常见全角符号。
/// 遇到非法输入返回 null，不抛异常。
public static class Calculator
{
    public static double? Evaluate(string input)
    {
        var chars = Normalize(input);
        if (chars.Length == 0 || chars.Length > 256) return null;
        var parser = new Parser(chars);
        var value = parser.ParseExpression();
        if (value is not { } v || !parser.IsAtEnd || !double.IsFinite(v)) return null;
        return v;
    }

    /// 整数不带小数点；其他的最多 12 位有效数字，很大或很小的数用 1e+20 这样的写法
    public static string Format(double value)
    {
        if (value == Math.Round(value, MidpointRounding.AwayFromZero) && Math.Abs(value) < 1e15)
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        }
        return value.ToString("G12", CultureInfo.InvariantCulture).Replace('E', 'e');
    }

    /// 统一符号、去掉空白和千分位逗号、去掉末尾的等号。
    public static string Normalize(string input)
    {
        var result = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            switch (ch)
            {
                case '×' or '＊' or '✕': result.Append('*'); break;
                case '÷' or '／': result.Append('/'); break;
                case '＋': result.Append('+'); break;
                case '－' or '−': result.Append('-'); break;
                case '（': result.Append('('); break;
                case '）': result.Append(')'); break;
                case '％': result.Append('%'); break;
                case '，' or ',': break;
                default:
                    if (!char.IsWhiteSpace(ch)) result.Append(ch);
                    break;
            }
        }
        while (result.Length > 0 && result[^1] is '=' or '＝')
        {
            result.Length--;
        }
        return result.ToString();
    }

    // expression := term (('+' | '-') term)*
    // term       := unary (('*' | '/') unary)*
    // unary      := ('+' | '-') unary | power
    // power      := postfix ('^' unary)?
    // postfix    := primary '%'*
    // primary    := number | '(' expression ')'
    private sealed class Parser(string chars)
    {
        private int _index;
        private int _depth;

        public bool IsAtEnd => _index >= chars.Length;

        private char? Peek() => _index < chars.Length ? chars[_index] : null;

        public double? ParseExpression()
        {
            _depth++;
            try
            {
                if (_depth >= 64 || ParseTerm() is not { } value) return null;
                while (Peek() is { } op && (op == '+' || op == '-'))
                {
                    _index++;
                    if (ParseTerm() is not { } rhs) return null;
                    value = op == '+' ? value + rhs : value - rhs;
                }
                return value;
            }
            finally
            {
                _depth--;
            }
        }

        private double? ParseTerm()
        {
            if (ParseUnary() is not { } value) return null;
            while (Peek() is { } op && (op == '*' || op == '/'))
            {
                _index++;
                if (ParseUnary() is not { } rhs) return null;
                if (op == '*')
                {
                    value *= rhs;
                }
                else
                {
                    if (rhs == 0) return null;
                    value /= rhs;
                }
            }
            return value;
        }

        private double? ParseUnary()
        {
            if (Peek() is { } op && (op == '+' || op == '-'))
            {
                _index++;
                _depth++;
                try
                {
                    if (_depth >= 64 || ParseUnary() is not { } value) return null;
                    return op == '-' ? -value : value;
                }
                finally
                {
                    _depth--;
                }
            }
            return ParsePower();
        }

        private double? ParsePower()
        {
            if (ParsePostfix() is not { } b) return null;
            if (Peek() == '^')
            {
                _index++;
                if (ParseUnary() is not { } exponent) return null;
                return Math.Pow(b, exponent);
            }
            return b;
        }

        private double? ParsePostfix()
        {
            if (ParsePrimary() is not { } value) return null;
            while (Peek() == '%')
            {
                _index++;
                value /= 100;
            }
            return value;
        }

        private double? ParsePrimary()
        {
            if (Peek() == '(')
            {
                _index++;
                if (ParseExpression() is not { } value || Peek() != ')') return null;
                _index++;
                return value;
            }
            return ParseNumber();
        }

        private double? ParseNumber()
        {
            var start = _index;
            var sawDigit = false;
            var sawDot = false;
            while (Peek() is { } c)
            {
                if (char.IsAsciiDigit(c))
                {
                    sawDigit = true;
                    _index++;
                }
                else if (c == '.' && !sawDot)
                {
                    sawDot = true;
                    _index++;
                }
                else
                {
                    break;
                }
            }
            if (!sawDigit)
            {
                _index = start;
                return null;
            }
            return double.TryParse(chars.AsSpan(start, _index - start), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number) ? number : null;
        }
    }
}
