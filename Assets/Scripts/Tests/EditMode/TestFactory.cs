using System.Collections.Generic;
using System.Linq;
using TCGinFU.Core;

namespace TCGinFU.Core.Tests
{
    /// <summary>测试用的建卡与建局快捷方法。</summary>
    internal static class TestFactory
    {
        public static CharacterSheet Sheet(
            string name = "测试角色",
            int level = 5,
            DieRank might = DieRank.D8,
            DieRank insight = DieRank.D6,
            DieRank dexterity = DieRank.D8,
            DieRank willpower = DieRank.D6)
        {
            // 多职业共享等级：每个职业最多 10 级，按需要开新职业凑够总等级
            var classes = new List<ClassLevel>();
            int remaining = level;
            int index = 0;
            while (remaining > 0)
            {
                int take = System.Math.Min(remaining, ClassLevel.MaxPerClass);
                classes.Add(new ClassLevel($"职业{index++}", take));
                remaining -= take;
            }

            return new CharacterSheet(name, classes, new AttributeDice(might, insight, dexterity, willpower));
        }

        public static PlayerCharacter Player(
            string id = "p1",
            CharacterSheet sheet = null,
            int chargeCapacity = 2,
            WeaponProfile weapon = null,
            ArmorProfile armor = null,
            int? hp = null,
            int? mp = null)
            => new(id, sheet ?? Sheet(), chargeCapacity, hp, mp, weapon, armor);

        public static Enemy Enemy(
            string id = "e1",
            int hp = 100,
            EnemyRank rank = EnemyRank.Normal,
            IEnumerable<EnemyIntent> intents = null)
        {
            var enemy = new Enemy(id, id, hp, rank);
            List<EnemyIntent> list = intents?.ToList() ?? new List<EnemyIntent>();
            enemy.IntentPlanner = (_, _) => list;
            return enemy;
        }

        public static EnemyIntent Attack(int damage, DamageElement element = DamageElement.Physical)
            => new("攻击", IntentTargeting.DefaultSingle) { Damage = damage, Element = element };

        public static CardDefinition StrikeCard(
            string id = "strike",
            int apCost = 1,
            int mpCost = 0,
            int damage = 5,
            bool hasCrit = false,
            params AttributeColor[] colors)
            => new(id, id, CardCategory.Weapon, CardPlay.Skill)
            {
                ApCost = apCost,
                MpCost = mpCost,
                HasCrit = hasCrit,
                Colors = colors.Length > 0 ? colors : new[] { AttributeColor.Might },
                Effects = new[] { EffectData.Damage(ValueExpr.Flat(damage)) },
            };

        /// <summary>形态牌。「形态」是标记，打法另算——这张是「形态 · 技能」。</summary>
        public static CardDefinition FormStrike(
            string id = "form_strike",
            int bonus = 4,
            CardPlay play = CardPlay.Skill,
            string classId = "浪客")
            => new(id, id, CardCategory.Class, play)
            {
                Role = CardRole.Form,
                ClassId = classId,
                ApCost = 1,
                Colors = new[] { AttributeColor.Might },
                Effects = new[] { EffectData.Damage(ValueExpr.FormAttack(bonus)) },
            };

        /// <summary>升变牌。不按打法结算，所以没有打法。</summary>
        public static CardDefinition TransformCard(
            string id = "transform",
            string classId = "浪客",
            TransformTrigger trigger = TransformTrigger.Immediate)
            => new(id, id, CardCategory.Class, play: null)
            {
                Role = CardRole.Transform,
                ClassId = classId,
                TransformTrigger = trigger,
                Colors = new[] { AttributeColor.Dexterity },
            };

        /// <summary>
        /// 英雄技能牌：§3.3 要求所属职业满 10 级精通才能编入。
        /// 「英雄技能」是职业牌上的标记，不是打法——它照样可以是任意打法。
        /// </summary>
        public static CardDefinition HeroCard(
            string id = "hero", string classId = "浪客", CardPlay play = CardPlay.Skill)
            => new(id, id, CardCategory.Class, play)
            {
                IsHeroSkill = true,
                ClassId = classId,
                ApCost = 3,
                Colors = new[] { AttributeColor.Dexterity },
                Effects = new[] { EffectData.Damage(ValueExpr.FormAttack(0)) },
            };

        /// <summary>卡面写「消耗」的一次性牌：打出结算完进除外区，不进弃牌堆（§4.1）。</summary>
        public static CardDefinition OneShotCard(string id = "one_shot", int damage = 5)
            => new(id, id, CardCategory.Weapon, CardPlay.Skill)
            {
                ExileOnResolve = true,
                ApCost = 1,
                Colors = new[] { AttributeColor.Might },
                Effects = new[] { EffectData.Damage(ValueExpr.Flat(damage)) },
            };

        public static CardDefinition FillerCard(string id)
            => new(id, id, CardCategory.Class, CardPlay.Skill) { ApCost = 1 };

        /// <summary>给角色的主卡组塞 n 张占位牌，让抽牌不会抽空。</summary>
        public static void FillDeck(PlayerCharacter player, int count, CardDefinition def = null)
        {
            def ??= FillerCard("filler");
            for (int i = 0; i < count; i++)
            {
                player.Zones.Deck.AddBottom(new CardInstance(def, player.Id));
            }
        }

        public static CardInstance AddToHand(PlayerCharacter player, CardDefinition def)
        {
            var card = new CardInstance(def, player.Id);
            player.Zones.Hand.AddBottom(card);
            return card;
        }

        public static BattleState State(
            IRng rng,
            IEnumerable<(PlayerCharacter, Row)> players,
            IEnumerable<(Enemy, Row)> enemies)
            => BattleSetup.Create(rng, players, enemies);

        /// <summary>一名前排玩家 + 一只前排小怪的最小局面。</summary>
        public static (BattleEngine engine, PlayerCharacter player, Enemy enemy) Duel(
            IRng rng = null,
            PlayerCharacter player = null,
            Enemy enemy = null)
        {
            rng ??= new XorShift32Rng(1);
            player ??= Player();
            enemy ??= Enemy();

            BattleState state = BattleSetup.Create(
                rng,
                new[] { (player, Row.Front) },
                new[] { (enemy, Row.Front) });

            return (new BattleEngine(state), player, enemy);
        }
    }
}
