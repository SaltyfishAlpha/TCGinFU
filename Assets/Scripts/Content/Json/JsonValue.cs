using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TCGinFU.Content.Json
{
    public enum JsonKind { Missing, Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// 一个 JSON 节点。取值一律走 <c>AsXxx(fallback)</c>：**缺字段返回默认值，不抛异常**。
    ///
    /// 这样设计是因为内容数据最终要交给玩家编辑（见 data_architecture.md §9）：
    /// 少写一个可选字段不该让游戏起不来。但**写错的值**要被发现——
    /// 所以 <see cref="AsEnum{T}"/> 这类会把问题塞进调用方传入的 errors 列表，
    /// 由 <c>ContentLoader</c> 一次性汇总报出来，而不是静默读成 0。
    /// </summary>
    public readonly struct JsonValue
    {
        private readonly object _value;

        public JsonKind Kind { get; }

        private JsonValue(JsonKind kind, object value)
        {
            Kind = kind;
            _value = value;
        }

        public static readonly JsonValue Missing = new(JsonKind.Missing, null);
        public static readonly JsonValue Null = new(JsonKind.Null, null);
        public static readonly JsonValue True = new(JsonKind.Bool, true);
        public static readonly JsonValue False = new(JsonKind.Bool, false);

        public static JsonValue FromString(string s) => new(JsonKind.String, s);
        public static JsonValue FromNumber(double d) => new(JsonKind.Number, d);
        public static JsonValue FromArray(List<JsonValue> list) => new(JsonKind.Array, list);
        public static JsonValue FromObject(Dictionary<string, JsonValue> map) => new(JsonKind.Object, map);

        /// <summary>字段是否存在且非 null。</summary>
        public bool Exists => Kind != JsonKind.Missing && Kind != JsonKind.Null;

        public JsonValue this[string key]
            => _value is Dictionary<string, JsonValue> map && map.TryGetValue(key, out JsonValue v)
                ? v
                : Missing;

        public JsonValue this[int index]
            => _value is List<JsonValue> list && index >= 0 && index < list.Count ? list[index] : Missing;

        public int Count => _value switch
        {
            List<JsonValue> list => list.Count,
            Dictionary<string, JsonValue> map => map.Count,
            _ => 0,
        };

        public IEnumerable<JsonValue> Items
            => _value as List<JsonValue> ?? Enumerable.Empty<JsonValue>();

        public IEnumerable<KeyValuePair<string, JsonValue>> Fields
            => _value as Dictionary<string, JsonValue>
               ?? Enumerable.Empty<KeyValuePair<string, JsonValue>>();

        public string AsString(string fallback = null)
            => Kind switch
            {
                JsonKind.String => (string)_value,
                JsonKind.Number => ((double)_value).ToString(CultureInfo.InvariantCulture),
                JsonKind.Bool => (bool)_value ? "true" : "false",
                _ => fallback,
            };

        public int AsInt(int fallback = 0)
            => Kind == JsonKind.Number ? (int)Math.Round((double)_value) : fallback;

        public double AsDouble(double fallback = 0)
            => Kind == JsonKind.Number ? (double)_value : fallback;

        public bool AsBool(bool fallback = false)
            => Kind == JsonKind.Bool ? (bool)_value : fallback;

        /// <summary>
        /// 按**枚举名**解析（内容 JSON 一律写名字而不是数字，免得枚举增删项时静默错位）。
        /// 值非法时记一条错误并返回 fallback。
        /// </summary>
        public T AsEnum<T>(T fallback, string context, List<string> errors) where T : struct, Enum
        {
            if (!Exists) return fallback;

            string raw = AsString();
            if (raw != null && Enum.TryParse(raw, ignoreCase: true, out T parsed)
                && Enum.IsDefined(typeof(T), parsed))
            {
                return parsed;
            }

            errors?.Add($"{context}：「{raw ?? Kind.ToString()}」不是合法的 {typeof(T).Name}，"
                        + $"可选值 {string.Join(" / ", Enum.GetNames(typeof(T)))}");
            return fallback;
        }

        public override string ToString() => Kind switch
        {
            JsonKind.String => $"\"{_value}\"",
            JsonKind.Number => ((double)_value).ToString(CultureInfo.InvariantCulture),
            JsonKind.Bool => (bool)_value ? "true" : "false",
            JsonKind.Array => $"[{Count} 项]",
            JsonKind.Object => $"{{{Count} 字段}}",
            _ => Kind.ToString(),
        };
    }
}
