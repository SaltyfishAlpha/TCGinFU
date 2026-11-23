using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TCGinFU.Content.Loading;
using TCGinFU.Core;
using TCGinFU.Core.Tests;

namespace TCGinFU.Tests
{
    /// <summary>
    /// §7.1 的两个维度：**类别**（从哪来）× **打法**（什么时候打）。
    /// 升变牌 / 形态牌 / 敌人意图是打法之外的**标记**，英雄技能牌是职业牌上的标记——
    /// 这些都不是第三、第四种打法。
    ///
    /// <para>这组测试守的是「两个维度不许再塌回一个字段」：
    /// 一旦有人把标记和打法重新合并，形态卡组里那四张「受击时打出」的牌就会第一个失效。</para>
    /// </summary>
    [TestFixture]
    public sealed class CardClassificationTests
    {
        // ──────────────────────── 两个维度彼此独立 ────────────────────────

        [Test]
        public void 形态牌保留自己的打法()
        {
            CardDefinition timing = TestFactory.FormStrike("f1", play: CardPlay.Timing);
            CardDefinition skill = TestFactory.FormStrike("f2", play: CardPlay.Skill);

            Assert.That(timing.Role, Is.EqualTo(CardRole.Form));
            Assert.That(skill.Role, Is.EqualTo(CardRole.Form));
            Assert.That(timing.Play, Is.EqualTo(CardPlay.Timing), "形态标记不该吃掉打法");
            Assert.That(skill.Play, Is.EqualTo(CardPlay.Skill));
        }

        [Test]
        public void 英雄技能牌是职业牌上的标记而不是打法()
        {
            CardDefinition hero = TestFactory.HeroCard(play: CardPlay.Timing);

            Assert.That(hero.Category, Is.EqualTo(CardCategory.Class), "英雄技能牌属于职业牌");
            Assert.That(hero.Role, Is.EqualTo(CardRole.Normal), "英雄技能不占标记位");
            Assert.That(hero.IsHeroSkill, Is.True);
            Assert.That(hero.Play, Is.EqualTo(CardPlay.Timing), "英雄技能牌可以是任意打法");
        }

        [Test]
        public void 升变牌不按打法结算()
        {
            CardDefinition transform = TestFactory.TransformCard();

            Assert.That(transform.Role, Is.EqualTo(CardRole.Transform));
            Assert.That(transform.Play, Is.Null, "升变牌走升变宣告，没有打法");
            Assert.That(transform.CopyLimit, Is.EqualTo(1), "§3.2 同名升变牌限 1 张");
        }

        // 「能不能响应」这条规则本身由 CardEffectFixTests 走引擎验（F22 / F22b / F23 / F24）——
        // 它现在就是一句 `Play == CardPlay.Timing`，没有中间层可以单独测。

        // ──────────────────────── 内容包里的 play / role ────────────────────────

        [Test]
        public void 漏写play的牌不会被默认成技能牌()
        {
            // 默认成「技能」最糟：卡面写着「受击时打出」的牌会安静地变成打不出去的牌，
            // 玩家只会觉得这张卡坏了，不会觉得是数据漏了一行。
            // 老包（写 kind 的那种）也落在这条错误上，所以错误里要说清改法。
            LoadReport report = LoadCards("{ \"id\": \"x\", \"category\": \"Armor\", \"kind\": \"Timing\" }");

            Assert.That(report.IsValid, Is.False);
            Assert.That(report.ToString(), Does.Contain("缺少 play"));
            Assert.That(report.ToString(), Does.Contain("kind 已拆成"));
        }

        [Test]
        public void 升变牌写了play会报错()
        {
            LoadReport report = LoadCards(
                "{ \"id\": \"x\", \"role\": \"Transform\", \"play\": \"Skill\" }");

            Assert.That(report.ToString(), Does.Contain("不按打法结算"));
        }

        [Test]
        public void 形态牌必须声明属于哪一组形态()
        {
            LoadReport missing = LoadCards(
                "{ \"id\": \"x\", \"role\": \"Form\", \"play\": \"Timing\" }");
            Assert.That(missing.ToString(), Does.Contain("缺少 formId"));

            LoadReport stray = LoadCards(
                "{ \"id\": \"y\", \"play\": \"Skill\", \"formId\": \"phantom\" }");
            Assert.That(stray.ToString(), Does.Contain("只有 role 为 Form"));
        }

        [Test]
        public void 敌人意图不能写在卡牌文件里()
        {
            // 写在这里的「敌人牌」既进不了卡组也没人宣告，是一张永远不会出现的牌
            LoadReport report = LoadCards(
                "{ \"id\": \"x\", \"role\": \"EnemyIntent\", \"play\": \"Skill\" }");

            Assert.That(report.ToString(), Does.Contain("enemies/*.json"));
        }

        [Test]
        public void 合法的形态时机牌读得进来()
        {
            var db = new ContentDatabase();
            LoadReport report = LoadCards(
                "{ \"id\": \"smoke\", \"category\": \"Class\", \"role\": \"Form\","
                + " \"play\": \"Timing\", \"formId\": \"phantom\" }",
                db);

            Assert.That(report.IsValid, Is.True, report.ToString());

            CardDefinition card = db.Card("smoke");
            Assert.That(card.Role, Is.EqualTo(CardRole.Form));
            Assert.That(card.Play, Is.EqualTo(CardPlay.Timing));
            Assert.That(card.FormId, Is.EqualTo("phantom"));
        }

        // ──────────────────────── 构筑校验 ────────────────────────

        [Test]
        public void 形态组混进别组的形态牌会被挡下()
        {
            DeckList deck = DeckWithFormCards(
                Enumerable.Range(0, DeckList.FormDeckSize)
                    .Select(i => TestFactory.FormStrike($"form{i}", formId: i == 0 ? "hawkeye" : "phantom"))
                    .ToList());

            DeckValidationResult result = DeckValidator.Validate(deck);

            Assert.That(result.ToString(), Does.Contain("混了 2 个形态的牌"));
        }

        [Test]
        public void 同组形态牌照常通过校验()
        {
            DeckList deck = DeckWithFormCards(
                Enumerable.Range(0, DeckList.FormDeckSize)
                    .Select(i => TestFactory.FormStrike($"form{i}", formId: "phantom"))
                    .ToList());

            Assert.That(DeckValidator.Validate(deck).ToString(), Does.Not.Contain("形态"));
        }

        // ──────────────────────── 脚手架 ────────────────────────

        private static LoadReport LoadCards(string cardJson, ContentDatabase db = null)
        {
            var report = new LoadReport();
            ContentLoader.LoadPack(
                db ?? new ContentDatabase(),
                rel => rel switch
                {
                    "pack.json" => "{ \"schemaVersion\": 1, \"cards\": [\"cards/test.json\"] }",
                    "cards/test.json" => "{ \"cards\": [" + cardJson + "] }",
                    _ => null,
                },
                report);

            return report;
        }

        /// <summary>一份只在形态组上做文章的合法卡表：30 张主卡组里插 1 张升变牌。</summary>
        private static DeckList DeckWithFormCards(List<CardDefinition> formCards)
        {
            var deck = new DeckList();
            CardDefinition transform = TestFactory.TransformCard("phantom_nightwalk");

            deck.MainDeck.Add(transform);
            for (int i = 0; i < DeckList.MainDeckSize - 1; i++)
            {
                deck.MainDeck.Add(TestFactory.FillerCard($"filler{i / 3}"));
            }

            deck.FormDecks[transform.Id] = formCards;
            return deck;
        }
    }
}
