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
        /// <summary>充能被消耗后没进弃牌堆，留在了充能区（「真理炼金矩阵」）</summary>
        ChargeRetained,
        /// <summary>充能被敌人吞噬（「以太虹吸」）。不是玩家主动消耗，不计入 ChargesSpent</summary>
        ChargeDevoured,
        DamageDealt,
        /// <summary>一次攻击被完全闪避，没有进入伤害管线</summary>
        AttackAvoided,
        /// <summary>「触发即消失」的状态（破绽等）被这次结算消耗掉了</summary>
        StatusConsumed,
        /// <summary>脚本化行为抛了异常，已被隔离——这张卡的这个钩子本次没生效</summary>
        BehaviourFaulted,
        /// <summary>脚本自己往战报里写的一行</summary>
        ScriptLog,
        Healed,
        ShieldGained,
        ShieldRefreshed,
        StatusApplied,
        StatusExpired,
        RowChanged,
        /// <summary>获得一次「可免费移动」的权利，等玩家自己兑现</summary>
        FreeMoveGranted,
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

        /// <summary>
        /// 打出的那张牌。表现层拿它做出牌演出（左侧亮出卡面）。
        ///
        /// <para>光有 <see cref="CardId"/> 不够：敌人的意图牌是内嵌在 <c>EnemyContent</c> 里的，
        /// 不进内容包的卡牌索引，按 id 查不到。与其让表现层对玩家牌和敌人牌走两条不同的
        /// 查找路径（还得赶在意图被清空之前查），不如由发事件的一方直接带上。</para>
        ///
        /// <para><see cref="CardDefinition"/> 是不可变的，所以这仍然是只读快照。
        /// 但它**不可序列化**——将来下发给远端客户端时要落回 <see cref="CardId"/>，
        /// 由对端自己按内容包解析。</para>
        /// </summary>
        public CardDefinition Card { get; init; }

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
