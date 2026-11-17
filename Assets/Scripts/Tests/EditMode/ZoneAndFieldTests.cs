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
