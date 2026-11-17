using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 战斗外构筑出来的一份卡表（§3.1）：固定 30 张主卡组 +
    /// 每张升变牌各配一组 10 张的形态额外卡组（形态组不计入 30 张）。
    /// </summary>
    public sealed class DeckList
    {
        public const int MainDeckSize = 30;
        public const int FormDeckSize = 10;

        public List<CardDefinition> MainDeck { get; } = new();

        /// <summary>升变牌定义 Id → 它的 10 张形态额外卡组。</summary>
        public Dictionary<string, List<CardDefinition>> FormDecks { get; } = new();

        public IEnumerable<CardDefinition> TransformCards
            => MainDeck.Where(c => c.Kind == CardKind.Transform);

        public int CountOf(CardCategory category) => MainDeck.Count(c => c.Category == category);

        /// <summary>把卡表实例化成某个角色的区域内容。</summary>
        public void DealTo(PlayerCharacter player, IRng rng)
        {
            player.Zones.Deck.Clear();
            foreach (CardDefinition def in MainDeck)
            {
                player.Zones.Deck.AddBottom(new CardInstance(def, player.Id));
            }

            player.Zones.Deck.Shuffle(rng);

            foreach (KeyValuePair<string, List<CardDefinition>> pair in FormDecks)
            {
                player.Zones.SetFormDeck(pair.Key, pair.Value.Select(d => new CardInstance(d, player.Id)));
            }
        }
    }
}
