using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TCGinFU.Content.Json;
using TCGinFU.Content.Loading;
using TCGinFU.Core;
using TCGinFU.Presentation;

namespace TCGinFU.Tests
{
    /// <summary>
    /// 老版本（Godot）数据迁移的验收测试，对应 data_architecture.md §8 的 T-1 ~ T-7。
    /// 数据读的是 <c>Assets/StreamingAssets/Content</c> 下的真实内容包。
    /// </summary>
    [TestFixture]
    public sealed class LegacyContentTests
    {
        private ContentDatabase _db;
        private LoadReport _report;
        private string _root;

        [OneTimeSetUp]
        public void LoadContent()
        {
            _root = ContentPaths.Resolve();
            Assert.That(_root, Is.Not.Null,
                $"找不到内容包目录 {ContentPaths.RepoRelative}（可设环境变量 {ContentPaths.EnvironmentVariable}）");

            _db = ContentPaths.LoadDefault(out _report);
        }

        // ── T-1 内容包加载 ──

        [Test]
        public void T01_内容包加载无错误()
        {
            Assert.That(_report.IsValid, Is.True, _report.ToString());
        }

        [Test]
        public void T01_条目数量符合预期()
        {
            Assert.That(_db.Cards, Has.Count.EqualTo(49), "卡牌种类");
            Assert.That(_db.Characters, Has.Count.EqualTo(2), "角色");
            Assert.That(_db.Enemies, Has.Count.EqualTo(2), "敌人（首领 + 部位）");
            Assert.That(_db.Decks, Has.Count.EqualTo(2), "卡组");
        }

        // ── T-2 卡组张数 ──

        [TestCase("miku_starter")]
        [TestCase("weiting_starter")]
        public void T02_主卡组30张且形态组各10张(string deckId)
        {
            var report = new LoadReport();
            DeckList deck = _db.BuildDeck(deckId, report);

            Assert.That(report.IsValid, Is.True, report.ToString());
            Assert.That(deck.MainDeck, Has.Count.EqualTo(DeckList.MainDeckSize));
            Assert.That(deck.FormDecks, Has.Count.EqualTo(2), "每名角色 2 张升变牌各配 1 组形态组");

            foreach (KeyValuePair<string, List<CardDefinition>> form in deck.FormDecks)
            {
                Assert.That(form.Value, Has.Count.EqualTo(DeckList.FormDeckSize), $"形态组 {form.Key}");
            }
        }

        // ── T-3 构筑校验（§5.3 的重新归类是否真的合法） ──

        [TestCase("miku")]
        [TestCase("weiting")]
        public void T03_卡组通过构筑校验(string heroId)
        {
            CharacterContent character = _db.Character(heroId);
            DeckList deck = _db.BuildDeck(character.DeckId, new LoadReport());

            DeckValidationResult result = DeckValidator.Validate(deck, character.Sheet);
            Assert.That(result.IsValid, Is.True, result.ToString());
        }

        [TestCase("miku", 12, 8, 7, 3)]
        [TestCase("weiting", 13, 5, 8, 4)]
        public void T03_来源配比与文档一致(string heroId, int cls, int weapon, int armor, int treasure)
        {
            DeckList deck = _db.BuildDeck(_db.Character(heroId).DeckId, new LoadReport());

            Assert.That(deck.CountOf(CardCategory.Class), Is.EqualTo(cls), "职业牌");
            Assert.That(deck.CountOf(CardCategory.Weapon), Is.EqualTo(weapon), "武器牌");
            Assert.That(deck.CountOf(CardCategory.Armor), Is.EqualTo(armor), "防具牌");
            Assert.That(deck.CountOf(CardCategory.Treasure), Is.EqualTo(treasure), "秘宝牌");
        }

        // ── T-4 Id 唯一性（老版本 validate() 也查这条） ──

        [Test]
        public void T04_behaviourId不重复()
        {
            var duplicates = _db.Cards.Values
                .Where(c => c.BehaviourId != null)
                .GroupBy(c => c.BehaviourId)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} ← {string.Join(" / ", g.Select(c => c.Id))}")
                .ToList();

