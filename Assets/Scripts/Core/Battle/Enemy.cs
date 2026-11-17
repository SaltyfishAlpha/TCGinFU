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

        public override string ToString() => $"{Label}（{Targeting}{(Damage > 0 ? $" {Damage}点{Element}" : "")}）";
    }

    public sealed class Enemy : Combatant
    {
        private readonly List<EnemyIntent> _intents = new();

        public Enemy(string id, string name, int maxHp, EnemyRank rank, int? critResist = null)
            : base(id, name, maxHp)
        {
            Rank = rank;
            CritResist = critResist ?? rank.DefaultCritResist();
        }

        public EnemyRank Rank { get; }

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
