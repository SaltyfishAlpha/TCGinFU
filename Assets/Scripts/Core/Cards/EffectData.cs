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
        /// <summary>移动排位。不指定 <see cref="EffectData.RowTarget"/> 时是「在前后排之间切换」</summary>
        MoveRow,
        /// <summary>授予「可免费移动 1 次」的权利，由玩家自己决定何时兑现</summary>
        GrantFreeMove,
        /// <summary>己方或敌方前后排整体互换，保持各自的列</summary>
        SwapRows,
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

        /// <summary>
        /// 这条效果单独投一次暴击骰，而不是共用整张牌那一次（裁定 R-23）。
        ///
        /// <para>老卡「压制连射」卡面写的是「两次射击**分别独立**投掷敏捷骰判定暴击」，
        /// 展开成 2 条 Damage 后共用一次骰结果是错的：数值期望对，但两段要么全暴要么全不暴，
        /// 和卡面承诺的手感不是一回事。多段攻击的卡请把每一段都标上这个。</para>
        /// </summary>
        public bool RollsOwnCrit { get; set; }

        /// <summary>
        /// <see cref="EffectKind.MoveRow"/> 的目标排。null 表示「在前后排之间切换」。
        ///
        /// <para>定向与切换必须分开：「被击退至后排」对已经在后排的人应当无事发生，
        /// 用切换会把他拉到前排，效果正好相反。</para>
        /// </summary>
        public Row? RowTarget { get; set; }

        /// <summary>
        /// <see cref="EffectKind.MoveRow"/>：移动是强制的，但**排内站哪由玩家挑**（裁定 R-17c）。
        ///
        /// <para>给「先声夺人」这类自己打出的定向移动用——卡面写死了去哪一排，
        /// 可选的只有排内的相对位置。敌人造成的强制移动（击退）不开这个，自动选同列。</para>
        /// </summary>
        public bool PlayerChoosesSlot { get; set; }

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
