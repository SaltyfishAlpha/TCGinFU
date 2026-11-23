using System.Linq;
using NUnit.Framework;
using TCGinFU.Core;

namespace TCGinFU.Core.Tests
{
    /// <summary>
    /// 数据驱动卡牌词条的规则，用最小构造卡验证。
    ///
    /// <para>这些规则原先挂在 C# 样例卡组上（`SampleDeckIntegrationTests`）。样例内容移除后，
    /// 守的仍是同一批**引擎规则**，只是改用 <see cref="TestFactory"/> 就地造卡，不再依赖任何成套内容。
    /// 「整套内容接上去能不能跑完一局」那一层改由内容包测试负责（`LegacyContentTests` / `LuaBehaviourTests`）。</para>
    /// </summary>
    public class CardEffectRuleTests
    {
        private const string Bleed = "test_bleed";

        /// <summary>一名玩家 + 一只 100 HP 木桩，带上注册过流血的状态表。</summary>
        private static (BattleEngine engine, PlayerCharacter player, Enemy enemy) Duel(
            int seed = 20251117, WeaponProfile weapon = null)
        {
            PlayerCharacter player = TestFactory.Player(weapon: weapon);
            Enemy enemy = TestFactory.Enemy(hp: 100);
            TestFactory.FillDeck(player, 20);

            var statuses = new StatusRegistry();
            statuses.Register(new StatusDefinition(Bleed)
            {
                DamagePerRound = 3,
                DotElement = DamageElement.Physical,
            });

            BattleState state = BattleSetup.Create(
                new XorShift32Rng(seed),
                new[] { (player, Row.Front) },
                new[] { (enemy, Row.Front) });

            var engine = new BattleEngine(state, EffectRegistry.CreateDefault(), null, statuses);
            engine.Start();
            return (engine, player, enemy);
        }

        // ───────────────────────── 装备档案 ─────────────────────────

        [Test]
        public void 装备档案由武器牌与防具牌派生()
        {
            var bow = new CardDefinition("bow", "短弓", CardCategory.Weapon, CardKind.Skill)
            {
                WeaponBaseDamage = 7,
                TwoHanded = true,
            };
            var armor = new CardDefinition("armor", "皮甲", CardCategory.Armor, CardKind.Skill)
            {
                ArmorBaseShield = 6,
            };

            Assert.AreEqual(7, WeaponProfile.FromCard(bow).Tier1BaseDamage);
            Assert.AreEqual(6, ArmorProfile.FromCard(armor).Tier1BaseShield);

            // 类别搞错时必须炸出来：默默派生出一把 0 伤害的武器更难查
            Assert.Throws<System.ArgumentException>(
                () => WeaponProfile.FromCard(armor), "防具牌不能当武器装备");
            Assert.Throws<System.ArgumentException>(() => ArmorProfile.FromCard(bow));
        }

        [Test]
        public void 武器牌伤害跟当前装备槽走()
        {
            var shot = new CardDefinition("shot", "速射", CardCategory.Weapon, CardKind.Skill)
            {
                ApCost = 1,
                Colors = new[] { AttributeColor.Dexterity },
                Effects = new[] { EffectData.Damage(new ValueExpr(ValueSource.WeaponDamage)) },
            };

            // 同一张牌，换把弓就该自动变强——伤害取的是装备槽而不是卡面写死的数
            (BattleEngine weak, PlayerCharacter p1, Enemy e1) = Duel(weapon: new WeaponProfile("短弓", 7));
            CardInstance c1 = TestFactory.AddToHand(p1, shot);
            Assert.IsTrue(weak.Submit(new PlayCardAction(p1.Id, c1.Uid, e1.Id)).Ok);
            Assert.AreEqual(7, e1.MaxHp - e1.Hp);

            (BattleEngine strong, PlayerCharacter p2, Enemy e2) = Duel(weapon: new WeaponProfile("神射弓", 12));
            CardInstance c2 = TestFactory.AddToHand(p2, shot);
            Assert.IsTrue(strong.Submit(new PlayCardAction(p2.Id, c2.Uid, e2.Id)).Ok);
            Assert.AreEqual(12, e2.MaxHp - e2.Hp);
        }

