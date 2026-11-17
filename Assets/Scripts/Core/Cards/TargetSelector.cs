namespace TCGinFU.Core
{
    public enum TargetScope
    {
        /// <summary>施放者自己</summary>
        Self,
        /// <summary>由玩家指定的单个敌人</summary>
        SingleEnemy,
        AllEnemies,
        FrontRowEnemies,
        BackRowEnemies,
        /// <summary>由玩家指定的单个队友（含自己）</summary>
        SingleAlly,
        AllAllies,
        /// <summary>与施放者相邻的队友（§4.3）</summary>
        AdjacentAllies,
        /// <summary>本次结算的既定目标（用于时机牌/援护牌响应）</summary>
        CurrentTarget,
    }

    public readonly struct TargetSelector
    {
        public TargetScope Scope { get; }

        public TargetSelector(TargetScope scope) => Scope = scope;

        public static readonly TargetSelector Self = new(TargetScope.Self);
        public static readonly TargetSelector SingleEnemy = new(TargetScope.SingleEnemy);
        public static readonly TargetSelector AllEnemies = new(TargetScope.AllEnemies);
        public static readonly TargetSelector SingleAlly = new(TargetScope.SingleAlly);
        public static readonly TargetSelector CurrentTarget = new(TargetScope.CurrentTarget);

        /// <summary>该作用域是否需要玩家在提交动作时附带一个目标 Id。</summary>
        public bool RequiresExplicitTarget
            => Scope is TargetScope.SingleEnemy or TargetScope.SingleAlly;

        public override string ToString() => Scope.ToString();
    }
}
