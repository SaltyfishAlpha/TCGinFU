using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>一段行为是以攻方还是守方身份被问到的。</summary>
    public enum AttackSide
    {
        Attacker,
        Defender,
    }

    /// <summary>一段常驻行为的来源，脚本可据此区分「我是持续牌还是状态」。</summary>
    public enum BehaviourSource
    {
        /// <summary>正在打出的这张牌自己</summary>
        Card,
        /// <summary>持续区的牌</summary>
        Continuous,
        /// <summary>升变期间的升变牌</summary>
        Transform,
        /// <summary>挂在角色身上的状态</summary>
        Status,
    }

    /// <summary>
    /// 一次攻击的**可变**结算单。在真正扣血之前，它会依次经过攻方与守方所有常驻行为之手，
    /// 每一方都可以加减伤害、改属性、无视护盾、乃至整个取消。
    ///
    /// <para>为什么需要它：老引擎里「下一次攻击+3」「本轮物理转风属性」「无视2点物理护盾」
    /// 「完全闪避」这类词条有十几种，如果每条都在 <see cref="BattleEngine.DealDamage"/> 里特判，
    /// 伤害管线会变成一坨 if；而且玩家自定义卡牌根本无法参与。
    /// 把它做成一个过手的可变对象，脚本就和内置逻辑站在同一个位置上。</para>
    /// </summary>
    public sealed class AttackContext
    {
        public AttackContext(Combatant source, Combatant target, int baseAmount, DamageElement element, CritResult crit)
        {
            Source = source;
            Target = target;
            BaseAmount = baseAmount;
            Element = element;
            Crit = crit;
        }

        /// <summary>伤害来源。轮末持续伤害等无主伤害为 null。</summary>
        public Combatant Source { get; }

        public Combatant Target { get; internal set; }

        /// <summary>玩家出牌造成的伤害会带上这张牌；敌人意图与持续伤害为 null。</summary>
        public CardInstance Card { get; init; }

        /// <summary>敌人意图造成的伤害会带上这条意图；玩家出牌为 null。</summary>
        public EnemyIntent Intent { get; init; }

        /// <summary>
        /// 这是一次**分摊**拆出来的半份，值为被分摊的那位（B-14）；不是分摊则为 null。
        ///
        /// <para>两个半份各自走一遍完整攻击管线，所以「分别抵扣护盾」是自然结果而不是特例。
        /// 命中后附带的状态只跟原目标那一半走——分摊的是伤害，不是整条意图。</para>
        /// </summary>
        public Combatant SharedFrom { get; init; }

        public bool IsSharedHalf => SharedFrom != null;

        /// <summary>卡面/意图写的基础值。</summary>
        public int BaseAmount { get; set; }

        /// <summary>各路增伤累加（可为负）。「下一次攻击+3」这类词条写这里。</summary>
        public int FlatBonus { get; set; }

        /// <summary>被抵消的点数。时机牌「仍为目标抵消6点伤害」写这里。</summary>
        public int Reduction { get; set; }

        public DamageElement Element { get; set; }

        /// <summary>暴击结果。时机牌可以把它抹成 <see cref="CritResult.None"/>（老卡「时间悖论」）。</summary>
        public CritResult Crit { get; set; }

        /// <summary>无视目标多少点护盾（老卡「鹰眼锁定」无视 2 点物理护盾）。</summary>
        public int IgnoreShield { get; set; }

        /// <summary>完全闪避：本次攻击不发生，不结算伤害也不触发命中后效果。</summary>
        public bool Avoided { get; set; }

        /// <summary>
        /// 改写暴击倍率，分数形式。null 表示用 <see cref="CritResult.Multiplier"/> 的默认值。
        /// <para>老卡「幻影夜行」升变期间暴击倍率 ×1.75 → 写 (7, 4)。
        /// 用分数而不是 float 是为了让「×1.75 再向下取整」在任何平台上都是同一个整数。</para>
        /// </summary>
        public (int Numerator, int Denominator)? CritMultiplier { get; set; }

        /// <summary>脚本之间传临时标记用（同一次攻击内有效）。</summary>
        public Dictionary<string, int> Scratch { get; } = new();

        /// <summary>送进伤害管线的最终基础值。</summary>
        public int FinalAmount
        {
            get
            {
                int value = BaseAmount + FlatBonus - Reduction;
                return value < 0 ? 0 : value;
            }
        }

        /// <summary>结算完成后回填，供「命中后」类效果读取实际战果。闪避时为 null。</summary>
        public DamageResult Result { get; internal set; }

        public bool IsFromCard => Card != null;
        public bool IsFromIntent => Intent != null;

        public override string ToString()
            => $"{Source?.Name ?? "无主"}→{Target?.Name}：{BaseAmount}{(FlatBonus != 0 ? $"{(FlatBonus > 0 ? "+" : "")}{FlatBonus}" : "")}"
               + $"{(Reduction != 0 ? $"-{Reduction}" : "")}={FinalAmount} {Element}{(Avoided ? " [闪避]" : "")}";
    }

    /// <summary>
    /// 一次暴击掷骰的**可变**结果。骰子已经投完，但还没判定成不成——
    /// 中间这一步是给「掷骰结果+1」「把一颗骰视为 7」「暴击抵抗-1」这类词条用的。
    ///
    /// <para>为什么不能等判定完再改：暴击与超暴击是由骰值和抵抗线共同决定的，
    /// 拿到布尔结果之后再改就只能硬掰成 true/false，双色牌的超暴击规则直接丢失。</para>
    /// </summary>
    public sealed class CritRollContext
    {
        private readonly List<int> _rolls;

        internal CritRollContext(CritResult rolled, CardDefinition card, Combatant target, int chargeSpent = 0)
        {
            _rolls = new List<int>(rolled.Rolls);
            CritResist = rolled.CritResist;
            Card = card;
            Target = target;
            DualColor = rolled.Rolls.Count >= 2;
            ChargeSpent = chargeSpent;
        }

        /// <summary>投出的骰值，可直接改（下标从 0 起，与卡面「第 1 颗骰」差 1）。</summary>
        public IList<int> Rolls => _rolls;

        /// <summary>给每颗骰的统一加成。「所有【暴击】掷骰结果+1」写这里。</summary>
        public int RollBonus { get; set; }

        /// <summary>对暴击抵抗线的修正，负数等于更容易暴击。</summary>
        public int ResistDelta { get; set; }

        /// <summary>
        /// 「本次攻击**必定**暴击」（暗夜剔骨消耗充能时）。
        ///
        /// <para>只能把结果抬成暴击，抬不成超暴击——超暴击是双色两颗骰的关系判出来的，
        /// 凭一个布尔开关造出来会把 §7.3 那套规则绕过去。骰值本身也不改：
        /// 战报里该让玩家看见真实点数，而不是一串被篡改过的数字。</para>
        /// </summary>
        public bool ForceCrit { get; set; }

        /// <summary>本次打出消耗掉的充能数。「若消耗1个充能指示物，本次攻击必定暴击」要读它。</summary>
        public int ChargeSpent { get; }

        public int CritResist { get; }
        public CardDefinition Card { get; }
        public Combatant Target { get; }
        public bool DualColor { get; }

        /// <summary>按改完之后的骰值与抵抗线重新判定。</summary>
        internal CritResult Evaluate()
        {
            var adjusted = new List<int>(_rolls.Count);
            foreach (int roll in _rolls) adjusted.Add(roll + RollBonus);

            CritResult result = CritResolver.Evaluate(adjusted, CritResist + ResistDelta, DualColor);
            if (!ForceCrit || result.IsCrit) return result;

            return new CritResult(true, result.IsSuperCrit, result.Rolls, result.CritResist);
        }
    }

    /// <summary>
    /// 一次出牌的**可变**费用。在校验之前过手，用于「免除 N MP」「下一张某类牌 -5 MP」这类词条。
    ///
    /// <para><b>改它的钩子必须无副作用。</b>费用要在校验阶段算出来（还要给 UI 显示真实费用），
    /// 这意味着同一张牌的 <c>ModifyCost</c> 可能被调用多次、而且可能最终并没有打出去。
    /// 「每轮限用一次的折扣」这种要记账的，请在 <see cref="BattleTrigger.CardPlayed"/>
    /// 里挂个状态来记，而不是在这里改状态。</para>
    /// </summary>
    public sealed class CostContext
    {
        internal CostContext(PlayerCharacter caster, CardDefinition card, int round)
        {
            Caster = caster;
            Card = card;
            Round = round;
            Ap = card.ApCost;
            Mp = card.MpCost;
            Charge = card.ChargeCost;
        }

        public PlayerCharacter Caster { get; }
        public CardDefinition Card { get; }
        public int Round { get; }

        private int _ap;
        private int _mp;
        private int _charge;

        /// <summary>费用不会低于 0——「免除 5 MP」的牌本来只要 3 MP 时不该倒找玩家。</summary>
        public int Ap { get => _ap; set => _ap = value < 0 ? 0 : value; }
        public int Mp { get => _mp; set => _mp = value < 0 ? 0 : value; }
        public int Charge { get => _charge; set => _charge = value < 0 ? 0 : value; }

        public override string ToString() => $"{Ap}AP / {Mp}MP / {Charge}充能";
    }

    /// <summary>充能指示物从哪个区来。</summary>
    public enum ChargeSource
    {
        /// <summary>弃牌堆顶</summary>
        Discard,
        /// <summary>牌库顶（盲贴）</summary>
        DeckTop,
        /// <summary>手牌最后一张</summary>
        HandBottom,
    }

    /// <summary>
    /// 一次「消耗充能」的可变结算单。牌已经离开充能区、还没进弃牌堆的那一刻。
    ///
    /// <para>老卡「真理炼金矩阵」：全队消耗充能时投骰，偶数则该充能**不进弃牌堆、继续留在充能区**。
    /// 这个时点没法用别的钩子代替——<see cref="BattleTrigger.ChargeSpent"/> 广播时
    /// 牌已经躺在弃牌堆里了，再捞回来就是两次移动，事件流也对不上。</para>
    /// </summary>
    public sealed class ChargeSpendContext
    {
        internal ChargeSpendContext(PlayerCharacter player, ChargeToken token, ChargeUse use)
        {
            Player = player;
            Token = token;
            Use = use;
        }

        /// <summary>消耗这个充能的人。</summary>
        public PlayerCharacter Player { get; }

        public ChargeToken Token { get; }
        public ChargeUse Use { get; }

        /// <summary>置 true 则该充能留在充能区，不进弃牌堆。</summary>
        public bool Retained { get; set; }
    }

    /// <summary>引擎在固定位置广播的时点。脚本订阅这些时点来实现「每当……时」类词条。</summary>
    public enum BattleTrigger
    {
        /// <summary>宿主受到了伤害（闪避不触发）</summary>
        DamageTaken,

        /// <summary>
        /// 宿主身上一次攻击结算完毕。与 <see cref="DamageTaken"/> 的区别是它
        /// **掉 0 血也照样广播**——「若完全抵消本次攻击伤害则反击」（风墙反制）
        /// 恰恰只在没掉血时才成立，挂在 DamageTaken 上永远等不到。
        /// </summary>
        AttackResolved,

        /// <summary>宿主造成了伤害</summary>
        DamageDealt,

        /// <summary>宿主造成的伤害触发了暴击</summary>
        CritDealt,

        /// <summary>宿主打出了一张牌（费用已付、效果已结算）</summary>
        CardPlayed,

        /// <summary>宿主消耗了一个充能</summary>
        ChargeSpent,

        /// <summary>轮始（§8 ①，意图揭示之前）</summary>
        RoundStart,

        /// <summary>轮末（§8 ⑤，持续伤害结算之后、状态递减之前）</summary>
        RoundEnd,
    }

    /// <summary>一次触发广播的上下文。</summary>
    public sealed class TriggerContext
    {
        public TriggerContext(BattleEngine engine, BattleTrigger trigger, Combatant host)
        {
            Engine = engine;
            Trigger = trigger;
            Host = host;
        }

        public BattleEngine Engine { get; }
        public BattleState State => Engine.State;
        public BattleTrigger Trigger { get; }

        /// <summary>
        /// 这段行为挂在谁身上——也就是「你」。可能是敌人：
        /// 敌人身上的脚本化状态（「装甲重构」）也要能收到轮末这类时点。
        /// </summary>
        public Combatant Host { get; }

        /// <summary>这段行为的来源（持续牌 / 升变牌 / 状态）。</summary>
        public BehaviourSource Source { get; internal set; }

        /// <summary>事件的另一方：受伤时是伤害来源，造成伤害时是挨打的那个。</summary>
        public Combatant Other { get; init; }

        /// <summary>事件相关的数值：伤害时为实际扣除的 HP。</summary>
        public int Amount { get; init; }

        /// <summary>伤害类触发的完整分解，非伤害触发为 null。</summary>
        public DamageResult Damage { get; init; }

        /// <summary>相关卡牌（打牌触发、消耗充能触发）。</summary>
        public CardInstance Card { get; init; }

        public override string ToString() => $"{Trigger}@{Host?.Name}（{Source}）";
    }
}
