using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TCGinFU.Content.Json
{
    /// <summary>
    /// 极简只读 JSON 解析器。
    ///
    /// 为什么要自己写：内容层 <c>TCGinFU.Content</c> 设了 <c>noEngineReferences</c>
    /// （为了能脱离 Unity 跑 <c>dotnet test</c>），所以 <c>JsonUtility</c> 用不了；
    /// 而项目没有引 Newtonsoft。内容数据只用到对象/数组/字符串/数字/布尔/null，
    /// 一个两百行的递归下降解析器就够，比加一个包的依赖成本低。
    ///
    /// 只读、不做反射、不生成代码，netstandard2.1 与 net8.0 都能编。
    /// </summary>
    public static class Json
    {
        public static JsonValue Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            var parser = new Parser(text);
            JsonValue value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw parser.Error("文档末尾有多余内容");
            return value;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) => _s = s;

            public bool AtEnd => _i >= _s.Length;

            private int Line
            {
                get
                {
                    int line = 1;
                    for (int j = 0; j < _i && j < _s.Length; j++) if (_s[j] == '\n') line++;
                    return line;
                }
            }

            public JsonException Error(string message) => new($"第 {Line} 行：{message}");

            public void SkipWhitespace()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            public JsonValue ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) throw Error("内容意外结束");

                return _s[_i] switch
                {
                    '{' => ParseObject(),
                    '[' => ParseArray(),
                    '"' => JsonValue.FromString(ParseString()),
                    't' => ParseLiteral("true", JsonValue.True),
                    'f' => ParseLiteral("false", JsonValue.False),
                    'n' => ParseLiteral("null", JsonValue.Null),
                    _ => ParseNumber(),
                };
            }

            private JsonValue ParseLiteral(string literal, JsonValue result)
            {
                if (_i + literal.Length > _s.Length || string.CompareOrdinal(_s, _i, literal, 0, literal.Length) != 0)
                {
                    throw Error($"期望 {literal}");
                }

                _i += literal.Length;
                return result;
            }

            private JsonValue ParseObject()
            {
                _i++; // '{'
                var map = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}') { _i++; return JsonValue.FromObject(map); }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"') throw Error("对象的键必须是字符串");
                    string key = ParseString();

                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':') throw Error($"键 {key} 后面缺少冒号");
                    _i++;

                    map[key] = ParseValue();

                    SkipWhitespace();
                    if (AtEnd) throw Error("对象未闭合");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return JsonValue.FromObject(map); }
                    throw Error("对象里期望 , 或 }");
                }
            }

            private JsonValue ParseArray()
            {
                _i++; // '['
                var list = new List<JsonValue>();

                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']') { _i++; return JsonValue.FromArray(list); }

                while (true)
                {
                    list.Add(ParseValue());

                    SkipWhitespace();
                    if (AtEnd) throw Error("数组未闭合");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return JsonValue.FromArray(list); }
                    throw Error("数组里期望 , 或 ]");
                }
            }

            private string ParseString()
            {
                _i++; // '"'
                var sb = new StringBuilder();

                while (true)
                {
                    if (AtEnd) throw Error("字符串未闭合");
                    char c = _s[_i++];

                    if (c == '"') return sb.ToString();

                    if (c != '\\') { sb.Append(c); continue; }

                    if (AtEnd) throw Error("转义符后内容结束");
                    char esc = _s[_i++];
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
                            if (_i + 4 > _s.Length) throw Error("\\u 转义不完整");
                            sb.Append((char)Convert.ToInt32(_s.Substring(_i, 4), 16));
                            _i += 4;
                            break;
                        default: throw Error($"未知转义 \\{esc}");
                    }
                }
            }

            private JsonValue ParseNumber()
            {
                int start = _i;
                if (!AtEnd && (_s[_i] == '-' || _s[_i] == '+')) _i++;
                while (!AtEnd && (char.IsDigit(_s[_i]) || _s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E'
                                  || _s[_i] == '-' || _s[_i] == '+')) _i++;

                string raw = _s.Substring(start, _i - start);
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    throw Error($"不是合法的数字：{raw}");
                }

                return JsonValue.FromNumber(number);
            }
        }
    }

    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message) { }
    }
}
