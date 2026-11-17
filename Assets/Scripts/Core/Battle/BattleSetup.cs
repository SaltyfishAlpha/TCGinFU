using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 组装一局战斗：建 <see cref="BattleState"/>、按排站位、发牌。
    /// 战斗开始**不**回满 HP/MP——角色按跑团里剩下的数值入场（§1）。
    /// </summary>
    public static class BattleSetup
    {
        public static BattleState Create(
            IRng rng,
            IEnumerable<(PlayerCharacter Player, Row Row)> players,
            IEnumerable<(Enemy Enemy, Row Row)> enemies)
        {
            var playerList = players.ToList();
            var enemyList = enemies.ToList();

            var state = new BattleState(rng, playerList.Select(p => p.Player), enemyList.Select(e => e.Enemy));

            foreach ((PlayerCharacter player, Row row) in playerList)
            {
                // 充能区容量取决于全队人数（§4.1），只有到这里才知道，统一重设一遍
                player.Zones.Charges.SetCapacity(state.ChargeCapacity);
                state.PlayerField.Place(player, row);
            }

            foreach ((Enemy enemy, Row row) in enemyList)
            {
                state.EnemyField.Place(enemy, row);
            }

            return state;
        }

        /// <summary>把卡表发给角色（洗切主卡组、登记形态额外卡组）。</summary>
        public static void Deal(BattleState state, PlayerCharacter player, DeckList deck)
            => deck.DealTo(player, state.Rng);
    }
}
