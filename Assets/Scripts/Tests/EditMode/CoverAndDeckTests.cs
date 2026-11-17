using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TCGinFU.Core;

namespace TCGinFU.Core.Tests
{
    /// <summary>§4.3 援护机制（裁定 R-12）。</summary>
    public class CoverTests
    {
        private static CardDefinition CoverCard(string id = "cover") =>
            new(id, "挺身而出", CardCategory.Armor, CardKind.Timing)
            {
                ApCost = 0,
                Effects = new[] { EffectData.Cover() },
            };

        private static (BattleEngine engine, PlayerCharacter front, PlayerCharacter back, Enemy enemy)
            Setup(int backSlot = 0)
        {
            PlayerCharacter front = TestFactory.Player("front", TestFactory.Sheet("前排"));
            PlayerCharacter back = TestFactory.Player("back", TestFactory.Sheet("后排"));
            Enemy enemy = TestFactory.Enemy(intents: new[] { TestFactory.Attack(20) });

            TestFactory.FillDeck(front, 20);
            TestFactory.FillDeck(back, 20);

            BattleState state = BattleSetup.Create(
                new XorShift32Rng(3),
                new[] { (front, Row.Front), (back, Row.Back) },
                new[] { (enemy, Row.Front) });

            // BattleSetup 默认把后排放到 0 号槽（与前排同列即相邻），按用例挪到指定槽位
            state.PlayerField.Remove(back);
            state.PlayerField.PlaceAt(back, Row.Back, backSlot);

            return (new BattleEngine(state), front, back, enemy);
        }

        [Test]
        public void 相邻队友可以替承伤()
        {
            (BattleEngine engine, PlayerCharacter front, PlayerCharacter back, _) = Setup(backSlot: 0);
            engine.Start();

            CardInstance card = TestFactory.AddToHand(back, CoverCard());
            int frontHpBefore = front.Hp;
            int backHpBefore = back.Hp;

            engine.Submit(new DeclareReadyAction(front.Id));
            engine.Submit(new DeclareReadyAction(back.Id));

            Assert.AreEqual(DecisionKind.EnemyResponse, engine.Pending.Kind);
            Assert.AreSame(front, engine.CurrentIntentTargets.Single(), "单体攻击默认指向前排");

            Assert.IsTrue(engine.Submit(new PlayCardAction(back.Id, card.Uid, front.Id)).Ok);
            engine.Submit(new PassResponseAction(front.Id));
            engine.Submit(new PassResponseAction(back.Id));

            Assert.AreEqual(frontHpBefore, front.Hp, "伤害被援护者接走");
            Assert.Less(back.Hp, backHpBefore);
        }

        [Test]
        public void 非相邻队友无法援护()
        {
            // 后排放到 2 号槽，与 0 号槽的前排既不同列也不同排
            (BattleEngine engine, PlayerCharacter front, PlayerCharacter back, _) = Setup(backSlot: 2);
            engine.Start();

            CardInstance card = TestFactory.AddToHand(back, CoverCard());
            int frontHpBefore = front.Hp;
            int backHpBefore = back.Hp;

            engine.Submit(new DeclareReadyAction(front.Id));
            engine.Submit(new DeclareReadyAction(back.Id));
            engine.Submit(new PlayCardAction(back.Id, card.Uid, front.Id));
            engine.Submit(new PassResponseAction(front.Id));
            engine.Submit(new PassResponseAction(back.Id));

            Assert.Less(front.Hp, frontHpBefore, "援护未生效，伤害仍落在前排");
            Assert.AreEqual(backHpBefore, back.Hp);
        }
    }

    /// <summary>§3.2 主卡组构筑校验。</summary>
    public class DeckValidatorTests
    {
        private static CardDefinition Card(string id, CardCategory category, CardKind kind = CardKind.Skill)
            => new(id, id, category, kind);

        /// <summary>造一份合法的 30 张卡表：14 职业 + 6 武器 + 6 防具 + 4 秘宝。</summary>
        private static DeckList ValidDeck()
        {
            var deck = new DeckList();
            Add(deck, CardCategory.Class, "cls", 14);
            Add(deck, CardCategory.Weapon, "wpn", 6);
            Add(deck, CardCategory.Armor, "arm", 6);
            Add(deck, CardCategory.Treasure, "tre", 4);
            return deck;
        }

