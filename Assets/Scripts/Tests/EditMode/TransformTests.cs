using System.Linq;
using NUnit.Framework;
using TCGinFU.Core;

namespace TCGinFU.Core.Tests
{
    /// <summary>§6 升变机制。</summary>
    public class TransformTests
    {
        private const string TransformId = "rogue_awaken";

        private static (BattleEngine engine, PlayerCharacter player, Enemy enemy) Setup(
            TransformTrigger trigger = TransformTrigger.Immediate,
            bool transformInDeck = false)
        {
            PlayerCharacter player = TestFactory.Player(weapon: new WeaponProfile("短弓", 7));
            Enemy enemy = TestFactory.Enemy(hp: 500);

            TestFactory.FillDeck(player, 40);

            CardDefinition transformDef = TestFactory.TransformCard(TransformId, trigger: trigger);
            var transformCard = new CardInstance(transformDef, player.Id);

            if (transformInDeck) player.Zones.Deck.AddBottom(transformCard);
            else player.Zones.Hand.AddBottom(transformCard);

            player.Zones.SetFormDeck(
                TransformId,
                Enumerable.Range(0, DeckList.FormDeckSize)
                    .Select(i => new CardInstance(TestFactory.FormStrike($"form{i}"), player.Id)));

            (BattleEngine engine, _, _) = TestFactory.Duel(new XorShift32Rng(11), player, enemy);
            return (engine, player, enemy);
        }

        [Test]
        public void 轮始可宣告升变并抽五张形态牌()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            Assert.AreEqual(DecisionKind.TransformDeclaration, engine.Pending.Kind);
            CardInstance candidate = engine.TransformCandidates(player).Single();

            Assert.IsTrue(engine.Submit(new DeclareTransformAction(player.Id, candidate.Uid)).Ok);

            Assert.IsTrue(player.Transform.Active);
            Assert.AreEqual(5, player.Zones.Hand.Count, "形态首轮抽 5 张，代替常规抽牌");
            Assert.IsTrue(player.Zones.Hand.All(c => c.IsFormCard));
            Assert.AreEqual(5, player.Zones.FormDeck.Count, "形态组 10 张抽掉 5 张");
            Assert.AreEqual(40, player.Zones.Deck.Count, "主卡组留在场上，不被形态牌顶替");
        }

