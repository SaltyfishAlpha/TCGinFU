using TCGinFU.Core;

namespace TCGinFU.Content
{
    /// <summary>
    /// 「影刃」样例卡组：一份合法的 30 张主卡组 + 10 张形态额外卡组。
    /// 配比按 §3.2：职业牌 15（12–16）/ 武器牌 6（3–8）/ 防具牌 6（3–8）/ 秘宝牌 3（0–6）。
    /// </summary>
    public static class SampleDeck
    {
        public static DeckList Build()
        {
            var deck = new DeckList();

            // ── 职业牌 15 张（含 1 张升变牌）──
            Add(deck, SampleCards.QuickBlade, 3);
            Add(deck, SampleCards.ArcaneBolt, 2);
            Add(deck, SampleCards.PiercingShot, 2);
            Add(deck, SampleCards.DesperateSlash, 2);
            Add(deck, SampleCards.Overcharge, 2);
            Add(deck, SampleCards.PerfectParry, 2);
            Add(deck, SampleCards.HuntersMark, 1);
            Add(deck, SampleCards.RogueAwaken, 1); // 同名升变牌限 1 张

            // ── 武器牌 6 张 ──
            Add(deck, SampleCards.ShortbowShot, 3);
            Add(deck, SampleCards.AimedShot, 3);

            // ── 防具牌 6 张 ──
            Add(deck, SampleCards.Block, 3);
            Add(deck, SampleCards.GuardianStep, 3);

            // ── 秘宝牌 3 张 ──
            Add(deck, SampleCards.Elixir, 2);
            Add(deck, SampleCards.ManaCrystal, 1);

            // ── 形态额外卡组 10 张（含 1 张英雄技能牌）──
            deck.FormDecks[SampleCards.RogueAwaken.Id] = new()
            {
                SampleCards.FormShadowStrike, SampleCards.FormShadowStrike,
                SampleCards.FormShadowStrike, SampleCards.FormShadowStrike,
                SampleCards.FormTwinFang, SampleCards.FormTwinFang,
                SampleCards.FormVeil, SampleCards.FormVeil,
                SampleCards.FormHaste,
                SampleCards.HeroThousandCuts,
            };

            return deck;
        }

        private static void Add(DeckList deck, CardDefinition card, int count)
        {
            for (int i = 0; i < count; i++) deck.MainDeck.Add(card);
        }

        /// <summary>样例卡用到的状态数值。</summary>
        public static StatusRegistry CreateStatusRegistry()
        {
            var registry = new StatusRegistry();

            registry.Register(new StatusDefinition(SampleStatuses.Bleed)
            {
                DamagePerRound = 3,
                DotElement = DamageElement.Physical,
            });

            registry.Register(new StatusDefinition(SampleStatuses.MarkedPrey)
            {
                OutgoingDamageBonus = 2,
            });

            return registry;
        }

        /// <summary>样例角色：初始 5 级，浪客 3 + 神射手 2（§2.1 的建卡起点）。</summary>
        public static CharacterSheet CreateSheet(string name = "影刃")
            => new(
                name,
                new[]
                {
                    new ClassLevel(SampleCards.RogueClass, 3),
                    new ClassLevel("神射手", 2),
                },
                new AttributeDice(DieRank.D6, DieRank.D8, DieRank.D10, DieRank.D6));

        /// <summary>
        /// 精通版样例角色：浪客满 10 级，因此有资格带英雄技能牌「千刃乱舞」（§3.3）。
        /// 20/40 级各升过 1 次属性骰，所以敏捷到了 d12。
        /// </summary>
        public static CharacterSheet CreateMasteredSheet(string name = "影刃·极")
            => new(
                name,
                new[]
                {
                    new ClassLevel(SampleCards.RogueClass, 10),
                    new ClassLevel("神射手", 10),
                    new ClassLevel("武器大师", 10),
                    new ClassLevel("游侠", 10),
                    new ClassLevel("刺客", 10),
                },
                new AttributeDice(DieRank.D6, DieRank.D10, DieRank.D12, DieRank.D6));

        /// <summary>装备槽里的武器与防具直接由对应的卡牌定义派生，数值不另记一份（§3.2）。</summary>
        public static WeaponProfile Shortbow => WeaponProfile.FromCard(SampleCards.ShortbowShot);

        public static ArmorProfile LeatherArmor => ArmorProfile.FromCard(SampleCards.Block);

        /// <summary>按样例卡组装好一名可直接入场的角色。</summary>
        public static PlayerCharacter CreatePlayer(
            string id,
            IRng rng,
            string name = "影刃",
            int? hp = null,
            int? mp = null,
            int chargeCapacity = 2)
        {
            var player = new PlayerCharacter(
                id, CreateSheet(name), chargeCapacity, hp, mp, Shortbow, LeatherArmor);

            Build().DealTo(player, rng);
            return player;
        }
    }
}
