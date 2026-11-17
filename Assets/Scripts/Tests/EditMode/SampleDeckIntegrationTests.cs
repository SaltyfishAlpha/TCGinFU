using System.Linq;
using NUnit.Framework;
using TCGinFU.Content;
using TCGinFU.Core;

namespace TCGinFU.Core.Tests
{
    /// <summary>
    /// 拿 <see cref="SampleDeck"/> 这副真卡组端到端跑引擎。
    /// 上面那些单元测试用的是手搓的最小卡，这里验证的是「成套内容接上去能不能正常走完一局」。
    /// </summary>
    public class SampleDeckIntegrationTests
    {
        private sealed class Harness
        {
            public BattleEngine Engine;
            public PlayerCharacter Player;
            public Enemy Enemy;

            public CardInstance HandCard(CardDefinition def)
                => Player.Zones.Hand.FirstOrDefault(c => c.Definition.Id == def.Id);

            /// <summary>把指定卡塞进手牌（绕开抽牌随机性，让断言稳定）。</summary>
            public CardInstance Plant(CardDefinition def)
            {
                var card = new CardInstance(def, Player.Id);
                Player.Zones.Hand.AddBottom(card);
                return card;
            }

            public void EndRound()
            {
                if (Engine.Pending.Kind == DecisionKind.TransformDeclaration)
                {
                    Engine.Submit(new DeclineTransformAction(Player.Id));
                }

                Engine.Submit(new DeclareReadyAction(Player.Id));

                while (Engine.Pending.Kind == DecisionKind.EnemyResponse)
                {
                    Engine.Submit(new PassResponseAction(Player.Id));
                }

                if (Engine.Pending.Kind == DecisionKind.HandLimitDiscard)
                {
                    int required = Engine.RequiredDiscards(Player);
                    Engine.Submit(new DiscardToHandLimitAction(
                        Player.Id, Player.Zones.Hand.Take(required).Select(c => c.Uid).ToList()));
                }
            }
        }

        private static Harness Build(Enemy enemy = null, int seed = 20251117, bool start = true)
            => BuildWithRng(new XorShift32Rng(seed), enemy, start);

        /// <summary>需要钉死骰点时用这个重载；洗牌仍走独立的 RNG，免得脚本序列被洗牌吃掉。</summary>
        private static Harness BuildWithRng(IRng battleRng, Enemy enemy = null, bool start = true)
        {
            PlayerCharacter player = SampleDeck.CreatePlayer(
                "p1", chargeCapacity: 2, rng: new XorShift32Rng(20251117));
            enemy ??= SampleEnemies.TrainingDummy();

            BattleState state = BattleSetup.Create(
                battleRng,
                new[] { (player, Row.Front) },
                new[] { (enemy, Row.Front) });

            var engine = new BattleEngine(
                state,
                EffectRegistry.CreateDefault(),
                SampleBehaviours.CreateRegistry(),
                SampleDeck.CreateStatusRegistry());

            if (start) engine.Start();
            return new Harness { Engine = engine, Player = player, Enemy = enemy };
        }

        // ───────────────────────── 构筑 ─────────────────────────

        [Test]
        public void 样例卡组通过构筑校验()
        {
            DeckValidationResult result = DeckValidator.Validate(SampleDeck.Build());
            Assert.IsTrue(result.IsValid, result.ToString());
        }

        [Test]
        public void 样例卡组覆盖全部四种卡牌来源与六种结算形态()
        {
            DeckList deck = SampleDeck.Build();
            var all = deck.MainDeck.Concat(deck.FormDecks.Values.SelectMany(f => f)).ToList();

            CollectionAssert.AreEquivalent(
                System.Enum.GetValues(typeof(CardCategory)).Cast<CardCategory>().ToList(),
                all.Select(c => c.Category).Distinct().ToList());

            CollectionAssert.AreEquivalent(
                System.Enum.GetValues(typeof(CardKind)).Cast<CardKind>().ToList(),
                all.Select(c => c.Kind).Distinct().ToList());
        }

        [Test]
        public void 样例卡组的脚本钩子全部已注册()
        {
            BehaviourRegistry registry = SampleBehaviours.CreateRegistry();
            DeckList deck = SampleDeck.Build();

            var hooked = deck.MainDeck
                .Concat(deck.FormDecks.Values.SelectMany(f => f))
                .Where(c => !string.IsNullOrEmpty(c.BehaviourId))
                .ToList();

            Assert.AreEqual(3, hooked.Select(c => c.BehaviourId).Distinct().Count(), "样例内容有 3 张挂钩子的卡");
            foreach (CardDefinition card in hooked)
            {
                Assert.IsNotNull(registry.Resolve(card), $"「{card.Name}」的钩子 {card.BehaviourId} 未注册");
            }
        }