        // ───────────────────────── 条件词条 ─────────────────────────

        [Test]
        public void 危机条件词条只在危机状态生效()
        {
            CardDefinition slash = Conditional(EffectCondition.IfCasterInCrisis);

            (BattleEngine healthy, PlayerCharacter p1, Enemy e1) = Duel();
            CardInstance c1 = TestFactory.AddToHand(p1, slash);
            healthy.Submit(new PlayCardAction(p1.Id, c1.Uid, e1.Id));
            Assert.AreEqual(6, e1.MaxHp - e1.Hp, "非危机只结算第一段");

            (BattleEngine dying, PlayerCharacter p2, Enemy e2) = Duel();
            p2.LoseHp(p2.MaxHp - 1);
            Assert.IsTrue(p2.IsInCrisis);

            CardInstance c2 = TestFactory.AddToHand(p2, slash);
            dying.Submit(new PlayCardAction(p2.Id, c2.Uid, e2.Id));
            Assert.AreEqual(13, e2.MaxHp - e2.Hp, "危机时追加第二段 7 点");
        }

        /// <summary>第一段无条件 6 点，第二段带条件 7 点。</summary>
        private static CardDefinition Conditional(EffectCondition condition)
            => new("conditional", "条件牌", CardCategory.Class, CardKind.Skill)
            {
                ApCost = 1,
                Colors = new[] { AttributeColor.Might },
                Effects = new[]
                {
                    EffectData.Damage(ValueExpr.Flat(6)),
                    new EffectData
                    {
                        Kind = EffectKind.Damage,
                        Amount = ValueExpr.Flat(7),
                        Target = TargetSelector.SingleEnemy,
                        Condition = condition,
                    },
                },
            };

        // ───────────────────────── 充能词条 ─────────────────────────

        /// <summary>卡面 ChargeCost 1 → ChargeSpent = 1 → 1×5 + 4 = 9 点雷伤。</summary>
        private static CardDefinition Overcharge()
            => new("overcharge", "超载", CardCategory.Class, CardKind.Skill)
            {
                ApCost = 1,
                ChargeCost = 1,
                Colors = new[] { AttributeColor.Willpower },
                Effects = new[]
                {
                    EffectData.Damage(
                        new ValueExpr(ValueSource.ChargeSpent, add: 4, times: 5),
                        DamageElement.Lightning),
                },
            };