        private static void Add(DeckList deck, CardCategory category, string prefix, int count)
        {
            // 每种名字最多 3 张，凑够数量
            int index = 0;
            while (count > 0)
            {
                int copies = System.Math.Min(3, count);
                for (int i = 0; i < copies; i++) deck.MainDeck.Add(Card($"{prefix}{index}", category));
                count -= copies;
                index++;
            }
        }

        [Test]
        public void 合法卡表通过校验()
        {
            DeckValidationResult result = DeckValidator.Validate(ValidDeck());
            Assert.IsTrue(result.IsValid, result.ToString());
        }

        [Test]
        public void 张数不是三十被拒绝()
        {
            DeckList deck = ValidDeck();
            deck.MainDeck.RemoveAt(0);

            DeckValidationResult result = DeckValidator.Validate(deck);
            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("30", result.ToString());
        }

        [Test]
        public void 职业牌少于十二张被拒绝()
        {
            var deck = new DeckList();
            Add(deck, CardCategory.Class, "cls", 11);
            Add(deck, CardCategory.Weapon, "wpn", 8);
            Add(deck, CardCategory.Armor, "arm", 8);
            Add(deck, CardCategory.Treasure, "tre", 3);

            DeckValidationResult result = DeckValidator.Validate(deck);
            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("职业牌", result.ToString());
        }

        [Test]
        public void 同名普通牌超过三张被拒绝()
        {
            DeckList deck = ValidDeck();
            deck.MainDeck[0] = Card("wpn0", CardCategory.Weapon);
            deck.MainDeck[1] = Card("wpn0", CardCategory.Weapon);

            DeckValidationResult result = DeckValidator.Validate(deck);
            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("最多编入 3 张", result.ToString());
        }

        [Test]
        public void 同名升变牌超过一张被拒绝()
        {
            DeckList deck = ValidDeck();
            CardDefinition transform = TestFactory.TransformCard("awaken");
            deck.MainDeck[0] = transform;
            deck.MainDeck[1] = transform;
            deck.FormDecks["awaken"] = FormDeck();

            DeckValidationResult result = DeckValidator.Validate(deck);
            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("同名升变牌限 1 张", result.ToString());
        }

        [Test]
        public void 升变牌缺少形态卡组被拒绝()
        {
            DeckList deck = ValidDeck();
            deck.MainDeck[0] = TestFactory.TransformCard("awaken");

            DeckValidationResult result = DeckValidator.Validate(deck);
            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("缺少对应的形态额外卡组", result.ToString());
        }

        [Test]
        public void 形态卡组不是十张被拒绝()
        {
            DeckList deck = ValidDeck();
            deck.MainDeck[0] = TestFactory.TransformCard("awaken");
            deck.FormDecks["awaken"] = FormDeck().Take(9).ToList();

            DeckValidationResult result = DeckValidator.Validate(deck);
            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("10 张", result.ToString());
        }

        [Test]
        public void 带升变牌的合法卡表通过校验()
        {
            DeckList deck = ValidDeck();
            deck.MainDeck[0] = TestFactory.TransformCard("awaken");
            deck.FormDecks["awaken"] = FormDeck();

            DeckValidationResult result = DeckValidator.Validate(deck);
            Assert.IsTrue(result.IsValid, result.ToString());
        }

        [Test]
        public void 多张不同职业的升变牌可以同时携带()
        {
            DeckList deck = ValidDeck();
            deck.MainDeck[0] = TestFactory.TransformCard("awaken_a", "浪客");
            deck.MainDeck[1] = TestFactory.TransformCard("awaken_b", "神射手");
            deck.FormDecks["awaken_a"] = FormDeck();
            deck.FormDecks["awaken_b"] = FormDeck();

            DeckValidationResult result = DeckValidator.Validate(deck);
            Assert.IsTrue(result.IsValid, result.ToString());
        }

        [Test]
        public void 多余的形态卡组被拒绝()
        {
            DeckList deck = ValidDeck();
            deck.FormDecks["orphan"] = FormDeck();

            DeckValidationResult result = DeckValidator.Validate(deck);
            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("没有对应的升变牌", result.ToString());
        }

        private static List<CardDefinition> FormDeck()
            => Enumerable.Range(0, DeckList.FormDeckSize)
                .Select(i => TestFactory.FormStrike($"form{i}"))
                .ToList();
    }
}
