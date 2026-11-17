namespace TCGinFU.Core
{
    /// <summary>数据驱动效果的种类。新增一种就加一个 <see cref="IEffectHandler"/>。</summary>
    public enum EffectKind
    {
        Damage,
        Heal,
        GainShield,
        RestoreMp,
        GainAp,
        Draw,
        /// <summary>从手牌弃置 N 张（由玩家选）</summary>
        Discard,
        /// <summary>直接产生 N 个充能指示物</summary>
        GainCharge,
        ApplyStatus,
        /// <summary>移动排位</summary>
        MoveRow,
        /// <summary>把本次结算的承伤目标改写为施放者（援护，裁定 R-12）</summary>
        Cover,
    }

    /// <summary>效果的附加生效条件。不满足时该条 EffectData 整条跳过。</summary>
    public enum EffectCondition
    {
        Always,
        /// <summary>本次打出触发了暴击</summary>
        OnCrit,
        /// <summary>本次打出触发了超暴击</summary>
        OnSuperCrit,
        /// <summary>施放者处于危机状态</summary>
        IfCasterInCrisis,
        /// <summary>本次打出消耗了至少 1 个充能</summary>
        IfChargeSpent,
        /// <summary>目标处于危机状态</summary>
        IfTargetInCrisis,
    }

    /// <summary>
    /// 一条数据驱动的卡牌效果。一张卡 = 一串有序 EffectData，由 <see cref="EffectRegistry"/> 解释执行。
    /// 绝大多数卡靠调这些字段就能做出来，不必写代码；特殊卡再挂 <see cref="ICardBehaviour"/>。
    /// </summary>
    public sealed class EffectData
    {
        public EffectKind Kind { get; set; }
        public ValueExpr Amount { get; set; } = ValueExpr.Flat(0);
        public TargetSelector Target { get; set; } = TargetSelector.SingleEnemy;
        public DamageElement Element { get; set; } = DamageElement.Physical;
        public ShieldKind ShieldKind { get; set; } = ShieldKind.Physical;
        public string StatusId { get; set; }
        public int Duration { get; set; } = 1;
        public EffectCondition Condition { get; set; } = EffectCondition.Always;

        public static EffectData Damage(ValueExpr amount, DamageElement element = DamageElement.Physical,
            TargetScope scope = TargetScope.SingleEnemy)
            => new() { Kind = EffectKind.Damage, Amount = amount, Element = element, Target = new TargetSelector(scope) };

        public static EffectData Shield(ValueExpr amount, ShieldKind kind = ShieldKind.Physical,
            TargetScope scope = TargetScope.Self)
            => new() { Kind = EffectKind.GainShield, Amount = amount, ShieldKind = kind, Target = new TargetSelector(scope) };

        public static EffectData Heal(ValueExpr amount, TargetScope scope = TargetScope.SingleAlly)
            => new() { Kind = EffectKind.Heal, Amount = amount, Target = new TargetSelector(scope) };

        public static EffectData Draw(int count)
            => new() { Kind = EffectKind.Draw, Amount = ValueExpr.Flat(count), Target = TargetSelector.Self };

        /// <summary>
        /// 援护。作用域固定为 CurrentTarget——援护改写的是「本次结算的既定目标」，
        /// 用默认的 SingleEnemy 会在目标校验时被当成攻击牌拒掉。
        /// </summary>
        public static EffectData Cover()
            => new() { Kind = EffectKind.Cover, Target = TargetSelector.CurrentTarget };

        public override string ToString()
            => $"{Kind} {Amount} → {Target}" + (Condition == EffectCondition.Always ? "" : $" [{Condition}]");
    }
}
