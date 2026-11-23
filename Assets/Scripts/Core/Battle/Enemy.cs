using System;
using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>敌人意图的指向方式（§4.2）。</summary>
    public enum IntentTargeting
    {
        /// <summary>常规单体：默认优先前排；前排无人则打后排上一轮输出最高者</summary>
        DefaultSingle,
        FrontRowAoe,
        BackRowAoe,
        AllPlayers,
        /// <summary>指定某排单体死刑重击</summary>
        DesignatedRowSingle,
        /// <summary>不指向玩家（自强化、召唤等）</summary>
        SelfOrNone,

        /// <summary>当前 HP 最高者（老 Boss 的「因果抹杀令」）</summary>
        HighestHpSingle,
    }

    /// <summary>敌人本轮揭示的一条行动意图（§8 ① / ④）。</summary>
    public sealed class EnemyIntent
    {
        public EnemyIntent(string label, IntentTargeting targeting)
        {
            Label = label;
            Targeting = targeting;
        }

        public string Label { get; }
        public IntentTargeting Targeting { get; }

        /// <summary>伤害值。0 表示该意图不造成伤害。</summary>
        public int Damage { get; init; }

        public DamageElement Element { get; init; } = DamageElement.Physical;

        /// <summary>Targeting 为 DesignatedRowSingle / FrontRowAoe / BackRowAoe 时指定的排。</summary>
        public Row DesignatedRow { get; init; } = Row.Front;

        /// <summary>命中后给目标挂的状态（可空）。</summary>
        public StatusEffect InflictedStatus { get; init; }

        /// <summary>
        /// 允许相邻队友在响应窗口里**分摊**这次攻击：伤害对半拆，两人各自抵扣护盾
        /// （因果抹杀令「可在响应时由相邻队友分摊」）。见 <see cref="ShareDamageAction"/>。
        ///
        /// <para>做成意图上的一个数据开关而不是行为钩子：卡面那半句没有任何额外效果，
        /// 它只是在响应窗口里**多给玩家一个选项**，而响应窗口开在行为钩子之前，
        /// 挂钩子的话等脚本被问到时窗口已经关了。</para>
        /// </summary>
        public bool Shareable { get; init; }

        /// <summary>
        /// 这条意图要打出的那张**敌人的牌**。
        ///
        /// <para>敌人的意图不是另一套机制——它就是敌人手里一套玩家看不到的牌，
        /// 按阶段从备选池里挑一张宣告出来。所以这里直接挂 <see cref="CardDefinition"/>，
        /// 结算时走和玩家出牌**完全相同**的那条管线：同样的 <see cref="EffectData"/>、
        /// 同样的 <see cref="ICardBehaviour"/> 钩子、同样的伤害管线。</para>
        ///
        /// <para>这么做之前我给意图单独加过一套 on_intent_* 钩子，那是错的抽象：
        /// 数据驱动效果、状态、触发器全都要再实现一遍，mod 作者还得学两套 API。</para>
        /// </summary>
        public CardDefinition Card { get; init; }

        /// <summary>意图直接复用卡的行为 Id。</summary>
        public string BehaviourId => Card?.BehaviourId;

        /// <summary>打出这张牌要花的 MP。敌人不付 AP（裁定：敌人每轮只出一条意图）。</summary>
        public int MpCost => Card?.MpCost ?? 0;

        /// <summary>
        /// 目标是否在**宣告时**就锁定（裁定 R-24）。
        ///
        /// <para>分界线在意图文本：</para>
        /// <list type="bullet">
        /// <item>「**选择/随机选择一个**位于后排的角色，攻击」——已经选定了具体某个人，
        /// 他之后走到哪都算数，响应窗口里移动躲不掉。</item>
        /// <item>「攻击**前排角色**」——指的是「结算时站在前排的那些人」，
        /// 所以在响应窗口里挪出前排就是真的躲开了，挪进来的也会被打到。</item>
        /// </list>
        ///
        /// <para>不显式指定时按 <see cref="Targeting"/> 推导：单体指向锁定，范围指向不锁定。
        /// 自定义内容可以覆盖它。</para>
        /// </summary>
        public bool? LocksTargetOverride { get; init; }

        public bool LocksTarget => LocksTargetOverride
            ?? Targeting is IntentTargeting.DefaultSingle or IntentTargeting.DesignatedRowSingle;

        public override string ToString() => $"{Label}（{Targeting}{(Damage > 0 ? $" {Damage}点{Element}" : "")}）";
    }

    public sealed class Enemy : Combatant
    {
        private readonly List<EnemyIntent> _intents = new();

        public Enemy(string id, string name, int maxHp, EnemyRank rank, int? critResist = null, int maxMp = 0)
            : base(id, name, maxHp)
        {
            Rank = rank;
            CritResist = critResist ?? rank.DefaultCritResist();
            MaxMp = maxMp;
            Mp = maxMp;
        }

        public EnemyRank Rank { get; }

        /// <summary>
        /// 敌人也有 MP：老数据里「以太虹吸」「相位折跃」都标着消耗 10 MP，
        /// 而它们恰好是最强的两条——读起来就是「放大招要蓄力」。
        /// MP 不够时这条意图出不了，决策层会改挑别的。
        /// </summary>
        public int MaxMp { get; }

        public int Mp { get; internal set; }

        /// <summary>这条意图此刻出不出得起。</summary>
        public bool CanAfford(EnemyIntent intent) => intent == null || Mp >= intent.MpCost;

        public override int CritResist { get; }

        /// <summary>意图可见度（§7.3）：小怪完全公开，精英半公开（隐藏数值与目标）。</summary>
        public bool IntentFullyVisible => Rank == EnemyRank.Normal;

        /// <summary>本轮揭示的意图，按顺序自上而下结算。</summary>
        public IReadOnlyList<EnemyIntent> Intents => _intents;

        /// <summary>意图生成器。由具体敌人 AI 提供；为 null 时不行动。</summary>
        public Func<Enemy, BattleState, IEnumerable<EnemyIntent>> IntentPlanner { get; set; }

        public Enemy WithAffinities(AffinityTable table)
        {
            Affinities = table ?? new AffinityTable();
            return this;
        }

        internal void RevealIntents(BattleState state)
        {
            _intents.Clear();
            if (IntentPlanner == null || IsDown) return;
            _intents.AddRange(IntentPlanner(this, state));
        }

        internal void ClearIntents() => _intents.Clear();
    }
}
