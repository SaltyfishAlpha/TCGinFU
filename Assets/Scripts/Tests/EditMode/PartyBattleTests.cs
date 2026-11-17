using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TCGinFU.Content;
using TCGinFU.Core;

namespace TCGinFU.Core.Tests
{
    /// <summary>
    /// 三人小队局。此前的测试几乎都是单人，但规则里有一堆东西只有多人才生效：
    /// 充能区容量公式、跨角色贴充能、援护、前排空虚后的仇恨回退。
    /// </summary>
    public class PartyBattleTests
    {
        private sealed class Party
        {
            public BattleEngine Engine;
            public PlayerCharacter Tank, Archer, Mage;
            public Enemy Enemy;

            public IEnumerable<PlayerCharacter> All => new[] { Tank, Archer, Mage };

            public void AllReady()
            {
                foreach (PlayerCharacter p in All.Where(p => !p.IsDown))
                {
                    Engine.Submit(new DeclareReadyAction(p.Id));
                }
            }

            public void AllPass()
            {
                while (Engine.Pending.Kind == DecisionKind.EnemyResponse)
                {
                    foreach (string id in Engine.Pending.WaitingOn.ToList())
                    {
                        Engine.Submit(new PassResponseAction(id));
                    }
                }
            }

            public void DeclineTransforms()
            {
                while (Engine.Pending.Kind == DecisionKind.TransformDeclaration)
                {
                    foreach (string id in Engine.Pending.WaitingOn.ToList())
                    {
                        Engine.Submit(new DeclineTransformAction(id));
                    }
                }
            }

            public void HandLimit()
            {
                while (Engine.Pending.Kind == DecisionKind.HandLimitDiscard)
                {
                    foreach (string id in Engine.Pending.WaitingOn.ToList())
                    {
                        PlayerCharacter p = Engine.State.FindPlayer(id);
                        Engine.Submit(new DiscardToHandLimitAction(
                            id, p.Zones.Hand.Take(Engine.RequiredDiscards(p)).Select(c => c.Uid).ToList()));
                    }
                }
            }

            public void EndRound()
            {
                DeclineTransforms();
                AllReady();
                AllPass();
                HandLimit();
            }
        }

        private static Party Build(Enemy enemy = null, int seed = 99)
        {
            IRng rng = new XorShift32Rng(seed);

            PlayerCharacter tank = SampleDeck.CreatePlayer("tank", rng, "护卫");
            PlayerCharacter archer = SampleDeck.CreatePlayer("archer", rng, "弓手");
            PlayerCharacter mage = SampleDeck.CreatePlayer("mage", rng, "术士");
            enemy ??= SampleEnemies.Goblin(hp: 200);

            BattleState state = BattleSetup.Create(
                rng,
                new[] { (tank, Row.Front), (archer, Row.Back), (mage, Row.Back) },
                new[] { (enemy, Row.Front) });

            var engine = new BattleEngine(
                state, EffectRegistry.CreateDefault(),
                SampleBehaviours.CreateRegistry(), SampleDeck.CreateStatusRegistry());
            engine.Start();

            return new Party { Engine = engine, Tank = tank, Archer = archer, Mage = mage, Enemy = enemy };
        }

        [Test]
        public void 充能区容量按全队人数自动算出()
        {
            Party p = Build();

            // 3 人局 → max(3-1, 2) = 2
            Assert.AreEqual(2, p.Engine.State.ChargeCapacity);
            foreach (PlayerCharacter member in p.All)
            {
                Assert.AreEqual(2, member.Zones.Charges.Capacity);
            }
        }

        [Test]
        public void 建局时会纠正调用方填错的充能区容量()
        {
            // 故意用默认的 2 建 5 名角色，BattleSetup 应按人数改成 max(5-1,2) = 4
            IRng rng = new XorShift32Rng(5);
            var players = Enumerable.Range(0, 5)
                .Select(i => (SampleDeck.CreatePlayer($"p{i}", rng, $"角色{i}"), i < 4 ? Row.Front : Row.Back))
                .ToList();

            BattleState state = BattleSetup.Create(
                rng, players, new[] { (SampleEnemies.TrainingDummy(), Row.Front) });

            Assert.AreEqual(4, state.ChargeCapacity);
            Assert.IsTrue(state.Players.All(p => p.Zones.Charges.Capacity == 4));
        }

