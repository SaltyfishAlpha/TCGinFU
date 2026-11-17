using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>
    /// 已登记的统计项。条件型升变牌的前置条件（§6.1：「造成过暴击」「全队累计消耗过 3 个充能」）
    /// 要有数据来源，统一走这里，而不是往 PlayerCharacter 上挂一堆零散计数器。
    /// </summary>
    public static class BattleStatKeys
    {
        public const string CritsDealt = "crits_dealt";
        public const string SuperCritsDealt = "super_crits_dealt";
        public const string ChargesSpent = "charges_spent";
        public const string CardsPlayed = "cards_played";
        public const string DamageDealt = "damage_dealt";
        public const string EnemiesDowned = "enemies_downed";
    }

    /// <summary>
    /// 本局累计统计。每项同时记「全队合计」与「每个角色各自」，
    /// 因为卡面条件两种口径都有（「你造成过暴击」vs「全队消耗过 3 个充能」）。
    /// </summary>
    public sealed class BattleStats
    {
        private readonly Dictionary<string, int> _team = new();
        private readonly Dictionary<(string Actor, string Key), int> _perActor = new();

        /// <summary>全队累计值。</summary>
        public int Team(string key) => _team.TryGetValue(key, out int value) ? value : 0;

        /// <summary>某个角色的累计值。</summary>
        public int For(string actorId, string key)
            => actorId != null && _perActor.TryGetValue((actorId, key), out int value) ? value : 0;

        internal void Add(string key, string actorId, int amount = 1)
        {
            if (amount == 0) return;

            _team[key] = Team(key) + amount;
            if (actorId == null) return;

            _perActor[(actorId, key)] = For(actorId, key) + amount;
        }
    }
}
