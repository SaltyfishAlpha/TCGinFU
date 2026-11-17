using System.Collections.Generic;
using System.Linq;
using TCGinFU.Core;

namespace TCGinFU.Content
{
    /// <summary>
    /// 样例敌人。意图由 <see cref="Enemy.IntentPlanner"/> 每轮重新生成，
    /// 所以可以按轮次、血量做分阶段行为。
    /// </summary>
    public static class SampleEnemies
    {
        /// <summary>哥布林：普通小怪，意图完全公开，暴击抵抗 4，轮流挥砍与投石。</summary>
        public static Enemy Goblin(string id = "goblin", int hp = 60)
        {
            var enemy = new Enemy(id, "哥布林", hp, EnemyRank.Normal);
            enemy.WithAffinities(new AffinityTable()
                .Vulnerable(DamageElement.Fire)
                .Resistant(DamageElement.Dark));

            enemy.IntentPlanner = (_, state) => state.Round % 2 == 1
                ? new[]
                {
                    new EnemyIntent("挥砍", IntentTargeting.DefaultSingle)
                    {
                        Damage = 8,
                        Element = DamageElement.Physical,
                    },
                }
                : new[]
                {
                    new EnemyIntent("投掷毒石", IntentTargeting.DefaultSingle)
                    {
                        Damage = 5,
                        Element = DamageElement.Poison,
                    },
                };

            return enemy;
        }

        /// <summary>
        /// 石像鬼：精英，意图半公开，暴击抵抗 6。对物理有抗性，逼玩家改用元素牌。
        /// 第 2 轮起加一发后排 AOE，用来验证排位机制（§4.2）。
        /// </summary>
        public static Enemy Gargoyle(string id = "gargoyle", int hp = 140)
        {
            var enemy = new Enemy(id, "石像鬼", hp, EnemyRank.Elite);
            enemy.WithAffinities(new AffinityTable()
                .Resistant(DamageElement.Physical)
                .Vulnerable(DamageElement.Lightning));

            enemy.IntentPlanner = (_, state) =>
            {
                var intents = new List<EnemyIntent>
                {
                    new("石爪", IntentTargeting.DefaultSingle)
                    {
                        Damage = 11,
                        Element = DamageElement.Physical,
                    },
                };

                if (state.Round >= 2)
                {
                    intents.Add(new EnemyIntent("尖啸", IntentTargeting.BackRowAoe)
                    {
                        Damage = 6,
                        Element = DamageElement.Wind,
                        DesignatedRow = Row.Back,
                    });
                }

                return intents;
            };

            return enemy;
        }

        /// <summary>
        /// 深渊领主：首领，暴击抵抗 8。进入后半血切换阶段——
        /// 从单体死刑重击换成全体 AOE，演示按血量分阶段的意图生成。
        /// </summary>
        public static Enemy AbyssLord(string id = "abyss_lord", int hp = 300)
        {
            var enemy = new Enemy(id, "深渊领主", hp, EnemyRank.Boss, critResist: 8);
            enemy.WithAffinities(new AffinityTable()
                .Immune(DamageElement.Dark)
                .Absorb(DamageElement.Poison)
                .Vulnerable(DamageElement.Light));

            enemy.IntentPlanner = (self, _) => self.Hp * 2 > self.MaxHp
                ? new[]
                {
                    new EnemyIntent("湮灭之握", IntentTargeting.DesignatedRowSingle)
                    {
                        Damage = 18,
                        Element = DamageElement.Dark,
                        DesignatedRow = Row.Front,
                        InflictedStatus = new StatusEffect("despair", 2) { IncomingDamageReduction = -2 },
                    },
                }
                : new[]
                {
                    new EnemyIntent("深渊咆哮", IntentTargeting.AllPlayers)
                    {
                        Damage = 12,
                        Element = DamageElement.Dark,
                    },
                    new EnemyIntent("追击", IntentTargeting.DefaultSingle)
                    {
                        Damage = 9,
                        Element = DamageElement.Physical,
                    },
                };

            return enemy;
        }

        /// <summary>一组不行动的木桩，方便做数值验证。</summary>
        public static Enemy TrainingDummy(string id = "dummy", int hp = 999)
            => new(id, "训练木桩", hp, EnemyRank.Normal);

        public static IEnumerable<Enemy> GoblinPack(int count = 3)
            => Enumerable.Range(0, count).Select(i => Goblin($"goblin{i}", 40));
    }
}
