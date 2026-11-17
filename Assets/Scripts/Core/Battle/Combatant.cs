using System;

namespace TCGinFU.Core
{
    /// <summary>战场上的一个参战者，玩家角色与敌人的共同基类。</summary>
    public abstract class Combatant
    {
        protected Combatant(string id, string name, int maxHp)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            MaxHp = maxHp;
            Hp = maxHp;
        }

        public string Id { get; }
        public string Name { get; }

        public int MaxHp { get; protected set; }
        public int Hp { get; private set; }

        public Row Row { get; internal set; }

        /// <summary>排位槽（0–3）。决定 §4.3 的「正前方」与左右相邻关系。</summary>
        public int Slot { get; internal set; }

        public ShieldPool Shields { get; } = new();
        public StatusCollection Statuses { get; } = new();
        public AffinityTable Affinities { get; protected set; } = new();

        /// <summary>暴击抵抗线（§7.3）。</summary>
        public abstract int CritResist { get; }

        public bool IsDown => Hp <= 0;

        /// <summary>本轮造成的总伤害，用于前排空虚时的仇恨回退判定（§4.2）。</summary>
        public int DamageDealtThisRound { get; internal set; }

        public int DamageDealtLastRound { get; internal set; }

        /// <summary>扣血，返回实际损失的 HP。</summary>
        public int LoseHp(int amount)
        {
            if (amount <= 0) return 0;
            int before = Hp;
            Hp = Math.Max(0, Hp - amount);
            return before - Hp;
        }

        /// <summary>回血，返回实际恢复量。</summary>
        public int GainHp(int amount)
        {
            if (amount <= 0) return 0;
            int before = Hp;
            Hp = Math.Min(MaxHp, Hp + amount);
            return Hp - before;
        }

        internal void RollDamageStats()
        {
            DamageDealtLastRound = DamageDealtThisRound;
            DamageDealtThisRound = 0;
        }

        public override string ToString() => $"{Name}({Hp}/{MaxHp})";
    }
}
