using System;
using System.Collections.Generic;

namespace TCGinFU.Core
{
    public enum DecisionKind
    {
        None,
        /// <summary>轮始：可宣告进入升变（含第 3 轮保底跳出）</summary>
        TransformDeclaration,
        /// <summary>玩家阶段：全队自由穿插行动，直到全员 Ready</summary>
        PlayerActions,
        /// <summary>敌人阶段：某条意图结算前的响应窗口（裁定 R-11）</summary>
        EnemyResponse,
        /// <summary>轮末：手牌超过 7 张，需要选择弃牌</summary>
        HandLimitDiscard,
        /// <summary>
        /// 卡牌给的定向移动结算完毕，等玩家在目标排内选一个落点槽位（裁定 R-17c）。
        /// <para>只会插在别的窗口中间，选完就原样还原被打断的那个窗口。</para>
        /// </summary>
        ChooseSlot,
    }

    /// <summary>
    /// 引擎停下来等外部输入的点。引擎不会自己跨过它——
    /// 外部（UI 或 AI）读 <see cref="BattleEngine.Pending"/> 决定能提交什么动作。
    /// </summary>
    public sealed class PendingDecision
    {
        public PendingDecision(DecisionKind kind, IEnumerable<string> waitingOn = null)
        {
            Kind = kind;
            WaitingOn = new List<string>(waitingOn ?? Array.Empty<string>());
        }

        public DecisionKind Kind { get; }

        /// <summary>还没表态的角色 Id。空集合表示引擎可以继续推进。</summary>
        public List<string> WaitingOn { get; }

        /// <summary>EnemyResponse 窗口对应的意图。</summary>
        public EnemyIntent Intent { get; init; }

        /// <summary>EnemyResponse 窗口中该意图的来源敌人 Id。</summary>
        public string IntentSourceId { get; init; }

        /// <summary>HandLimitDiscard 时，该角色需要弃几张。</summary>
        public int RequiredDiscards { get; init; }

        /// <summary>
        /// ChooseSlot 时，玩家只能在这一排里挑槽位——
        /// 卡面说的是「移动到后排」，排已经由卡决定了，可选的只有排内的相对位置。
        /// </summary>
        public Row SlotChoiceRow { get; init; }

        public bool IsResolved => WaitingOn.Count == 0;

        public static readonly PendingDecision None = new(DecisionKind.None);

        public override string ToString()
            => $"{Kind}" + (WaitingOn.Count > 0 ? $" 等待 [{string.Join(",", WaitingOn)}]" : "");
    }
}
