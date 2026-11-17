using System.Collections.Generic;

namespace TCGinFU.Core
{
    public enum BattleEventType
    {
        BattleStarted,
        RoundStarted,
        PhaseChanged,
        IntentRevealed,
        CardDrawn,
        DeckReshuffled,
        CardPlayed,
        CardDiscarded,
        ContinuousReplaced,
        ChargePlaced,
        ChargeEvicted,
        ChargeSpent,
        DamageDealt,
        Healed,
        ShieldGained,
        ShieldRefreshed,
        StatusApplied,
        StatusExpired,
        RowChanged,
        Covered,
        CrisisEntered,
        CrisisLeft,
        CombatantDown,
        TransformEntered,
        TransformExited,
        TransformShuffledBack,
        ApGained,
        MpRestored,
        HandLimitDiscard,
        SceneClockAdvanced,
        BattleEnded,
    }

    /// <summary>
    /// 引擎产出的一条事件。表现层订阅它做动画与飘字，测试拿它做断言，
    /// 将来联网时它就是下发给客户端的增量。刻意做成只读快照，不持有活对象引用之外的状态。
    /// </summary>
    public sealed class BattleEvent
    {
        public BattleEvent(BattleEventType type, string actorId = null, string targetId = null)
        {
            Type = type;
            ActorId = actorId;
            TargetId = targetId;
        }

        public BattleEventType Type { get; }
        public string ActorId { get; }
        public string TargetId { get; }

        /// <summary>主数值（伤害、治疗、抽牌数…）。</summary>
        public int Amount { get; init; }

        public string CardId { get; init; }
        public string Text { get; init; }
        public int Round { get; init; }

        /// <summary>伤害事件的完整分解，供 UI 做「暴击！弱点！」分段飘字。</summary>
        public DamageResult Damage { get; init; }

        public override string ToString()
        {
            var parts = new List<string> { Type.ToString() };
            if (ActorId != null) parts.Add($"actor={ActorId}");
            if (TargetId != null) parts.Add($"target={TargetId}");
            if (Amount != 0) parts.Add($"amount={Amount}");
            if (CardId != null) parts.Add($"card={CardId}");
            if (Text != null) parts.Add(Text);
            return string.Join(" ", parts);
        }
    }
}