        [Test]
        public void 充能词条牌按消耗的充能数放大伤害()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("token"), player.Id));
            CardInstance card = TestFactory.AddToHand(player, Overcharge());

            Assert.IsTrue(engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id)).Ok);
            Assert.AreEqual(9, enemy.MaxHp - enemy.Hp);
            Assert.AreEqual(0, player.Zones.Charges.Count, "打出时应当把充能扣掉");
        }

        [Test]
        public void 充能不足时充能词条牌打不出()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();
            CardInstance card = TestFactory.AddToHand(player, Overcharge());

            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("充能不足", result.Reason);
            Assert.AreEqual(100, enemy.Hp, "被拒绝的动作不该造成伤害");
        }

        // ───────────────────────── 持续牌 ─────────────────────────

        [Test]
        public void 持续牌打出后进持续区而不进弃牌堆()
        {
            // 进错区的后果是「这张牌的常驻效果再也不生效」，而界面上看起来一切正常
            (BattleEngine engine, PlayerCharacter player, Enemy _) = Duel();

            var mark = new CardDefinition("mark", "猎人印记", CardCategory.Class, CardKind.Continuous)
            {
                ApCost = 1,
                Colors = new[] { AttributeColor.Insight },
            };

            CardInstance card = TestFactory.AddToHand(player, mark);
            Assert.IsTrue(engine.Submit(new PlayCardAction(player.Id, card.Uid)).Ok);

            Assert.AreSame(card, player.Zones.Continuous);
            Assert.AreEqual(0, player.Zones.Discard.Count);
        }

        [Test]
        public void 持续牌挂的常驻词条放大后续伤害()
        {
            const string marked = "test_marked";

            PlayerCharacter player = TestFactory.Player();
            Enemy enemy = TestFactory.Enemy(hp: 100);
            TestFactory.FillDeck(player, 20);

            var statuses = new StatusRegistry();
            statuses.Register(new StatusDefinition(marked) { OutgoingDamageBonus = 2 });

            BattleState state = BattleSetup.Create(
                new XorShift32Rng(20251117),
                new[] { (player, Row.Front) },
                new[] { (enemy, Row.Front) });

            var engine = new BattleEngine(state, EffectRegistry.CreateDefault(), null, statuses);
            engine.Start();

            var mark = new CardDefinition("mark", "猎人印记", CardCategory.Class, CardKind.Continuous)
            {
                ApCost = 1,
                Colors = new[] { AttributeColor.Insight },
                Effects = new[]
                {
                    new EffectData
                    {
                        Kind = EffectKind.ApplyStatus,
                        StatusId = marked,
                        Duration = -1,
                        Amount = ValueExpr.Flat(1),
                        Target = TargetSelector.Self,
                    },
                },
            };

            CardInstance markCard = TestFactory.AddToHand(player, mark);
            Assert.IsTrue(engine.Submit(new PlayCardAction(player.Id, markCard.Uid)).Ok);

            CardInstance strike = TestFactory.AddToHand(player, TestFactory.StrikeCard(damage: 7));
            engine.Submit(new PlayCardAction(player.Id, strike.Uid, enemy.Id));

            Assert.AreEqual(9, enemy.MaxHp - enemy.Hp, "7 + 印记 2");
        }

        // ───────────────────────── 状态持续伤害 ─────────────────────────

        [Test]
        public void 状态在轮末造成持续伤害()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            var cut = new CardDefinition("cut", "割裂", CardCategory.Class, CardKind.Skill)
            {
                ApCost = 1,
                Colors = new[] { AttributeColor.Dexterity },
                Effects = new[]
                {
                    EffectData.Damage(ValueExpr.Flat(5)),
                    new EffectData
                    {
                        Kind = EffectKind.ApplyStatus,
                        StatusId = Bleed,
                        Duration = 2,
                        Amount = ValueExpr.Flat(1),
                        Target = TargetSelector.SingleEnemy,
                    },
                },
            };

            CardInstance card = TestFactory.AddToHand(player, cut);
            Assert.IsTrue(engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id)).Ok);

            Assert.IsTrue(enemy.Statuses.Has(Bleed));
            int afterHit = enemy.Hp;

            engine.Submit(new DeclareReadyAction(player.Id));

            Assert.AreEqual(afterHit - 3, enemy.Hp, "轮末每层扣 3 点");
        }

        // ───────────────────────── 属性克制 × 暴击 ─────────────────────────

        [Test]
        public void 暴击与易损在整条出牌链路上叠乘()
        {
            // 裁定 R-01 的乘法顺序在 CombatMathTests 里按单元验过，
            // 这里验的是「从打出一张牌走完整条管线」时顺序没有被绕过去。
            PlayerCharacter player = TestFactory.Player();
            Enemy enemy = TestFactory.Enemy(hp: 200)
                .WithAffinities(new AffinityTable().Vulnerable(DamageElement.Fire));
            TestFactory.FillDeck(player, 20);

            BattleState state = BattleSetup.Create(
                new ScriptedRng(12),
                new[] { (player, Row.Front) },
                new[] { (enemy, Row.Front) });

            var engine = new BattleEngine(state);
            engine.Start();

            var bolt = new CardDefinition("bolt", "奥术飞弹", CardCategory.Class, CardKind.Skill)
            {
                ApCost = 1,
                HasCrit = true,
                Colors = new[] { AttributeColor.Insight },
                Effects = new[] { EffectData.Damage(ValueExpr.Flat(9), DamageElement.Fire) },
            };

            CardInstance card = TestFactory.AddToHand(player, bolt);
            engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));

            DamageResult damage = engine.Events.Last(e => e.Type == BattleEventType.DamageDealt).Damage;

            Assert.IsTrue(damage.IsCrit, "骰点拉满应当必暴");
            Assert.AreEqual(13, damage.AfterCrit, "⌊9 × 1.5⌋ = 13");
            Assert.AreEqual(26, damage.AfterAffinity, "再吃易损 ×2 = 26");
        }
    }
}
