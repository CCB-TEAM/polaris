// 最小 JSON 解析器：源生成器要跑在 netstandard2.0 且不能带 NuGet 依赖，
// 所以不复用 System.Text.Json / Newtonsoft。
// 只实现 jmap 需要的形状：object / array / string / number / bool / null。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Polaris.SdkGen;

public abstract class JsonValue
{
    public virtual string? AsString() => null;
    public virtual long AsLong() => 0;
    public virtual double AsDouble() => 0;
    public virtual bool AsBool() => false;
    public virtual JsonObject? AsObject() => null;
    public virtual JsonArray? AsArray() => null;
}

public sealed class JsonObject : JsonValue
{
    public Dictionary<string, JsonValue> Members { get; } = new(StringComparer.Ordinal);

    public JsonValue? this[string key] => Members.TryGetValue(key, out var v) ? v : null;
    public override JsonObject AsObject() => this;
}

public sealed class JsonArray : JsonValue
{
    public List<JsonValue> Items { get; } = new();
    public override JsonArray AsArray() => this;
}

public sealed class JsonString : JsonValue
{
    public JsonString(string value) => Value = value;
    public string Value { get; }
    public override string? AsString() => Value;
}

public sealed class JsonNumber : JsonValue
{
    public JsonNumber(double value, long longValue)
    {
        DoubleValue = value;
        LongValue = longValue;
    }

    public double DoubleValue { get; }
    public long LongValue { get; }
    public override long AsLong() => LongValue;
    public override double AsDouble() => DoubleValue;
}

public sealed class JsonBool : JsonValue
{
    public JsonBool(bool value) => Value = value;
    public bool Value { get; }
    public override bool AsBool() => Value;
}

public sealed class JsonNull : JsonValue
{
    public static readonly JsonNull Instance = new();
}

public static class Json
{
    public static JsonValue Parse(string text)
    {
        var parser = new Parser(text);
        var value = parser.ParseValue();
        parser.SkipWhitespace();
        return value;
    }

    private sealed class Parser(string text)
    {
        private int _pos;

        public void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
            {
                _pos++;
            }
        }

        public JsonValue ParseValue()
        {
            SkipWhitespace();
            if (_pos >= text.Length)
            {
                throw new FormatException("JSON 提前结束");
            }

            return text[_pos] switch
            {
                '{' => ParseObject(),
                '[' => ParseArray(),
                '"' => new JsonString(ParseString()),
                't' => ParseLiteral("true", JsonBoolFactory.True),
                'f' => ParseLiteral("false", JsonBoolFactory.False),
                'n' => ParseLiteral("null", JsonNull.Instance),
                _ => ParseNumber(),
            };
        }

        private JsonValue ParseLiteral(string literal, JsonValue value)
        {
            if (_pos + literal.Length > text.Length || string.CompareOrdinal(text, _pos, literal, 0, literal.Length) != 0)
            {
                throw new FormatException($"JSON 字面量错误 @{_pos}");
            }

            _pos += literal.Length;
            return value;
        }

        private JsonObject ParseObject()
        {
            var obj = new JsonObject();
            _pos++; // {
            SkipWhitespace();
            if (_pos < text.Length && text[_pos] == '}')
            {
                _pos++;
                return obj;
            }

            while (true)
            {
                SkipWhitespace();
                var key = ParseString();
                SkipWhitespace();
                Expect(':');
                var value = ParseValue();
                obj.Members[key] = value;
                SkipWhitespace();
                if (_pos < text.Length && text[_pos] == ',')
                {
                    _pos++;
                    continue;
                }

                Expect('}');
                return obj;
            }
        }

        private JsonArray ParseArray()
        {
            var arr = new JsonArray();
            _pos++; // [
            SkipWhitespace();
            if (_pos < text.Length && text[_pos] == ']')
            {
                _pos++;
                return arr;
            }

            while (true)
            {
                arr.Items.Add(ParseValue());
                SkipWhitespace();
                if (_pos < text.Length && text[_pos] == ',')
                {
                    _pos++;
                    continue;
                }

                Expect(']');
                return arr;
            }
        }

        private string ParseString()
        {
            Expect('"');
            var sb = new StringBuilder();
            while (_pos < text.Length)
            {
                var c = text[_pos++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (_pos >= text.Length)
                {
                    throw new FormatException("JSON 转义提前结束");
                }

                var esc = text[_pos++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_pos + 4 > text.Length)
                        {
                            throw new FormatException("JSON \\u 转义提前结束");
                        }

                        var hex = text.Substring(_pos, 4);
                        _pos += 4;
                        sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        break;
                    default:
                        throw new FormatException($"JSON 未知转义 \\{esc}");
                }
            }

            throw new FormatException("JSON 字符串没有闭合");
        }

        private JsonValue ParseNumber()
        {
            var start = _pos;
            while (_pos < text.Length && "+-0123456789.eE".IndexOf(text[_pos]) >= 0)
            {
                _pos++;
            }

            var slice = text.Substring(start, _pos - start);
            var d = double.Parse(slice, CultureInfo.InvariantCulture);
            var l = slice.IndexOfAny(['.', 'e', 'E']) >= 0 ? (long)d : long.Parse(slice, CultureInfo.InvariantCulture);
            return new JsonNumber(d, l);
        }

        private void Expect(char c)
        {
            if (_pos >= text.Length || text[_pos] != c)
            {
                throw new FormatException($"JSON 期望 '{c}' @{_pos}");
            }

            _pos++;
        }
    }

    private static class JsonBoolFactory
    {
        public static readonly JsonBool True = new(true);
        public static readonly JsonBool False = new(false);
    }
}
