using System.Collections.Generic;

namespace TCGinFU.Core
{
    public readonly struct CritResult
    {
        public CritResult(bool isCrit, bool isSuperCrit, IReadOnlyList<int> rolls, int critResist)
        {
            IsCrit = isCrit;
            IsSuperCrit = isSuperCrit;
            Rolls = rolls;
            CritResist = critResist;
        }

        public bool IsCrit { get; }
        public bool IsSuperCrit { get; }
        public IReadOnlyList<int> Rolls { get; }
        public int CritResist { get; }

        /// <summary>超暴击 ×2，暴击 ×1.5，否则 ×1。以分数形式返回避免浮点误差。</summary>
        public (int numerator, int denominator) Multiplier
            => IsSuperCrit ? (2, 1) : IsCrit ? (3, 2) : (1, 1);

        /// <summary>按调整后的骰值重新判定。<see cref="CritRollContext"/> 用它收尾。</summary>
        public CritResult WithRolls(IReadOnlyList<int> rolls, int critResist)
            => CritResolver.Evaluate(rolls, critResist, rolls.Count >= 2);

        public static readonly CritResult None = new(false, false, System.Array.Empty<int>(), 0);

        public override string ToString()
            => IsSuperCrit ? $"超暴击[{string.Join(",", Rolls)}]"
                : IsCrit ? $"暴击[{string.Join(",", Rolls)}]"
                : $"普通[{string.Join(",", Rolls)}]";
    }

    /// <summary>四色暴击判定与超暴击（§7.3）。</summary>
    public static class CritResolver
    {
        /// <summary>双色牌「两颗同点」触发超暴击的最低点数门槛。</summary>
        public const int MatchedPairThreshold = 4;

        /// <summary>
        /// 只有卡面带【暴击】标识的牌才调用这里。投哪颗骰取决于卡牌颜色。
        /// 单色：投 1 颗，≥ 抵抗即暴击。
        /// 双色：投 2 颗取高比对；两颗都 ≥ 抵抗**或**两颗同点且 ≥4 → 超暴击（裁定 R-04）。
        /// </summary>
        public static CritResult Roll(
            IRng rng,
            AttributeDice dice,
            IReadOnlyList<AttributeColor> colors,
            int critResist)
        {
            if (colors == null || colors.Count == 0) return CritResult.None;

            if (colors.Count == 1)
            {
                int roll = rng.RollDie(dice[colors[0]]);
                return new CritResult(roll >= critResist, false, new[] { roll }, critResist);
            }

            int a = rng.RollDie(dice[colors[0]]);
            int b = rng.RollDie(dice[colors[1]]);

            return Evaluate(new[] { a, b }, critResist, dualColor: true);
        }

        /// <summary>
        /// 由已知骰值判定暴击。单独拆出来是因为骰值会被词条改——
        /// 老卡「鹰眼锁定」掷骰+1、「命运锚点·七」把一颗骰视为 7，
        /// 改完必须用同一套规则重新判，不能各自写一遍。
        /// </summary>
        public static CritResult Evaluate(IReadOnlyList<int> rolls, int critResist, bool dualColor)
        {
            if (rolls == null || rolls.Count == 0) return CritResult.None;

            if (!dualColor || rolls.Count == 1)
            {
                return new CritResult(rolls[0] >= critResist, false, rolls, critResist);
            }

            int a = rolls[0];
            int b = rolls[1];

            bool bothReach = a >= critResist && b >= critResist;
            bool matchedPair = a == b && a >= MatchedPairThreshold;
            bool superCrit = bothReach || matchedPair;
            bool crit = superCrit || a >= critResist || b >= critResist;

            return new CritResult(crit, superCrit, rolls, critResist);
        }
    }
}
