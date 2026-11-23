using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TCGinFU.Content.Loading;
using TCGinFU.Core;
using TCGinFU.Core.Tests;
using TCGinFU.Presentation;
using TCGinFU.Scripting;

namespace TCGinFU.Tests
{
    /// <summary>
    /// 行为层（Lua）的验收测试。
    ///
    /// <para>与 <see cref="LegacyContentTests"/> 的分工：那边验「数据能加载、能跑完一局」，
    /// 这边验「**打出去真的有事发生**」——卡面承诺的数值、状态、闪避是否真的落到了战斗状态上。
    /// 老的 T-1~T-7 全绿恰恰是因为空效果的卡什么都没做，所以这一组必须直接断言 HP / 护盾 / 状态。</para>
    ///
    /// <para>断言的依据是**卡面文字**，不是老版本 GDScript——老代码本身可能有 bug，
    /// 对不上的地方在各自的测试注释里标出。</para>
    /// </summary>
    [TestFixture]
    public sealed class LuaBehaviourTests
    {
        private ContentDatabase _db;
        private string _root;
        private LoadReport _report;

        [OneTimeSetUp]
        public void LoadContent()
        {
            _root = ContentPaths.Resolve();
            Assert.That(_root, Is.Not.Null, "找不到内容包目录");
            _db = ContentPaths.LoadDefault(out _report, _root);
        }

        // ──────────────────────────── 脚本层本身 ────────────────────────────

        [Test]
        public void L01_内容包的脚本能无错加载()
        {
            LuaScriptHost host = ContentScenario.LoadScripts(_db, _root, out ScriptLoadReport report);
            Assert.That(report.Errors, Is.Empty, report.ToString());

            host.BuildRegistry(_db.DeclaredBehaviourIds, report);
            Assert.That(report.Registered, Is.Not.Empty, "一个行为都没注册，脚本清单可能没接上");
        }

        [Test]
        public void L02_已实现的行为都挂了钩子且与卡面声明对得上()
        {
            LuaScriptHost host = ContentScenario.LoadScripts(_db, _root, out ScriptLoadReport report);
            host.BuildRegistry(_db.DeclaredBehaviourIds, report);

            // 每个注册的行为至少要挂一个钩子，否则等于没写
            var empty = report.Registered.Where(p => p.Value.Count == 0).Select(p => p.Key).ToList();
            Assert.That(empty, Is.Empty, "这些行为一个钩子都没挂：" + string.Join(", ", empty));

            // 脚本注册了但没有卡面引用 = 拼错了 behaviourId，这种错不喊出来很难查
            var orphans = report.Warnings.Where(w => w.Contains("没有卡面引用")).ToList();
            Assert.That(orphans, Is.Empty, string.Join("\n", orphans));
        }

        [Test]
        public void L03_一个脚本写坏不影响其他脚本()
        {
            var host = new LuaScriptHost();
            var report = new ScriptLoadReport();

            host.Load("this is not lua ((", "broken.lua", report);
            host.Load("behaviour('ok_card', { on_play = function(ctx) end })", "good.lua", report);

            Assert.That(report.Errors, Has.Count.EqualTo(1), "坏文件应当只污染它自己");
            Assert.That(report.Errors[0], Does.Contain("broken.lua"), "报错要指得出是哪个文件");

            BehaviourRegistry registry = host.BuildRegistry(new[] { "ok_card" }, report);
            Assert.That(registry.IsRegistered("ok_card"), Is.True, "好文件应当照常注册");
        }

        [Test]
        public void L04_脚本抛异常被隔离成事件而不是崩掉整局()
        {
            var host = new LuaScriptHost();
            var report = new ScriptLoadReport();
            host.Load("behaviour('boom', { on_play = function(ctx) error('我炸了') end })", "boom.lua", report);

            CardDefinition def = Bomb("boom");
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel(host, report, new[] { "boom" });

            CardInstance card = TestFactory.AddToHand(player, def);
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));

            Assert.That(result.Ok, Is.True, "脚本抛异常不该让这张牌打不出去");
            Assert.That(enemy.Hp, Is.EqualTo(95), "钩子炸了，但数据驱动的那 5 点伤害照常结算");