            Assert.That(duplicates, Is.Empty, "behaviourId 必须唯一：\n" + string.Join("\n", duplicates));
        }

        /// <summary>
        /// 四种来源都要有卡覆盖到：少掉一种意味着那条代码路径在整个内容包里
        /// 一次都没被走到，引擎测试再绿也说明不了它在真实数据上成立。
        /// </summary>
        [Test]
        public void T04_内容包覆盖全部四种卡牌来源()
        {
            CollectionAssert.AreEquivalent(
                System.Enum.GetValues(typeof(CardCategory)).Cast<CardCategory>().ToList(),
                _db.Cards.Values.Select(c => c.Category).Distinct().ToList());
        }

        /// <summary>
        /// 三种打法都要有卡覆盖到（§7.1）。敌人的牌不在 <c>_db.Cards</c> 里
        /// （它们挂在意图上，见 I01），但意图不按打法结算，所以这条只数玩家卡牌。
        /// </summary>
        [Test]
        public void T04_内容包覆盖全部三种打法()
        {
            CollectionAssert.AreEquivalent(
                System.Enum.GetValues(typeof(CardPlay)).Cast<CardPlay>().ToList(),
                _db.Cards.Values.Where(c => c.Play.HasValue).Select(c => c.Play.Value).Distinct().ToList());
        }

        /// <summary>
        /// 打法之外的标记也要有卡覆盖到：普通牌、升变牌、形态牌、敌人意图。
        /// 少掉一种意味着那条代码路径在整个内容包里一次都没被走到。
        /// </summary>
        [Test]
        public void T04_内容包覆盖全部卡牌标记()
        {
            var roles = _db.Cards.Values.Select(c => c.Role)
                .Concat(_db.Enemies.Values
                    .SelectMany(e => e.Intents.Values)
                    .Where(i => i.Card != null)
                    .Select(i => i.Card.Role))
                .Distinct()
                .ToList();

            CollectionAssert.AreEquivalent(
                System.Enum.GetValues(typeof(CardRole)).Cast<CardRole>().ToList(), roles);
        }

        /// <summary>
        /// <b>英雄技能牌目前整个内容包一张都没有</b>——「命刻之战」的两名角色都没到
        /// 10 级精通，按 §3.3 本来就带不了。这是**内容待补**，不是代码缺陷，所以如实记在这里；
        /// 等内容包补上英雄技能牌，把这条测试删掉即可。
        /// §3.3 的精通校验规则本身由 <c>DeckValidatorTests</c> 用构造卡守着。
        /// </summary>
        [Test]
        public void T04_内容包还没有英雄技能牌()
        {
            CollectionAssert.IsEmpty(
                _db.Cards.Values.Where(c => c.IsHeroSkill).Select(c => c.Id).ToList(),
                "内容包补上英雄技能牌了——请删掉这条测试，并确认 DeckValidator 的 10 级精通校验真的作用到了它身上");
        }

        [Test]
        public void T04_每张卡都有规则文字()
        {
            var blank = _db.Cards.Values
                .Where(c => string.IsNullOrWhiteSpace(c.RulesText))
                .Select(c => c.Id)
                .ToList();

            Assert.That(blank, Is.Empty, "缺少卡面文字：" + string.Join(", ", blank));
        }

        // ── T-5 形态公式求值 == 老数据在 5 级构筑下的具体值 ──

