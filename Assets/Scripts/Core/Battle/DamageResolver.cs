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
        /// <param name="ignoreShield">无视目标多少点护盾（老卡「鹰眼锁定」）。</param>
        /// <param name="critMultiplier">改写暴击倍率，null 用默认的 ×1.5 / ×2（老卡「幻影夜行」×1.75）。</param>
        public static DamageResult Resolve(
            Combatant source,
            Combatant target,
            int baseAmount,
            DamageElement element,
            CritResult crit,
            int ignoreShield = 0,
            (int Numerator, int Denominator)? critMultiplier = null)
        {
            bool wasInCrisis = IsInCrisis(target);

            // ② flat 修正：攻方状态加成 + 守方状态易伤（破绽）- 守方状态减伤
            int modifiers = (source?.Statuses.SumOutgoingDamageBonus ?? 0)
                            + target.Statuses.SumIncomingDamageBonus
                            - target.Statuses.SumIncomingDamageReduction;
            int afterModifiers = Max0(baseAmount + modifiers);

            // ③ 暴击。改写的倍率只在真的暴了的时候算数——
            // 不然「暴击倍率×1.75」会把普通攻击也放大 1.75 倍
            (int num, int den) = crit.IsCrit && critMultiplier.HasValue
                ? (critMultiplier.Value.Numerator, critMultiplier.Value.Denominator)
                : crit.Multiplier;
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

            // ④.5 受伤倍率（老 Boss「过热：受到最终伤害×1.5」）。
            // 放在克制之后、护盾之前：它放大的是「最终伤害」，而护盾扣的就是最终伤害
            (int inNum, int inDen) = target.Statuses.IncomingMultiplier;
            if (inDen > 0 && inNum != inDen) afterAffinity = afterAffinity * inNum / inDen;

            // ⑤ 护盾（裁定 R-03：护盾扣的是最终伤害）
            // 无视护盾的那几点先从「可被护盾吃掉的额度」里扣掉，再让护盾去挡剩下的——
            // 护盾本身不会因为被无视而消耗，这与老引擎的 ignore_shield 一致。
            int shieldable = Max0(afterAffinity - ignoreShield);
            int absorbed = target.Shields.Absorb(element.ToShieldKind(), shieldable);
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
