using System;
using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 角色面板（规则书 §2.1）。只存**输入**：职业等级表、四色骰、职业与秘宝加值。
    /// 最大 HP/MP、危机值、自动双盾、等级加值、装备阶梯全部是派生属性，不做缓存，
    /// 避免改一处忘记同步另一处。
    /// </summary>
    public sealed class CharacterSheet
    {
        public const int StartingLevel = 5;
        public const int MaxTotalLevel = 50;

        private readonly List<ClassLevel> _classes;

        public CharacterSheet(
            string name,
            IEnumerable<ClassLevel> classes,
            AttributeDice dice,
            int hpBonus = 0,
            int mpBonus = 0)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _classes = (classes ?? throw new ArgumentNullException(nameof(classes))).ToList();

            if (_classes.Count == 0)
            {
                throw new ArgumentException("角色至少要有 1 个职业", nameof(classes));
            }

            string duplicate = _classes.GroupBy(c => c.ClassId).FirstOrDefault(g => g.Count() > 1)?.Key;
            if (duplicate != null)
            {
                throw new ArgumentException($"职业 {duplicate} 重复出现", nameof(classes));
            }

            int total = _classes.Sum(c => c.Level);
            if (total > MaxTotalLevel)
            {
                throw new ArgumentException($"角色总等级 {total} 超过上限 {MaxTotalLevel}", nameof(classes));
            }

            Dice = dice;
            HpBonus = hpBonus;
            MpBonus = mpBonus;
        }

        public string Name { get; }
        public IReadOnlyList<ClassLevel> Classes => _classes;
        public AttributeDice Dice { get; }

        /// <summary>职业与秘宝提供的最大 HP 加值。</summary>
        public int HpBonus { get; }

        /// <summary>职业与秘宝提供的最大 MP 加值。</summary>
        public int MpBonus { get; }

        public int TotalLevel => _classes.Sum(c => c.Level);

        /// <summary>最大 HP = 总等级 + 力量骰面 × 5 + 加值</summary>
        public int MaxHp => TotalLevel + Dice.Face(AttributeColor.Might) * 5 + HpBonus;

        /// <summary>最大 MP = 总等级 + 意志骰面 × 5 + 加值</summary>
        public int MaxMp => TotalLevel + Dice.Face(AttributeColor.Willpower) * 5 + MpBonus;

        /// <summary>危机值 = ⌊最大 HP ÷ 2⌋</summary>
        public int CrisisThreshold => MaxHp / 2;

        /// <summary>每轮自动获得的物理护盾 = 敏捷骰面 ÷ 2</summary>
        public int AutoPhysicalShield => Dice.Face(AttributeColor.Dexterity) / 2;

        /// <summary>每轮自动获得的魔法护盾 = 洞察骰面 ÷ 2</summary>
        public int AutoMagicShield => Dice.Face(AttributeColor.Insight) / 2;

        /// <summary>形态卡组公式用的角色等级加值 L_bonus = ⌊总等级 ÷ 5⌋（规则书 §6.3）。</summary>
        public int LevelBonus => TotalLevel / 5;

        /// <summary>装备牌阶梯（规则书 §7.2）。</summary>
        public EquipTier EquipTier => TotalLevel >= 40 ? EquipTier.Tier3
            : TotalLevel >= 20 ? EquipTier.Tier2
            : EquipTier.Tier1;

        public bool HasClass(string classId) => _classes.Any(c => c.ClassId == classId);

        public int LevelOf(string classId)
            => _classes.FirstOrDefault(c => c.ClassId == classId).Level;

        /// <summary>已精通（10 级）的职业，每个提供 1 张英雄技能牌。</summary>
        public IEnumerable<string> MasteredClasses => _classes.Where(c => c.IsMastered).Select(c => c.ClassId);

        public override string ToString() => $"{Name}（总 {TotalLevel} 级，{Dice}）";
    }
}