        [Test]
        public void 发牌后卡组是三十张且形态组在场外()
        {
            Harness h = Build(start: false);

            Assert.AreEqual(30, h.Player.Zones.Deck.Count);
            Assert.AreEqual(0, h.Player.Zones.Hand.Count);
            Assert.AreEqual(10, h.Player.Zones.GetFormDeck(SampleCards.RogueAwaken.Id).Count,
                "形态组置于场外，不计入主卡组 30 张");
        }

        // ───────────────────────── 数据驱动卡 ─────────────────────────

        [Test]
        public void 武器牌伤害跟当前武器走()
        {
            Harness h = Build();
            CardInstance card = h.Plant(SampleCards.ShortbowShot);

            // 5 级一阶，短弓 7 点
            Assert.IsTrue(h.Engine.Submit(new PlayCardAction(h.Player.Id, card.Uid, h.Enemy.Id)).Ok);
            Assert.AreEqual(7, h.Enemy.MaxHp - h.Enemy.Hp);
        }

        [Test]
        public void 持续牌入持续区并提供常驻伤害加成()
        {
            Harness h = Build();

            CardInstance mark = h.Plant(SampleCards.HuntersMark);
            Assert.IsTrue(h.Engine.Submit(new PlayCardAction(h.Player.Id, mark.Uid)).Ok);

            Assert.AreSame(mark, h.Player.Zones.Continuous, "持续牌进持续区，不进弃牌堆");
            Assert.AreEqual(0, h.Player.Zones.Discard.Count);

            CardInstance bow = h.Plant(SampleCards.ShortbowShot);
            h.Engine.Submit(new PlayCardAction(h.Player.Id, bow.Uid, h.Enemy.Id));

            Assert.AreEqual(9, h.Enemy.MaxHp - h.Enemy.Hp, "短弓 7 + 猎人印记 2");
        }

        [Test]
        public void 秘宝牌不受职业限制且零费()
        {
            Harness h = Build();
            h.Player.LoseHp(20);
            int apBefore = h.Player.Ap;

            CardInstance elixir = h.Plant(SampleCards.Elixir);
            Assert.IsTrue(h.Engine.Submit(new PlayCardAction(h.Player.Id, elixir.Uid, h.Player.Id)).Ok);

            Assert.AreEqual(apBefore, h.Player.Ap);
            Assert.AreEqual(h.Player.MaxHp - 8, h.Player.Hp);
        }

        [Test]
        public void 危机条件词条只在危机状态生效()
        {
            Harness healthy = Build();
            CardInstance card = healthy.Plant(SampleCards.DesperateSlash);
            healthy.Engine.Submit(new PlayCardAction(healthy.Player.Id, card.Uid, healthy.Enemy.Id));
            Assert.AreEqual(6, healthy.Enemy.MaxHp - healthy.Enemy.Hp, "非危机只结算第一段");

            Harness dying = Build();
            dying.Player.LoseHp(dying.Player.MaxHp - 1);
            Assert.IsTrue(dying.Player.IsInCrisis);

            CardInstance card2 = dying.Plant(SampleCards.DesperateSlash);
            dying.Engine.Submit(new PlayCardAction(dying.Player.Id, card2.Uid, dying.Enemy.Id));
            Assert.AreEqual(13, dying.Enemy.MaxHp - dying.Enemy.Hp, "危机时追加第二段 7 点");
        }

        [Test]
        public void 充能词条牌按消耗的充能数放大伤害()
        {
            Harness h = Build();

            h.Engine.PlaceCharge(h.Player, new CardInstance(SampleCards.Elixir, h.Player.Id));
            CardInstance card = h.Plant(SampleCards.Overcharge);

            // 卡面 ChargeCost 1 → ChargeSpent = 1 → 1×5 + 4 = 9 点雷伤
            Assert.IsTrue(h.Engine.Submit(new PlayCardAction(h.Player.Id, card.Uid, h.Enemy.Id)).Ok);
            Assert.AreEqual(9, h.Enemy.MaxHp - h.Enemy.Hp);
            Assert.AreEqual(0, h.Player.Zones.Charges.Count);
        }

        [Test]
        public void 充能不足时充能词条牌打不出()
        {
            Harness h = Build();
            CardInstance card = h.Plant(SampleCards.Overcharge);

            ActionResult result = h.Engine.Submit(new PlayCardAction(h.Player.Id, card.Uid, h.Enemy.Id));
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("充能不足", result.Reason);
        }