        [Test]
        public void 升变不动持续区与充能区()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            var keeper = new CardInstance(TestFactory.FillerCard("keeper"), player.Id);
            player.Zones.SetContinuous(keeper, out _);
            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("fuel"), player.Id));

            CardInstance candidate = engine.TransformCandidates(player).Single();
            engine.Submit(new DeclareTransformAction(player.Id, candidate.Uid));

            Assert.AreSame(keeper, player.Zones.Continuous, "持续区的牌保留不变（§6.2 ①）");
            Assert.AreEqual(1, player.Zones.Charges.Count, "充能区的牌保留不变");
        }

        [Test]
        public void 形态攻击牌按动态基准结算()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup();
            engine.Start();

            CardInstance candidate = engine.TransformCandidates(player).Single();
            engine.Submit(new DeclareTransformAction(player.Id, candidate.Uid));

            // 5 级持短弓（一阶伤害 7），L_bonus = 1 → B_atk = 8，卡面 B_atk + 4 = 12
            CardInstance formCard = player.Zones.Hand[0];
            Assert.IsTrue(engine.Submit(new PlayCardAction(player.Id, formCard.Uid, enemy.Id)).Ok);

            Assert.AreEqual(500 - 12, enemy.Hp);
            Assert.AreEqual(1, player.Zones.FormDiscard.Count, "形态牌进形态弃牌堆，不混入主卡组弃牌堆");
            Assert.AreEqual(0, player.Zones.Discard.Count);
        }

        [Test]
        public void 升变持续两轮后退出并取回原手牌()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup();
            engine.Start();

            CardInstance candidate = engine.TransformCandidates(player).Single();
            engine.Submit(new DeclareTransformAction(player.Id, candidate.Uid));

            // ── 升变第 1 轮：打掉 1 张形态牌 ──
            engine.Submit(new PlayCardAction(player.Id, player.Zones.Hand[0].Uid, enemy.Id));
            AdvanceOneRound(engine, player);

            // ── 升变第 2 轮：形态组剩余 5 张全部抽上手 ──
            Assert.AreEqual(2, engine.State.Round);
            Assert.IsTrue(player.Transform.Active);
            Assert.AreEqual(9, player.Zones.Hand.Count, "4 张留存 + 剩余 5 张全抽");
            Assert.AreEqual(0, player.Zones.FormDeck.Count);
            Assert.AreEqual(40, player.Zones.Deck.Count, "整个升变期间主卡组一张没动");

            AdvanceOneRound(engine, player);

            // ── 第 3 轮轮始：退出升变 ──
            Assert.AreEqual(3, engine.State.Round);
            Assert.IsFalse(player.Transform.Active);
            Assert.AreEqual(11, player.Zones.Exile.Count, "10 张形态牌 + 1 张升变牌整套进除外区");
            Assert.AreEqual(0, player.Zones.FormDiscard.Count);
            Assert.IsFalse(player.Zones.Hand.Any(c => c.IsFormCard));
            Assert.AreEqual(3, player.Zones.Hand.Count, "取回原手牌（空）后按常规抽 3 张");
            Assert.AreEqual(37, player.Zones.Deck.Count);
        }

        /// <summary>
        /// 墓地对外算一个：主弃牌堆与形态弃牌堆在规则上是两摞（形态牌要整体移出本局），
        /// 但展示和「从墓地回收」这类效果都该同时够得着两边。
        /// </summary>
        [Test]
        public void 弃牌堆同时含主卡组与升变卡组的弃牌()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup();
            engine.Start();

            var spent = new CardInstance(TestFactory.FillerCard("spent"), player.Id);
            player.Zones.Discard.AddTop(spent);

            CardInstance candidate = engine.TransformCandidates(player).Single();
            engine.Submit(new DeclareTransformAction(player.Id, candidate.Uid));
            engine.Submit(new PlayCardAction(player.Id, player.Zones.Hand[0].Uid, enemy.Id));

            CardInstance[] all = player.Zones.AllDiscards.ToArray();
            Assert.AreEqual(2, all.Length, "升变期间打出的形态牌也算在弃牌堆里");
            Assert.IsTrue(all[0].IsFormCard, "刚打出的形态牌露在最上面");
            Assert.AreSame(spent, all[1]);
        }

        [Test]
        public void 一局只能升变一次()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            CardInstance candidate = engine.TransformCandidates(player).Single();
            engine.Submit(new DeclareTransformAction(player.Id, candidate.Uid));

            AdvanceOneRound(engine, player);
            AdvanceOneRound(engine, player);

            Assert.IsFalse(player.Transform.Active);
            CollectionAssert.IsEmpty(engine.TransformCandidates(player));
        }

        /// <summary>§6.1 第 3 轮保底：升变牌仍在主卡组里时可直接跳出。</summary>
        [Test]
        public void 第三轮保底可让升变牌从主卡组跳出()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup(
                TransformTrigger.Conditional, transformInDeck: true);
            engine.Start();

            Assert.AreEqual(RoundPhase.PlayerPhase, engine.State.Phase, "第 1 轮没有可发动的升变牌");

            AdvanceOneRound(engine, player);
            Assert.AreEqual(2, engine.State.Round);
            CollectionAssert.IsEmpty(engine.TransformCandidates(player), "保底只在第 3 轮提供");

            AdvanceOneRound(engine, player);

            Assert.AreEqual(3, engine.State.Round);
            Assert.AreEqual(DecisionKind.TransformDeclaration, engine.Pending.Kind);

            CardInstance candidate = engine.TransformCandidates(player).Single();
            Assert.IsTrue(engine.Submit(new DeclareTransformAction(player.Id, candidate.Uid)).Ok);

            Assert.IsTrue(player.Transform.Active);
            Assert.IsTrue(player.Transform.UsedThirdRoundGuarantee);
            Assert.AreEqual(5, player.Zones.Hand.Count);
        }

        /// <summary>§6.1 手里效果：展示升变牌 → 洗回主卡组 → 抽 1 张。</summary>
        [Test]
        public void 升变牌可洗回主卡组并补抽一张()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            CardInstance transformCard = engine.TransformCandidates(player).Single();
            engine.Submit(new DeclineTransformAction(player.Id));

            int handBefore = player.Zones.Hand.Count;
            Assert.IsTrue(engine.Submit(new ShuffleBackTransformAction(player.Id, transformCard.Uid)).Ok);

            Assert.AreEqual(handBefore, player.Zones.Hand.Count, "洗回 1 张 + 抽 1 张，净持平");
            Assert.IsTrue(player.Zones.Deck.Contains(transformCard));
        }

        [Test]
        public void 升变牌不能当普通牌打出()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup();
            engine.Start();

            CardInstance transformCard = engine.TransformCandidates(player).Single();
            engine.Submit(new DeclineTransformAction(player.Id));

            ActionResult result = engine.Submit(new PlayCardAction(player.Id, transformCard.Uid, enemy.Id));
            Assert.IsFalse(result.Ok);
        }

        private static void AdvanceOneRound(BattleEngine engine, PlayerCharacter player)
        {
            if (engine.Pending.Kind == DecisionKind.TransformDeclaration)
            {
                engine.Submit(new DeclineTransformAction(player.Id));
            }

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
