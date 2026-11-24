using System.Linq;
using NUnit.Framework;
using TCGinFU.Core;

namespace TCGinFU.Core.Tests
{
    /// <summary>§4.1 / §5.2 充能区。</summary>
    public class ChargeZoneTests
    {
        [TestCase(1, 2)]
        [TestCase(2, 2)]
        [TestCase(3, 2)]
        [TestCase(4, 3)]
        [TestCase(5, 4)]
        public void 容量是玩家人数减一且不低于二(int playerCount, int expected)
        {
            Assert.AreEqual(expected, ChargeZone.CapacityFor(playerCount));
        }

        [Test]
        public void 溢出时顶出最早置入的那个()
        {
            var zone = new ChargeZone(2);
            var a = new ChargeToken(new CardInstance(TestFactory.FillerCard("a"), "p1"));
            var b = new ChargeToken(new CardInstance(TestFactory.FillerCard("b"), "p1"));
            var c = new ChargeToken(new CardInstance(TestFactory.FillerCard("c"), "p1"));

            zone.Push(a, out ChargeToken e1);
            zone.Push(b, out ChargeToken e2);
            zone.Push(c, out ChargeToken e3);

            Assert.IsNull(e1);
            Assert.IsNull(e2);
            Assert.AreSame(a, e3);
            Assert.AreEqual(2, zone.Count);
        }

        /// <summary>
        /// 「真理炼金矩阵」保住的充能要回到**原位**（最早置入端）。
        /// 当成新贴的追加到末尾的话，下一次溢出顶谁就跟着错了。
        /// </summary>
        [Test]
        public void 保住的充能放回最早置入端()
        {
            var zone = new ChargeZone(2);
            var a = new ChargeToken(new CardInstance(TestFactory.FillerCard("a"), "p1"));
            var b = new ChargeToken(new CardInstance(TestFactory.FillerCard("b"), "p1"));

            zone.Push(a, out ChargeToken _);
            zone.Push(b, out ChargeToken _);

            // 消耗一个 → 取走最早的 a；判定为「留在充能区」→ 放回去
            Assert.AreSame(a, zone.Pop());
            zone.Restore(a, out ChargeToken evicted);

            Assert.IsNull(evicted);
            Assert.AreEqual(2, zone.Count);
            Assert.AreSame(a, zone.Tokens[0], "a 仍是最早置入的那个");

            // 再贴一张，顶出去的应当还是 a
            var c = new ChargeToken(new CardInstance(TestFactory.FillerCard("c"), "p1"));
            zone.Push(c, out ChargeToken e);
            Assert.AreSame(a, e);
        }

        /// <summary>
        /// 放回时充能区已经满了，只可能是 Pop 与放回之间钩子又贴了充能。
        /// 顶出去的是那次贴入，而不是这个从没离开过充能区的。
        /// </summary>
        [Test]
        public void 放回时已满则顶出期间新贴的那个()
        {
            var zone = new ChargeZone(2);
            var a = new ChargeToken(new CardInstance(TestFactory.FillerCard("a"), "p1"));
            var b = new ChargeToken(new CardInstance(TestFactory.FillerCard("b"), "p1"));
            var mid = new ChargeToken(new CardInstance(TestFactory.FillerCard("mid"), "p1"));

            zone.Push(a, out ChargeToken _);
            zone.Push(b, out ChargeToken _);

            Assert.AreSame(a, zone.Pop());
            zone.Push(mid, out ChargeToken _); // 钩子里又贴了一个，充能区重新满了
            zone.Restore(a, out ChargeToken evicted);

            Assert.AreSame(mid, evicted);
            Assert.AreEqual(2, zone.Count);
            Assert.AreSame(a, zone.Tokens[0]);
        }