        [Test]
        public void 敌人属性克制生效()
        {
            // 奥术飞弹带【暴击】标识，这里把骰点压到抵抗线以下，单独验证克制这一步
            Harness h = BuildWithRng(new ScriptedRng(1), SampleEnemies.Goblin());

            CardInstance bolt = h.Plant(SampleCards.ArcaneBolt);
            h.Engine.Submit(new PlayCardAction(h.Player.Id, bolt.Uid, h.Enemy.Id));

            DamageResult damage = h.Engine.Events.Last(e => e.Type == BattleEventType.DamageDealt).Damage;
            Assert.IsFalse(damage.IsCrit);
            Assert.AreEqual(Affinity.Vulnerable, damage.Affinity);
            Assert.AreEqual(9, damage.AfterModifiers);
            Assert.AreEqual(18, damage.AfterAffinity, "哥布林易损火属性，9 → 18");
        }

        [Test]
        public void 暴击与易损叠乘()
        {
            // 骰点拉满必定暴击：⌊9 × 1.5⌋ = 13 → 易损 ×2 = 26（裁定 R-01 的顺序）
            Harness h = BuildWithRng(new ScriptedRng(12), SampleEnemies.Goblin());

            CardInstance bolt = h.Plant(SampleCards.ArcaneBolt);
            h.Engine.Submit(new PlayCardAction(h.Player.Id, bolt.Uid, h.Enemy.Id));

            DamageResult damage = h.Engine.Events.Last(e => e.Type == BattleEventType.DamageDealt).Damage;
            Assert.IsTrue(damage.IsCrit);
            Assert.AreEqual(13, damage.AfterCrit);
            Assert.AreEqual(26, damage.AfterAffinity);
        }

        [Test]
        public void 流血状态在轮末造成持续伤害()
        {
            // 固定骰点让穿心射必定暴击，从而挂上流血
            IRng rng = new ScriptedRng(12);
            PlayerCharacter player = SampleDeck.CreatePlayer("p1", 2, new XorShift32Rng(1));
            Enemy dummy = SampleEnemies.TrainingDummy();

            BattleState state = BattleSetup.Create(rng, new[] { (player, Row.Front) }, new[] { (dummy, Row.Front) });
            var engine = new BattleEngine(
                state, EffectRegistry.CreateDefault(),
                SampleBehaviours.CreateRegistry(), SampleDeck.CreateStatusRegistry());
            engine.Start();

            var shot = new CardInstance(SampleCards.PiercingShot, player.Id);
            player.Zones.Hand.AddBottom(shot);
            Assert.IsTrue(engine.Submit(new PlayCardAction(player.Id, shot.Uid, dummy.Id)).Ok);

            Assert.IsTrue(dummy.Statuses.Has(SampleStatuses.Bleed), "暴击时才挂流血");
            int hpAfterHit = dummy.Hp;

            engine.Submit(new DeclareReadyAction(player.Id));

            Assert.AreEqual(hpAfterHit - 3, dummy.Hp, "轮末流血扣 3 点");
        }

        // ───────────────────────── 脚本钩子卡 ─────────────────────────

        [Test]
        public void 见切只能应对物理攻击()
        {
            // 哥布林第 1 轮挥砍（物理），第 2 轮投掷毒石（毒）
            Harness h = Build(SampleEnemies.Goblin());
            CardInstance parry = h.Plant(SampleCards.PerfectParry);

            h.Engine.Submit(new DeclareReadyAction(h.Player.Id));
            Assert.AreEqual(DamageElement.Physical, h.Engine.Pending.Intent.Element);
            Assert.IsTrue(h.Engine.Submit(new PlayCardAction(h.Player.Id, parry.Uid)).Ok);

            h.Engine.Submit(new PassResponseAction(h.Player.Id));

            // 第 2 轮意图是毒属性，见切应被钩子拒掉
            CardInstance parry2 = h.Plant(SampleCards.PerfectParry);
            h.Engine.Submit(new DeclareReadyAction(h.Player.Id));

            Assert.AreEqual(DamageElement.Poison, h.Engine.Pending.Intent.Element);
            ActionResult result = h.Engine.Submit(new PlayCardAction(h.Player.Id, parry2.Uid));

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("物理攻击", result.Reason);
        }

        [Test]
        public void 见切在玩家阶段打不出()
        {
            Harness h = Build(SampleEnemies.Goblin());
            CardInstance parry = h.Plant(SampleCards.PerfectParry);

            ActionResult result = h.Engine.Submit(new PlayCardAction(h.Player.Id, parry.Uid));
            Assert.IsFalse(result.Ok, "玩家阶段没有敌人意图可以见切");
        }

        [Test]
        public void 条件型升变牌在条件未达成时不可发动()
        {
            Harness h = Build();
            h.Plant(SampleCards.RogueAwaken);

            Assert.AreEqual(0, h.Engine.State.Stats.For(h.Player.Id, BattleStatKeys.CritsDealt));
            CollectionAssert.IsEmpty(h.Engine.TransformCandidates(h.Player), "既没暴击也没消耗充能");
        }

