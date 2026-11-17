using NUnit.Framework;
using TCGinFU.Core;

namespace TCGinFU.Core.Tests
{
    /// <summary>§2.1 面板派生公式。</summary>
    public class CharacterSheetTests
    {
        [Test]
        public void 最大HP等于总等级加力量骰面乘五()
        {
            CharacterSheet sheet = TestFactory.Sheet(level: 5, might: DieRank.D8);
            Assert.AreEqual(5 + 8 * 5, sheet.MaxHp);
        }

        [Test]
        public void 最大MP等于总等级加意志骰面乘五()
        {
            CharacterSheet sheet = TestFactory.Sheet(level: 20, willpower: DieRank.D10);
            Assert.AreEqual(20 + 10 * 5, sheet.MaxMp);
        }

        [Test]
        public void 危机值是最大HP的一半向下取整()
        {
            // 总等级 5 + 力量 d6 ×5 = 35 → 危机值 17
            CharacterSheet sheet = TestFactory.Sheet(level: 5, might: DieRank.D6);
            Assert.AreEqual(35, sheet.MaxHp);
            Assert.AreEqual(17, sheet.CrisisThreshold);
        }

        [Test]
        public void 自动双盾分别取敏捷与洞察骰面的一半()
        {
            CharacterSheet sheet = TestFactory.Sheet(dexterity: DieRank.D10, insight: DieRank.D12);
            Assert.AreEqual(5, sheet.AutoPhysicalShield);
            Assert.AreEqual(6, sheet.AutoMagicShield);
        }

        [TestCase(5, 1)]
        [TestCase(9, 1)]
        [TestCase(20, 4)]
        [TestCase(50, 10)]
        public void 等级加值是总等级除以五(int level, int expected)
        {
            Assert.AreEqual(expected, TestFactory.Sheet(level: level).LevelBonus);
        }

        [TestCase(5, EquipTier.Tier1)]
        [TestCase(19, EquipTier.Tier1)]
        [TestCase(20, EquipTier.Tier2)]
        [TestCase(39, EquipTier.Tier2)]
        [TestCase(40, EquipTier.Tier3)]
        [TestCase(50, EquipTier.Tier3)]
        public void 装备阶梯按二十与四十级跃迁(int level, EquipTier expected)
        {
            Assert.AreEqual(expected, TestFactory.Sheet(level: level).EquipTier);
        }

        [Test]
        public void 单职业等级上限为十且满十即精通()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ClassLevel("浪客", 11));
            Assert.IsTrue(new ClassLevel("浪客", 10).IsMastered);
            Assert.IsFalse(new ClassLevel("浪客", 9).IsMastered);
        }

        [Test]
        public void 总等级超过五十被拒绝()
        {
            var classes = new[]
            {
                new ClassLevel("A", 10), new ClassLevel("B", 10), new ClassLevel("C", 10),
                new ClassLevel("D", 10), new ClassLevel("E", 10), new ClassLevel("F", 10),
            };

            Assert.Throws<System.ArgumentException>(
                () => new CharacterSheet("超标", classes, AttributeDice.AllD6));
        }

        [Test]
        public void 属性骰升级机会在二十与四十级各获得一次()
        {
            Assert.AreEqual(0, CharacterProgression.EarnedDieUpgrades(19));
            Assert.AreEqual(1, CharacterProgression.EarnedDieUpgrades(20));
            Assert.AreEqual(2, CharacterProgression.EarnedDieUpgrades(40));
        }

        [Test]
        public void 属性骰升级封顶在d12()
        {
            AttributeDice dice = AttributeDice.AllD6
                .Upgraded(AttributeColor.Might)
                .Upgraded(AttributeColor.Might)
                .Upgraded(AttributeColor.Might)
                .Upgraded(AttributeColor.Might);

            Assert.AreEqual(DieRank.D12, dice.Might);
            Assert.IsFalse(dice.CanUpgrade(AttributeColor.Might));
        }

        [Test]
        public void 超额消费属性骰升级被拒绝()
        {
            AttributeDice current = AttributeDice.AllD6
                .Upgraded(AttributeColor.Might)
                .Upgraded(AttributeColor.Insight);

            Assert.IsFalse(CharacterProgression.ValidateDice(AttributeDice.AllD6, current, 20, out _));
            Assert.IsTrue(CharacterProgression.ValidateDice(AttributeDice.AllD6, current, 40, out _));
        }
    }
}
