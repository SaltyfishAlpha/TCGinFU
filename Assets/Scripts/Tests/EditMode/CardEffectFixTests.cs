using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TCGinFU.Content.Loading;
using TCGinFU.Core;
using TCGinFU.Core.Tests;
using TCGinFU.Presentation;
using TCGinFU.Scripting;

namespace TCGinFU.Tests
{
    /// <summary>
    /// 「声明了 behaviourId 却没人实现」那一批卡的验收。
    ///
    /// <para>这些卡的共同症状是**只兑现了卡面前半句**：伤害照常结算，所以战斗跑得通、
    /// 老测试全绿，但回血、抽牌、削盾、净化这些后半句静默失效。
    /// 引擎把「声明了但没注册」当纯数据卡放过（见 <see cref="BehaviourRegistry.Resolve"/>），
    /// 这是故意的——做占位内容时很有用——代价就是漏写不会报错。</para>
    ///
    /// <para>所以这一组**只断言卡面后半句**：前半句有别的测试守着。
    /// 断言依据是卡面文字，不是老版本 GDScript。</para>
    /// </summary>
    [TestFixture]
    public sealed class CardEffectFixTests
    {
        private ContentDatabase _db;
        private string _root;

        [OneTimeSetUp]
        public void LoadContent()
        {
            _root = ContentPaths.Resolve();
            Assert.That(_root, Is.Not.Null, "找不到内容包目录");
            _db = ContentPaths.LoadDefault(out LoadReport _, _root);
        }

        // ──────────────────────────── 抽牌 ────────────────────────────

        [Test]
        public void F01_顺风充能真的会抽牌()
        {
            // 卡面：「**抽1张牌**，并将此牌直接置入……充能区」。
            // 脚本注释写着「抽牌走数据」，但卡面 effects 是空数组——
            // 于是这张牌只把自己贴进了充能区，那 1 张牌从来没抽过。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = Duel();

            int hand = player.Zones.Hand.Count;
            CardInstance card = TestFactory.AddToHand(player, _db.Card("tailwind_charge"));

            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid));
            Assert.That(result.Ok, Is.True, result.Reason);

