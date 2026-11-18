using System.Collections.Generic;
using System.Linq;
using TCGinFU.Content.Loading;
using TCGinFU.Core;

namespace TCGinFU.Presentation
{
    /// <summary>
    /// 用内容包里的真实数据组一局战斗。
    ///
    /// 与 <c>SampleDeck</c> / <c>SampleEnemies</c> 那套写死在 C# 里的样例内容并行存在：
    /// 样例内容服务于引擎自测（覆盖各种机制组合），本类服务于真实卡池。
    /// 两边都只产出 Core 的 POCO，引擎本身不知道数据从哪来。
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
        /// 组好阵容并建引擎。行为钩子注册表目前是空的——49 张卡的 <c>behaviourId</c>
        /// 都还是占位（见 data_architecture.md §6「留给后续」），
        /// <c>BehaviourRegistry.Resolve</c> 对未注册的 Id 返回 null，按纯数据卡结算。
        /// </summary>
        public static BattleEngine CreateEngine(
            ContentDatabase db,
            IRng rng,
            IEnumerable<string> heroIds,
            IEnumerable<string> enemyIds,
            LoadReport report)
        {
            Lineup lineup = BuildLineup(db, rng, heroIds, enemyIds, report);
            if (lineup.Party.Count == 0 || lineup.Enemies.Count == 0)
            {
                report.Errors.Add("阵容不完整，无法开始战斗");
                return null;
            }

            BattleState state = BattleSetup.Create(rng, lineup.Party, lineup.Enemies);

            var engine = new BattleEngine(
                state,
                EffectRegistry.CreateDefault(),
                new BehaviourRegistry(),
                new StatusRegistry());

            engine.Start();
            return engine;
        }

        /// <summary>一次性加载默认内容包并开一局命刻之战。</summary>
        public static BattleEngine CreateClockworkCollapse(int seed, out LoadReport report)
        {
            ContentDatabase db = ContentPaths.LoadDefault(out report);
            if (!report.IsValid) return null;

            return CreateEngine(db, new XorShift32Rng(seed), ClockworkHeroes, ClockworkEnemies, report);
        }
    }
}