        [Test]
        public void 可以把手牌贴到队友的充能区()
        {
            Party p = Build();
            CardInstance card = p.Archer.Zones.Hand[0];

            Assert.IsTrue(p.Engine.Submit(new PlaceChargeAction(p.Archer.Id, card.Uid, p.Tank.Id)).Ok);

            Assert.AreEqual(1, p.Tank.Zones.Charges.Count, "充能进的是护卫的区");
            Assert.AreEqual(0, p.Archer.Zones.Charges.Count);
            Assert.AreEqual(p.Archer.Id, p.Tank.Zones.Charges.Tokens[0].OwnerId, "但原持有者仍是弓手");
        }

        [Test]
        public void 队友充能被消耗后回到原持有者的弃牌堆()
        {
            Party p = Build();

            // 弓手和术士各贴 1 张到护卫区，护卫消耗掉其中最早的那个
            p.Engine.Submit(new PlaceChargeAction(p.Archer.Id, p.Archer.Zones.Hand[0].Uid, p.Tank.Id));
            p.Engine.Submit(new PlaceChargeAction(p.Mage.Id, p.Mage.Zones.Hand[0].Uid, p.Tank.Id));

            int archerDiscardBefore = p.Archer.Zones.Discard.Count;
            Assert.IsTrue(p.Engine.Submit(new SpendChargeForApAction(p.Tank.Id)).Ok);

            Assert.AreEqual(archerDiscardBefore + 1, p.Archer.Zones.Discard.Count);
            Assert.AreEqual(0, p.Tank.Zones.Discard.Count, "护卫的弃牌堆不该收到别人的牌");
        }

        [Test]
        public void 充能区满时顶出最早的一张并退还原持有者()
        {
            Party p = Build();

            p.Engine.Submit(new PlaceChargeAction(p.Archer.Id, p.Archer.Zones.Hand[0].Uid, p.Tank.Id));
            p.Engine.Submit(new PlaceChargeAction(p.Mage.Id, p.Mage.Zones.Hand[0].Uid, p.Tank.Id));
            Assert.AreEqual(2, p.Tank.Zones.Charges.Count, "已满");

            int archerDiscardBefore = p.Archer.Zones.Discard.Count;
            p.Engine.Submit(new PlaceChargeAction(p.Tank.Id, p.Tank.Zones.Hand[0].Uid, p.Tank.Id));

            Assert.AreEqual(2, p.Tank.Zones.Charges.Count, "容量不变");
            Assert.AreEqual(archerDiscardBefore + 1, p.Archer.Zones.Discard.Count, "被顶出的是弓手的牌");
        }

        [Test]
        public void 单体攻击默认指向前排的护卫()
        {
            Party p = Build();
            p.DeclineTransforms();
            p.AllReady();

            Assert.AreEqual(DecisionKind.EnemyResponse, p.Engine.Pending.Kind);
            Assert.AreSame(p.Tank, p.Engine.CurrentIntentTargets.Single());
        }

        [Test]
        public void 前排倒下后改打后排上一轮输出最高者()
        {
            Party p = Build();

            // 做出「术士上一轮输出更高」的局面，再让护卫退场
            p.Archer.DamageDealtLastRound = 4;
            p.Mage.DamageDealtLastRound = 25;
            p.Tank.LoseHp(p.Tank.MaxHp);

            Assert.AreSame(p.Mage, p.Engine.State.DefaultAggroTarget());
        }

        [Test]
        public void 后排可以援护正前方的前排队友()
        {
            Party p = Build(SampleEnemies.Goblin(hp: 200));

            // 弓手在后排 0 号槽，与前排 0 号槽的护卫同列，构成相邻
            Assert.IsTrue(p.Engine.State.PlayerField.AreAdjacent(p.Archer, p.Tank));

            var cover = new CardInstance(SampleCards.GuardianStep, p.Archer.Id);
            p.Archer.Zones.Hand.AddBottom(cover);

            p.DeclineTransforms();
            p.AllReady();

            int tankHpBefore = p.Tank.Hp;
            Assert.IsTrue(p.Engine.Submit(new PlayCardAction(p.Archer.Id, cover.Uid, p.Tank.Id)).Ok);
            p.AllPass();

            // 弓手盾够厚时 HP 可能一点不掉，所以断言「这一刀打在谁身上」而不是「谁掉了血」
            BattleEvent hit = p.Engine.Events.Last(e => e.Type == BattleEventType.DamageDealt);
            Assert.AreEqual(p.Archer.Id, hit.TargetId, "伤害应改由弓手承受");
            Assert.AreEqual(tankHpBefore, p.Tank.Hp);
        }

