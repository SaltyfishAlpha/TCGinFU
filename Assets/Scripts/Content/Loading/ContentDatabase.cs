using System;
using System.Collections.Generic;
using System.Linq;
using TCGinFU.Core;

namespace TCGinFU.Content.Loading
{
    /// <summary>加载过程中攒下来的问题。沿用 <c>DeckValidationResult</c> 的口径：一次报全部，不是第一条。</summary>
    public sealed class LoadReport
    {
        public List<string> Errors { get; } = new();
        public bool IsValid => Errors.Count == 0;

        public override string ToString()
            => IsValid ? "内容加载正常" : string.Join("\n", Errors);
    }

    /// <summary>一名可入场角色的完整数据：面板 + 装备 + 卡组。</summary>
    public sealed class CharacterContent
    {
        public string Id { get; init; }
        public string DisplayName { get; init; }
        public string Subtitle { get; init; }
        public CharacterSheet Sheet { get; init; }
        public WeaponProfile Weapon { get; init; }
        public ArmorProfile Armor { get; init; }
        public Row StartingRow { get; init; }
        public int ChargeCapacity { get; init; } = 2;
        public string DeckId { get; init; }

        /// <summary>老版本写死的面板值，仅供对账（见 data_architecture.md §5.4）。</summary>
        public int LegacyHp { get; init; }
        public int LegacyMp { get; init; }

        /// <summary>按这份数据造一名可直接入场的角色。</summary>
        public PlayerCharacter CreatePlayer(DeckList deck, IRng rng)
        {
            var player = new PlayerCharacter(Id, Sheet, ChargeCapacity, null, null, Weapon, Armor);
            deck?.DealTo(player, rng);
            return player;
        }
    }

    /// <summary>敌人数据 + 意图池。<see cref="CreateEnemy"/> 每次造一个新实例。</summary>
    public sealed class EnemyContent
    {
        public string Id { get; init; }
        public string DisplayName { get; init; }
        public int Hp { get; init; }
        public EnemyRank Rank { get; init; }
        public int? CritResist { get; init; }
        public Row Row { get; init; }
        public AffinityTable Affinities { get; init; } = new();

        /// <summary>意图 Id → 意图原型。</summary>
        public IReadOnlyDictionary<string, EnemyIntent> Intents { get; init; }
            = new Dictionary<string, EnemyIntent>();

        public IReadOnlyList<string> PhaseOne { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> PhaseTwo { get; init; } = Array.Empty<string>();

        /// <summary>切入二阶段的血线。老版本是首领 HP ≤ 70（见 battle_state.gd）。</summary>
        public int PhaseTwoHpThreshold { get; init; }

        /// <summary>每轮揭示几条意图。老版本固定 3 条（2 明 1 暗）。</summary>
        public int IntentsPerRound { get; init; } = 3;

        public Enemy CreateEnemy()
        {
            var enemy = new Enemy(Id, DisplayName, Hp, Rank, CritResist);
            enemy.WithAffinities(Affinities);
            enemy.IntentPlanner = PlanIntents;
            return enemy;
        }

        /// <summary>
        /// 按阶段抽意图。老版本是洗牌后抽 3 张，这里用 <c>state.Rng</c> 复现，
        /// 所以同一个种子跑出来的意图序列是可复现的。
        /// </summary>
        private IEnumerable<EnemyIntent> PlanIntents(Enemy self, BattleState state)
        {
            IReadOnlyList<string> pool =
                PhaseTwo.Count > 0 && self.Hp <= PhaseTwoHpThreshold ? PhaseTwo : PhaseOne;
            if (pool.Count == 0) yield break;

            var bag = pool.ToList();
            for (int i = 0; i < IntentsPerRound && bag.Count > 0; i++)
            {
                int pick = state.Rng.NextInt(0, bag.Count);
                string id = bag[pick];
                bag.RemoveAt(pick);
                if (Intents.TryGetValue(id, out EnemyIntent intent)) yield return intent;
            }
        }
    }

    /// <summary>
    /// 一个或多个内容包加载后的总库，按 Id 索引。
    /// 后加载的包**按 Id 覆盖**先前条目——玩家自定义包就是靠这条生效的
    /// （见 data_architecture.md §3.1）。
    /// </summary>
    public sealed class ContentDatabase
    {
        private readonly Dictionary<string, CardDefinition> _cards = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CharacterContent> _characters = new(StringComparer.Ordinal);
        private readonly Dictionary<string, EnemyContent> _enemies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DeckContent> _decks = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, CardDefinition> Cards => _cards;
        public IReadOnlyDictionary<string, CharacterContent> Characters => _characters;
        public IReadOnlyDictionary<string, EnemyContent> Enemies => _enemies;
        public IReadOnlyDictionary<string, DeckContent> Decks => _decks;

        internal void Put(CardDefinition card) => _cards[card.Id] = card;
        internal void Put(CharacterContent c) => _characters[c.Id] = c;
        internal void Put(EnemyContent e) => _enemies[e.Id] = e;
        internal void Put(DeckContent d) => _decks[d.Id] = d;

        public CardDefinition Card(string id)
            => _cards.TryGetValue(id, out CardDefinition c) ? c : null;

        public CharacterContent Character(string id)
            => _characters.TryGetValue(id, out CharacterContent c) ? c : null;

        public EnemyContent Enemy(string id)
            => _enemies.TryGetValue(id, out EnemyContent e) ? e : null;

        /// <summary>把一份卡组配方展开成 <see cref="DeckList"/>。卡缺失时记错误并跳过。</summary>
        public DeckList BuildDeck(string deckId, LoadReport report)
        {
            DeckContent content = _decks.TryGetValue(deckId, out DeckContent d) ? d : null;
            if (content == null)
            {
                report?.Errors.Add($"找不到卡组 {deckId}");
                return null;
            }

            var deck = new DeckList();

            foreach ((string cardId, int count) in content.Main)
            {
                CardDefinition def = Card(cardId);
                if (def == null) { report?.Errors.Add($"卡组 {deckId}：找不到卡牌 {cardId}"); continue; }
                for (int i = 0; i < count; i++) deck.MainDeck.Add(def);
            }

            foreach (FormDeckContent form in content.FormDecks)
            {
                var cards = new List<CardDefinition>();
                foreach ((string cardId, int count) in form.Cards)
                {
                    CardDefinition def = Card(cardId);
                    if (def == null) { report?.Errors.Add($"卡组 {deckId}：找不到形态牌 {cardId}"); continue; }
                    for (int i = 0; i < count; i++) cards.Add(def);
                }

                deck.FormDecks[form.TransformCardId] = cards;
            }

            return deck;
        }
    }

    public sealed class DeckContent
    {
        public string Id { get; init; }
        public string DisplayName { get; init; }
        public string CharacterId { get; init; }
        public IReadOnlyList<(string CardId, int Count)> Main { get; init; }
            = Array.Empty<(string, int)>();
        public IReadOnlyList<FormDeckContent> FormDecks { get; init; }
            = Array.Empty<FormDeckContent>();
    }

    public sealed class FormDeckContent
    {
        public string TransformCardId { get; init; }
        public IReadOnlyList<(string CardId, int Count)> Cards { get; init; }
            = Array.Empty<(string, int)>();
    }
}
