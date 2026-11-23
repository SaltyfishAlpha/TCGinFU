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

        /// <param name="chargeCapacity">
        /// 临时值即可：<see cref="BattleSetup.Create"/> 会按全队实际人数重设成 max(人数-1, 2)。
        /// </param>
        public PlayerCharacter(
            string id,
            CharacterSheet sheet,
            int chargeCapacity = 2,
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

            // 和 HP 一样夹到上限。不夹的话传个超额的 currentMp 进来，
            // 角色会一直顶在上限之上，而「回复 10 MP」却只回得了 0——
            // 表现出来是「这张回蓝牌没生效」，查起来会怀疑到卡牌身上
            Mp = Math.Clamp(currentMp ?? sheet.MaxMp, 0, MaxMp);

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

        /// <summary>
        /// 本轮额外的贴充能次数（老卡「真理炼金矩阵」：升变期间每轮可贴 2 次）。
        /// 常规那一次用完后才消耗这里，轮始重置。
        /// </summary>
        public int ExtraChargePlacements { get; internal set; }

        /// <summary>
        /// 充能区上限的临时加成（老卡「奥术结界」）。不随轮次清零——
        /// 卡面说的是「本轮及下一轮」，什么时候收回由发它的那张卡自己负责。
        /// </summary>
        public int ChargeCapacityBonus { get; internal set; }

        /// <summary>本轮是否已走位（裁定 R-16）。</summary>
        public bool HasMovedThisRound { get; internal set; }

        /// <summary>
        /// 攒着的「免费移动」次数。卡面写「**可**免费移动1次」时存在这里，
        /// 由玩家自己决定何时兑现——不花 AP，也不占本轮唯一一次常规走位。
        /// </summary>
        public int FreeMoves { get; internal set; }

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

        public override ValueContext ValueContext(int chargeSpent = 0)
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
            ExtraChargePlacements = 0;
            HasMovedThisRound = false;
            IsReady = false;
            Shields.RefreshAuto(Sheet.AutoPhysicalShield, Sheet.AutoMagicShield);
        }

        internal void EndRound()
        {
            Ap = 0;
            // 没用掉的免费移动不留到下一轮——卡面给的是「本轮」的一次机动
            FreeMoves = 0;
            Shields.ClearAuto();
            RollDamageStats();
        }
    }
}