        [Test]
        public void 术士无法援护非相邻的前排()
        {
            Party p = Build(SampleEnemies.Goblin(hp: 200));

            // 术士在后排 1 号槽：与前排 0 号槽的护卫既不同排也不同列
            Assert.IsFalse(p.Engine.State.PlayerField.AreAdjacent(p.Mage, p.Tank));

            var cover = new CardInstance(SampleCards.GuardianStep, p.Mage.Id);
            p.Mage.Zones.Hand.AddBottom(cover);

            p.DeclineTransforms();
            p.AllReady();

            p.Engine.Submit(new PlayCardAction(p.Mage.Id, cover.Uid, p.Tank.Id));
            p.AllPass();

            BattleEvent hit = p.Engine.Events.Last(e => e.Type == BattleEventType.DamageDealt);
            Assert.AreEqual(p.Tank.Id, hit.TargetId, "援护未生效，护卫照常挨打");
        }

        [Test]
        public void 全员宣布完毕才进入敌人阶段()
        {
            Party p = Build();
            p.DeclineTransforms();

            p.Engine.Submit(new DeclareReadyAction(p.Tank.Id));
            Assert.AreEqual(DecisionKind.PlayerActions, p.Engine.Pending.Kind, "还有两人没表态");

            p.Engine.Submit(new DeclareReadyAction(p.Archer.Id));
            Assert.AreEqual(DecisionKind.PlayerActions, p.Engine.Pending.Kind);

            p.Engine.Submit(new DeclareReadyAction(p.Mage.Id));
            Assert.AreEqual(DecisionKind.EnemyResponse, p.Engine.Pending.Kind);
        }

        [Test]
        public void 已宣布完毕的角色不能再出牌()
        {
            Party p = Build();
            p.DeclineTransforms();
            p.Engine.Submit(new DeclareReadyAction(p.Archer.Id));

            CardInstance card = p.Archer.Zones.Hand[0];
            ActionResult result = p.Engine.Submit(
                new PlayCardAction(p.Archer.Id, card.Uid, p.Enemy.Id));

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("已宣布行动完毕", result.Reason);
        }

        [Test]
        public void 三人小队能打完一局()
        {
            Party p = Build(SampleEnemies.Gargoyle(hp: 150));

            for (int round = 0; round < 30 && p.Engine.State.Outcome == BattleOutcome.Ongoing; round++)
            {
                p.DeclineTransforms();

                foreach (PlayerCharacter member in p.All.Where(m => !m.IsDown))
                {
                    PlayPlayable(p.Engine, member);
                }

                p.AllReady();
                p.AllPass();
                p.HandLimit();
            }

            Assert.AreNotEqual(BattleOutcome.Ongoing, p.Engine.State.Outcome);
            Assert.AreEqual(RoundPhase.Finished, p.Engine.State.Phase);

            // 战报能完整渲染出来，不抛异常也不留空行
            string log = BattleLog.Format(p.Engine);
            StringAssert.Contains("=== 战斗开始 ===", log);
            StringAssert.Contains("=== 战斗结束", log);
        }

        private static void PlayPlayable(BattleEngine engine, PlayerCharacter player)
        {
            for (int guard = 0; guard < 15; guard++)
            {
                if (engine.Pending.Kind != DecisionKind.PlayerActions) return;
                if (!engine.Pending.WaitingOn.Contains(player.Id)) return;

                bool played = false;
                foreach (CardInstance card in player.Zones.Hand.ToList())
                {
                    string target = card.Definition.Effects.Any(e => e.Target.Scope == TargetScope.SingleEnemy)
                        ? engine.State.LivingEnemies.FirstOrDefault()?.Id
                        : player.Id;

                    if (engine.Submit(new PlayCardAction(player.Id, card.Uid, target)).Ok)
                    {
                        played = true;
                        break;
                    }
                }

                if (!played) return;
            }
        }
    }
}