        /// <summary>§4.1：被顶出的牌回到**该卡原持有者**的弃牌堆，而不是充能区主人的。</summary>
        [Test]
        public void 顶出的牌回到原持有者的弃牌堆()
        {
            PlayerCharacter alice = TestFactory.Player("alice", chargeCapacity: 2);
            PlayerCharacter bob = TestFactory.Player("bob", TestFactory.Sheet("鲍勃"), chargeCapacity: 2);

            BattleState state = BattleSetup.Create(
                new XorShift32Rng(1),
                new[] { (alice, Row.Front), (bob, Row.Back) },
                new[] { (TestFactory.Enemy(), Row.Front) });
            var engine = new BattleEngine(state);

            // 鲍勃先往爱丽丝的充能区贴两张自己的牌，再被爱丽丝的牌顶掉第一张
            var bobCard1 = new CardInstance(TestFactory.FillerCard("b1"), bob.Id);
            var bobCard2 = new CardInstance(TestFactory.FillerCard("b2"), bob.Id);
            var aliceCard = new CardInstance(TestFactory.FillerCard("a1"), alice.Id);

            engine.PlaceCharge(alice, bobCard1);
            engine.PlaceCharge(alice, bobCard2);
            engine.PlaceCharge(alice, aliceCard);

            Assert.AreEqual(1, bob.Zones.Discard.Count, "被顶出的牌应进鲍勃的弃牌堆");
            Assert.AreSame(bobCard1, bob.Zones.Discard[0]);
            Assert.AreEqual(0, alice.Zones.Discard.Count);
        }

        [Test]
        public void 消耗充能后牌进原持有者弃牌堆()
        {
            PlayerCharacter alice = TestFactory.Player("alice");
            PlayerCharacter bob = TestFactory.Player("bob", TestFactory.Sheet("鲍勃"));

            BattleState state = BattleSetup.Create(
                new XorShift32Rng(1),
                new[] { (alice, Row.Front), (bob, Row.Back) },
                new[] { (TestFactory.Enemy(), Row.Front) });
            var engine = new BattleEngine(state);

            engine.PlaceCharge(alice, new CardInstance(TestFactory.FillerCard("b1"), bob.Id));
            Assert.IsTrue(engine.SpendCharge(alice, ChargeUse.ConvertToMp));

            Assert.AreEqual(1, bob.Zones.Discard.Count);
            Assert.AreEqual(0, alice.Zones.Discard.Count);
        }
    }

    /// <summary>§4.1 持续区上限 1 张。</summary>
    public class ContinuousZoneTests
    {
        [Test]
        public void 新持续牌顶替旧的且旧牌进弃牌堆()
        {
            PlayerCharacter player = TestFactory.Player();
            var first = new CardInstance(TestFactory.FillerCard("c1"), player.Id);
            var second = new CardInstance(TestFactory.FillerCard("c2"), player.Id);

            player.Zones.SetContinuous(first, out CardInstance replaced1);
            Assert.IsNull(replaced1);

            player.Zones.SetContinuous(second, out CardInstance replaced2);
            Assert.AreSame(first, replaced2);
            Assert.AreSame(second, player.Zones.Continuous);
        }
    }

    /// <summary>§4.1 除外区：卡面写「消耗」的一次性牌打出后离场，不进弃牌堆。</summary>
    public class ExileZoneTests
    {
        [Test]
        public void 一次性牌打出后进除外区而不是弃牌堆()
        {
            PlayerCharacter player = TestFactory.Player();
            Enemy enemy = TestFactory.Enemy(hp: 100);
            (BattleEngine engine, _, _) = TestFactory.Duel(new XorShift32Rng(7), player, enemy);
            engine.Start();

            CardInstance card = TestFactory.AddToHand(player, TestFactory.OneShotCard());
            Assert.IsTrue(engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id)).Ok);

