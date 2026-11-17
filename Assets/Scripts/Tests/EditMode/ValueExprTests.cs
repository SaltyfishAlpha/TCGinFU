using NUnit.Framework;
using TCGinFU.Core;

namespace TCGinFU.Core.Tests
{
    /// <summary>§6.3 形态卡组的动态基准公式。</summary>
    public class ValueExprTests
    {
        [Test]
        public void 一阶武器伤害换算()
        {
            // ⌊武器原固定伤害 ÷ 2⌋ + 3
            Assert.AreEqual(8, EquipMath.Tier1WeaponDamage(10));
            Assert.AreEqual(8, EquipMath.Tier1WeaponDamage(11));
            Assert.AreEqual(3, EquipMath.Tier1WeaponDamage(0));
        }

        [Test]
        public void 阶梯补正为零三六()
        {
            Assert.AreEqual(0, EquipMath.TierBonus(EquipTier.Tier1));
            Assert.AreEqual(3, EquipMath.TierBonus(EquipTier.Tier2));
            Assert.AreEqual(6, EquipMath.TierBonus(EquipTier.Tier3));
        }

        /// <summary>规则书 §6.3 的示例：5 级持短弓（武器伤 7）的强攻牌 B_atk + 4 = 12。</summary>
        [Test]
        public void 规则书示例五级短弓造成十二点()
        {
            PlayerCharacter player = TestFactory.Player(
                sheet: TestFactory.Sheet(level: 5),
                weapon: new WeaponProfile("短弓", 7));

            Assert.AreEqual(EquipTier.Tier1, player.Tier);
            Assert.AreEqual(12, ValueExpr.FormAttack(4).Resolve(player.ValueContext()));
        }

        /// <summary>同一张卡在 50 级持三阶神器弓（一阶基础 7 → 三阶 13）时自动成长为 27 点。</summary>
        [Test]
        public void 规则书示例五十级神器弓造成二十七点()
        {
            PlayerCharacter player = TestFactory.Player(
                sheet: TestFactory.Sheet(level: 50),
                weapon: new WeaponProfile("神器弓", 7));

            Assert.AreEqual(EquipTier.Tier3, player.Tier);
            Assert.AreEqual(13, player.Weapon.DamageAt(player.Tier));
            Assert.AreEqual(10, player.Sheet.LevelBonus);
            Assert.AreEqual(27, ValueExpr.FormAttack(4).Resolve(player.ValueContext()));
        }

        [Test]
        public void 形态防御基准取防具护盾加等级加值()
        {
            PlayerCharacter player = TestFactory.Player(
                sheet: TestFactory.Sheet(level: 20),
                armor: new ArmorProfile("锁甲", 8));

            // 20 级 → 二阶，护盾 8+3=11；L_bonus = 4
            Assert.AreEqual(15, player.FormDefenseBase);
            Assert.AreEqual(17, ValueExpr.FormDefense(2).Resolve(player.ValueContext()));
        }

        [Test]
        public void 倍率除法向下取整()
        {
            var ctx = new ValueContext(weaponDamage: 7, armorShield: 0, levelBonus: 0, totalLevel: 0);
            Assert.AreEqual(3, new ValueExpr(ValueSource.WeaponDamage, add: 0, times: 1, dividedBy: 2).Resolve(ctx));
        }

        [Test]
        public void 充能数可作为取值来源()
        {
            var ctx = new ValueContext(0, 0, 0, 0, chargeSpent: 3);
            Assert.AreEqual(6, new ValueExpr(ValueSource.ChargeSpent, times: 2).Resolve(ctx));
        }
    }
}
