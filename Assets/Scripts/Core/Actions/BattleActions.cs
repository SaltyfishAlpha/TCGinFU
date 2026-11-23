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
        public PlayCardAction(string actorId, int cardUid, string targetId = null, int chargeForEffect = 0)
        {
            ActorId = actorId;
            CardUid = cardUid;
            TargetId = targetId;
            ChargeForEffect = chargeForEffect;
        }

        public string ActorId { get; }
        public int CardUid { get; }

        /// <summary>SingleEnemy / SingleAlly 作用域需要。</summary>
        public string TargetId { get; }

        /// <summary>
        /// 额外付给**卡面词条**的充能数（「若消耗1个充能指示物则…」），
        /// 可选值由 <see cref="CardDefinition.ChargeOptions"/> 声明。
        ///
        /// <para>这里**不包含**抵 MP：拿充能换 MP 是一个独立动作
        /// （<see cref="SpendChargeForMpAction"/>），不跟着出牌走。一个充能只算一种用途，
        /// 所以转过 MP 的充能不会再被算进 <c>ctx.charge_spent</c>（裁定 R-09，2026-11-23 改写）。</para>
        /// </summary>
        public int ChargeForEffect { get; }

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

    /// <summary>消耗 1 个充能换 +1 AP（只限玩家阶段，轮末清零，裁定 R-10）。</summary>
    public sealed class SpendChargeForApAction : IBattleAction
    {
        public SpendChargeForApAction(string actorId) => ActorId = actorId;
        public string ActorId { get; }
    }

    /// <summary>
    /// 消耗 1 个充能回复 5 MP（§5.2）。
    ///
    /// <para>是个**独立动作**而不是出牌时的抵扣：抵扣那套（旧裁定 R-09）既说不清
    /// 「抵掉的充能算不算支付了卡面词条」，又让玩家只能在打某几张牌时才用得上充能。
    /// 拆开之后两件事互不相干——转 MP 的充能不计入 <see cref="PlayCardAction.ChargeForEffect"/>。</para>
    ///
    /// <para>响应窗口里也能用：看破闪避这类时机牌同样要花 MP。</para>
    /// </summary>
    public sealed class SpendChargeForMpAction : IBattleAction
    {
        public SpendChargeForMpAction(string actorId) => ActorId = actorId;
        public string ActorId { get; }
    }

    /// <summary>
    /// 消耗 1 AP 走位（§4.2，每轮 1 次，裁定 R-16）。
    ///
    /// <para>落点是**任意排的任意槽位**：跨排换位和同排内改变相对位置都是「移动 1 次」，
    /// 同样是 1 AP（裁定 R-17b）。目标槽有队友时挤开，所以满员的排也调得动站位。</para>
    /// </summary>
    public sealed class MoveRowAction : IBattleAction
    {
        /// <summary>切到对排、落点槽位自动选（同列优先）。老的「拖到对排」行为。</summary>
        public MoveRowAction(string actorId) => ActorId = actorId;

        /// <summary>移动到指定排的指定槽位。</summary>
        public MoveRowAction(string actorId, Row targetRow, int targetSlot)
        {
            ActorId = actorId;
            TargetRow = targetRow;
            TargetSlot = targetSlot;
        }

        public string ActorId { get; }

        /// <summary>目标排。为 null 表示「切到对排」。</summary>
        public Row? TargetRow { get; }

        /// <summary>目标槽位。为 null 表示由引擎自动选列。</summary>
        public int? TargetSlot { get; }
    }

    /// <summary>
    /// 回应 <see cref="DecisionKind.ChooseSlot"/>：在卡面允许的范围内挑一个落点。
    /// 不花 AP，也不占本轮那一次常规走位——这是卡牌效果的一部分（裁定 R-17c）。
    /// </summary>
    public sealed class ChooseSlotAction : IBattleAction
    {
        /// <summary>范围已经把排定死时（「移动到后排」）只需给出槽位。</summary>
        public ChooseSlotAction(string actorId, int slot)
        {
            ActorId = actorId;
            Slot = slot;
        }

        /// <summary>范围跨两排时（「打出后移动 1 次」）排也要玩家挑。</summary>
        public ChooseSlotAction(string actorId, Row row, int slot)
        {
            ActorId = actorId;
            Row = row;
            Slot = slot;
        }

        public string ActorId { get; }
        public int Slot { get; }

        /// <summary>目标排。为 null 时由范围推导。</summary>
        public Row? Row { get; }
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

    /// <summary>
    /// 敌人阶段的响应窗口里站出来**分摊**一次攻击：伤害对半拆开，
    /// 原目标与分摊者各承一份、各自抵扣自己的护盾（因果抹杀令，B-14）。
    ///
    /// <para>和援护（<see cref="EffectKind.Cover"/>）是两回事：援护是把这次攻击**整份**
    /// 转移给援护者，分摊是拆成两份。两者互斥，一次攻击只能挑一种。</para>
    ///
    /// <para>不花 AP、不打牌——卡面写的是「可在响应时由相邻队友分摊」，
    /// 它是意图自带的一个选项，不是某张牌的效果。能不能分摊由意图的
    /// <see cref="EnemyIntent.Shareable"/> 决定。</para>
    /// </summary>
    public sealed class ShareDamageAction : IBattleAction
    {
        public ShareDamageAction(string actorId, string targetId = null)
        {
            ActorId = actorId;
            TargetId = targetId;
        }

        /// <summary>站出来分摊的那位（必须与原目标相邻）。</summary>
        public string ActorId { get; }

        /// <summary>替谁分摊。留空则取本次意图的首个目标。</summary>
        public string TargetId { get; }
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
