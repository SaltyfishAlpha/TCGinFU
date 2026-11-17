using System;
using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 一局战斗的全部可变状态。引擎是唯一的写入者；外部只读。
    /// </summary>
    public sealed class BattleState
    {
        private readonly List<PlayerCharacter> _players = new();
        private readonly List<Enemy> _enemies = new();

        public BattleState(IRng rng, IEnumerable<PlayerCharacter> players, IEnumerable<Enemy> enemies)
        {
            Rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _players.AddRange(players ?? throw new ArgumentNullException(nameof(players)));
            _enemies.AddRange(enemies ?? throw new ArgumentNullException(nameof(enemies)));

            if (_players.Count == 0) throw new ArgumentException("至少需要 1 名玩家角色", nameof(players));
        }

        public IRng Rng { get; }

        public IReadOnlyList<PlayerCharacter> Players => _players;
        public IReadOnlyList<Enemy> Enemies => _enemies;

        public Battlefield PlayerField { get; } = new();
        public Battlefield EnemyField { get; } = new();

        /// <summary>当前轮次，战斗开始后从 1 计。</summary>
        public int Round { get; internal set; }

        public RoundPhase Phase { get; internal set; } = RoundPhase.RoundStart;

        /// <summary>场景命刻（§8 ⑤），每轮末 +1。</summary>
        public int SceneClock { get; internal set; }

        public BattleOutcome Outcome { get; internal set; } = BattleOutcome.Ongoing;

        public IEnumerable<PlayerCharacter> LivingPlayers => _players.Where(p => !p.IsDown);
        public IEnumerable<Enemy> LivingEnemies => _enemies.Where(e => !e.IsDown);

        public int ChargeCapacity => ChargeZone.CapacityFor(_players.Count);

        public PlayerCharacter FindPlayer(string id) => _players.FirstOrDefault(p => p.Id == id);
        public Enemy FindEnemy(string id) => _enemies.FirstOrDefault(e => e.Id == id);

        public Combatant Find(string id) => (Combatant)FindPlayer(id) ?? FindEnemy(id);

        public Battlefield FieldOf(Combatant combatant)
            => combatant is PlayerCharacter ? PlayerField : EnemyField;

        /// <summary>
        /// 常规仇恨指向（§4.2）：优先前排；前排无人则取后排**上一轮造成伤害最高**者。
        /// 第 1 轮前排空虚时没有「上一轮」数据，此时取后排第一个存活者。
        /// </summary>
        public PlayerCharacter DefaultAggroTarget()
        {
            List<PlayerCharacter> front = LivingPlayers.Where(p => p.Row == Row.Front).ToList();
            if (front.Count > 0)
            {
                return front.OrderBy(p => p.Slot).First();
            }

            List<PlayerCharacter> back = LivingPlayers.Where(p => p.Row == Row.Back).ToList();
            if (back.Count == 0) return null;

            return back
                .OrderByDescending(p => p.DamageDealtLastRound)
                .ThenBy(p => p.Slot)
                .First();
        }

        public IEnumerable<PlayerCharacter> LivingPlayersInRow(Row row)
            => LivingPlayers.Where(p => p.Row == row);

        internal void CheckOutcome()
        {
            if (Outcome != BattleOutcome.Ongoing) return;

            if (!LivingEnemies.Any())
            {
                Outcome = BattleOutcome.Victory;
            }
            else if (!LivingPlayers.Any())
            {
                Outcome = BattleOutcome.Defeat;
            }
        }
    }
}
