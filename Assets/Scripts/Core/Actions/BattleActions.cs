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

        /// <summary>
        /// 卡面要玩家做的**额外选择**，按名字取。界面收集，行为层通过 <c>ctx.choice("…")</c> 读。
        ///
        /// <para>为什么是个开放的字典而不是固定字段：这些选择完全由卡面决定——
        /// 「从弃牌堆选哪张」是 uid、「二选一」是选项序号、「附加什么元素」是属性名。
        /// 给每种都加一个字段会让 <see cref="PlayCardAction"/> 变成所有卡面的并集，
        /// 而且玩家自定义卡牌还会引入我们没预料到的选择类型。</para>
        ///
        /// <para><b>约定</b>：键名由卡的脚本自己定，和界面约好即可；
        /// 引擎不认识任何一个键，只负责原样传过去。</para>
        /// </summary>
        public Dictionary<string, int> Choices { get; } = new();

        /// <summary>链式写法，方便界面一行把选择填进去。</summary>
        public PlayCardAction With(string key, int value)
        {
            if (!string.IsNullOrEmpty(key)) Choices[key] = value;
            return this;
        }

        /// <summary>一次填多个（「选 2 张牌」这种），键名自动加下标：<c>card0</c> <c>card1</c>…</summary>
        public PlayCardAction WithMany(string key, IEnumerable<int> values)
        {
            int index = 0;
            foreach (int value in values) Choices[$"{key}{index++}"] = value;
            Choices[$"{key}_count"] = index;
            return this;
        }
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
