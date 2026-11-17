using System;
using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>
    /// 玩家提交给引擎的一个动作。引擎先整体校验，不合法就**原样拒绝且不改动任何状态**，
    /// 所以 UI 可以乐观地直接提交，失败了拿 reason 提示即可。
    /// </summary>
    public interface IBattleAction
    {
        string ActorId { get; }
    }

    public readonly struct ActionResult
    {
        private ActionResult(bool ok, string reason)
        {
            Ok = ok;
            Reason = reason;
        }

        public bool Ok { get; }
        public string Reason { get; }

        public static readonly ActionResult Success = new(true, null);
        public static ActionResult Fail(string reason) => new(false, reason);

        public override string ToString() => Ok ? "OK" : $"拒绝：{Reason}";
    }

    /// <summary>打出一张手牌。</summary>
    public sealed class PlayCardAction : IBattleAction
    {
        public PlayCardAction(string actorId, int cardUid, string targetId = null, int chargeForMp = 0)
        {
            ActorId = actorId;
            CardUid = cardUid;
            TargetId = targetId;
            ChargeForMp = chargeForMp;
        }

        public string ActorId { get; }
        public int CardUid { get; }

        /// <summary>SingleEnemy / SingleAlly 作用域需要。</summary>
        public string TargetId { get; }

        /// <summary>额外消耗几个充能来抵扣 MP（每个抵 5 点，不找零，裁定 R-09）。</summary>
        public int ChargeForMp { get; }
    }

    /// <summary>贴充能：0 AP，每角色每轮 1 次，可贴给自己或任意队友（§5.2）。</summary>
    public sealed class PlaceChargeAction : IBattleAction
    {
        public PlaceChargeAction(string actorId, int cardUid, string chargeZoneOwnerId = null)
        {
            ActorId = actorId;
            CardUid = cardUid;
            ChargeZoneOwnerId = chargeZoneOwnerId ?? actorId;
        }

        public string ActorId { get; }
        public int CardUid { get; }

        /// <summary>充能贴到谁的充能区，默认自己。</summary>
        public string ChargeZoneOwnerId { get; }
    }

    /// <summary>消耗 1 个充能换 +1 AP（另两种用途绑在打牌动作里）。</summary>
    public sealed class SpendChargeForApAction : IBattleAction
    {
        public SpendChargeForApAction(string actorId) => ActorId = actorId;
        public string ActorId { get; }
    }

    /// <summary>消耗 1 AP 在前后排之间移动（§4.2，每轮 1 次，裁定 R-16）。</summary>
    public sealed class MoveRowAction : IBattleAction
    {
        public MoveRowAction(string actorId) => ActorId = actorId;
        public string ActorId { get; }
    }

    /// <summary>宣布本轮行动完毕。全员宣布后进入敌人阶段。</summary>
    public sealed class DeclareReadyAction : IBattleAction
    {
        public DeclareReadyAction(string actorId) => ActorId = actorId;
        public string ActorId { get; }
    }

    /// <summary>
    /// 轮始宣告进入升变。CardUid 可以是手牌中的升变牌，
    /// 也可以是第 3 轮保底跳出时主卡组中的那张（§6.1）。
    /// </summary>
    public sealed class DeclareTransformAction : IBattleAction
    {
        public DeclareTransformAction(string actorId, int cardUid)
        {
            ActorId = actorId;
            CardUid = cardUid;
        }

        public string ActorId { get; }
        public int CardUid { get; }
    }

    /// <summary>本轮不升变。</summary>
    public sealed class DeclineTransformAction : IBattleAction
    {
        public DeclineTransformAction(string actorId) => ActorId = actorId;
        public string ActorId { get; }
    }

    /// <summary>
    /// 条件未达成时的手里效果：展示升变牌 → 洗回主卡组 → 抽 1 张（§6.1）。免费，不耗 AP。
    /// </summary>
    public sealed class ShuffleBackTransformAction : IBattleAction
    {
        public ShuffleBackTransformAction(string actorId, int cardUid)
        {
            ActorId = actorId;
            CardUid = cardUid;
        }

        public string ActorId { get; }
        public int CardUid { get; }
    }

    /// <summary>敌人阶段的响应窗口里放弃响应。</summary>
    public sealed class PassResponseAction : IBattleAction
    {
        public PassResponseAction(string actorId) => ActorId = actorId;
        public string ActorId { get; }
    }

    /// <summary>轮末把手牌弃到 7 张（§8 ⑤）。</summary>
    public sealed class DiscardToHandLimitAction : IBattleAction
    {
        public DiscardToHandLimitAction(string actorId, IEnumerable<int> cardUids)
        {
            ActorId = actorId;
            CardUids = new List<int>(cardUids ?? Array.Empty<int>());
        }

        public string ActorId { get; }
        public IReadOnlyList<int> CardUids { get; }
    }
}
