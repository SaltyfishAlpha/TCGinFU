using System;

namespace TCGinFU.Core
{
    /// <summary>
    /// 一名玩家角色。HP/MP 跨战斗保留，战斗开始与结束均不自动回满（§1），
    /// 所以构造时要显式传入跑团带进来的当前 HP/MP。
    /// </summary>
    public sealed class PlayerCharacter : Combatant
    {
        public const int ApPerRound = 3;

        public PlayerCharacter(
            string id,
            CharacterSheet sheet,
            int chargeCapacity,
            int? currentHp = null,
            int? currentMp = null,
            WeaponProfile weapon = null,
            ArmorProfile armor = null)
            : base(id, sheet.Name, sheet.MaxHp)
        {
            Sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            Zones = new PlayerZones(chargeCapacity);
            Weapon = weapon ?? WeaponProfile.Unarmed;
            Armor = armor ?? ArmorProfile.Unarmored;

            MaxMp = sheet.MaxMp;
            Mp = currentMp ?? sheet.MaxMp;

            if (currentHp.HasValue)
            {
                LoseHp(MaxHp - Math.Clamp(currentHp.Value, 0, MaxHp));
            }
        }

        public CharacterSheet Sheet { get; }
        public PlayerZones Zones { get; }
        public WeaponProfile Weapon { get; set; }
        public ArmorProfile Armor { get; set; }

        public int MaxMp { get; }
        public int Mp { get; private set; }

        /// <summary>本轮剩余行动力。轮始直接赋值为 3，不累加（裁定 R-10）。</summary>
        public int Ap { get; internal set; }

        public TransformState Transform { get; } = new();

        /// <summary>本轮是否已用掉那唯一一次贴充能机会（§5.2，裁定 R-08）。</summary>
        public bool HasPlacedChargeThisRound { get; internal set; }

        /// <summary>本轮是否已走位（裁定 R-16）。</summary>
        public bool HasMovedThisRound { get; internal set; }

        /// <summary>玩家阶段是否已宣布行动完毕。</summary>
        public bool IsReady { get; internal set; }

        public override int CritResist => 0;

        /// <summary>HP 降至危机值或以下即进入危机状态，实时判定（裁定 R-07）。</summary>
        public bool IsInCrisis => Hp <= Sheet.CrisisThreshold;

        public EquipTier Tier => Sheet.EquipTier;

        /// <summary>形态攻击基准 B_atk = 当前武器基础伤害 + L_bonus（§6.3）。</summary>
        public int FormAttackBase => Weapon.DamageAt(Tier) + Sheet.LevelBonus;

        /// <summary>形态防御基准 B_def = 当前防具基础护盾 + L_bonus（§6.3）。</summary>
        public int FormDefenseBase => Armor.ShieldAt(Tier) + Sheet.LevelBonus;

        public ValueContext ValueContext(int chargeSpent = 0)
            => new(Weapon.DamageAt(Tier), Armor.ShieldAt(Tier), Sheet.LevelBonus, Sheet.TotalLevel, chargeSpent);

        public bool SpendMp(int amount)
        {
            if (amount <= 0) return true;
            if (Mp < amount) return false;
            Mp -= amount;
            return true;
        }

        public int RestoreMp(int amount)
        {
            if (amount <= 0) return 0;
            int before = Mp;
            Mp = Math.Min(MaxMp, Mp + amount);
            return Mp - before;
        }

        internal void BeginRound()
        {
            Ap = ApPerRound;
            HasPlacedChargeThisRound = false;
            HasMovedThisRound = false;
            IsReady = false;
            Shields.RefreshAuto(Sheet.AutoPhysicalShield, Sheet.AutoMagicShield);
        }

        internal void EndRound()
        {
            Ap = 0;
            Shields.ClearAuto();
            RollDamageStats();
        }
    }
}
