using System;
using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>
    /// 引擎用到的全部随机性入口。Core 不引用 UnityEngine，也刻意不使用 System.Random，
    /// 这样同一个种子在任何平台、任何 .NET 版本上都产生同一局战斗，可做回放与断言。
    /// </summary>
    public interface IRng
    {
        /// <summary>返回 [minInclusive, maxExclusive) 内的整数。</summary>
        int NextInt(int minInclusive, int maxExclusive);
    }

    /// <summary>xorshift32，确定性且跨平台一致。</summary>
    public sealed class XorShift32Rng : IRng
    {
        private uint _state;

        public XorShift32Rng(int seed)
        {
            // 0 是 xorshift 的吸收态，必须避开。
            _state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentException($"空区间 [{minInclusive}, {maxExclusive})");
            }

            uint range = unchecked((uint)(maxExclusive - minInclusive));
            return minInclusive + (int)(NextUInt() % range);
        }

        private uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }
    }

    /// <summary>
    /// 测试替身：按给定序列依次吐出结果，用尽后循环。
    /// 值会被钳进请求区间，所以写 <c>new ScriptedRng(12)</c> 就能稳定投出 d12 的 12。
    /// </summary>
    public sealed class ScriptedRng : IRng
    {
        private readonly IReadOnlyList<int> _values;
        private int _cursor;

        public ScriptedRng(params int[] values)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException("ScriptedRng 至少需要一个值", nameof(values));
            }

            _values = values;
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            int value = _values[_cursor];
            _cursor = (_cursor + 1) % _values.Count;

            if (value < minInclusive) return minInclusive;
            if (value >= maxExclusive) return maxExclusive - 1;
            return value;
        }
    }

    public static class RngExtensions
    {
        /// <summary>投一颗属性骰，返回 1..面数。</summary>
        public static int RollDie(this IRng rng, DieRank rank) => rng.NextInt(1, (int)rank + 1);

        /// <summary>Fisher–Yates 原地洗牌。</summary>
        public static void Shuffle<T>(this IRng rng, IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
