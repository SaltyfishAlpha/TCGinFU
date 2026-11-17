namespace TCGinFU.Core
{
    /// <summary>装备牌的数值换算（规则书 §7.1 / §7.2）。</summary>
    public static class EquipMath
    {
        /// <summary>一阶武器牌基础伤害 = ⌊武器原固定伤害 ÷ 2⌋ + 3（§7.1）。</summary>
        public static int Tier1WeaponDamage(int fuFixedDamage) => fuFixedDamage / 2 + 3;

        /// <summary>阶梯补正：一阶 +0 / 二阶 +3 / 三阶 +6（§7.2，武器与防具同表）。</summary>
        public static int TierBonus(EquipTier tier) => ((int)tier - 1) * 3;
    }

    /// <summary>角色当前装备槽中的武器，为形态公式提供 B_atk 的武器项。</summary>
    public sealed class WeaponProfile
    {
        public WeaponProfile(string name, int tier1BaseDamage, DamageElement element = DamageElement.Physical, bool twoHanded = false)
        {
            Name = name;
            Tier1BaseDamage = tier1BaseDamage;
            Element = element;
            TwoHanded = twoHanded;
        }

        public string Name { get; }

        /// <summary>一阶基础伤害（3–10 点）。高阶补正在 <see cref="DamageAt"/> 里加。</summary>
        public int Tier1BaseDamage { get; }

        public DamageElement Element { get; }

        /// <summary>双手武器偏向高费高单发伤害，单手偏向低费高频率。</summary>
        public bool TwoHanded { get; }

        public int DamageAt(EquipTier tier) => Tier1BaseDamage + EquipMath.TierBonus(tier);

        /// <summary>
        /// 从一张武器牌定义建装备档案（§3.2：武器牌的卡池来源就是装备槽里的武器）。
        /// 这样「装备什么武器」和「卡面印多少伤害」只有一处定义，不会各记一份然后对不上。
        /// </summary>
        public static WeaponProfile FromCard(CardDefinition card)
        {
            if (card == null) throw new System.ArgumentNullException(nameof(card));
            if (card.Category != CardCategory.Weapon)
            {
                throw new System.ArgumentException($"「{card.Name}」不是武器牌", nameof(card));
            }

            return new WeaponProfile(card.Name, card.WeaponBaseDamage, card.Element, card.TwoHanded);
        }

        public static readonly WeaponProfile Unarmed = new("徒手", 3);
    }

    /// <summary>角色当前装备槽中的身体防具与盾牌，为形态公式提供 B_def 的防具项。</summary>
    public sealed class ArmorProfile
    {
        public ArmorProfile(string name, int tier1BaseShield, ShieldKind kind = ShieldKind.Physical)
        {
            Name = name;
            Tier1BaseShield = tier1BaseShield;
            Kind = kind;
        }

        public string Name { get; }

        /// <summary>一阶基础护盾（6–10 点）。</summary>
        public int Tier1BaseShield { get; }

        public ShieldKind Kind { get; }

        public int ShieldAt(EquipTier tier) => Tier1BaseShield + EquipMath.TierBonus(tier);

        /// <summary>从一张防具牌定义建装备档案（§3.2）。</summary>
        public static ArmorProfile FromCard(CardDefinition card)
        {
            if (card == null) throw new System.ArgumentNullException(nameof(card));
            if (card.Category != CardCategory.Armor)
            {
                throw new System.ArgumentException($"「{card.Name}」不是防具牌", nameof(card));
            }

            return new ArmorProfile(card.Name, card.ArmorBaseShield, card.ShieldKind);
        }

        public static readonly ArmorProfile Unarmored = new("无防具", 6);
    }
}