            // 加进来 +1、打出去 -1（变成充能不进弃牌堆）、抽 1 张 +1 → 净 +1
            Assert.That(player.Zones.Hand.Count, Is.EqualTo(hand + 1), "没有抽到那 1 张牌");
        }

        [Test]
        public void F02_以太重组坩埚先贴充能再抽牌()
        {
            // 卡面：「从你的弃牌堆将最多2张牌直接贴入……充能区，**随后**抽1张牌。」
            //
            // 顺序是有意义的：先抽牌会在牌库空时把弃牌堆洗回卡组，
            // 「从弃牌堆贴 2 张」就没得可贴了。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = Duel();

            for (int i = 0; i < 2; i++)
            {
                player.Zones.Discard.AddTop(new CardInstance(_db.Card("ambush_opening"), player.Id));
            }

            int hand = player.Zones.Hand.Count;
            int charges = player.Zones.Charges.Count;

            CardInstance card = TestFactory.AddToHand(player, _db.Card("aether_reassembly_crucible"));
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, player.Id));
            Assert.That(result.Ok, Is.True, result.Reason);

            Assert.That(player.Zones.Charges.Count, Is.GreaterThan(charges), "弃牌堆里的牌没被贴成充能");
            // 加进来 +1、打出去 -1、抽 1 张 +1 → 净 +1
            Assert.That(player.Zones.Hand.Count, Is.EqualTo(hand + 1), "没有抽到那 1 张牌");
        }

        [Test]
        public void F03_弱点解析揭示弱点并抽牌()
        {
            // 卡面：「造成4点魔法伤害，**揭示目标1项属性弱点并抽1张牌**。」
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            int hand = player.Zones.Hand.Count;
            CardInstance card = TestFactory.AddToHand(player, _db.Card("weakness_analysis"));

            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));
            Assert.That(result.Ok, Is.True, result.Reason);

            Assert.That(enemy.Statuses.Has("weakness_revealed"), Is.True, "没有揭示弱点");
            Assert.That(player.Zones.Hand.Count, Is.EqualTo(hand + 1), "没有抽到那 1 张牌");
        }

        [Test]
        public void F04_秘法冲击只对已揭示或带负面的目标加伤()
        {
            // 卡面：「造成6点魔法伤害；**若目标已被揭示属性弱点或处于负面状态**，额外+3。」
            // 只验「加了伤」会让一个无条件 +3 的实现也通过，所以必须同时钉住反面。
            CardDefinition def = _db.Card("arcane_bolt");

            (BattleEngine clean, PlayerCharacter p1, Enemy e1) = Duel();
            var plain = new AttackContext(p1, e1, 6, DamageElement.Light, CritResult.None)
            {
                Card = new CardInstance(def, p1.Id),
            };
            clean.ResolveAttack(plain);
            Assert.That(plain.Result.AfterModifiers, Is.EqualTo(6), "干净目标不该吃 +3");

            (BattleEngine marked, PlayerCharacter p2, Enemy e2) = Duel();
            marked.ApplyStatus(e2, marked.Statuses.Create("weakness_revealed", -1, 1));

            var boosted = new AttackContext(p2, e2, 6, DamageElement.Light, CritResult.None)
            {
                Card = new CardInstance(def, p2.Id),
            };
            marked.ResolveAttack(boosted);
            Assert.That(boosted.Result.AfterModifiers, Is.EqualTo(9), "已揭示弱点的目标应当 6+3");
        }

        [Test]
        public void F05_痛觉铭刻的自伤无视护盾且回蓝抽牌()
        {
            // 卡面：「立即对自身造成2点**无视护盾**的刺痛伤害；……**回复10 MP并抽1张牌**。」
            //
            // 自伤原本写在数据层的 Damage 里，会被自动双盾整个吃掉——
            // 等于这张牌对自己毫发无伤，连带「灵智回流」也跟着哑火，而那正是它的用法。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = Duel();

            player.Shields.Grant(ShieldKind.Physical, 99);
            player.Shields.Grant(ShieldKind.Magical, 99);

            player.SpendMp(player.Mp);
            int mp = player.Mp;
            int hp = player.Hp;
            int hand = player.Zones.Hand.Count;

            CardInstance card = TestFactory.AddToHand(player, _db.Card("pain_inscription"));
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid));
            Assert.That(result.Ok, Is.True, result.Reason);

            Assert.That(hp - player.Hp, Is.EqualTo(2), "刺痛应当无视护盾直接进血");
            Assert.That(player.Mp, Is.EqualTo(mp + 10), "没回那 10 点 MP");
            Assert.That(player.Zones.Hand.Count, Is.EqualTo(hand + 1), "没有抽到那 1 张牌");
        }

        // ──────────────────────────── 回血 ────────────────────────────

        [Test]
        public void F06_噬魂狂宴让全队回血回蓝()
        {
            // 卡面：「对单个目标造成B_atk点暗属性伤害，**全队所有角色回复6 HP与10 MP**。」
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            player.LoseHp(20);
            player.SpendMp(player.Mp);
            int hp = player.Hp;
            int mp = player.Mp;

            CardInstance card = TestFactory.AddToHand(player, _db.Card("soul_devouring_feast"));
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));
            Assert.That(result.Ok, Is.True, result.Reason);

            Assert.That(player.Hp, Is.EqualTo(hp + 6));
            Assert.That(player.Mp, Is.EqualTo(mp + 10));
        }

        [Test]
        public void F07_活力汲取按实际伤害的一半回血而不是卡面值()
        {
            // 卡面：「造成6点暗属性魔法伤害，回复等同于**本次实际伤害**一半（向上取整）的HP。」
            //
            // 「实际伤害」是战果：护盾挡掉一部分时回的也该跟着少。
            // 照卡面那个 6 去算一半，是这类卡最容易写错的地方。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            player.LoseHp(20);
            int hp = player.Hp;

            // 把敌人盾堆高，让实际伤害明显低于卡面的 6
            enemy.Shields.Grant(ShieldKind.Magical, 4);

            int enemyHp = enemy.Hp;
            CardInstance card = TestFactory.AddToHand(player, _db.Card("dark_word_vitality_drain"));
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));
            Assert.That(result.Ok, Is.True, result.Reason);

            int dealt = enemyHp - enemy.Hp;
            Assert.That(dealt, Is.GreaterThan(0), "一点伤害都没打出来，这条用例就没意义了");

            int expected = (dealt + 1) / 2;   // 向上取整
            Assert.That(player.Hp - hp, Is.EqualTo(expected),
                $"实际打掉 {dealt} 点，应当回 {expected} HP");
        }

        [Test]
        public void F08_深渊虹吸满血时把溢出治疗转成魔法护盾()
        {
            // 卡面：「你立即回复6 HP；**若你生命值已满，溢出的治疗量自动转化为……魔法护盾**。」
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            // 搭局用的角色起手是 40/45，不是满的——这条用例要的就是满血，先补满
            player.GainHp(player.MaxHp);
            Assert.That(player.Hp, Is.EqualTo(player.MaxHp));

            int shield = player.Shields.Magical;

            CardInstance card = TestFactory.AddToHand(player, _db.Card("abyssal_siphon"));
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));
            Assert.That(result.Ok, Is.True, result.Reason);

            Assert.That(player.Shields.Magical, Is.EqualTo(shield + 6), "满血时 6 点治疗应当整份转成魔法护盾");
        }

        [Test]
        public void F09_深渊虹吸没满血时正常回血不转盾()
        {
            // 反面：只验「满血转盾」会让一个无条件转盾的实现也通过。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            player.LoseHp(20);
            int hp = player.Hp;
            int shield = player.Shields.Magical;

            CardInstance card = TestFactory.AddToHand(player, _db.Card("abyssal_siphon"));
            Assert.That(engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id)).Ok, Is.True);

            Assert.That(player.Hp, Is.EqualTo(hp + 6), "没满血就该正常回血");
            Assert.That(player.Shields.Magical, Is.EqualTo(shield), "没有溢出就不该转盾");
        }

        // ──────────────────────────── 可选消耗充能 ────────────────────────────

        [Test]
        public void F09b_万能灵药不选消耗就打不出()
        {
            // 卡面：「**打出时需消耗1个充能指示物**，从以下两项中二选一；
            //   若消耗2个充能指示物则两项同时生效。」
            //
            // 旧写法看的是「充能区里有没有」，于是充能区满着、玩家一个都不付，
            // 这张牌照样打得出去，还白拿一份效果。
            CardDefinition def = _db.Card("universal_elixir");
            Assert.That(def.ChargeOptions, Is.EquivalentTo(new[] { 1, 2 }), "卡面应声明可选消耗 1 或 2");

            (BattleEngine engine, PlayerCharacter player, Enemy _) = Duel();
            engine.PlaceCharge(player, new CardInstance(_db.Card("ambush_opening"), player.Id));
            engine.PlaceCharge(player, new CardInstance(_db.Card("ambush_opening"), player.Id));

            CardInstance card = TestFactory.AddToHand(player, def);
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, player.Id));

            Assert.That(result.Ok, Is.False, "充能区里有充能，但这次一个都没付");
            Assert.That(player.Zones.Charges.Count, Is.EqualTo(2));
        }

        [Test]
        public void F09c_万能灵药消耗一个二选一消耗两个两项都生效()
        {
            CardDefinition def = _db.Card("universal_elixir");

            // ── 消耗 1 个 + mode=1（元素涂油）：只涂油，不回血回蓝 ──
            (BattleEngine one, PlayerCharacter p1, Enemy _) = Duel();
            one.PlaceCharge(p1, new CardInstance(_db.Card("ambush_opening"), p1.Id));
            p1.LoseHp(20);
            p1.SpendMp(20);
            int hp1 = p1.Hp;
            int mp1 = p1.Mp;

            CardInstance c1 = TestFactory.AddToHand(p1, def);
            ActionResult r1 = one.Submit(
                new PlayCardAction(p1.Id, c1.Uid, p1.Id, chargeForEffect: 1).With("mode", 1));

            Assert.That(r1.Ok, Is.True, r1.Reason);
            Assert.That(p1.Hp, Is.EqualTo(hp1), "选了涂油就不该回血");
            Assert.That(p1.Mp, Is.EqualTo(mp1), "选了涂油就不该回蓝");
            Assert.That(p1.Statuses.Has("oil"), Is.True, "没有涂上油");

            // ── 消耗 2 个：两项同时生效 ──
            (BattleEngine both, PlayerCharacter p2, Enemy _) = Duel();
            both.PlaceCharge(p2, new CardInstance(_db.Card("ambush_opening"), p2.Id));
            both.PlaceCharge(p2, new CardInstance(_db.Card("ambush_opening"), p2.Id));
            p2.LoseHp(20);
            p2.SpendMp(20);
            int hp2 = p2.Hp;
            int mp2 = p2.Mp;

            CardInstance c2 = TestFactory.AddToHand(p2, def);
            ActionResult r2 = both.Submit(
                new PlayCardAction(p2.Id, c2.Uid, p2.Id, chargeForEffect: 2).With("mode", 1));

            Assert.That(r2.Ok, Is.True, r2.Reason);
            Assert.That(p2.Hp, Is.EqualTo(hp2 + 10), "消耗 2 个时生命灵药也该生效");
            Assert.That(p2.Mp, Is.EqualTo(mp2 + 10), "消耗 2 个时生命灵药也该生效");
            Assert.That(p2.Statuses.Has("oil"), Is.True, "消耗 2 个时涂油也该生效");
            Assert.That(p2.Zones.Charges.Count, Is.EqualTo(0), "两个充能都该被消耗掉");
        }

        // ──────────────────────────── 形态牌 ────────────────────────────

        [Test]
        public void F10_暗夜剔骨消耗充能后必定暴击并抽牌()
        {
            // 卡面：「若消耗1个充能指示物，**本次攻击必定暴击且抽1张牌**。」
            //
            // 「必定暴击」必须在判定之前生效：事后把布尔值掰成 true
            // 会绕过 §7.3 那套由骰值与抵抗线共同决定的规则。
            CardDefinition def = _db.Card("night_eviscerate");

            // 换种子多跑几局：不消耗充能时本来就有可能自然暴击，
            // 一局一局看说明不了「必定」
            for (int seed = 1; seed <= 12; seed++)
            {
                (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel(seed);
                engine.PlaceCharge(player, new CardInstance(_db.Card("ambush_opening"), player.Id));

                int hand = player.Zones.Hand.Count;
                CardInstance card = TestFactory.AddToHand(player, def);

                ActionResult result = engine.Submit(
                    new PlayCardAction(player.Id, card.Uid, enemy.Id, chargeForEffect: 1));
                Assert.That(result.Ok, Is.True, result.Reason);

                BattleEvent hit = engine.Events.LastOrDefault(
                    e => e.Type == BattleEventType.DamageDealt && e.CardId == def.Id);

                Assert.That(hit, Is.Not.Null, $"seed {seed}：这张牌没打出伤害");
                Assert.That(hit.Damage.IsCrit, Is.True, $"seed {seed}：消耗了充能却没暴击");
                Assert.That(player.Zones.Hand.Count, Is.EqualTo(hand + 1), $"seed {seed}：没有抽到那 1 张牌");
            }
        }

        [Test]
        public void F10b_充能先转成MP就不算付给暗夜剔骨的词条()
        {
            // B-16 的回归：以前抵 MP 和付卡面词条共用 ChargeSpent，
            // 一个充能既抵掉 5 MP 又触发了「必定暴击 + 抽 1」，等于白赚一次。
            // 现在转 MP 是独立动作，转掉的充能不计入 ctx.charge_spent。
            CardDefinition def = _db.Card("night_eviscerate");
            bool sawNonCrit = false;

            for (int seed = 1; seed <= 12; seed++)
            {
                (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel(seed);
                engine.PlaceCharge(player, new CardInstance(_db.Card("ambush_opening"), player.Id));

                // 这张牌要 5 MP，刚好由那个充能转出来
                player.SpendMp(player.Mp);
                Assert.That(engine.Submit(new SpendChargeForMpAction(player.Id)).Ok, Is.True);

                CardInstance card = TestFactory.AddToHand(player, def);
                int hand = player.Zones.Hand.Count;

                ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));
                Assert.That(result.Ok, Is.True, result.Reason);

                Assert.That(player.Zones.Hand.Count, Is.EqualTo(hand - 1),
                    $"seed {seed}：转 MP 的充能不该触发抽 1 张（手里只该少掉打出去的那张）");

                BattleEvent hit = engine.Events.LastOrDefault(
                    e => e.Type == BattleEventType.DamageDealt && e.CardId == def.Id);
                Assert.That(hit, Is.Not.Null, $"seed {seed}：这张牌没打出伤害");

                // 自然暴击仍然可能发生，所以断言的是「不是**必定**暴击」：
                // 12 个种子里至少得有一次没暴击
                if (!hit.Damage.IsCrit) sawNonCrit = true;
            }

            Assert.That(sawNonCrit, Is.True, "12 个种子每次都暴击：转 MP 的充能很可能仍被当成付了卡面词条");
        }

        [Test]
        public void F10c_暗夜剔骨的可选消耗只认卡面声明的数目()
        {
            // 卡面声明的是 chargeOptions [0, 1]，没有「消耗 2 个」这种选项——
            // 界面据此生成提问，引擎也照它校验，两边同一个来源
            CardDefinition def = _db.Card("night_eviscerate");
            Assert.That(def.ChargeOptions, Is.EquivalentTo(new[] { 0, 1 }));

            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();
            engine.PlaceCharge(player, new CardInstance(_db.Card("ambush_opening"), player.Id));
            engine.PlaceCharge(player, new CardInstance(_db.Card("ambush_opening"), player.Id));

            CardInstance card = TestFactory.AddToHand(player, def);
            ActionResult result = engine.Submit(
                new PlayCardAction(player.Id, card.Uid, enemy.Id, chargeForEffect: 2));

            Assert.That(result.Ok, Is.False, "卡面没有「消耗 2 个」这一档");
            Assert.That(player.Zones.Charges.Count, Is.EqualTo(2), "被拒绝的动作不该动充能区");
        }

        [Test]
        public void F11_瞬影突袭本轮打过牌才返还AP且每轮只返一次()
        {
            // 卡面：「**若本轮已打出过其他牌**，返还1 AP（**每轮限返还1次**）。」
            CardDefinition def = _db.Card("shadow_dash_assault");

            // ── 本轮第一张就打它：不该返还 ──
            (BattleEngine first, PlayerCharacter p1, Enemy e1) = Duel();
            int ap1 = p1.Ap;
            CardInstance c1 = TestFactory.AddToHand(p1, def);
            Assert.That(first.Submit(new PlayCardAction(p1.Id, c1.Uid, e1.Id)).Ok, Is.True);
            Assert.That(p1.Ap, Is.EqualTo(ap1 - def.ApCost), "本轮还没打过别的牌，不该返还 AP");

            // ── 先打一张别的，再打它：返还 1 点 ──
            (BattleEngine second, PlayerCharacter p2, Enemy e2) = Duel();
            CardInstance warmup = TestFactory.AddToHand(p2, _db.Card("ambush_opening"));
            Assert.That(second.Submit(new PlayCardAction(p2.Id, warmup.Uid, e2.Id)).Ok, Is.True);

            int ap2 = p2.Ap;
            CardInstance c2 = TestFactory.AddToHand(p2, def);
            Assert.That(second.Submit(new PlayCardAction(p2.Id, c2.Uid, e2.Id)).Ok, Is.True);
            Assert.That(p2.Ap, Is.EqualTo(ap2 - def.ApCost + 1), "本轮已出过牌，应当返还 1 AP");

            // ── 同一轮再打一张：不该再返 ──
            int ap3 = p2.Ap;
            CardInstance c3 = TestFactory.AddToHand(p2, def);
            Assert.That(second.Submit(new PlayCardAction(p2.Id, c3.Uid, e2.Id)).Ok, Is.True);
            Assert.That(p2.Ap, Is.EqualTo(ap3 - def.ApCost), "每轮限返还 1 次");
        }

        [Test]
        public void F12_箭雨覆盖削减敌人物理护盾且不算伤害()
        {
            // 卡面：「对敌方全体造成B_atk点物理伤害，**并削减所有敌人3点物理护盾**。」
            //
            // 削盾不是伤害：它不该被暴击与属性克制放大，也不触发受击。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            enemy.Shields.Grant(ShieldKind.Physical, 50);
            enemy.Shields.Grant(ShieldKind.Magical, 50);
            int magical = enemy.Shields.Magical;

            CardInstance card = TestFactory.AddToHand(player, _db.Card("arrow_storm_coverage"));
            Assert.That(engine.Submit(new PlayCardAction(player.Id, card.Uid)).Ok, Is.True);

            Assert.That(engine.Events.Any(e => e.Type == BattleEventType.ShieldReduced), Is.True,
                "没有削盾事件");
            Assert.That(enemy.Shields.Magical, Is.EqualTo(magical), "卡面只削物理盾，魔法盾不该动");
        }

        [Test]
        public void F13_视界奇点无视目标魔法护盾()
        {
            // 卡面：「造成B_atk+4点暗属性魔法伤害，**无视目标魔法护盾**。」
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            enemy.Shields.Grant(ShieldKind.Magical, 50);
            int hp = enemy.Hp;

            CardInstance card = TestFactory.AddToHand(player, _db.Card("event_horizon_singularity"));
            Assert.That(engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id)).Ok, Is.True);

            Assert.That(hp - enemy.Hp, Is.GreaterThan(0), "50 点魔法盾把伤害全挡住了——没有无视护盾");
        }

        [Test]
        public void F14_炼金防护力场保护一整排并净化负面状态()
        {
            // 卡面：「为**前排全体或后排全体**提供B_def点全能护盾，
            //       并**移除每名受保护角色身上的1个负面状态**。」
            //
            // 卡面 effects 原本是两条 SingleAlly 护盾——把一张群体保护牌做成了单体牌。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = Duel();

            engine.ApplyStatus(player, engine.Statuses.Create("exposed", 1, 1));
            int physical = player.Shields.Physical;

            CardInstance card = TestFactory.AddToHand(player, _db.Card("alchemical_protection_field"));
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid));
            Assert.That(result.Ok, Is.True, result.Reason);

            Assert.That(player.Shields.Physical, Is.GreaterThan(physical), "前排的自己没拿到护盾");
            Assert.That(player.Statuses.Has("exposed"), Is.False, "【破绽】是负面状态，应当被净化掉");
        }

        // ──────────────────────────── 新加的引擎能力 ────────────────────────────

        [Test]
        public void F15_元素易损状态让该属性伤害翻倍且只影响该属性()
        {
            // 「令目标在本轮内获得该元素的【易损】状态：受到该属性伤害×2」（元素增殖炸弹）。
            // 角色自带的克制表是建角色时定死的，这条词条要的是**临时**弱点。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            var before = new AttackContext(player, enemy, 10, DamageElement.Fire, CritResult.None);
            engine.ResolveAttack(before);
            Assert.That(before.Result.AfterAffinity, Is.EqualTo(10), "挂状态前应当是原值");

            engine.ApplyStatus(enemy, engine.Statuses.Create("vulnerable_fire", 1, 1));

            var fire = new AttackContext(player, enemy, 10, DamageElement.Fire, CritResult.None);
            engine.ResolveAttack(fire);
            Assert.That(fire.Result.AfterAffinity, Is.EqualTo(20), "火属性应当 ×2");

            var ice = new AttackContext(player, enemy, 10, DamageElement.Ice, CritResult.None);
            engine.ResolveAttack(ice);
            Assert.That(ice.Result.AfterAffinity, Is.EqualTo(10), "只有选定的那个属性翻倍");
        }

        [Test]
        public void F16_攻击结算触发在掉0血时也广播()
        {
            // 「若**完全抵消**本次攻击伤害，立即反击」（风墙反制）只在掉 0 血时成立，
            // 挂在「每当你受到伤害时」上永远等不到——那个触发 0 伤害不广播。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            player.Shields.Grant(ShieldKind.Physical, 99);
            engine.ApplyStatus(player, engine.Statuses.Create("wind_wall", 1, 1));

            int enemyHp = enemy.Hp;
            int hp = player.Hp;
            engine.DealDamage(enemy, player, 10, DamageElement.Physical, CritResult.None);

            Assert.That(player.Hp, Is.EqualTo(hp), "护盾应当完全挡下这 10 点");
            Assert.That(enemy.Hp, Is.LessThan(enemyHp), "完全抵消之后应当反击攻击者");
            Assert.That(player.Statuses.Has("wind_wall"), Is.False, "反击是一次性的，用过就该消失");
        }

        [Test]
        public void F17_没完全抵消就不反击()
        {
            // 反面：只验「挡住了会反击」会让一个无条件反击的实现也通过。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            engine.ApplyStatus(player, engine.Statuses.Create("wind_wall", 1, 1));

            int enemyHp = enemy.Hp;
            engine.DealDamage(enemy, player, 60, DamageElement.Physical, CritResult.None);

            Assert.That(player.Hp, Is.LessThan(player.MaxHp), "这一下该打穿护盾");
            Assert.That(enemy.Hp, Is.EqualTo(enemyHp), "没完全抵消就不该反击");
        }

        [Test]
        public void F18_弃牌效果有执行器而不是当场抛异常()
        {
            // EffectKind.Discard 在枚举里有、执行器从来没注册过，
            // 任何一张用到它的卡都会在结算时抛 InvalidOperationException。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = Duel();

            var def = new CardDefinition("discard_test", "弃牌测试", CardCategory.Class, CardPlay.Skill)
            {
                Colors = new[] { AttributeColor.Might },
                Effects = new[]
                {
                    new EffectData
                    {
                        Kind = EffectKind.Discard,
                        Amount = ValueExpr.Flat(2),
                        Target = TargetSelector.Self,
                    },
                },
            };

            TestFactory.FillDeck(player, 4);
            engine.DrawCards(player, 3);

            int hand = player.Zones.Hand.Count;
            Assert.That(hand, Is.GreaterThanOrEqualTo(2), "手牌不够，这条用例就没意义了");

            CardInstance card = TestFactory.AddToHand(player, def);
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid));

            Assert.That(result.Ok, Is.True, result.Reason);
            // 加进来 +1、打出去 -1、弃 2 张 -2
            Assert.That(player.Zones.Hand.Count, Is.EqualTo(hand - 2), "没弃掉那 2 张");
        }

        [Test]
        public void F19_归零坍缩在首领的意图池里()
        {
            // 卡面上最吓人的那一招却从来不会出现：它没被放进任何一个阶段的意图池，
            // 于是「释放后下轮过热」那套机制在实战里永远不触发。
            EnemyContent boss = _db.Enemy("boss");
            IEnumerable<string> pooled = boss.PhaseOne.Concat(boss.PhaseTwo);

            Assert.That(pooled, Does.Contain("zero_collapse"), "归零坍缩不在任何意图池里");
        }

        [Test]
        public void F20_因果抹杀令从内容包里读出来就是可分摊的()
        {
            // 分摊的引擎机制由 DamageShareTests 用合成意图验过了；这里验的是**接线**：
            // 真实内容包里那条意图确实带着 `shareable: true`。
            // 漏掉这个字段不会报任何错，只会让「可在响应时由相邻队友分摊」在实战中点不出来——
            // 和它当初挂着空行为时的表现一模一样。
            EnemyIntent intent = _db.Enemy("boss").Intents["causal_erasure"];

            Assert.That(intent.Shareable, Is.True, "JSON 里的 shareable 没读出来");
            Assert.That(intent.Targeting, Is.EqualTo(IntentTargeting.HighestHpSingle), "索敌应当是「当前HP最高者」");
            Assert.That(intent.Damage, Is.EqualTo(24));
        }

        [Test]
        public void F21_分摊是个别开关而不是所有意图都能拆()
        {
            // 反面：`shareable` 若不小心写成默认 true，上面那条照样绿。
            // 卡面只给因果抹杀令开了这个口子。
            var shareable = new List<string>();

            foreach (EnemyContent enemy in _db.Enemies.Values)
            {
                foreach (KeyValuePair<string, EnemyIntent> pair in enemy.Intents)
                {
                    if (pair.Value.Shareable) shareable.Add(pair.Key);
                }
            }

            Assert.That(shareable, Is.EquivalentTo(new[] { "causal_erasure" }),
                "可分摊的意图不止一条：" + string.Join(", ", shareable));
        }

        // ──────────────────────────── 卡面时点与目标 ────────────────────────────

        [Test]
        public void F22_卡面写了受击时打出的形态牌在响应窗口打得出来()
        {
            // 这四张的卡面都写着「受击时打出」「敌人阶段打出」。「形态」是标记、「时机」是打法，
            // 两者是独立维度（§7.1）——标记吃掉打法的话，它们在响应窗口一张都打不出来。
            string[] timingForms =
            {
                "smoke_decoy", "wind_wall_counter", "time_paradox", "alchemical_protection_field",
            };

            foreach (string id in timingForms)
            {
                CardDefinition def = _db.Card(id);
                Assert.That(def.Role, Is.EqualTo(CardRole.Form), $"{def.Name} 丢了形态标记");
                Assert.That(def.Play, Is.EqualTo(CardPlay.Timing), $"{def.Name} 在响应窗口里会被拒收");
            }
        }

        // 一张职业牌 + 一张防具牌：能不能响应只看打法，和类别没关系
        [TestCase("tactical_concealment", CardCategory.Class)]
        [TestCase("arcane_barrier", CardCategory.Armor)]
        public void F22b_技能牌不会因为类别而获得响应资格(string cardId, CardCategory category)
        {
            // §7.1 改成「只有时机牌能响应，与类别无关」之前，「防具 · 技能」
            // 靠「是防具」混进响应窗口——那样防具牌标成技能还是时机就没区别了。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = Duel(intentDamage: 20);

            CardDefinition def = _db.Card(cardId);
            Assert.That(def.Category, Is.EqualTo(category), "类别变了，这条用例要换一张同类别的牌");
            Assert.That(def.Play, Is.EqualTo(CardPlay.Skill), "这条测的是技能牌");

            CardInstance card = TestFactory.AddToHand(player, def);
            engine.Submit(new DeclareReadyAction(player.Id));
            Assert.That(engine.Pending.Kind, Is.EqualTo(DecisionKind.EnemyResponse), "没开出响应窗口");

            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, player.Id));
            Assert.That(result.Ok, Is.False, $"{def.Name} 的打法是技能，不该能在敌人阶段打出");

            // 挡回去的理由必须是打法，不能是 AP 不够这种碰巧——否则这条测试什么也没守住
            Assert.That(result.Reason, Does.Contain("时机牌"), $"挡回的理由不对：{result.Reason}");
        }

        [TestCase("read_dodge")]
        [TestCase("rolling_cloak")]
        [TestCase("sages_robe")]
        public void F22c_时机牌照旧能响应(string cardId)
        {
            // 反面：别把「只有时机牌能响应」收紧成「只有某个类别能响应」
            Assert.That(_db.Card(cardId).Play, Is.EqualTo(CardPlay.Timing), $"{cardId} 的打法标错了");
        }

        [Test]
        public void F23_风墙反制真的能在响应窗口打出来()
        {
            // 上一条只验标记，这条走真实流程——标记对了但窗口仍然拒收的话，玩家照样用不上。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = Duel(intentDamage: 20);

            CardInstance card = TestFactory.AddToHand(player, _db.Card("wind_wall_counter"));
            engine.Submit(new DeclareReadyAction(player.Id));
            Assert.That(engine.Pending.Kind, Is.EqualTo(DecisionKind.EnemyResponse), "没开出响应窗口");

            int physical = player.Shields.Physical;
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, player.Id));

            Assert.That(result.Ok, Is.True, "响应窗口拒收了这张卡面写着「受击时打出」的牌：" + result.Reason);
            Assert.That(player.Shields.Physical, Is.GreaterThan(physical), "护盾没给上");
        }

        [Test]
        public void F24_普通技能牌仍然进不了响应窗口()
        {
            // 反面：响应窗口要是放行所有牌，上面那两条照样绿。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel(intentDamage: 20);

            CardInstance card = TestFactory.AddToHand(player, _db.Card("ambush_opening"));
            engine.Submit(new DeclareReadyAction(player.Id));

            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));
            Assert.That(result.Ok, Is.False, "普通攻击牌不该能在敌人阶段打出来");
        }

        [Test]
        public void F25_指定的队友不会被引擎丢掉()
        {
            // 「从你的弃牌堆将最多2张牌直接贴入**全队任意角色**的充能区」——
            // 这张牌的目标只给行为层用，数据效果里一条 SingleAlly 都没有。
            // 引擎原本从 Effects 反推「需不需要目标」，于是玩家点的队友被整个丢掉，永远贴给自己。
            //
            // 单人局测不出来：默认值本来就是自己。必须有第二个人在场。
            (BattleEngine engine, PlayerCharacter me, PlayerCharacter mate, Enemy _) = PartyDuel();

            for (int i = 0; i < 2; i++)
            {
                me.Zones.Discard.AddTop(new CardInstance(_db.Card("ambush_opening"), me.Id));
            }

            int mine = me.Zones.Charges.Count;
            int theirs = mate.Zones.Charges.Count;

            CardInstance card = TestFactory.AddToHand(me, _db.Card("aether_reassembly_crucible"));
            ActionResult result = engine.Submit(new PlayCardAction(me.Id, card.Uid, mate.Id));
            Assert.That(result.Ok, Is.True, result.Reason);

            Assert.That(mate.Zones.Charges.Count, Is.GreaterThan(theirs), "充能没贴到指定的队友身上");
            Assert.That(me.Zones.Charges.Count, Is.EqualTo(mine), "贴给队友的充能却落在了自己身上");
        }

        [Test]
        public void F26_指向队友的卡声明了Ally而不是靠效果反推()
        {
            // 这几张的「1名我方角色 / 任意队友」全在行为层，effects 里反推不出来。
            // 漏声明不会报错，只会让目标被悄悄丢掉——和 F25 里那个 bug 一模一样。
            string[] allyTargeted =
            {
                "thieves_trick", "philosophers_stone_release", "aether_reassembly_crucible",
                "tailwind_charge", "universal_elixir", "emergency_catalyst",
            };

            foreach (string id in allyTargeted)
            {
                CardDefinition def = _db.Card(id);
                Assert.That(def.ResolvedTargeting, Is.EqualTo(CardTargeting.Ally), $"{def.Name} 没声明指向队友");
                Assert.That(BattleSession.NeedsAllyTarget(def), Is.True, $"{def.Name} 界面会按指向敌人处理");
            }
        }

        [Test]
        public void F27_不指定队友时仍然打得出且落在自己身上()
        {
            // 卡面写的是「自己**或**任意队友」——不选就是自己，不该因此打不出去。
            // 把它做成硬性必填会让单人局每张这种牌都卡住。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = Duel();

            player.Zones.Discard.AddTop(new CardInstance(_db.Card("ambush_opening"), player.Id));
            int charges = player.Zones.Charges.Count;

            CardInstance card = TestFactory.AddToHand(player, _db.Card("aether_reassembly_crucible"));
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid));

            Assert.That(result.Ok, Is.True, result.Reason);
            Assert.That(player.Zones.Charges.Count, Is.GreaterThan(charges), "没指定目标时应当落在自己身上");
        }

        // ──────────────────────────── 元素增殖炸弹的易损 ────────────────────────────

        [Test]
        public void F28_元素炸弹本次伤害不吃自己挂的易损()
        {
            // 卡面：「造成B_atk+3点指定元素伤害，**并令**目标……获得该元素的【易损】状态」。
            // 「并令」是在伤害**之后**，所以这一下自己不该先被自己翻倍。
            // 挂状态的时点写错（on_play 而不是 on_resolved）这条就会红。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            var choices = new Dictionary<string, int> { ["element"] = 1 };   // 1 = 火
            CardInstance card = TestFactory.AddToHand(player, _db.Card("elemental_proliferation_bomb"));

            var action = new PlayCardAction(player.Id, card.Uid, enemy.Id);
            foreach (KeyValuePair<string, int> pair in choices) action.Choices[pair.Key] = pair.Value;

            ActionResult result = engine.Submit(action);
            Assert.That(result.Ok, Is.True, result.Reason);

            BattleEvent hit = engine.Events.Last(
                e => e.Type == BattleEventType.DamageDealt && e.CardId == "elemental_proliferation_bomb");

            Assert.That(hit.Damage.Element, Is.EqualTo(DamageElement.Fire), "没按选的属性打");
            Assert.That(hit.Damage.AfterAffinity, Is.EqualTo(hit.Damage.AfterCrit),
                "本次伤害被自己挂的【易损】翻倍了——卡面写的是「造成伤害，并令目标获得易损」");

            Assert.That(enemy.Statuses.Has("vulnerable_fire"), Is.True, "打完之后才该挂上【易损·火】");
        }

        [Test]
        public void F29_易损挂上之后同属性伤害才翻倍()
        {
            // 接 F28：炸弹的价值全在「后续」那一下。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel();

            var action = new PlayCardAction(player.Id,
                TestFactory.AddToHand(player, _db.Card("elemental_proliferation_bomb")).Uid, enemy.Id);
            action.Choices["element"] = 1;   // 火
            Assert.That(engine.Submit(action).Ok, Is.True);

            var fire = new AttackContext(player, enemy, 10, DamageElement.Fire, CritResult.None);
            engine.ResolveAttack(fire);
            Assert.That(fire.Result.AfterAffinity, Is.EqualTo(20), "后续的火属性伤害应当 ×2");

            var ice = new AttackContext(player, enemy, 10, DamageElement.Ice, CritResult.None);
            engine.ResolveAttack(ice);
            Assert.That(ice.Result.AfterAffinity, Is.EqualTo(10), "只有选定的那个属性翻倍");
        }

        // ──────────────────────────── 授盾 ────────────────────────────

        [Test]
        public void F30_战术隐蔽真的给了7点物理护盾()
        {
            // 卡面第一句就是「**获得7点物理护盾**」。脚本注释写着「7 点护盾走数据（卡面 effects）」，
            // 但 effects 是空数组——于是这张牌只给了隐蔽，护盾一点没加。
            // 和顺风充能那个抽牌 bug 是同一类：注释承诺了数据层，数据层却是空的。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = Duel();

            int physical = player.Shields.Physical;
            int magical = player.Shields.Magical;

            CardInstance card = TestFactory.AddToHand(player, _db.Card("tactical_concealment"));
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid));
            Assert.That(result.Ok, Is.True, result.Reason);

            Assert.That(player.Shields.Physical, Is.EqualTo(physical + 7), "没拿到那 7 点物理护盾");
            Assert.That(player.Shields.Magical, Is.EqualTo(magical), "卡面只给物理盾");
        }

        [Test]
        public void F31_奥术结界真的给了7点魔法护盾()
        {
            // 卡面：「**获得7点魔法护盾**，并使自身充能区上限在本轮及下一轮临时+1。」
            // 上限 +1 一直是生效的（脚本里写了），只有护盾那半句从来没落地。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = Duel();

            int physical = player.Shields.Physical;
            int magical = player.Shields.Magical;
            int capacity = player.Zones.Charges.Capacity;

            CardInstance card = TestFactory.AddToHand(player, _db.Card("arcane_barrier"));
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid));
            Assert.That(result.Ok, Is.True, result.Reason);

            Assert.That(player.Shields.Magical, Is.EqualTo(magical + 7), "没拿到那 7 点魔法护盾");
            Assert.That(player.Shields.Physical, Is.EqualTo(physical), "卡面只给魔法盾");
            Assert.That(player.Zones.Charges.Capacity, Is.EqualTo(capacity + 1), "充能上限 +1 也该照旧生效");
        }

        [Test]
        public void F32_每张写了获得护盾的卡都真的会授盾()
        {
            // 上面两条是针对具体卡的。这一条是**体检**：凡是卡面写了「获得／提供……护盾」的牌，
            // 要么有 GainShield 数据效果，要么行为层里调了 shield()。
            // 两处都没有就是漏做——这正是战术隐蔽与奥术结界藏了这么久的形态。
            var missing = new List<string>();

            foreach (CardDefinition def in _db.Cards.Values)
            {
                string text = def.RulesText ?? "";
                if (!text.Contains("护盾")) continue;
                if (!System.Text.RegularExpressions.Regex.IsMatch(text, "获得.*?护盾|提供.*?护盾")) continue;

                bool viaData = def.Effects.Any(e => e.Kind == EffectKind.GainShield);
                if (viaData) continue;

                // 行为层授盾的（睿智法袍、翻滚披风…）在这里看不出来，
                // 只能把「有行为」当成可能实现了；真正要抓的是两边都没有的那种
                if (!string.IsNullOrEmpty(def.BehaviourId)) continue;

                missing.Add($"{def.Name}（{def.Id}）");
            }

            Assert.That(missing, Is.Empty,
                "这些卡写了获得护盾，但既没有 GainShield 效果也没有行为：" + string.Join(", ", missing));
        }

        // ──────────────────────────── 搭局辅助 ────────────────────────────

        /// <summary>两名玩家 + 一只敌人。验「目标到底落在谁身上」必须有第二个人在场。</summary>
        private (BattleEngine, PlayerCharacter, PlayerCharacter, Enemy) PartyDuel(int seed = 7)
        {
            LuaScriptHost host = ContentScenario.LoadScripts(_db, _root, out ScriptLoadReport report);
            BehaviourRegistry behaviours = host.BuildRegistry(_db.DeclaredBehaviourIds, report);

            PlayerCharacter me = TestFactory.Player("me", TestFactory.Sheet("我"), hp: 40, mp: 50);
            PlayerCharacter mate = TestFactory.Player("mate", TestFactory.Sheet("队友"), hp: 40, mp: 50);
            Enemy enemy = TestFactory.Enemy(hp: 100, rank: EnemyRank.Elite);

            TestFactory.FillDeck(me, 20);
            TestFactory.FillDeck(mate, 20);

            BattleState state = BattleSetup.Create(
                new XorShift32Rng(seed),
                new[] { (me, Row.Front), (mate, Row.Back) },
                new[] { (enemy, Row.Front) });

            var engine = new BattleEngine(
                state, EffectRegistry.CreateDefault(), behaviours, _db.BuildStatusRegistry());

            host.BindEngine(engine);
            engine.Start();

            if (engine.Pending.Kind == DecisionKind.TransformDeclaration)
            {
                foreach (PlayerCharacter p in state.Players) engine.Submit(new DeclineTransformAction(p.Id));
            }

            return (engine, me, mate, enemy);
        }

        /// <summary>用真实内容包的脚本与状态开一局 1v1。</summary>
        private (BattleEngine, PlayerCharacter, Enemy) Duel(int seed = 7, int intentDamage = 0)
        {
            LuaScriptHost host = ContentScenario.LoadScripts(_db, _root, out ScriptLoadReport report);
            BehaviourRegistry behaviours = host.BuildRegistry(_db.DeclaredBehaviourIds, report);

            PlayerCharacter player = TestFactory.Player(hp: 40, mp: 50);
            Enemy enemy = TestFactory.Enemy(
                hp: 100,
                rank: EnemyRank.Elite,
                intents: intentDamage > 0 ? new[] { TestFactory.Attack(intentDamage) } : null);

            TestFactory.FillDeck(player, 20);

            BattleState state = BattleSetup.Create(
                new XorShift32Rng(seed),
                new[] { (player, Row.Front) },
                new[] { (enemy, Row.Front) });

            var engine = new BattleEngine(
                state, EffectRegistry.CreateDefault(), behaviours, _db.BuildStatusRegistry());

            host.BindEngine(engine);
            engine.Start();

            if (engine.Pending.Kind == DecisionKind.TransformDeclaration)
            {
                engine.Submit(new DeclineTransformAction(player.Id));
            }

            return (engine, player, enemy);
        }
    }
}
