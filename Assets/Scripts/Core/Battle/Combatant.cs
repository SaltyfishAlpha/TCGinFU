using System;
using System.Collections.Generic;

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

        /// <summary>
        /// 求值 <see cref="ValueExpr"/> 用的面板快照。
        ///
        /// <para>敌人没有武器与防具，所以默认是一组零值——
        /// 敌人的卡面本来就全是固定数值（老数据里一条公式都没有）。
        /// 真要给敌人做「随等级成长」的卡时再在这里补。</para>
        /// </summary>
        public virtual ValueContext ValueContext(int chargeSpent = 0)
            => new(0, 0, 0, 0, chargeSpent);

        public bool IsDown => Hp <= 0;

        /// <summary>本轮造成的总伤害，用于前排空虚时的仇恨回退判定（§4.2）。</summary>
        public int DamageDealtThisRound { get; internal set; }

        public int DamageDealtLastRound { get; internal set; }

        /// <summary>
        /// 本轮累计受到的 HP 伤害。老卡「若目标本轮已受到过伤害」这类前置条件要读它，
        /// 而「本轮」这个口径靠状态表达不了（状态最细也只到轮）。
        /// </summary>
        public int DamageTakenThisRound { get; internal set; }

        /// <summary>
        /// 本轮打出过几张牌。「若本轮已打出过其他牌」（瞬影突袭）这类前置条件要读它。
        /// <see cref="BattleStatKeys.CardsPlayed"/> 记的是整局累计，答不了「本轮」。
        /// </summary>
        public int CardsPlayedThisRound { get; internal set; }

        /// <summary>
        /// 本轮谁对我造成过伤害。「若本轮已有其他队友对该目标造成过伤害」（交叉火力网）
        /// 要的是**按来源区分**的口径，<see cref="DamageTakenThisRound"/> 只有总量答不了。
        /// </summary>
        public HashSet<string> DamagersThisRound { get; } = new(StringComparer.Ordinal);

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
            DamageTakenThisRound = 0;
            CardsPlayedThisRound = 0;
            DamagersThisRound.Clear();
        }

        public override string ToString() => $"{Name}({Hp}/{MaxHp})";
    }
}
