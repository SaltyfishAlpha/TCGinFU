using System.Linq;
using NUnit.Framework;
using TCGinFU.Core;

namespace TCGinFU.Core.Tests
{
    /// <summary>§8 标准战斗轮次流程。</summary>
    public class BattleFlowTests
    {
        private static (BattleEngine engine, PlayerCharacter player, Enemy enemy) Setup(
            int deckSize = 40,
            params EnemyIntent[] intents)
        {
            PlayerCharacter player = TestFactory.Player();
            Enemy enemy = TestFactory.Enemy(intents: intents);
            TestFactory.FillDeck(player, deckSize);

            (BattleEngine engine, _, _) = TestFactory.Duel(new XorShift32Rng(7), player, enemy);
            return (engine, player, enemy);
        }

        [Test]
        public void 第一轮抽五张第二轮抽三张()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            Assert.AreEqual(1, engine.State.Round);
            Assert.AreEqual(RoundPhase.PlayerPhase, engine.State.Phase);
            Assert.AreEqual(5, player.Zones.Hand.Count);

            AdvanceOneRound(engine, player);

            Assert.AreEqual(2, engine.State.Round);
            Assert.AreEqual(8, player.Zones.Hand.Count, "第 2 轮补 3 张");
        }

        [Test]
        public void 轮始获得三点行动力且不累加()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            Assert.AreEqual(3, player.Ap);

