using NUnit.Framework;
using TCGinFU.Core;

namespace TCGinFU.Core.Tests
{
    /// <summary>§7.3 暴击判定。</summary>
    public class CritResolverTests
    {
        private static readonly AttributeDice Dice =
            new(DieRank.D12, DieRank.D12, DieRank.D12, DieRank.D12);

        [Test]
        public void 单色牌点数达到抵抗线即暴击()
        {
            CritResult result = CritResolver.Roll(
                new ScriptedRng(4), Dice, new[] { AttributeColor.Might }, critResist: 4);

            Assert.IsTrue(result.IsCrit);
            Assert.IsFalse(result.IsSuperCrit);
        }

        [Test]
        public void 单色牌低于抵抗线不暴击()
        {
            CritResult result = CritResolver.Roll(
                new ScriptedRng(3), Dice, new[] { AttributeColor.Might }, critResist: 4);

            Assert.IsFalse(result.IsCrit);
        }

        [Test]
        public void 双色牌取较高的一颗比对()
        {
            CritResult result = CritResolver.Roll(
                new ScriptedRng(2, 7), Dice,
                new[] { AttributeColor.Might, AttributeColor.Insight }, critResist: 6);

            Assert.IsTrue(result.IsCrit);
            Assert.IsFalse(result.IsSuperCrit, "只有一颗达到抵抗线，不构成超暴击");
        }

        [Test]
        public void 双色牌两颗都达到抵抗线触发超暴击()
        {
            CritResult result = CritResolver.Roll(
                new ScriptedRng(6, 8), Dice,
                new[] { AttributeColor.Might, AttributeColor.Insight }, critResist: 6);

            Assert.IsTrue(result.IsSuperCrit);
            Assert.AreEqual((2, 1), result.Multiplier);
        }

        /// <summary>裁定 R-04：两颗同点且 ≥4 即使低于抵抗线也算超暴击。</summary>
        [Test]
        public void 双色牌同点且不低于四即使未达抵抗线也超暴击()
        {
            CritResult result = CritResolver.Roll(
                new ScriptedRng(5, 5), Dice,
                new[] { AttributeColor.Might, AttributeColor.Insight }, critResist: 7);

            Assert.IsTrue(result.IsSuperCrit);
        }

        [Test]
        public void 双色牌同点但小于四不触发超暴击()
        {
            CritResult result = CritResolver.Roll(
                new ScriptedRng(3, 3), Dice,
                new[] { AttributeColor.Might, AttributeColor.Insight }, critResist: 7);

            Assert.IsFalse(result.IsSuperCrit);
            Assert.IsFalse(result.IsCrit);
        }

        [Test]
        public void 默认暴击抵抗线按敌人级别()
        {
            Assert.AreEqual(4, EnemyRank.Normal.DefaultCritResist());
            Assert.AreEqual(6, EnemyRank.Elite.DefaultCritResist());
            Assert.AreEqual(7, EnemyRank.Boss.DefaultCritResist());
        }
    }

    /// <summary>§7.4 + 裁定 R-01/R-02/R-03/R-05/R-06：伤害管线。</summary>
    public class DamageResolverTests
    {
        private static Enemy Dummy(int hp = 100, AffinityTable affinities = null)
        {
            var enemy = new Enemy("e", "靶子", hp, EnemyRank.Normal);
            if (affinities != null) enemy.WithAffinities(affinities);
            return enemy;
        }

        [Test]
        public void 无修正时直接扣血()
        {
            Enemy target = Dummy();
            DamageResult result = DamageResolver.Resolve(null, target, 10, DamageElement.Physical, CritResult.None);

            Assert.AreEqual(10, result.HpLost);
            Assert.AreEqual(90, target.Hp);
        }

        [Test]
        public void 暴击一点五倍向下取整()
        {
            Enemy target = Dummy();
            var crit = new CritResult(true, false, new[] { 6 }, 4);
            DamageResult result = DamageResolver.Resolve(null, target, 9, DamageElement.Physical, crit);

            Assert.AreEqual(13, result.AfterCrit, "9 × 1.5 = 13.5 → 13");
        }

        [Test]
        public void 超暴击两倍()
        {
            Enemy target = Dummy();
            var crit = new CritResult(true, true, new[] { 6, 6 }, 4);
            DamageResult result = DamageResolver.Resolve(null, target, 9, DamageElement.Physical, crit);

            Assert.AreEqual(18, result.AfterCrit);
        }

        /// <summary>裁定 R-01：先暴击后克制，各自独立取整。</summary>
        [Test]
        public void 暴击在属性克制之前结算()
        {
            Enemy target = Dummy(affinities: new AffinityTable().Vulnerable(DamageElement.Fire));
            var crit = new CritResult(true, false, new[] { 6 }, 4);
            DamageResult result = DamageResolver.Resolve(null, target, 9, DamageElement.Fire, crit);

            Assert.AreEqual(13, result.AfterCrit, "⌊9 × 1.5⌋ = 13");
            Assert.AreEqual(26, result.AfterAffinity, "13 × 2 = 26（若顺序反过来会是 27）");
        }