        [Test]
        public void 造成暴击后条件型升变牌解锁()
        {
            Harness h = Build();
            CardInstance awaken = h.Plant(SampleCards.RogueAwaken);

            h.Engine.State.Stats.Add(BattleStatKeys.CritsDealt, h.Player.Id);
            h.EndRound();

            Assert.AreEqual(DecisionKind.TransformDeclaration, h.Engine.Pending.Kind);
            CollectionAssert.Contains(h.Engine.TransformCandidates(h.Player).ToList(), awaken);
        }

        [Test]
        public void 全队消耗三个充能后条件型升变牌解锁()
        {
            Harness h = Build();
            CardInstance awaken = h.Plant(SampleCards.RogueAwaken);

            for (int i = 0; i < AwakenConditionBehaviour.RequiredTeamCharges; i++)
            {
                h.Engine.State.Stats.Add(BattleStatKeys.ChargesSpent, h.Player.Id);
            }

            h.EndRound();
            CollectionAssert.Contains(h.Engine.TransformCandidates(h.Player).ToList(), awaken);
        }

        [Test]
        public void 英雄技能牌按充能数增加连斩次数()
        {
            Harness h = Build();
            h.Player.Ap = 9;

            // B_atk = 短弓 7 + L_bonus 1 = 8 → 每刀 4 点，基础 3 刀
            Assert.AreEqual(8, h.Player.FormAttackBase);

            CardInstance hero = h.Plant(SampleCards.HeroThousandCuts);
            Assert.IsTrue(h.Engine.Submit(new PlayCardAction(h.Player.Id, hero.Uid, h.Enemy.Id)).Ok);

            Assert.AreEqual(ThousandCutsBehaviour.BaseHits * 4, h.Enemy.MaxHp - h.Enemy.Hp);
        }

        // ───────────────────────── 完整对局 ─────────────────────────

        [Test]
        public void 用样例卡组打完一局不会卡死或抛异常()
        {
            Harness h = Build(SampleEnemies.Goblin(hp: 40));

            // 每轮把能打的牌都打出去，直到分出胜负或跑满 30 轮
            for (int round = 0; round < 30 && h.Engine.State.Outcome == BattleOutcome.Ongoing; round++)
            {
                if (h.Engine.Pending.Kind == DecisionKind.TransformDeclaration)
                {
                    var candidate = h.Engine.TransformCandidates(h.Player).FirstOrDefault();
                    h.Engine.Submit(candidate != null
                        ? new DeclareTransformAction(h.Player.Id, candidate.Uid)
                        : (IBattleAction)new DeclineTransformAction(h.Player.Id));
                }

                PlayEverythingPlayable(h);
                h.EndRound();
            }

            Assert.AreEqual(BattleOutcome.Victory, h.Engine.State.Outcome);
            Assert.AreEqual(RoundPhase.Finished, h.Engine.State.Phase);
            Assert.IsTrue(h.Engine.Events.Any(e => e.Type == BattleEventType.BattleEnded));
        }

        [Test]
        public void 对首领的长局不会卡死()
        {
            Harness h = Build(SampleEnemies.AbyssLord(hp: 120));
            h.Player.Weapon = new WeaponProfile("强弓", 10);

            int rounds = 0;
            while (h.Engine.State.Outcome == BattleOutcome.Ongoing && rounds < 40)
            {
                if (h.Engine.Pending.Kind == DecisionKind.TransformDeclaration)
                {
                    var candidate = h.Engine.TransformCandidates(h.Player).FirstOrDefault();
                    h.Engine.Submit(candidate != null
                        ? new DeclareTransformAction(h.Player.Id, candidate.Uid)
                        : (IBattleAction)new DeclineTransformAction(h.Player.Id));
                }

                PlayEverythingPlayable(h);
                h.EndRound();
                rounds++;
            }

            Assert.AreNotEqual(BattleOutcome.Ongoing, h.Engine.State.Outcome, $"{rounds} 轮后仍未分出胜负");
            Assert.AreEqual(RoundPhase.Finished, h.Engine.State.Phase);
        }

        /// <summary>把手牌里当前打得出的牌全部打出去（最多扫 20 次，防止死循环）。</summary>
        private static void PlayEverythingPlayable(Harness h)
        {
            for (int guard = 0; guard < 20; guard++)
            {
                if (h.Engine.Pending.Kind != DecisionKind.PlayerActions) return;

                bool played = false;
                foreach (CardInstance card in h.Player.Zones.Hand.ToList())
                {
                    string target = card.Definition.Effects.Any(
                        e => e.Target.Scope == TargetScope.SingleEnemy)
                        ? h.Engine.State.LivingEnemies.FirstOrDefault()?.Id
                        : h.Player.Id;

                    if (h.Engine.Submit(new PlayCardAction(h.Player.Id, card.Uid, target)).Ok)
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
