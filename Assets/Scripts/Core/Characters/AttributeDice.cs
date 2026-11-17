using System;

namespace TCGinFU.Core
{
    /// <summary>角色的四色属性骰（规则书 §2.1）。不可变；升级骰面产生新实例。</summary>
    public readonly struct AttributeDice : IEquatable<AttributeDice>
    {
        public DieRank Might { get; }
        public DieRank Insight { get; }
        public DieRank Dexterity { get; }
        public DieRank Willpower { get; }

        public AttributeDice(DieRank might, DieRank insight, DieRank dexterity, DieRank willpower)
        {
            Might = might;
            Insight = insight;
            Dexterity = dexterity;
            Willpower = willpower;
        }

        /// <summary>全 d6 的建卡起点。</summary>
        public static AttributeDice AllD6 => new(DieRank.D6, DieRank.D6, DieRank.D6, DieRank.D6);

        public DieRank this[AttributeColor color] => color switch
        {
            AttributeColor.Might => Might,
            AttributeColor.Insight => Insight,
            AttributeColor.Dexterity => Dexterity,
            AttributeColor.Willpower => Willpower,
            _ => throw new ArgumentOutOfRangeException(nameof(color)),
        };

        /// <summary>骰面数值（6/8/10/12），即规则书面板公式里用的那个数。</summary>
        public int Face(AttributeColor color) => (int)this[color];

        /// <summary>提升 1 个等级（d6→d8→d10→d12，封顶 d12）。</summary>
        public AttributeDice Upgraded(AttributeColor color)
        {
            DieRank next = this[color] switch
            {
                DieRank.D6 => DieRank.D8,
                DieRank.D8 => DieRank.D10,
                DieRank.D10 => DieRank.D12,
                _ => DieRank.D12,
            };

            return color switch
            {
                AttributeColor.Might => new AttributeDice(next, Insight, Dexterity, Willpower),
                AttributeColor.Insight => new AttributeDice(Might, next, Dexterity, Willpower),
                AttributeColor.Dexterity => new AttributeDice(Might, Insight, next, Willpower),
                AttributeColor.Willpower => new AttributeDice(Might, Insight, Dexterity, next),
                _ => throw new ArgumentOutOfRangeException(nameof(color)),
            };
        }

        public bool CanUpgrade(AttributeColor color) => this[color] != DieRank.D12;

        public bool Equals(AttributeDice other)
            => Might == other.Might && Insight == other.Insight
               && Dexterity == other.Dexterity && Willpower == other.Willpower;

        public override bool Equals(object obj) => obj is AttributeDice other && Equals(other);

        public override int GetHashCode()
            => ((int)Might << 24) ^ ((int)Insight << 16) ^ ((int)Dexterity << 8) ^ (int)Willpower;

        public override string ToString() => $"力d{(int)Might}/洞d{(int)Insight}/敏d{(int)Dexterity}/意d{(int)Willpower}";
    }
}