        [Test]
        public void 抗性减半向下取整()
        {
            Enemy target = Dummy(affinities: new AffinityTable().Resistant(DamageElement.Ice));
            DamageResult result = DamageResolver.Resolve(null, target, 9, DamageElement.Ice, CritResult.None);

            Assert.AreEqual(4, result.AfterAffinity);
        }

        [Test]
        public void 免疫归零()
        {
            Enemy target = Dummy(affinities: new AffinityTable().Immune(DamageElement.Poison));
            DamageResult result = DamageResolver.Resolve(null, target, 50, DamageElement.Poison, CritResult.None);

            Assert.AreEqual(0, result.HpLost);
            Assert.AreEqual(100, target.Hp);
        }

        /// <summary>裁定 R-05：吸收按克制前的数值转治疗，不再乘 2。</summary>
        [Test]
        public void 吸收转为治疗且不乘倍()
        {
            Enemy target = Dummy(affinities: new AffinityTable().Absorb(DamageElement.Dark));
            target.LoseHp(50);

            DamageResult result = DamageResolver.Resolve(null, target, 20, DamageElement.Dark, CritResult.None);

            Assert.AreEqual(20, result.HealedInstead);
            Assert.AreEqual(70, target.Hp);
            Assert.AreEqual(0, result.HpLost);
        }

        /// <summary>裁定 R-02：物理属性吃物理盾，其余八属性吃魔法盾。</summary>
        [Test]
        public void 物理伤害只吃物理盾()
        {
            Enemy target = Dummy();
            target.Shields.Grant(ShieldKind.Physical, 6);
            target.Shields.Grant(ShieldKind.Magical, 99);

            DamageResult result = DamageResolver.Resolve(null, target, 10, DamageElement.Physical, CritResult.None);

            Assert.AreEqual(6, result.AbsorbedByShield);
            Assert.AreEqual(4, result.HpLost);
            Assert.AreEqual(99, target.Shields.Magical, "魔法盾不应被物理伤害消耗");
        }

        [Test]
        public void 火焰伤害吃魔法盾()
        {
            Enemy target = Dummy();
            target.Shields.Grant(ShieldKind.Magical, 6);

            DamageResult result = DamageResolver.Resolve(null, target, 10, DamageElement.Fire, CritResult.None);

            Assert.AreEqual(6, result.AbsorbedByShield);
            Assert.AreEqual(4, result.HpLost);
        }

        /// <summary>裁定 R-03：护盾扣的是克制之后的最终伤害。</summary>
        [Test]
        public void 护盾在属性克制之后抵消()
        {
            Enemy target = Dummy(affinities: new AffinityTable().Vulnerable(DamageElement.Fire));
            target.Shields.Grant(ShieldKind.Magical, 10);

            DamageResult result = DamageResolver.Resolve(null, target, 10, DamageElement.Fire, CritResult.None);

            Assert.AreEqual(20, result.AfterAffinity);
            Assert.AreEqual(10, result.AbsorbedByShield);
            Assert.AreEqual(10, result.HpLost);
        }

        /// <summary>裁定 R-06：先扣会在轮末过期的自动盾，再扣卡牌给予的护盾。</summary>
        [Test]
        public void 优先消耗自动盾再消耗卡牌护盾()
        {
            Enemy target = Dummy();
            target.Shields.RefreshAuto(3, 0);
            target.Shields.Grant(ShieldKind.Physical, 5);

            DamageResolver.Resolve(null, target, 4, DamageElement.Physical, CritResult.None);

            Assert.AreEqual(0, target.Shields.AutoPhysical);
            Assert.AreEqual(4, target.Shields.GrantedPhysical);
        }

        [Test]
        public void 自动盾轮末清零而卡牌护盾保留()
        {
            var pool = new ShieldPool();
            pool.RefreshAuto(3, 4);
            pool.Grant(ShieldKind.Physical, 5);

            pool.ClearAuto();

            Assert.AreEqual(5, pool.Physical);
            Assert.AreEqual(0, pool.Magical);
        }

        [Test]
        public void 跨过危机线会被标记()
        {
            PlayerCharacter player = TestFactory.Player(sheet: TestFactory.Sheet(level: 5, might: DieRank.D6));
            Assert.AreEqual(35, player.MaxHp);
            Assert.AreEqual(17, player.Sheet.CrisisThreshold);

            DamageResult first = DamageResolver.Resolve(null, player, 10, DamageElement.Physical, CritResult.None);
            Assert.IsFalse(first.TargetEnteredCrisis);
            Assert.IsFalse(player.IsInCrisis);

            DamageResult second = DamageResolver.Resolve(null, player, 10, DamageElement.Physical, CritResult.None);
            Assert.IsTrue(second.TargetEnteredCrisis);
            Assert.IsTrue(player.IsInCrisis);
        }

        /// <summary>裁定 R-07：危机是动态判定，治疗回线上即脱离。</summary>
        [Test]
        public void 治疗回线上即脱离危机()
        {
            PlayerCharacter player = TestFactory.Player(sheet: TestFactory.Sheet(level: 5, might: DieRank.D6));
            player.LoseHp(20);
            Assert.IsTrue(player.IsInCrisis);

            player.GainHp(10);
            Assert.IsFalse(player.IsInCrisis);
        }
    }
}
