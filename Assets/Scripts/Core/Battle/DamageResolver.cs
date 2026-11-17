namespace TCGinFU.Core
{
    /// <summary>一次伤害结算的完整分解。每一步中间值都留着，UI 飘字和测试断言都从这里取。</summary>
    public sealed class DamageResult
    {
        /// <summary>① ValueExpr 求出的基础值</summary>
        public int BaseAmount { get; init; }

        /// <summary>② flat 加减修正后的值</summary>
        public int AfterModifiers { get; init; }

        /// <summary>③ 暴击倍率后的值</summary>
        public int AfterCrit { get; init; }

        /// <summary>④ 属性克制后的值</summary>
        public int AfterAffinity { get; init; }

        /// <summary>⑤ 被护盾挡下的点数</summary>
        public int AbsorbedByShield { get; init; }

        /// <summary>⑥ 实际扣除的 HP</summary>
        public int HpLost { get; init; }

        /// <summary>吸收属性时转成的治疗量</summary>
        public int HealedInstead { get; init; }

        public DamageElement Element { get; init; }
        public Affinity Affinity { get; init; }
        public CritResult Crit { get; init; }
        public bool TargetWentDown { get; init; }
        public bool TargetEnteredCrisis { get; init; }

        public bool IsCrit => Crit.IsCrit;
        public bool IsSuperCrit => Crit.IsSuperCrit;

        public override string ToString()
            => $"{BaseAmount}→修正{AfterModifiers}→暴击{AfterCrit}→克制{AfterAffinity}→盾挡{AbsorbedByShield}→掉血{HpLost}";
    }

    /// <summary>
    /// 伤害管线（裁定 R-01 / R-02 / R-03 / R-05）。顺序固定为：
    /// 基础值 → flat 修正 → 暴击 → 属性克制 → 护盾 → 扣血。
    /// 每一步独立向下取整。
    /// </summary>
    public static class DamageResolver
    {
        public static DamageResult Resolve(
            Combatant source,
            Combatant target,
            int baseAmount,
            DamageElement element,
            CritResult crit)
        {
            bool wasInCrisis = IsInCrisis(target);

            // ② flat 修正：攻方状态加成 - 守方状态减伤
            int modifiers = (source?.Statuses.SumOutgoingDamageBonus ?? 0) - target.Statuses.SumIncomingDamageReduction;
            int afterModifiers = Max0(baseAmount + modifiers);

            // ③ 暴击
            (int num, int den) = crit.Multiplier;
            int afterCrit = afterModifiers * num / den;

            // ④ 属性克制
            Affinity affinity = target.Affinities[element];
            if (affinity == Affinity.Absorb)
            {
                // 裁定 R-05：按暴击后、克制前的数值转为治疗，不再乘 2。
                int healed = target.GainHp(afterCrit);
                return new DamageResult
                {
                    BaseAmount = baseAmount,
                    AfterModifiers = afterModifiers,
                    AfterCrit = afterCrit,
                    AfterAffinity = 0,
                    HealedInstead = healed,
                    Element = element,
                    Affinity = affinity,
                    Crit = crit,
                };
            }

            int afterAffinity = affinity switch
            {
                Affinity.Vulnerable => afterCrit * 2,
                Affinity.Resistant => afterCrit / 2,
                Affinity.Immune => 0,
                _ => afterCrit,
            };

            // ⑤ 护盾（裁定 R-03：护盾扣的是最终伤害）
            int absorbed = target.Shields.Absorb(element.ToShieldKind(), afterAffinity);
            int toHp = afterAffinity - absorbed;

            // ⑥ 扣血
            int hpLost = target.LoseHp(toHp);
            if (source != null) source.DamageDealtThisRound += hpLost;

            return new DamageResult
            {
                BaseAmount = baseAmount,
                AfterModifiers = afterModifiers,
                AfterCrit = afterCrit,
                AfterAffinity = afterAffinity,
                AbsorbedByShield = absorbed,
                HpLost = hpLost,
                Element = element,
                Affinity = affinity,
                Crit = crit,
                TargetWentDown = target.IsDown,
                TargetEnteredCrisis = !wasInCrisis && IsInCrisis(target),
            };
        }

        private static bool IsInCrisis(Combatant c) => c is PlayerCharacter pc && pc.IsInCrisis;

        private static int Max0(int value) => value < 0 ? 0 : value;
    }
}