        [Test]
        public void T05_形态牌公式在5级构筑下算回老数值()
        {
            // 老卡面写的是「B_atk + 4 点（本 5 级构筑为 12 点）」。
            // 迁移时丢掉了 12、保留了公式，所以这里反过来验证公式能算回 12。
            var failures = new List<string>();

            foreach (CharacterContent character in _db.Characters.Values)
            {
                var ctx = new ValueContext(
                    character.Weapon.DamageAt(character.Sheet.EquipTier),
                    character.Armor.ShieldAt(character.Sheet.EquipTier),
                    character.Sheet.LevelBonus,
                    character.Sheet.TotalLevel);

                foreach ((string cardId, int expected) in LegacyFormDamage(character.Id))
                {
                    CardDefinition card = _db.Card(cardId);
                    EffectData damage = card.Effects.FirstOrDefault(e => e.Kind == EffectKind.Damage);

                    if (damage == null) { failures.Add($"{cardId}：没有伤害效果"); continue; }

                    int actual = damage.Amount.Resolve(ctx);
                    if (actual != expected) failures.Add($"{cardId}：算出 {actual}，老数据 {expected}");
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        /// <summary>直接从内容包 JSON 的 <c>unmapped.legacyDamageAtLevel5</c> 取老数值。</summary>
        private IEnumerable<(string CardId, int Damage)> LegacyFormDamage(string ownerId)
        {
            var source = new FileContentSource(_root);
            JsonValue root = Json.Parse(source.Read("cards/forms.json"));

            foreach (JsonValue card in root["cards"].Items)
            {
                if (card["owner"].AsString() != ownerId) continue;

                JsonValue legacy = card["unmapped"]["legacyDamageAtLevel5"];
                if (legacy.Exists) yield return (card["id"].AsString(), legacy.AsInt());
            }
        }

        // ── T-6 角色面板 == 老游戏面板 ──

        [TestCase("miku", 40, 35)]
        [TestCase("weiting", 35, 50)]
        public void T06_角色面板与老游戏一致(string heroId, int hp, int mp)
        {
            CharacterContent character = _db.Character(heroId);

            Assert.That(character.Sheet.MaxHp, Is.EqualTo(hp), "最大 HP");
            Assert.That(character.Sheet.MaxMp, Is.EqualTo(mp), "最大 MP");
            // JSON 里记的老数值应当和派生结果对得上，否则 hpBonus/mpBonus 配错了
            Assert.That(character.LegacyHp, Is.EqualTo(hp));
            Assert.That(character.LegacyMp, Is.EqualTo(mp));
        }

        [Test]
        public void T06_装备档案让B_atk与B_def回到老数值()
        {
            // 老卡面注明：米阔阔 B_atk=8，维汀 B_atk=7，两人 B_def=8
            Assert.That(BAtk("miku"), Is.EqualTo(8), "米阔阔 B_atk");
            Assert.That(BAtk("weiting"), Is.EqualTo(7), "维汀 B_atk");
            Assert.That(BDef("miku"), Is.EqualTo(8), "米阔阔 B_def");
            Assert.That(BDef("weiting"), Is.EqualTo(8), "维汀 B_def");
        }

        private int BAtk(string id)
        {
            CharacterContent c = _db.Character(id);
            return c.Weapon.DamageAt(c.Sheet.EquipTier) + c.Sheet.LevelBonus;
        }

        private int BDef(string id)
        {
            CharacterContent c = _db.Character(id);
            return c.Armor.ShieldAt(c.Sheet.EquipTier) + c.Sheet.LevelBonus;
        }

        // ── T-7 用真实数据真的打一局 ──

        [Test]
        public void T07_能用真实数据开局()
        {
            var report = new LoadReport();
            BattleEngine engine = ContentScenario.CreateEngine(
                _db, new XorShift32Rng(20261118),
                ContentScenario.ClockworkHeroes, ContentScenario.ClockworkEnemies, report);

            Assert.That(report.IsValid, Is.True, report.ToString());
            Assert.That(engine, Is.Not.Null);

            BattleState state = engine.State;
            Assert.That(state.Players, Has.Count.EqualTo(2));
            Assert.That(state.Enemies, Has.Count.EqualTo(2));
            Assert.That(state.FindPlayer("miku").MaxHp, Is.EqualTo(40));
            Assert.That(state.Enemies.First(e => e.Id == "boss").MaxHp, Is.EqualTo(140));
            // 第 1 轮抽 5 张（§8 ②）
            Assert.That(state.FindPlayer("miku").Zones.Hand, Has.Count.EqualTo(5));
        }

        [Test]
        public void T07_整局能推进到结束而不卡死()
        {
            var session = new BattleSession
            {
                Seed = 20261118,
                EnemyPreset = BattleScenario.命刻之战,
            };

            session.SkipToEnd();

            Assert.That(session.State.Outcome, Is.Not.EqualTo(BattleOutcome.Ongoing),
                "推进到上限仍未结束，说明某个阶段没能推进：" + session.Message);
        }
    }
}