            AdvanceOneRound(engine, player);
            Assert.AreEqual(3, player.Ap, "AP 是赋值不是累加（裁定 R-10）");
        }

        [Test]
        public void 轮始重新获得自动双盾()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            Assert.AreEqual(player.Sheet.AutoPhysicalShield, player.Shields.AutoPhysical);
            Assert.AreEqual(player.Sheet.AutoMagicShield, player.Shields.AutoMagical);
        }

        [Test]
        public void 打出武器牌消耗行动力并造成伤害()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup();
            engine.Start();

            CardInstance card = TestFactory.AddToHand(player, TestFactory.StrikeCard(damage: 7));
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));

            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(93, enemy.Hp);
            Assert.AreEqual(2, player.Ap);
            Assert.AreEqual(1, player.Zones.Discard.Count);
        }

        [Test]
        public void 行动力不足时拒绝出牌且不改状态()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup();
            engine.Start();

            player.Ap = 0;
            CardInstance card = TestFactory.AddToHand(player, TestFactory.StrikeCard(apCost: 1));
            int handBefore = player.Zones.Hand.Count;

            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(handBefore, player.Zones.Hand.Count, "被拒绝的动作不应改动手牌");
            Assert.AreEqual(100, enemy.Hp);
        }

        [Test]
        public void 充能转MP后才打得出原本MP不足的牌()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup();
            engine.Start();

            CardDefinition spell = new("spell", "法术", CardCategory.Class, CardPlay.Skill)
            {
                ApCost = 1,
                MpCost = 5,
                Colors = new[] { AttributeColor.Willpower },
                Effects = new[] { EffectData.Damage(ValueExpr.Flat(10)) },
            };

            CardInstance card = TestFactory.AddToHand(player, spell);
            player.SpendMp(player.Mp); // 清空 MP
            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("fuel"), player.Id));

            ActionResult withoutCharge = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));
            Assert.IsFalse(withoutCharge.Ok, "MP 不够应被拒绝");

            // 充能不再在出牌时抵扣（旧裁定 R-09 已作废），得先单独转成 MP
            Assert.IsTrue(engine.Submit(new SpendChargeForMpAction(player.Id)).Ok);
            Assert.AreEqual(5, player.Mp);

            ActionResult afterConvert = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));

            Assert.IsTrue(afterConvert.Ok, afterConvert.Reason);
            Assert.AreEqual(90, enemy.Hp);
            Assert.AreEqual(0, player.Zones.Charges.Count);
            Assert.AreEqual(0, player.Mp);
        }

        [Test]
        public void 充能转MP回到上限为止且满蓝时照样消耗()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            // 只差 2 点就满：转换回 2 点而不是 5 点，多出来的不找零
            player.SpendMp(2);
            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("fuel"), player.Id));
            Assert.IsTrue(engine.Submit(new SpendChargeForMpAction(player.Id)).Ok);
            Assert.AreEqual(player.MaxMp, player.Mp);

            // 满蓝时不拒绝：玩家可能就是想清掉充能区（界面负责提示会浪费）
            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("fuel2"), player.Id));
            Assert.IsTrue(engine.Submit(new SpendChargeForMpAction(player.Id)).Ok);
            Assert.AreEqual(player.MaxMp, player.Mp);
            Assert.AreEqual(0, player.Zones.Charges.Count);
        }

        [Test]
        public void 充能转MP计入回复统计()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            player.SpendMp(player.Mp);
            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("fuel"), player.Id));
            Assert.IsTrue(engine.Submit(new SpendChargeForMpAction(player.Id)).Ok);

            // 走的是和卡牌 restore_mp 同一条路径，所以「全队累计回复过 15 MP」
            // 这类升变前置（星穹坍缩）认这 5 点
            Assert.AreEqual(BattleEngine.MpPerCharge, engine.State.Stats.Team(BattleStatKeys.MpRestored));
            Assert.AreEqual(
                BattleEngine.MpPerCharge, engine.State.Stats.For(player.Id, BattleStatKeys.MpRestored));
        }

        [Test]
        public void 没有充能时不能转MP()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            player.SpendMp(player.Mp);
            ActionResult result = engine.Submit(new SpendChargeForMpAction(player.Id));

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(0, player.Mp, "被拒绝的动作不该回任何 MP");
        }

        [Test]
        public void 贴充能不耗行动力且每轮限一次()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            int apBefore = player.Ap;
            CardInstance first = player.Zones.Hand[0];
            CardInstance second = player.Zones.Hand[1];

            Assert.IsTrue(engine.Submit(new PlaceChargeAction(player.Id, first.Uid)).Ok);
            Assert.AreEqual(apBefore, player.Ap, "贴充能消耗 0 AP");
            Assert.AreEqual(1, player.Zones.Charges.Count);

            ActionResult again = engine.Submit(new PlaceChargeAction(player.Id, second.Uid));
            Assert.IsFalse(again.Ok, "每轮只能贴 1 次");
        }

        [Test]
        public void 充能可换取一点行动力()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            engine.Submit(new PlaceChargeAction(player.Id, player.Zones.Hand[0].Uid));
            int apBefore = player.Ap;

            Assert.IsTrue(engine.Submit(new SpendChargeForApAction(player.Id)).Ok);
            Assert.AreEqual(apBefore + 1, player.Ap);
        }

        [Test]
        public void 走位消耗一点行动力且每轮限一次()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            Assert.AreEqual(Row.Front, player.Row);
            Assert.IsTrue(engine.Submit(new MoveRowAction(player.Id)).Ok);
            Assert.AreEqual(Row.Back, player.Row);
            Assert.AreEqual(2, player.Ap);

            Assert.IsFalse(engine.Submit(new MoveRowAction(player.Id)).Ok, "每轮只能走位 1 次（裁定 R-16）");
        }

        [Test]
        public void 全员宣布完毕后进入敌人阶段并开启响应窗口()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup(intents: TestFactory.Attack(6));
            engine.Start();

            engine.Submit(new DeclareReadyAction(player.Id));

            Assert.AreEqual(RoundPhase.EnemyPhase, engine.State.Phase);
            Assert.AreEqual(DecisionKind.EnemyResponse, engine.Pending.Kind);
            Assert.AreSame(player, engine.CurrentIntentTargets.Single());
        }

        [Test]
        public void 响应窗口里能转MP但不能换AP()
        {
            // 响应牌也要花 MP（看破闪避 5 MP），所以转 MP 在响应窗口里必须可用；
            // 换 AP 不行——那 1 点 AP 轮末就清零了（裁定 R-10）
            (BattleEngine engine, PlayerCharacter player, _) = Setup(intents: TestFactory.Attack(6));
            engine.Start();

            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("fuel"), player.Id));
            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("fuel2"), player.Id));
            player.SpendMp(player.Mp);

            engine.Submit(new DeclareReadyAction(player.Id));
            Assert.AreEqual(DecisionKind.EnemyResponse, engine.Pending.Kind);

            Assert.IsFalse(engine.Submit(new SpendChargeForApAction(player.Id)).Ok, "响应窗口里不能换 AP");

            ActionResult mp = engine.Submit(new SpendChargeForMpAction(player.Id));
            Assert.IsTrue(mp.Ok, mp.Reason);
            Assert.AreEqual(BattleEngine.MpPerCharge, player.Mp);
        }

        /// <summary>
        /// 多人局才测得到的那一半：只有**全员**表态才推进。
        /// 单人局里「一个人宣布完毕」和「全员宣布完毕」是同一件事，
        /// 漏掉等待逻辑也照样绿，所以这两条必须用三人局守。
        /// </summary>
        [Test]
        public void 尚有队友未宣布时不进入敌人阶段()
        {
            (BattleEngine engine, PlayerCharacter[] party, _) = Party();

            engine.Submit(new DeclareReadyAction(party[0].Id));
            Assert.AreEqual(DecisionKind.PlayerActions, engine.Pending.Kind, "还有两人没表态");

            engine.Submit(new DeclareReadyAction(party[1].Id));
            Assert.AreEqual(DecisionKind.PlayerActions, engine.Pending.Kind, "还有一人没表态");

            engine.Submit(new DeclareReadyAction(party[2].Id));
            Assert.AreEqual(DecisionKind.EnemyResponse, engine.Pending.Kind, "全员表态后才进敌人阶段");
        }

        [Test]
        public void 已宣布完毕的角色不能再出牌()
        {
            (BattleEngine engine, PlayerCharacter[] party, Enemy enemy) = Party();

            CardInstance card = TestFactory.AddToHand(party[0], TestFactory.StrikeCard());
            engine.Submit(new DeclareReadyAction(party[0].Id));

            ActionResult result = engine.Submit(new PlayCardAction(party[0].Id, card.Uid, enemy.Id));

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("已宣布行动完毕", result.Reason);
            // 队友还没表态，局面仍停在玩家阶段——被拒绝的动作没把流程推歪
            Assert.AreEqual(DecisionKind.PlayerActions, engine.Pending.Kind);
        }

        /// <summary>三人小队 + 一只木桩，已 Start。</summary>
        private static (BattleEngine engine, PlayerCharacter[] party, Enemy enemy) Party()
        {
            PlayerCharacter[] party =
            {
                TestFactory.Player("tank", TestFactory.Sheet("护卫")),
                TestFactory.Player("archer", TestFactory.Sheet("弓手")),
                TestFactory.Player("mage", TestFactory.Sheet("术士")),
            };

            foreach (PlayerCharacter member in party) TestFactory.FillDeck(member, 20);

            Enemy enemy = TestFactory.Enemy(hp: 200, intents: new[] { TestFactory.Attack(6) });

            BattleState state = BattleSetup.Create(
                new XorShift32Rng(99),
                new[] { (party[0], Row.Front), (party[1], Row.Back), (party[2], Row.Back) },
                new[] { (enemy, Row.Front) });

            var engine = new BattleEngine(state);
            engine.Start();
            return (engine, party, enemy);
        }

        [Test]
        public void 响应窗口全体放弃后意图结算()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup(intents: TestFactory.Attack(6));
            engine.Start();

            int hpBefore = player.Hp;
            int shieldBefore = player.Shields.Physical;

            engine.Submit(new DeclareReadyAction(player.Id));
            engine.Submit(new PassResponseAction(player.Id));

            // 6 点物理伤害先被自动物理盾吃掉
            Assert.AreEqual(hpBefore - System.Math.Max(0, 6 - shieldBefore), player.Hp);
        }

        [Test]
        public void 响应窗口不接受技能牌()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup(intents: TestFactory.Attack(6));
            engine.Start();

            CardInstance skill = TestFactory.AddToHand(player, TestFactory.StrikeCard());
            engine.Submit(new DeclareReadyAction(player.Id));

            ActionResult result = engine.Submit(new PlayCardAction(player.Id, skill.Uid, enemy.Id));
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("时机牌", result.Reason);
        }

        [Test]
        public void 响应窗口可以打出防具牌加盾()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup(intents: TestFactory.Attack(10));
            engine.Start();

            CardDefinition guard = new("guard", "格挡", CardCategory.Armor, CardPlay.Timing)
            {
                ApCost = 0,
                Effects = new[] { EffectData.Shield(ValueExpr.Flat(8), ShieldKind.Physical, TargetScope.Self) },
            };

            CardInstance card = TestFactory.AddToHand(player, guard);
            engine.Submit(new DeclareReadyAction(player.Id));

            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid));
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(8, player.Shields.GrantedPhysical);
        }

        [Test]
        public void 轮末场景命刻加一()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            Assert.AreEqual(0, engine.State.SceneClock);
            AdvanceOneRound(engine, player);
            Assert.AreEqual(1, engine.State.SceneClock);
        }

        [Test]
        public void 轮末手牌超过七张需要弃置()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            // 第 1 轮 5 张不触发，走到第 2 轮补成 8 张
            AdvanceOneRound(engine, player);
            Assert.AreEqual(8, player.Zones.Hand.Count);

            engine.Submit(new DeclareReadyAction(player.Id));
            engine.Submit(new PassResponseAction(player.Id));

            Assert.AreEqual(DecisionKind.HandLimitDiscard, engine.Pending.Kind);
            Assert.AreEqual(1, engine.RequiredDiscards(player));

            ActionResult wrongCount = engine.Submit(
                new DiscardToHandLimitAction(player.Id, new[] { player.Zones.Hand[0].Uid, player.Zones.Hand[1].Uid }));
            Assert.IsFalse(wrongCount.Ok, "弃牌张数必须正好等于超出的张数");

            Assert.IsTrue(engine.Submit(
                new DiscardToHandLimitAction(player.Id, new[] { player.Zones.Hand[0].Uid })).Ok);

            Assert.AreEqual(3, engine.State.Round);
            Assert.AreEqual(BattleEngine.HandLimit + 3, player.Zones.Hand.Count, "弃到 7 张后第 3 轮再补 3 张");
        }

        [Test]
        public void 击杀全部敌人即胜利()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup();
            engine.Start();

            CardInstance card = TestFactory.AddToHand(player, TestFactory.StrikeCard(damage: 999));
            engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));

            Assert.AreEqual(BattleOutcome.Victory, engine.State.Outcome);
        }

        [Test]
        public void 战斗入场不回满HP与MP()
        {
            CharacterSheet sheet = TestFactory.Sheet();
            PlayerCharacter player = TestFactory.Player(sheet: sheet, hp: 12, mp: 3);
            TestFactory.FillDeck(player, 20);

            (BattleEngine engine, _, _) = TestFactory.Duel(player: player);
            engine.Start();

            Assert.AreEqual(12, player.Hp, "按跑团剩余 HP 入场（§1）");
            Assert.AreEqual(3, player.Mp);
        }

        /// <summary>把当前轮推完，停在下一轮的玩家阶段。</summary>
        private static void AdvanceOneRound(BattleEngine engine, PlayerCharacter player)
        {
            engine.Submit(new DeclareReadyAction(player.Id));

            while (engine.Pending.Kind == DecisionKind.EnemyResponse)
            {
                engine.Submit(new PassResponseAction(player.Id));
            }

            if (engine.Pending.Kind == DecisionKind.HandLimitDiscard)
            {
                int required = engine.RequiredDiscards(player);
                engine.Submit(new DiscardToHandLimitAction(
                    player.Id,
                    player.Zones.Hand.Take(required).Select(c => c.Uid).ToList()));
            }
        }
    }
}
