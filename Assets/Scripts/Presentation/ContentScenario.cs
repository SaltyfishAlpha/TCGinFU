using System.Collections.Generic;
using System.Linq;
using TCGinFU.Content.Loading;
using TCGinFU.Core;
using TCGinFU.Scripting;

namespace TCGinFU.Presentation
{
    /// <summary>
    /// 用内容包里的真实数据组一局战斗。这是**唯一**的开局入口——
    /// 曾经并行存在一套写死在 C# 里的样例内容（<c>SampleDeck</c> / <c>SampleEnemies</c>），
    /// 已于 2026-11-22 整体移除，见 Assets/docs/legacy_cleanup_handoff.md。
    ///
    /// 只产出 Core 的 POCO，引擎本身不知道数据从哪来。
    /// </summary>
    public static class ContentScenario
    {
        /// <summary>命刻之战的默认阵容。</summary>
        public static readonly string[] ClockworkHeroes = { "miku", "weiting" };
        public static readonly string[] ClockworkEnemies = { "arm", "boss" };

        public sealed class Lineup
        {
            public List<(PlayerCharacter Player, Row Row)> Party { get; } = new();
            public List<(Enemy Enemy, Row Row)> Enemies { get; } = new();
            public Dictionary<string, DeckList> Decks { get; } = new();
        }

        /// <summary>
        /// 按角色 Id 与敌人 Id 组阵容。缺数据不抛异常，记进 <paramref name="report"/>——
        /// 玩家自定义包写错一个 Id 时应当看到可读的报错，而不是空引用。
        /// </summary>
        public static Lineup BuildLineup(
            ContentDatabase db,
            IRng rng,
            IEnumerable<string> heroIds,
            IEnumerable<string> enemyIds,
            LoadReport report)
        {
            var lineup = new Lineup();

            foreach (string heroId in heroIds)
            {
                CharacterContent character = db.Character(heroId);
                if (character == null) { report.Errors.Add($"找不到角色 {heroId}"); continue; }

                DeckList deck = db.BuildDeck(character.DeckId, report);
                if (deck == null) continue;

                lineup.Decks[heroId] = deck;
                lineup.Party.Add((character.CreatePlayer(deck, rng), character.StartingRow));
            }

            foreach (string enemyId in enemyIds)
            {
                EnemyContent content = db.Enemy(enemyId);
                if (content == null) { report.Errors.Add($"找不到敌人 {enemyId}"); continue; }

                lineup.Enemies.Add((content.CreateEnemy(), content.Row));
            }

            return lineup;
        }

        /// <summary>
        /// 加载内容包里的 Lua 行为脚本。
        ///
        /// <para>脚本加载失败不会让战斗开不起来：<c>BehaviourRegistry.Resolve</c> 对未注册的 Id 返回 null，
        /// 那张卡退化成纯数据卡。这是给**玩家自定义内容**留的余地——
        /// 一个 mod 写坏了应该只影响它自己的卡，而不是让人连游戏都进不去。</para>
        /// </summary>
        public static LuaScriptHost LoadScripts(ContentDatabase db, string root, out ScriptLoadReport report)
        {
            report = new ScriptLoadReport();
            var host = new LuaScriptHost();

            if (root == null)
            {
                report.Errors.Add("找不到内容包目录，行为脚本全部未加载");
                return host;
            }

            var source = new FileContentSource(root);
            foreach (string path in db.ScriptPaths)
            {
                string code = source.Read(path);
                if (code == null) { report.Errors.Add($"读不到脚本 {path}"); continue; }

                host.Load(code, path, report);
            }

            return host;
        }

        /// <summary>组好阵容并建引擎。行为钩子来自内容包里的 Lua 脚本。</summary>
        public static BattleEngine CreateEngine(
            ContentDatabase db,
            IRng rng,
            IEnumerable<string> heroIds,
            IEnumerable<string> enemyIds,
            LoadReport report,
            string contentRoot = null)
        {
            Lineup lineup = BuildLineup(db, rng, heroIds, enemyIds, report);
            if (lineup.Party.Count == 0 || lineup.Enemies.Count == 0)
            {
                report.Errors.Add("阵容不完整，无法开始战斗");
                return null;
            }

            LuaScriptHost host = LoadScripts(db, contentRoot ?? ContentPaths.Resolve(), out ScriptLoadReport scripts);
            BehaviourRegistry behaviours = host.BuildRegistry(db.DeclaredBehaviourIds, scripts);

            // 脚本的错误与警告并进内容包报告，调用方只需要看一处
            foreach (string error in scripts.Errors) report.Errors.Add("脚本：" + error);

            BattleState state = BattleSetup.Create(rng, lineup.Party, lineup.Enemies);

            var engine = new BattleEngine(
                state,
                EffectRegistry.CreateDefault(),
                behaviours,
                db.BuildStatusRegistry());

            // 必须在 Start() 之前绑：轮始触发在 Start() 里就会广播
            host.BindEngine(engine);

            engine.Start();
            return engine;
        }

        /// <summary>
        /// 最近一次加载的内容包。表现层要从它取默认卡背、角色立绘这些**美术标识**——
        /// 引擎本身不认识这些，所以不放在 <see cref="BattleEngine"/> 上。
        /// </summary>
        public static ContentDatabase LastLoaded { get; private set; }

        /// <summary>一次性加载默认内容包并开一局命刻之战。</summary>
        public static BattleEngine CreateClockworkCollapse(int seed, out LoadReport report)
        {
            string root = ContentPaths.Resolve();
            ContentDatabase db = ContentPaths.LoadDefault(out report, root);
            if (!report.IsValid) return null;

            LastLoaded = db;
            return CreateEngine(db, new XorShift32Rng(seed), ClockworkHeroes, ClockworkEnemies, report, root);
        }
    }
}