            Assert.AreSame(card, player.Zones.Exile.Single(), "打出即消耗，牌进除外区");
            Assert.IsTrue(player.Zones.Discard.IsEmpty, "不进弃牌堆就不会被洗回主卡组");
        }

        [Test]
        public void 没写消耗的牌照旧进弃牌堆()
        {
            PlayerCharacter player = TestFactory.Player();
            Enemy enemy = TestFactory.Enemy(hp: 100);
            (BattleEngine engine, _, _) = TestFactory.Duel(new XorShift32Rng(7), player, enemy);
            engine.Start();

            CardInstance card = TestFactory.AddToHand(player, TestFactory.StrikeCard("plain"));
            Assert.IsTrue(engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id)).Ok);

            Assert.AreSame(card, player.Zones.Discard.Single());
            Assert.IsTrue(player.Zones.Exile.IsEmpty);
        }
    }

    /// <summary>§4.2 / §4.3 站位、相邻与仇恨。</summary>
    public class BattlefieldTests
    {
        [Test]
        public void 同排左右紧邻视为相邻()
        {
            var field = new Battlefield();
            var a = TestFactory.Player("a");
            var b = TestFactory.Player("b", TestFactory.Sheet("B"));
            var c = TestFactory.Player("c", TestFactory.Sheet("C"));

            field.Place(a, Row.Front);
            field.Place(b, Row.Front);
            field.Place(c, Row.Front);

            Assert.IsTrue(field.AreAdjacent(a, b));
            Assert.IsTrue(field.AreAdjacent(b, c));
            Assert.IsFalse(field.AreAdjacent(a, c), "隔了一个槽位不算相邻");
        }

        [Test]
        public void 后排与正前方的前排角色互为相邻()
        {
            var field = new Battlefield();
            var front = TestFactory.Player("front");
            var back = TestFactory.Player("back", TestFactory.Sheet("B"));

            field.PlaceAt(front, Row.Front, 2);
            field.PlaceAt(back, Row.Back, 2);

            Assert.IsTrue(field.AreAdjacent(front, back));
        }

        [Test]
        public void 后排与非同列的前排角色不相邻()
        {
            var field = new Battlefield();
            var front = TestFactory.Player("front");
            var back = TestFactory.Player("back", TestFactory.Sheet("B"));

            field.PlaceAt(front, Row.Front, 0);
            field.PlaceAt(back, Row.Back, 2);

            Assert.IsFalse(field.AreAdjacent(front, back));
        }

        [Test]
        public void 走位优先落在同列槽位()
        {
            var field = new Battlefield();
            var player = TestFactory.Player("p");
            field.PlaceAt(player, Row.Front, 2);

            Assert.IsTrue(field.SwitchRow(player));
            Assert.AreEqual(Row.Back, player.Row);
            Assert.AreEqual(2, player.Slot);
        }

        [Test]
        public void 对排已满时走位失败()
        {
            var field = new Battlefield();
            var mover = TestFactory.Player("mover");
            field.PlaceAt(mover, Row.Front, 0);

            for (int i = 0; i < Battlefield.SlotsPerRow; i++)
            {
                field.PlaceAt(TestFactory.Player($"blocker{i}", TestFactory.Sheet($"B{i}")), Row.Back, i);
            }

            Assert.IsFalse(field.SwitchRow(mover));
            Assert.AreEqual(Row.Front, mover.Row);
        }

        // ──────────────── 定向到槽位与挤开（裁定 R-17b） ────────────────

        [Test]
        public void 同排内可以改变相对位置()
        {
            var field = new Battlefield();
            var player = TestFactory.Player("p");
            field.PlaceAt(player, Row.Front, 0);

            Assert.IsTrue(field.MoveTo(player, Row.Front, 3));
            Assert.AreEqual(Row.Front, player.Row);
            Assert.AreEqual(3, player.Slot);
        }

        [Test]
        public void 原地不动不算移动()
        {
            var field = new Battlefield();
            var player = TestFactory.Player("p");
            field.PlaceAt(player, Row.Front, 1);

            Assert.IsFalse(field.MoveTo(player, Row.Front, 1), "原地不动不该白扣 1 AP");
            Assert.IsFalse(field.CanMoveTo(player, Row.Front, 1));
        }

        [Test]
        public void 落到被占槽位时把占位者朝空槽挤开()
        {
            var field = new Battlefield();
            var mover = TestFactory.Player("mover");
            var a = TestFactory.Player("a", TestFactory.Sheet("A"));
            var b = TestFactory.Player("b", TestFactory.Sheet("B"));

            // 前排：[a, b, _, mover]，mover 要插到 0 号位
            field.PlaceAt(a, Row.Front, 0);
            field.PlaceAt(b, Row.Front, 1);
            field.PlaceAt(mover, Row.Front, 3);

            Assert.IsTrue(field.MoveTo(mover, Row.Front, 0));

            // 空槽在右边（mover 腾出来的 3 号位），所以 a、b 整体右移一格
            Assert.AreEqual(0, mover.Slot);
            Assert.AreEqual(1, a.Slot);
            Assert.AreEqual(2, b.Slot);
        }

        [Test]
        public void 满排内部仍然调得动站位()
        {
            var field = new Battlefield();
            var mover = TestFactory.Player("mover");
            field.PlaceAt(mover, Row.Front, 0);

            var others = new PlayerCharacter[3];
            for (int i = 0; i < 3; i++)
            {
                others[i] = TestFactory.Player($"o{i}", TestFactory.Sheet($"O{i}"));
                field.PlaceAt(others[i], Row.Front, i + 1);
            }

            // 4 人满排：自己腾出的槽位就是挤开要用的那个空位
            Assert.IsTrue(field.CanMoveTo(mover, Row.Front, 3), "满排不该把站位锁死");
            Assert.IsTrue(field.MoveTo(mover, Row.Front, 3));

            Assert.AreEqual(3, mover.Slot);
            Assert.AreEqual(0, others[0].Slot);
            Assert.AreEqual(1, others[1].Slot);
            Assert.AreEqual(2, others[2].Slot);
        }

        [Test]
        public void 对排已满时挤不进去()
        {
            var field = new Battlefield();
            var mover = TestFactory.Player("mover");
            field.PlaceAt(mover, Row.Front, 0);

            for (int i = 0; i < Battlefield.SlotsPerRow; i++)
            {
                field.PlaceAt(TestFactory.Player($"blocker{i}", TestFactory.Sheet($"B{i}")), Row.Back, i);
            }

            Assert.IsFalse(field.CanMoveTo(mover, Row.Back, 2), "对排 4 个人都在，没有可以挤开的方向");
            Assert.IsFalse(field.MoveTo(mover, Row.Back, 2));
            Assert.AreEqual(Row.Front, mover.Row);
        }

        [Test]
        public void 挤开不会把人挤出排外()
        {
            var field = new Battlefield();
            var mover = TestFactory.Player("mover");
            var a = TestFactory.Player("a", TestFactory.Sheet("A"));

            field.PlaceAt(mover, Row.Front, 0);
            field.PlaceAt(a, Row.Back, 0);

            Assert.IsTrue(field.MoveTo(mover, Row.Back, 0));

            Assert.AreEqual((Row.Back, 0), (mover.Row, mover.Slot));
            Assert.AreEqual(Row.Back, a.Row);
            Assert.AreEqual(1, a.Slot, "被挤开的人仍然在场上");
            Assert.AreSame(a, field.At(Row.Back, 1));
        }

        [Test]
        public void 单体仇恨默认指向前排()
        {
            PlayerCharacter front = TestFactory.Player("front");
            PlayerCharacter back = TestFactory.Player("back", TestFactory.Sheet("B"));

            BattleState state = BattleSetup.Create(
                new XorShift32Rng(1),
                new[] { (front, Row.Front), (back, Row.Back) },
                new[] { (TestFactory.Enemy(), Row.Front) });

            Assert.AreSame(front, state.DefaultAggroTarget());
        }

        [Test]
        public void 前排无人时打后排上一轮输出最高者()
        {
            PlayerCharacter low = TestFactory.Player("low");
            PlayerCharacter high = TestFactory.Player("high", TestFactory.Sheet("H"));

            BattleState state = BattleSetup.Create(
                new XorShift32Rng(1),
                new[] { (low, Row.Back), (high, Row.Back) },
                new[] { (TestFactory.Enemy(), Row.Front) });

            low.DamageDealtLastRound = 5;
            high.DamageDealtLastRound = 12;

            Assert.AreSame(high, state.DefaultAggroTarget());
        }

        [Test]
        public void 倒下的角色不会被仇恨选中()
        {
            PlayerCharacter front = TestFactory.Player("front");
            PlayerCharacter back = TestFactory.Player("back", TestFactory.Sheet("B"));

            BattleState state = BattleSetup.Create(
                new XorShift32Rng(1),
                new[] { (front, Row.Front), (back, Row.Back) },
                new[] { (TestFactory.Enemy(), Row.Front) });

            front.LoseHp(front.MaxHp);
            Assert.AreSame(back, state.DefaultAggroTarget());
        }
    }

    /// <summary>牌堆基本操作。</summary>
    public class CardPileTests
    {
        [Test]
        public void 抽空牌堆返回空而不抛异常()
        {
            var pile = new CardPile("空堆");
            Assert.IsNull(pile.DrawTop());
            Assert.AreEqual(0, pile.DrawTop(3).Count);
        }

        [Test]
        public void 洗牌是确定性的()
        {
            CardPile Build()
            {
                var pile = new CardPile("堆");
                for (int i = 0; i < 20; i++)
                {
                    pile.AddBottom(new CardInstance(TestFactory.FillerCard($"c{i}"), "p1"));
                }

                return pile;
            }

            CardPile first = Build();
            CardPile second = Build();
            first.Shuffle(new XorShift32Rng(42));
            second.Shuffle(new XorShift32Rng(42));

            CollectionAssert.AreEqual(
                first.Select(c => c.Definition.Id).ToList(),
                second.Select(c => c.Definition.Id).ToList());
        }
    }
}