            BattleEvent fault = engine.Events.LastOrDefault(e => e.Type == BattleEventType.BehaviourFaulted);
            Assert.That(fault, Is.Not.Null, "故障要留在事件流里，而不是被吞掉");
            Assert.That(fault.Text, Does.Contain("我炸了"));
        }

        [Test]
        public void L05_沙箱挡住文件系统与进程操作()
        {
            var host = new LuaScriptHost();
            var report = new ScriptLoadReport();

            host.Load("local f = io.open('x', 'w')", "io.lua", report);
            host.Load("os.exit()", "os.lua", report);
            host.Load("dofile('evil.lua')", "dofile.lua", report);

            Assert.That(report.Errors, Has.Count.EqualTo(3),
                "io / os / dofile 必须全部挡掉——mod 脚本不该碰得到文件系统：\n" + report);
        }

        // ──────────────────────────── 效果真生效 ────────────────────────────

        [Test]
        public void E01_暗算起手造成5点伤害并给目标挂上破绽()
        {
            // 卡面：「对单个目标造成5点物理伤害【暴击】。命中后令目标获得【破绽】标记」
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = RealDuel();

            CardInstance card = TestFactory.AddToHand(player, _db.Card("ambush_opening"));
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));

            Assert.That(result.Ok, Is.True, result.Reason);
            Assert.That(enemy.Statuses.Has("exposed"), Is.True, "命中后应当挂上【破绽】");
            Assert.That(100 - enemy.Hp, Is.GreaterThanOrEqualTo(5), "至少要掉 5 点（暴击时更多）");
        }

        [Test]
        public void E02_破绽让下一次伤害加3且随即消失()
        {
            // 卡面：「本轮内下一次受到的任意伤害+3」——注意是「下一次」，
            // 靠持续轮数做不到，必须触发即消耗
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = RealDuel();

            engine.ApplyStatus(enemy, engine.Statuses.Create("exposed", 1, 1));
            Assert.That(enemy.Statuses.Has("exposed"), Is.True);

            engine.DealDamage(player, enemy, 10, DamageElement.Physical, CritResult.None);
            Assert.That(enemy.Hp, Is.EqualTo(87), "10 + 破绽 3 = 13");
            Assert.That(enemy.Statuses.Has("exposed"), Is.False, "破绽触发一次就该消失");

            engine.DealDamage(player, enemy, 10, DamageElement.Physical, CritResult.None);
            Assert.That(enemy.Hp, Is.EqualTo(77), "第二次不该再吃 +3");
        }

        [Test]
        public void E03_卑劣打击对本轮已受伤的目标基础伤害提升到9()
        {
            // 卡面：「造成6点物理伤害；若目标处于负面状态、带有【破绽】标记
            //       或本轮已受到过伤害，基础伤害提升至9点」
            CardDefinition def = _db.Card("cheap_shot_first_form");

            // ── 干净目标：6 点 ──
            (BattleEngine clean, PlayerCharacter p1, Enemy e1) = RealDuel();
            CardInstance c1 = TestFactory.AddToHand(p1, def);
            clean.Submit(new PlayCardAction(p1.Id, c1.Uid, e1.Id));
            int cleanDamage = 100 - e1.Hp;

            // ── 本轮已受过伤的目标：9 点 ──
            (BattleEngine hurt, PlayerCharacter p2, Enemy e2) = RealDuel();
            hurt.DealDamage(null, e2, 1, DamageElement.Physical, CritResult.None);
            int afterScratch = e2.Hp;

            CardInstance c2 = TestFactory.AddToHand(p2, def);
            hurt.Submit(new PlayCardAction(p2.Id, c2.Uid, e2.Id));
            int hurtDamage = afterScratch - e2.Hp;

            // 两局同种子同骰，暴击与否一致，差值只能来自那 3 点
            Assert.That(hurtDamage - cleanDamage, Is.GreaterThan(0),
                $"干净目标掉 {cleanDamage}，受过伤的掉 {hurtDamage}——条件加伤没生效");
        }

        [Test]
        public void E04_鹰眼锁定让后排远程攻击无视2点物理护盾()
        {
            // 卡面：「只要你处于后排，你的远程攻击无视目标2点物理护盾」
            //
            // 老版本 GDScript 还额外要求伤害属性是物理，卡面没有这一条，这里按卡面断言。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = RealDuel();

            engine.SwitchRow(player, free: true);
            Assert.That(player.Row, Is.EqualTo(Row.Back), "这张卡只在后排生效");

            player.Zones.SetContinuous(
                new CardInstance(_db.Card("hawkeye_lock"), player.Id), out CardInstance _);

            enemy.Shields.Grant(ShieldKind.Physical, 20);

            CardDefinition ranged = _db.Cards.Values.First(c => c.IsRanged && c.DealsDamage);
            var attack = new AttackContext(player, enemy, 10, DamageElement.Physical, CritResult.None)
            {
                Card = new CardInstance(ranged, player.Id),
            };

            engine.ResolveAttack(attack);

            Assert.That(attack.IgnoreShield, Is.EqualTo(2), "持续牌应当给这次攻击加上 2 点无视护盾");
            Assert.That(enemy.Hp, Is.EqualTo(98), "20 点盾挡下 8 点，2 点被无视直接进血");
        }

        [Test]
        public void E05_鹰眼锁定让暴击掷骰加1()
        {
            // 卡面：「且所有【暴击】掷骰结果+1」。
            // 掷骰加成必须在**判定之前**生效，所以验的是同一个种子下暴击率真的变高了。
            CardDefinition card = _db.Card("ambush_opening");

            int withoutLock = CountCrits(useLock: false, card);
            int withLock = CountCrits(useLock: true, card);

            Assert.That(withLock, Is.GreaterThan(withoutLock),
                $"不挂持续牌暴 {withoutLock} 次，挂了暴 {withLock} 次——掷骰 +1 没生效");
        }

        [Test]
        public void E06_幻影夜行条件未达成时不可发动()
        {
            // 卡面：「前置条件：本局累计触发2次暴击，或全队累计消耗2个充能指示物」
            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel();

            CardDefinition transform = _db.Card("phantom_nightwalk");
            player.Zones.SetFormDeck(transform.Id, FormCardsFor(transform.Id, player));
            TestFactory.AddToHand(player, transform);

            Assert.That(engine.TransformCandidates(player), Is.Empty,
                "一次暴击都没有、一个充能都没消耗，条件不该算达成");
        }

        [Test]
        public void E06_幻影夜行累计2次暴击后可以发动()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = RealDuel();

            CardDefinition transform = _db.Card("phantom_nightwalk");
            player.Zones.SetFormDeck(transform.Id, FormCardsFor(transform.Id, player));
            TestFactory.AddToHand(player, transform);

            var crit = new CritResult(true, false, new[] { 9 }, 6);
            engine.DealDamage(player, enemy, 1, DamageElement.Physical, crit);
            engine.DealDamage(player, enemy, 1, DamageElement.Physical, crit);

            Assert.That(engine.State.Stats.For(player.Id, BattleStatKeys.CritsDealt), Is.EqualTo(2));
            Assert.That(engine.TransformCandidates(player).Select(c => c.Definition.Id),
                Does.Contain(transform.Id), "累计 2 次暴击后应当可以发动");
        }

        [Test]
        public void E07_多段攻击各自独立投暴击骰()
        {
            // 卡面「压制连射」：「两次射击**分别独立**投掷敏捷骰判定暴击」。
            // 老引擎每次打牌只投一次骰，两段共享结果——数值期望对，但手感与卡面承诺不符。
            // 这里验的是「两段的暴击结果会出现不一致」，也就是确实分开投了。
            CardDefinition def = WithOwnCrit(_db.Card("suppressing_volley"));

            bool sawSplit = false;
            for (int seed = 1; seed <= 40 && !sawSplit; seed++)
            {
                (BattleEngine engine, PlayerCharacter player, Enemy enemy) = RealDuel(seed);
                CardInstance card = TestFactory.AddToHand(player, def);
                engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));

                List<BattleEvent> hits = engine.Events
                    .Where(e => e.Type == BattleEventType.DamageDealt && e.CardId == def.Id)
                    .ToList();

                if (hits.Count == 2 && hits[0].Damage.IsCrit != hits[1].Damage.IsCrit) sawSplit = true;
            }

            Assert.That(sawSplit, Is.True,
                "40 个种子里两段的暴击结果从未分叉，说明还是共用了同一次掷骰");
        }

        [Test]
        public void E08_升变期间暴击倍率变成1点75倍()
        {
            // 卡面：「升变期间你的暴击伤害倍率提升至×1.75」。
            // 默认暴击是 ×1.5，所以这条必须能改写倍率，而不只是加个 flat 伤害。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Transformed();

            var crit = new CritResult(true, false, new[] { 9 }, 6);
            var attack = new AttackContext(player, enemy, 100, DamageElement.Physical, crit);
            engine.ResolveAttack(attack);

            Assert.That(attack.Result.AfterCrit, Is.EqualTo(175), "100 × 1.75 = 175");
        }

        [Test]
        public void E09_升变期间暴击回复5点MP()
        {
            // 卡面：「每当你的攻击触发暴击时，回复5 MP」——典型的「每当……时」，
            // 没有触发器就只能在每张会暴击的卡里各抄一遍。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Transformed();

            player.SpendMp(20);
            int before = player.Mp;

            engine.DealDamage(player, enemy, 5, DamageElement.Physical,
                new CritResult(true, false, new[] { 9 }, 6));

            Assert.That(player.Mp, Is.EqualTo(before + 5));
        }

        [Test]
        public void E10_看破闪避在响应窗口里改写了来袭攻击()
        {
            // 卡面：「投掷1颗敏捷骰，若点数≥敌方暴击抵抗，完全闪避本次攻击并抽1张牌；
            //       若未达标，仍为目标抵消6点伤害。」
            //
            // 老版本 GDScript 把攻击者写死成「shield_quake 就是 arm，否则是 boss」，换一场战斗就取错抵抗线；
            // 这里按卡面取**本次攻击实际来源**的抵抗。两个分支都要覆盖到。
            var seen = new HashSet<string>();

            for (int seed = 1; seed <= 30; seed++)
            {
                (BattleEngine engine, PlayerCharacter player, Enemy enemy) = RealDuel(seed, intentDamage: 20);
                CardInstance card = TestFactory.AddToHand(player, _db.Card("read_dodge"));

                engine.Submit(new DeclareReadyAction(player.Id));
                Assert.That(engine.Pending.Kind, Is.EqualTo(DecisionKind.EnemyResponse), "没开出响应窗口");

                int mark = engine.Events.Count;
                ActionResult played = engine.Submit(new PlayCardAction(player.Id, card.Uid, player.Id));
                Assert.That(played.Ok, Is.True, played.Reason);
                engine.Submit(new PassResponseAction(player.Id));

                // 只看这次意图结算产出的事件——轮末换轮会刷新自动盾，直接比血量会被它骗到
                List<BattleEvent> after = engine.Events.Skip(mark).ToList();

                if (after.Any(e => e.Type == BattleEventType.AttackAvoided)) { seen.Add("闪避"); continue; }

                BattleEvent hit = after.FirstOrDefault(
                    e => e.Type == BattleEventType.DamageDealt && e.ActorId == enemy.Id);

                Assert.That(hit, Is.Not.Null, $"seed {seed}：既没闪避也没挨打");
                Assert.That(hit.Damage.BaseAmount, Is.EqualTo(14), $"seed {seed}：20 点减 6 应当是 14 进管线");
                seen.Add("减伤6");
            }

            Assert.That(seen, Is.EquivalentTo(new[] { "闪避", "减伤6" }),
                "两个分支都要能走到，只见到：" + string.Join("/", seen));
        }

        [Test]
        public void E11_整局打完零脚本故障()
        {
            // 脚本的异常会被隔离成事件而不是抛出去，所以「没崩」证明不了「没出错」——
            // 必须直接数 BehaviourFaulted，否则一局里每张卡都在静默报错也看不出来。
            BattleEngine engine = ContentScenario.CreateClockworkCollapse(20261118, out LoadReport report);
            Assert.That(engine, Is.Not.Null, report.ToString());

            for (int i = 0; i < 4000 && engine.State.Outcome == BattleOutcome.Ongoing; i++)
            {
                PendingDecision pending = engine.Pending;
                if (pending.WaitingOn.Count == 0) break;
                string who = pending.WaitingOn[0];

                switch (pending.Kind)
                {
                    case DecisionKind.TransformDeclaration:
                        engine.Submit(new DeclineTransformAction(who));
                        break;
                    case DecisionKind.PlayerActions:
                        engine.Submit(new DeclareReadyAction(who));
                        break;
                    case DecisionKind.EnemyResponse:
                        engine.Submit(new PassResponseAction(who));
                        break;
                    case DecisionKind.HandLimitDiscard:
                        PlayerCharacter p = engine.State.FindPlayer(who);
                        engine.Submit(new DiscardToHandLimitAction(
                            who, p.Zones.Hand.Take(engine.RequiredDiscards(p)).Select(c => c.Uid).ToList()));
                        break;
                    default:
                        Assert.Fail("卡在 " + pending.Kind);
                        break;
                }
            }

            Assert.That(engine.State.Outcome, Is.Not.EqualTo(BattleOutcome.Ongoing), "推进到上限仍未结束");

            var faults = engine.Events
                .Where(e => e.Type == BattleEventType.BehaviourFaulted)
                .Select(e => e.Text)
                .ToList();

            Assert.That(faults, Is.Empty, "整局不应有脚本故障：\n" + string.Join("\n", faults));
        }

        // ──────────────────────────── 走位 ────────────────────────────

        [Test]
        public void M01_定向移动与切换不是一回事()
        {
            // 「被击退至后排」对已经在后排的人应当无事发生。
            // 用切换（SwitchRow）实现击退会把他拉到前排——效果正好相反，
            // 这是个会真实发生的 bug，所以单独立一条守住。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel();

            Assert.That(engine.MoveRow(player, Row.Back, free: true), Is.True);
            Assert.That(player.Row, Is.EqualTo(Row.Back));

            Assert.That(engine.MoveRow(player, Row.Back, free: true), Is.False, "重复击退应当什么都不做");
            Assert.That(player.Row, Is.EqualTo(Row.Back), "定向移动不该把后排的人拉回前排");

            engine.SwitchRow(player, free: true);
            Assert.That(player.Row, Is.EqualTo(Row.Front), "切换才是「在前后排之间移动」");
        }

        [Test]
        public void M02_前后排互换保持各自的列()
        {
            // 「相位折跃：前后排互换」必须原子地做。
            // 逐个 SwitchRow 会让先动的人占掉对排槽位，后动的人被挤到别的列，
            // §4.3 的「正前方」相邻关系就乱了。
            var field = new Battlefield();
            Enemy a = TestFactory.Enemy("a");
            Enemy b = TestFactory.Enemy("b");
            Enemy c = TestFactory.Enemy("c");

            field.PlaceAt(a, Row.Front, 0);
            field.PlaceAt(b, Row.Back, 0);
            field.PlaceAt(c, Row.Front, 2);

            field.SwapRows();

            Assert.That((a.Row, a.Slot), Is.EqualTo((Row.Back, 0)));
            Assert.That((b.Row, b.Slot), Is.EqualTo((Row.Front, 0)));
            Assert.That((c.Row, c.Slot), Is.EqualTo((Row.Back, 2)));
            Assert.That(field.AreAdjacent(a, b), Is.True, "互换后同列相邻关系应当保持");
        }

        [Test]
        public void M03_游击速射给的是可兑现的免费移动而不是强制移动()
        {
            // 卡面：「打出前或结算后**可**免费在前后排之间移动1次」。
            //
            // 老版本 GDScript 写的是 `if h.row == "front": move_hero(hero_id, true)`，
            // 三处与卡面不符：强制而非「可」、只许前→后、没有时点选择。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = RealDuel();

            CardDefinition def = _db.Card("skirmish_shot");
            int apBefore = player.Ap;

            CardInstance card = TestFactory.AddToHand(player, def);
            ActionResult played = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));
            Assert.That(played.Ok, Is.True, played.Reason);

            Assert.That(player.FreeMoves, Is.EqualTo(1));
            Assert.That(player.Row, Is.EqualTo(Row.Front), "卡面写「可」，不该立刻移动");

            ActionResult moved = engine.Submit(new MoveRowAction(player.Id));
            Assert.That(moved.Ok, Is.True, moved.Reason);
            Assert.That(player.Row, Is.EqualTo(Row.Back));
            Assert.That(player.FreeMoves, Is.EqualTo(0));
            Assert.That(player.HasMovedThisRound, Is.False, "免费移动不该占掉本轮唯一一次常规走位");
            Assert.That(player.Ap, Is.EqualTo(apBefore - def.ApCost), "免费移动不该再扣 AP");
        }

        [Test]
        public void M04_穿云狙击仅限后排打出()
        {
            // 卡面：「仅限在后排打出」。这是硬前置条件，站错位就该打不出来。
            // 这个标记原本躺在 unmapped 里，等于完全没生效。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = RealDuel();

            CardDefinition def = _db.Card("cloud_piercing_snipe");
            Assert.That(def.RequiresBackRow, Is.True, "requiresBackRow 没从内容包读出来");

            CardInstance card = TestFactory.AddToHand(player, def);
            Assert.That(engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id)).Ok,
                Is.False, "前排不该打得出来");

            engine.SwitchRow(player, free: true);
            ActionResult ok = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));
            Assert.That(ok.Ok, Is.True, ok.Reason);
        }

        [Test]
        public void M05_翻滚披风把护盾与移动权都给目标()
        {
            // 卡面：「使**目标**获得4点物理护盾与4点魔法护盾，并可免费移动1次」——
            // 受益者是被保护的那个，不是打出者。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel(intentDamage: 20);

            CardInstance card = TestFactory.AddToHand(player, _db.Card("rolling_cloak"));
            engine.Submit(new DeclareReadyAction(player.Id));
            Assert.That(engine.Pending.Kind, Is.EqualTo(DecisionKind.EnemyResponse));

            int physical = player.Shields.Physical;
            int magical = player.Shields.Magical;

            ActionResult played = engine.Submit(new PlayCardAction(player.Id, card.Uid, player.Id));
            Assert.That(played.Ok, Is.True, played.Reason);

            Assert.That(player.Shields.Physical, Is.EqualTo(physical + 4));
            Assert.That(player.Shields.Magical, Is.EqualTo(magical + 4));
            Assert.That(player.FreeMoves, Is.EqualTo(1));

            // 这次机动本来就是为了在敌人落手之前挪开，所以响应窗口里必须能兑现
            ActionResult moved = engine.Submit(new MoveRowAction(player.Id));
            Assert.That(moved.Ok, Is.True, "响应窗口里兑现不了：" + moved.Reason);
        }

        // ──────────────────────────── 本轮新迁的卡 ────────────────────────────

        [Test]
        public void N01_翠风箭附把物理转成风且加成只吃一次()
        {
            // 卡面：「本轮你造成的所有物理伤害**转化为**风属性伤害，
            //       且你本轮**下一次**攻击造成的伤害+3」——
            // 两个词条的持续时间不同：属性转化持续本轮，+3 只有一次。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = RealDuel();

            CardInstance card = TestFactory.AddToHand(player, _db.Card("verdant_wind_arrow"));
            Assert.That(engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id)).Ok, Is.True);

            var first = new AttackContext(player, enemy, 10, DamageElement.Physical, CritResult.None);
            engine.ResolveAttack(first);
            Assert.That(first.Element, Is.EqualTo(DamageElement.Wind), "物理应当转成风");
            Assert.That(first.Result.AfterModifiers, Is.EqualTo(13), "首击 10+3");

            var second = new AttackContext(player, enemy, 10, DamageElement.Physical, CritResult.None);
            engine.ResolveAttack(second);
            Assert.That(second.Element, Is.EqualTo(DamageElement.Wind), "属性转化持续本轮");
            Assert.That(second.Result.AfterModifiers, Is.EqualTo(10), "+3 是「下一次」，不该一直加");
        }

        [Test]
        public void N02_顺风充能把自己变成充能且不进弃牌堆()
        {
            // 卡面：「将**此牌**直接置入充能区」。
            // 不给行为层接管牌去向的开关，同一张牌会既在充能区又在弃牌堆。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel();

            CardInstance card = TestFactory.AddToHand(player, _db.Card("tailwind_charge"));
            Assert.That(engine.Submit(new PlayCardAction(player.Id, card.Uid)).Ok, Is.True);

            Assert.That(player.Zones.Charges.Tokens.Any(t => t.Card.Uid == card.Uid), Is.True, "没进充能区");
            Assert.That(player.Zones.Discard.Contains(card), Is.False, "同时还躺在弃牌堆里——被安置了两次");
        }

        [Test]
        public void N03_战术隐蔽只在后排生效且常规单体不再选他()
        {
            // 卡面：「**若打出时自身处于后排**，本轮敌方的常规单体攻击意图无法将你选为目标」
            (BattleEngine front, PlayerCharacter p1, Enemy _) = RealDuel();
            CardInstance c1 = TestFactory.AddToHand(p1, _db.Card("tactical_concealment"));
            front.Submit(new PlayCardAction(p1.Id, c1.Uid));
            Assert.That(p1.Statuses.Has(BattleState.HiddenStatusId), Is.False, "前排打出不该获得隐蔽");

            (BattleEngine back, PlayerCharacter p2, Enemy _) = RealDuel();
            back.SwitchRow(p2, free: true);
            CardInstance c2 = TestFactory.AddToHand(p2, _db.Card("tactical_concealment"));
            back.Submit(new PlayCardAction(p2.Id, c2.Uid));

            Assert.That(p2.Statuses.Has(BattleState.HiddenStatusId), Is.True, "后排打出应当获得隐蔽");
            // 只剩他一个人时仍然得有人挨打——隐蔽改的是「优先选谁」，不是「没人可选」
            Assert.That(back.State.DefaultAggroTarget(), Is.Not.Null, "全员隐蔽时意图不该凭空消失");
        }

        [Test]
        public void N04_苦痛转导要求本轮真的掉过血且只加魔法伤害()
        {
            // 卡面：「仅限本轮你已受到过伤害时打出……使你本轮打出的下一张**魔法**攻击牌伤害+3」
            //
            // 「受到过伤害」的口径是**实际掉血**：被自动双盾整个挡下来时不算。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = RealDuel();

            CardDefinition def = _db.Card("pain_transduction");
            CardInstance card = TestFactory.AddToHand(player, def);
            Assert.That(engine.Submit(new PlayCardAction(player.Id, card.Uid)).Ok, Is.False, "没掉血不该打得出");

            // 必须打穿自动盾，否则 HP 一点没掉
            engine.DealDamage(enemy, player, 30, DamageElement.Physical, CritResult.None);
            Assert.That(player.DamageTakenThisRound, Is.GreaterThan(0));

            player.SpendMp(player.Mp);
            int mp = player.Mp;
            Assert.That(engine.Submit(new PlayCardAction(player.Id, card.Uid)).Ok, Is.True);
            Assert.That(player.Mp, Is.EqualTo(mp + 10));

            var physical = new AttackContext(player, enemy, 10, DamageElement.Physical, CritResult.None);
            engine.ResolveAttack(physical);
            Assert.That(physical.Result.AfterModifiers, Is.EqualTo(10), "物理不该吃这 +3");

            var magic = new AttackContext(player, enemy, 10, DamageElement.Fire, CritResult.None);
            engine.ResolveAttack(magic);
            Assert.That(magic.Result.AfterModifiers, Is.EqualTo(13), "魔法才吃");
        }

        [Test]
        public void N05_命运锚点把下一次掷骰的第一颗视为7()
        {
            // 改的是**骰值**不是暴击结果：双色牌的超暴击要靠两颗骰的关系判定，
            // 直接写布尔结果会把那套规则绕过去。
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = RealDuel();

            CardInstance card = TestFactory.AddToHand(player, _db.Card("fate_anchor_seven"));
            Assert.That(engine.Submit(new PlayCardAction(player.Id, card.Uid)).Ok, Is.True);

            CritResult crit = engine.RollCrit(player, _db.Card("ambush_opening"), enemy);
            Assert.That(crit.IsCrit, Is.True, $"第 1 颗视为 7 应当必暴（抵抗 {enemy.CritResist}）");
            Assert.That(player.Statuses.Has("anchor_seven"), Is.False, "用过应当消失");
        }

        [Test]
        public void N06_真理炼金矩阵的偶数骰会保住充能()
        {
            // 卡面：「全队角色每当消耗充能指示物时，投掷1颗任意骰子，
            //       若点数为偶数，则该充能指示物不进入弃牌堆，继续留在充能区中」
            CardDefinition def = _db.Card("truth_alchemy_matrix");
            bool retained = false;
            bool discarded = false;

            for (int seed = 1; seed <= 40 && !(retained && discarded); seed++)
            {
                (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel(seed);
                player.Zones.SetContinuous(new CardInstance(def, player.Id), out CardInstance _);

                for (int i = 0; i < 2; i++)
                {
                    engine.PlaceCharge(player, new CardInstance(_db.Card("ambush_opening"), player.Id));
                }

                int before = player.Zones.Charges.Count;
                engine.SpendCharge(player, ChargeUse.ConvertToAp);

                if (player.Zones.Charges.Count == before) retained = true;
                else discarded = true;
            }

            Assert.That(retained, Is.True, "40 个种子里一次都没留存");
            Assert.That(discarded, Is.True, "40 个种子里一次都没进弃牌堆——判定可能恒为真");
        }

        [Test]
        public void N07_贤者之石对同一角色每轮限一次()
        {
            // 「每轮对同一角色限用1次」——只验「第一次能用」会让无限次的实现也通过
            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel();
            CardDefinition def = _db.Card("philosophers_stone_release");

            player.SpendMp(player.Mp);
            int mp = player.Mp;
            int ap = player.Ap;

            CardInstance first = TestFactory.AddToHand(player, def);
            Assert.That(engine.Submit(new PlayCardAction(player.Id, first.Uid, player.Id)).Ok, Is.True);
            Assert.That(player.Mp, Is.EqualTo(mp + 10));
            Assert.That(player.Ap, Is.EqualTo(ap - def.ApCost + 2));

            CardInstance second = TestFactory.AddToHand(player, def);
            Assert.That(engine.Submit(new PlayCardAction(player.Id, second.Uid, player.Id)).Ok, Is.False,
                "同一角色本轮第二次不该还能用");
        }

        [Test]
        public void N08_角色起始MP会被夹到上限()
        {
            // HP 一直是夹的，MP 不夹——传个超额的 currentMp 进来，角色会顶在上限之上，
            // 而「回复 10 MP」只回得了 0，表现成「这张回蓝牌没生效」，会一路怀疑到卡牌身上。
            PlayerCharacter player = TestFactory.Player(mp: 9999);

            Assert.That(player.Mp, Is.EqualTo(player.MaxMp));
            Assert.That(player.Mp, Is.LessThanOrEqualTo(player.MaxMp));
        }

        // ──────────────────────────── 敌人意图 = 敌人的牌 ────────────────────────────

        /// <summary>直接驱动一条意图挂的那张牌结算（跳过排队与响应窗口，只验效果）。</summary>
        private static void PlayIntent(BattleEngine engine, Enemy enemy, EnemyIntent intent)
        {
            typeof(BattleEngine)
                .GetMethod("PlayIntentCard", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(engine, new object[] { enemy, intent });
        }

        [Test]
        public void I01_每条意图都是一张敌人的牌()
        {
            // 敌人的意图不是另一套机制——它就是敌人手里一套玩家看不到的牌。
            // 这条守住「别再给意图另起一套平行接口」：一旦有人加回 on_intent_*，
            // 数据驱动效果和状态就要实现两遍，mod 作者还得学两套 API。
            var missing = new List<string>();

            foreach (EnemyContent enemy in _db.Enemies.Values)
            {
                foreach (KeyValuePair<string, EnemyIntent> pair in enemy.Intents)
                {
                    if (pair.Value.Card == null) { missing.Add($"{pair.Key}：没有对应的牌"); continue; }
                    if (pair.Value.Card.Kind != CardKind.Enemy) missing.Add($"{pair.Key}：kind 不是 Enemy");
                }
            }

            Assert.That(missing, Is.Empty, string.Join("\n", missing));
        }

        [Test]
        public void I02_以太虹吸吞充能并让首领回血且不计入玩家的消耗统计()
        {
            // 卡面：「全体受到6点暗属性魔法伤害，各吞噬1个充能；每吞噬1个，首领回复8HP。消耗10MP。」
            BattleEngine engine = ContentScenario.CreateClockworkCollapse(20261118, out LoadReport report);
            Assert.That(engine, Is.Not.Null, report.ToString());

            Enemy boss = engine.State.FindEnemy("boss");
            EnemyIntent intent = _db.Enemy("boss").Intents["aether_siphon"];

            Assert.That(intent.MpCost, Is.EqualTo(10), "老数据的 mp 费用没提成正式字段");
            Assert.That(boss.MaxMp, Is.GreaterThan(0), "首领没有 MP 面板，这条意图就是无代价的");

            foreach (PlayerCharacter p in engine.State.Players)
            {
                engine.PlaceCharge(p, new CardInstance(_db.Card("ambush_opening"), p.Id));
            }

            int chargesBefore = engine.State.Players.Sum(p => p.Zones.Charges.Count);
            boss.LoseHp(40);
            int hp = boss.Hp;
            int mp = boss.Mp;

            PlayIntent(engine, boss, intent);

            int devoured = chargesBefore - engine.State.Players.Sum(p => p.Zones.Charges.Count);
            Assert.That(devoured, Is.GreaterThan(0), "一个充能都没吞");
            Assert.That(boss.Hp, Is.EqualTo(hp + devoured * 8));
            Assert.That(boss.Mp, Is.EqualTo(mp - 10));

            // 「吞噬」不是「消耗」：记进 ChargesSpent 的话，敌人一发虹吸
            // 就替玩家满足了「全队累计消耗 2 个充能」的升变条件
            Assert.That(engine.State.Stats.Team(BattleStatKeys.ChargesSpent), Is.Zero,
                "吞噬被记成了玩家消耗充能");
        }

        [Test]
        public void I03_相位折跃前后排互换且保持各自的列()
        {
            // 卡面：「随后前后排互换，命刻额外+1」
            BattleEngine engine = ContentScenario.CreateClockworkCollapse(20261118, out LoadReport _);
            Enemy boss = engine.State.FindEnemy("boss");

            var before = engine.State.Players.ToDictionary(p => p.Id, p => (p.Row, p.Slot));
            int clock = engine.State.SceneClock;

            PlayIntent(engine, boss, _db.Enemy("boss").Intents["phase_shift"]);

            foreach (PlayerCharacter p in engine.State.Players)
            {
                (Row row, int slot) = before[p.Id];
                Assert.That(p.Row, Is.Not.EqualTo(row), $"{p.Name} 没换排");
                Assert.That(p.Slot, Is.EqualTo(slot), $"{p.Name} 的列变了——§4.3 的相邻关系被打乱");
            }

            Assert.That(engine.State.SceneClock, Is.EqualTo(clock + 1));
        }

        [Test]
        public void I04_命运乱流按手牌数追加无视护盾的刺痛()
        {
            // 卡面：「每剩余1张手牌，额外受到2点**无视护盾**刺痛」
            BattleEngine engine = ContentScenario.CreateClockworkCollapse(20261118, out LoadReport _);
            Enemy boss = engine.State.FindEnemy("boss");
            PlayerCharacter player = engine.State.Players[0];

            player.Shields.Grant(ShieldKind.Physical, 99);
            player.Shields.Grant(ShieldKind.Magical, 99);

            int hand = player.Zones.Hand.Count;
            Assert.That(hand, Is.GreaterThan(0));
            int hp = player.Hp;

            PlayIntent(engine, boss, _db.Enemy("boss").Intents["fate_turbulence"]);

            Assert.That(hp - player.Hp, Is.EqualTo(hand * 2), "护盾应当被完全无视");
        }

        [Test]
        public void I05_过热让首领更易被暴击且受伤加重()
        {
            // 卡面「归零坍缩」：「释放后下轮过热：暴击抵抗4，受到最终伤害×1.5」
            BattleEngine engine = ContentScenario.CreateClockworkCollapse(20261118, out LoadReport _);
            Enemy boss = engine.State.FindEnemy("boss");
            PlayerCharacter player = engine.State.Players[0];

            engine.ApplyStatus(boss, engine.Statuses.Create("overheat", 2, 1));

            Assert.That(boss.Statuses.SumCritResistDelta, Is.EqualTo(-3), "抵抗 7 应当被压到 4");

            int hp = boss.Hp;
            engine.DealDamage(player, boss, 10, DamageElement.Physical, CritResult.None);
            Assert.That(hp - boss.Hp, Is.EqualTo(15), "10 × 1.5 = 15");
        }

        // ──────────────────────────── 执行时限 ────────────────────────────

        [Test]
        public void W01_钩子里的死循环会被中断()
        {
            // 沙箱挡得住「乱碰东西」，挡不住「不还回来」——
            // while true do end 不抛任何异常，只是让主线程再也不返回。
            var host = new LuaScriptHost(budgetMs: 50);
            var report = new ScriptLoadReport();
            host.Load("behaviour('spin', { on_play = function(ctx) while true do end end })", "spin.lua", report);

            var def = new CardDefinition("spin_card", "死循环", CardCategory.Class, CardKind.Skill)
            {
                ApCost = 1,
                BehaviourId = "spin",
                Colors = new[] { AttributeColor.Might },
                Effects = new[] { EffectData.Damage(ValueExpr.Flat(5)) },
            };

            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Duel(host, report, new[] { "spin" });
            CardInstance card = TestFactory.AddToHand(player, def);

            var clock = System.Diagnostics.Stopwatch.StartNew();
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));
            clock.Stop();

            Assert.That(clock.ElapsedMilliseconds, Is.LessThan(3000), "没被中断");
            Assert.That(host.TimeoutCount, Is.GreaterThan(0), "超时没被计数");
            Assert.That(result.Ok, Is.True, "应当只是那个钩子失效，不是整个动作被废掉");
            Assert.That(enemy.Hp, Is.EqualTo(95), "数据驱动的效果照常结算");

            BattleEvent fault = engine.Events.LastOrDefault(e => e.Type == BattleEventType.BehaviourFaulted);
            Assert.That(fault, Is.Not.Null);
            Assert.That(fault.Text, Does.Contain("超过"), "故障文本要说明是超时：" + fault?.Text);
        }

        [Test]
        public void W02_加载期的死循环也会被中断且宿主还能继续用()
        {
            // 死循环写在文件顶层一样能把游戏卡在加载界面
            var host = new LuaScriptHost(budgetMs: 50);
            var report = new ScriptLoadReport();

            var clock = System.Diagnostics.Stopwatch.StartNew();
            host.Load("while true do end", "spin_toplevel.lua", report);
            clock.Stop();

            // 加载预算是秒级的：编译整个文件本来就比跑一个钩子重得多，
            // 用钩子那档预算会把正常内容包直接误杀（真实的 miku.lua 就超过 50ms）
            Assert.That(clock.ElapsedMilliseconds, Is.LessThan(10000), "没被中断");
            Assert.That(report.Errors, Is.Not.Empty, "超时应当记成加载错误");

            host.Load("behaviour('ok', { on_play = function(ctx) end })", "ok.lua", report);
            Assert.That(host.BuildRegistry(new[] { "ok" }, report).IsRegistered("ok"), Is.True,
                "一次超时之后宿主就该还能继续加载别的文件");
        }

        [Test]
        public void W03_真实内容包不会被时限误杀()
        {
            // 误杀比超时更难查：一张写对的卡会静默失效，而且只在慢机器上复现
            LuaScriptHost host = ContentScenario.LoadScripts(_db, _root, out ScriptLoadReport report);
            host.BuildRegistry(_db.DeclaredBehaviourIds, report);

            Assert.That(report.Errors, Is.Empty, report.ToString());
            Assert.That(host.TimeoutCount, Is.Zero, "真实内容包加载不该触发时限");
        }

        // ──────────────────────────── 升变 ────────────────────────────

        [Test]
        public void T01_四张升变牌都有脚本且都写了条件()
        {
            // 条件型升变牌没挂 Behaviour 时引擎视为「条件已满足」（方便做占位内容），
            // 所以漏写不会报错，只会让它一直可发动——必须直接查有没有写。
            LuaScriptHost host = ContentScenario.LoadScripts(_db, _root, out ScriptLoadReport report);
            host.BuildRegistry(_db.DeclaredBehaviourIds, report);

            var missing = new List<string>();
            foreach (CardDefinition card in _db.Cards.Values.Where(c => c.Kind == CardKind.Transform))
            {
                if (!report.Registered.TryGetValue(card.BehaviourId ?? "", out List<string> hooks))
                {
                    missing.Add($"{card.Name}：没有脚本");
                    continue;
                }

                if (!hooks.Contains("transform_condition")) missing.Add($"{card.Name}：没写 transform_condition");
            }

            Assert.That(missing, Is.Empty, string.Join("\n", missing));
        }

        [Test]
        public void T02_鹰眼绝境只在后排可发动()
        {
            // 卡面：「轮始阶段若自身处于**后排**且此牌在手牌中，可在第1或第2轮直接发动升变」
            CardDefinition def = _db.Card("hawkeye_overdrive");

            (BattleEngine front, PlayerCharacter p1, Enemy _) = RealDuel();
            p1.Zones.SetFormDeck(def.Id, FormCardsFor(def.Id, p1));
            TestFactory.AddToHand(p1, def);

            Assert.That(front.TransformCandidates(p1).Any(c => c.Definition.Id == def.Id), Is.False,
                "前排不该可以发动");

            (BattleEngine back, PlayerCharacter p2, Enemy _) = RealDuel();
            back.SwitchRow(p2, free: true);
            p2.Zones.SetFormDeck(def.Id, FormCardsFor(def.Id, p2));
            TestFactory.AddToHand(p2, def);

            back.Submit(new DeclareReadyAction(p2.Id));
            Assert.That(back.Pending.Kind, Is.EqualTo(DecisionKind.TransformDeclaration));
            Assert.That(back.TransformCandidates(p2).Any(c => c.Definition.Id == def.Id), Is.True,
                "后排第 2 轮应当可以发动");
        }

        [Test]
        public void T03_幻影夜行的1点75倍不该把超暴击削弱()
        {
            // 卡面写的是「**暴击**伤害倍率提升至×1.75」。超暴击默认 ×2，
            // 无条件改成 ×1.75 等于把它削弱了——这正是我第一版写错的地方。
            (BattleEngine e1, PlayerCharacter p1, Enemy t1) = Transformed();
            var superAttack = new AttackContext(p1, t1, 100, DamageElement.Physical,
                new CritResult(true, true, new[] { 9, 9 }, 6));
            e1.ResolveAttack(superAttack);

            Assert.That(superAttack.Result.AfterCrit, Is.EqualTo(200), "超暴击仍该是 ×2");

            // 另起一局：100 点超暴击已经把敌人打死了，第二发会落在尸体上
            (BattleEngine e2, PlayerCharacter p2, Enemy t2) = Transformed();
            var normalAttack = new AttackContext(p2, t2, 100, DamageElement.Physical,
                new CritResult(true, false, new[] { 9 }, 6));
            e2.ResolveAttack(normalAttack);

            Assert.That(normalAttack.Result.AfterCrit, Is.EqualTo(175), "普通暴击 ×1.75");
        }

        [Test]
        public void T04_星穹坍缩在累计回复15MP后可发动()
        {
            // 卡面：「本局累计触发2次「灵智回流」效果，**或**累计回复过15 MP」
            CardDefinition def = _db.Card("cosmic_collapse");
            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel();

            player.Zones.SetFormDeck(def.Id, FormCardsFor(def.Id, player));
            TestFactory.AddToHand(player, def);

            Assert.That(engine.TransformCandidates(player).Any(c => c.Definition.Id == def.Id), Is.False,
                "一点 MP 都没回就可以发动");

            // 先把 MP 腾空：回复会被 MaxMp 夹住，统计记的是**实际回复量**
            player.SpendMp(player.Mp);
            engine.RestoreMp(player, 15);

            Assert.That(engine.State.Stats.For(player.Id, BattleStatKeys.MpRestored),
                Is.GreaterThanOrEqualTo(15));

            engine.Submit(new DeclareReadyAction(player.Id));
            Assert.That(engine.TransformCandidates(player).Any(c => c.Definition.Id == def.Id), Is.True);
        }

        // ──────────────────────────── 费用减免 ────────────────────────────

        [Test]
        public void S01_先声夺人打出即自动移位并抽牌()
        {
            // 卡面：「在前后排之间移动1次并抽1张牌」——**强制**，不是「可」。
            // 移动与抽牌纯数据就能表达，写在卡面 effects 里。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel();
            Assert.That(player.Row, Is.EqualTo(Row.Front));

            int hand = player.Zones.Hand.Count;
            CardInstance card = TestFactory.AddToHand(player, _db.Card("seize_initiative"));

            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid));
            Assert.That(result.Ok, Is.True, result.Reason);

            Assert.That(player.Row, Is.EqualTo(Row.Back), "打出后应当自动移位");
            // 加进来 +1、打出去 -1、抽牌 +1 → 净 +1
            Assert.That(player.Zones.Hand.Count, Is.EqualTo(hand + 1), "应当抽 1 张");
        }

        [Test]
        public void S02_先声夺人第1轮免除5MP并额外获得1AP()
        {
            // 卡面：「若在战斗的第1轮打出此牌，免除5 MP消耗，且本轮额外获得+1 AP」。
            // 「免除消耗」是**费用修正**，发生在校验之前——没有费用钩子就根本表达不了。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel();
            Assert.That(engine.State.Round, Is.EqualTo(1));

            CardDefinition def = _db.Card("seize_initiative");
            Assert.That(engine.ResolveCost(player, def).Mp, Is.EqualTo(0),
                $"第 1 轮应当免除 MP（卡面 {def.MpCost}）");

            int mp = player.Mp;
            int ap = player.Ap;

            CardInstance card = TestFactory.AddToHand(player, def);
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid));
            Assert.That(result.Ok, Is.True, result.Reason);

            Assert.That(player.Mp, Is.EqualTo(mp), "第 1 轮不该扣 MP");
            Assert.That(player.Ap, Is.EqualTo(ap - def.ApCost + 1), "应当额外 +1 AP");
        }

        [Test]
        public void S03_先声夺人第2轮照常收费且不送AP()
        {
            // 只验「第1轮免费」会让一个无条件免费的实现也通过，所以必须同时钉住反面。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel();

            engine.Submit(new DeclareReadyAction(player.Id));
            if (engine.Pending.Kind == DecisionKind.TransformDeclaration)
            {
                engine.Submit(new DeclineTransformAction(player.Id));
            }

            Assert.That(engine.State.Round, Is.EqualTo(2));

            CardDefinition def = _db.Card("seize_initiative");
            Assert.That(engine.ResolveCost(player, def).Mp, Is.EqualTo(def.MpCost), "第 2 轮应当照常收费");

            int mp = player.Mp;
            int ap = player.Ap;

            CardInstance card = TestFactory.AddToHand(player, def);
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid));
            Assert.That(result.Ok, Is.True, result.Reason);

            Assert.That(player.Mp, Is.EqualTo(mp - def.MpCost));
            Assert.That(player.Ap, Is.EqualTo(ap - def.ApCost), "第 2 轮不该送 AP");
        }

        // ──────────────────────────── 费用显示 ────────────────────────────

        [Test]
        public void C01_零费卡印0而不是免费()
        {
            // 费用位永远是个数字才好一眼横向比较；写「免费」还容易被看成「这里没加载出来」
            string text = BattleSession.CostText(
                new CardDefinition("x", "x", CardCategory.Treasure, CardKind.Skill));

            Assert.That(text, Is.EqualTo("0"));
            Assert.That(text, Does.Not.Contain("免费"));
        }

        [Test]
        public void C02_减免项染绿且减到0也要显示()
        {
            // 「先声夺人」卡面 0AP / 5MP，第 1 轮免除 MP。
            // 减免到 0 恰恰是最该让玩家看见的那一次，不能因为「是 0」就省略掉。
            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel();
            string text = BattleSession.CostText(_db.Card("seize_initiative"), engine, player);

            Assert.That(text, Does.Contain(BattleSession.DiscountColor), "减免项应当染绿：" + text);
            Assert.That(text, Does.Contain("0MP"), "减免到 0 也要显示：" + text);
            Assert.That(text, Does.Not.Contain("AP"), "AP 是 0 且没被改过，不该显示：" + text);
            Assert.That(text, Does.Not.Contain("免费"));
        }

        [Test]
        public void C03_没有减免时不掺颜色标记()
        {
            // 纯文本场合（战报、测试断言）不该被 <color=...> 污染
            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel();

            engine.Submit(new DeclareReadyAction(player.Id));
            if (engine.Pending.Kind == DecisionKind.TransformDeclaration)
            {
                engine.Submit(new DeclineTransformAction(player.Id));
            }

            string text = BattleSession.CostText(_db.Card("seize_initiative"), engine, player);

            Assert.That(text, Does.Not.Contain("<color"), "第 2 轮没有减免，不该染色：" + text);
            Assert.That(text, Does.Contain("5MP"));
        }

        [Test]
        public void C04_加价项染红()
        {
            // 费用钩子也能往上加。加价却用默认色显示会误导玩家，所以反向也要有颜色。
            var host = new LuaScriptHost();
            var report = new ScriptLoadReport();
            host.Load(
                "behaviour('pricey', { on_cost = function(cost, me) cost.mp = cost.mp + 3 end })",
                "pricey.lua", report);

            var def = new CardDefinition("pricey_card", "贵牌", CardCategory.Class, CardKind.Skill)
            {
                ApCost = 1,
                MpCost = 5,
                BehaviourId = "pricey",
                Colors = new[] { AttributeColor.Might },
            };

            (BattleEngine engine, PlayerCharacter player, Enemy _) =
                Duel(host, report, new[] { "pricey" });

            string text = BattleSession.CostText(def, engine, player);

            Assert.That(text, Does.Contain(BattleSession.SurchargeColor), "加价项应当染红：" + text);
            Assert.That(text, Does.Contain("8MP"), "5 + 3 = 8：" + text);
            Assert.That(text, Does.Contain("1AP"), "没被改过的 AP 照常显示：" + text);
        }

        // ──────────────────────────── 意图索敌时点（裁定 R-24） ────────────────────────────

        [Test]
        public void R24a_单体意图在宣告时锁定目标走位躲不掉()
        {
            // 意图文本是「选择/随机选择一个位于XX的角色，攻击」——
            // 已经选定了具体某个人，他之后挪到哪都算数。
            var intent = new EnemyIntent("死刑重击", IntentTargeting.DefaultSingle)
            {
                Damage = 20,
                Element = DamageElement.Physical,
            };

            Assert.That(intent.LocksTarget, Is.True, "单体指向应当在宣告时锁定");

            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel(intent: intent);
            engine.GrantFreeMove(player, 1);
            engine.Submit(new DeclareReadyAction(player.Id));

            int mark = engine.Events.Count;
            Assert.That(engine.Submit(new MoveRowAction(player.Id)).Ok, Is.True);
            engine.Submit(new PassResponseAction(player.Id));

            Assert.That(
                engine.Events.Skip(mark).Any(
                    e => e.Type == BattleEventType.DamageDealt && e.TargetId == player.Id),
                Is.True,
                "目标已被锁定，走位不应躲开");
        }

        [Test]
        public void R24b_范围意图在结算时才定目标走位躲得掉()
        {
            // 意图文本是「攻击前排角色」——指的是**结算时**站在前排的那些人，
            // 所以在响应窗口里挪出前排就是真的躲开了。
            var intent = new EnemyIntent("前排横扫", IntentTargeting.FrontRowAoe)
            {
                Damage = 20,
                Element = DamageElement.Physical,
            };

            Assert.That(intent.LocksTarget, Is.False, "范围指向不该锁定目标");

            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel(intent: intent);
            Assert.That(player.Row, Is.EqualTo(Row.Front));

            engine.GrantFreeMove(player, 1);
            engine.Submit(new DeclareReadyAction(player.Id));

            int mark = engine.Events.Count;
            Assert.That(engine.Submit(new MoveRowAction(player.Id)).Ok, Is.True);
            Assert.That(player.Row, Is.EqualTo(Row.Back));
            engine.Submit(new PassResponseAction(player.Id));

            Assert.That(
                engine.Events.Skip(mark).Any(
                    e => e.Type == BattleEventType.DamageDealt && e.TargetId == player.Id),
                Is.False,
                "挪出前排后不应再挨这一击");
        }

        [Test]
        public void R24c_范围意图里挪进来的人会被打到()
        {
            // R-24 的另一半：结算时才定目标，意味着「挪进去」同样要承担后果。
            // 只验「挪出去能躲」会让一个只在目标离开时才收缩的实现也蒙混过关。
            var intent = new EnemyIntent("前排横扫", IntentTargeting.FrontRowAoe)
            {
                Damage = 20,
                Element = DamageElement.Physical,
            };

            (BattleEngine engine, PlayerCharacter player, Enemy _) = RealDuel(intent: intent, startRow: Row.Back);
            Assert.That(player.Row, Is.EqualTo(Row.Back));

            engine.GrantFreeMove(player, 1);
            engine.Submit(new DeclareReadyAction(player.Id));

            int mark = engine.Events.Count;
            engine.Submit(new MoveRowAction(player.Id));
            Assert.That(player.Row, Is.EqualTo(Row.Front));
            engine.Submit(new PassResponseAction(player.Id));

            Assert.That(
                engine.Events.Skip(mark).Any(
                    e => e.Type == BattleEventType.DamageDealt && e.TargetId == player.Id),
                Is.True,
                "结算时站在前排就该挨打");
        }

        // ──────────────────────────── 搭局辅助 ────────────────────────────

        /// <summary>用真实内容包的脚本与状态开一局 1v1。</summary>
        private (BattleEngine, PlayerCharacter, Enemy) RealDuel(
            int seed = 7,
            int intentDamage = 0,
            EnemyIntent intent = null,
            Row startRow = Row.Front)
        {
            LuaScriptHost host = ContentScenario.LoadScripts(_db, _root, out ScriptLoadReport report);
            return Duel(host, report, _db.DeclaredBehaviourIds, seed, intentDamage, intent, startRow);
        }

        private (BattleEngine, PlayerCharacter, Enemy) Duel(
            LuaScriptHost host,
            ScriptLoadReport report,
            IEnumerable<string> declared,
            int seed = 7,
            int intentDamage = 0,
            EnemyIntent customIntent = null,
            Row startRow = Row.Front)
        {
            BehaviourRegistry behaviours = host.BuildRegistry(declared, report);

            EnemyIntent[] intents =
                customIntent != null ? new[] { customIntent }
                : intentDamage > 0 ? new[] { TestFactory.Attack(intentDamage) }
                : null;

            PlayerCharacter player = TestFactory.Player(hp: 40, mp: 50);
            Enemy enemy = TestFactory.Enemy(hp: 100, rank: EnemyRank.Elite, intents: intents);

            TestFactory.FillDeck(player, 10);

            BattleState state = BattleSetup.Create(
                new XorShift32Rng(seed),
                new[] { (player, startRow) },
                new[] { (enemy, Row.Front) });

            var engine = new BattleEngine(
                state, EffectRegistry.CreateDefault(), behaviours, _db.BuildStatusRegistry());

            host.BindEngine(engine);
            engine.Start();

            // 轮始的升变宣告窗口会挡住出牌，没有可发动的升变牌时直接放弃
            if (engine.Pending.Kind == DecisionKind.TransformDeclaration)
            {
                engine.Submit(new DeclineTransformAction(player.Id));
            }

            return (engine, player, enemy);
        }

        /// <summary>
        /// 走**真实流程**进入升变：第 1 轮攒够 2 次暴击，第 2 轮轮始宣告发动。
        /// 不直接捅 <c>Transform.Enter</c>——那样测不到「条件判定 → 候选 → 宣告」这条链。
        /// </summary>
        private (BattleEngine, PlayerCharacter, Enemy) Transformed()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = RealDuel();

            CardDefinition transform = _db.Card("phantom_nightwalk");
            player.Zones.SetFormDeck(transform.Id, FormCardsFor(transform.Id, player));
            TestFactory.AddToHand(player, transform);

            var crit = new CritResult(true, false, new[] { 9 }, 6);
            engine.DealDamage(player, enemy, 1, DamageElement.Physical, crit);
            engine.DealDamage(player, enemy, 1, DamageElement.Physical, crit);

            engine.Submit(new DeclareReadyAction(player.Id));
            Assert.That(engine.Pending.Kind, Is.EqualTo(DecisionKind.TransformDeclaration),
                "第 2 轮轮始应当开出升变宣告窗口");

            CardInstance candidate = engine.TransformCandidates(player)
                .FirstOrDefault(c => c.Definition.Id == transform.Id);
            Assert.That(candidate, Is.Not.Null, "条件已达成但幻影夜行不在候选里");

            ActionResult result = engine.Submit(new DeclareTransformAction(player.Id, candidate.Uid));
            Assert.That(result.Ok, Is.True, result.Reason);
            Assert.That(player.Transform.Active, Is.True);

            return (engine, player, enemy);
        }

        /// <summary>同一张卡、同一批种子，数「暴了几次」。</summary>
        private int CountCrits(bool useLock, CardDefinition card)
        {
            int crits = 0;

            for (int seed = 1; seed <= 60; seed++)
            {
                (BattleEngine engine, PlayerCharacter player, Enemy enemy) = RealDuel(seed);
                engine.SwitchRow(player, free: true);

                if (useLock)
                {
                    player.Zones.SetContinuous(
                        new CardInstance(_db.Card("hawkeye_lock"), player.Id), out CardInstance _);
                }

                CardInstance instance = TestFactory.AddToHand(player, card);
                engine.Submit(new PlayCardAction(player.Id, instance.Uid, enemy.Id));

                if (engine.Events.Any(e => e.Type == BattleEventType.DamageDealt && e.Damage is { IsCrit: true }))
                {
                    crits++;
                }
            }

            return crits;
        }

        /// <summary>把每条伤害效果标成「各自投骰」，验 R-23 用。</summary>
        private static CardDefinition WithOwnCrit(CardDefinition source)
        {
            var effects = source.Effects
                .Select(e => new EffectData
                {
                    Kind = e.Kind,
                    Amount = e.Amount,
                    Target = e.Target,
                    Element = e.Element,
                    ShieldKind = e.ShieldKind,
                    StatusId = e.StatusId,
                    Duration = e.Duration,
                    Condition = e.Condition,
                    RollsOwnCrit = e.Kind == EffectKind.Damage,
                })
                .ToArray();

            return new CardDefinition(source.Id, source.Name, source.Category, source.Kind)
            {
                Colors = source.Colors,
                ApCost = source.ApCost,
                MpCost = 0,
                HasCrit = source.HasCrit,
                IsRanged = source.IsRanged,
                BehaviourId = source.BehaviourId,
                Effects = effects,
            };
        }

        private static CardDefinition Bomb(string behaviourId)
            => new("bomb", "炸弹", CardCategory.Class, CardKind.Skill)
            {
                ApCost = 1,
                BehaviourId = behaviourId,
                Colors = new[] { AttributeColor.Might },
                Effects = new[] { EffectData.Damage(ValueExpr.Flat(5)) },
            };

        private List<CardInstance> FormCardsFor(string transformCardId, PlayerCharacter owner)
            => _db.Cards.Values
                .Where(c => c.Kind == CardKind.Form)
                .Take(DeckList.FormDeckSize)
                .Select(c => new CardInstance(c, owner.Id))
                .ToList();
    }
}
