using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>升变期间暂存到场外的原手牌与原主卡组（§6.2 ①）。</summary>
    public sealed class StashedDeck
    {
        public List<CardInstance> Hand { get; } = new();
        public List<CardInstance> Deck { get; } = new();
        public bool HasContent => Hand.Count > 0 || Deck.Count > 0;

        public void Clear()
        {
            Hand.Clear();
            Deck.Clear();
        }
    }

    /// <summary>
    /// 角色个人面板的全部区域（§4.1）。注意本规则**不设消耗/除外区**，
    /// 唯一会离场的是升变结束后的形态牌，它们进「已完成升变区」。
    /// </summary>
    public sealed class PlayerZones
    {
        /// <summary>每张升变牌对应一组 10 张形态额外卡组，按升变牌的定义 Id 索引（§3.1）。</summary>
        private readonly Dictionary<string, CardPile> _formDecks = new();

        public PlayerZones(int chargeCapacity)
        {
            Charges = new ChargeZone(chargeCapacity);
        }

        /// <summary>主卡组区 / 临时形态卡组区（升变期间这里装的是形态牌）。</summary>
        public CardPile Deck { get; } = new("卡组");

        public CardPile Hand { get; } = new("手牌");

        public CardPile Discard { get; } = new("弃牌堆");

        /// <summary>升变期间打出的形态牌堆在这里，退出升变时随形态牌一起移出本局（裁定 R-14）。</summary>
        public CardPile FormDiscard { get; } = new("形态弃牌堆");

        /// <summary>已完成升变区：本局用完的升变牌与形态牌，移出本局。</summary>
        public CardPile CompletedTransform { get; } = new("已完成升变区");

        /// <summary>持续区，上限 1 张。被顶替的旧持续牌进弃牌堆。</summary>
        public CardInstance Continuous { get; private set; }

        public ChargeZone Charges { get; }

        public StashedDeck Stash { get; } = new();

        public IReadOnlyDictionary<string, CardPile> FormDecks => _formDecks;

        /// <summary>为某张升变牌登记它的 10 张形态额外卡组。</summary>
        public void SetFormDeck(string transformCardId, IEnumerable<CardInstance> cards)
            => _formDecks[transformCardId] = new CardPile($"形态组:{transformCardId}", cards);

        public CardPile GetFormDeck(string transformCardId)
            => _formDecks.TryGetValue(transformCardId, out CardPile pile) ? pile : null;

        public bool HasFormDeck(string transformCardId)
            => _formDecks.TryGetValue(transformCardId, out CardPile pile) && !pile.IsEmpty;

        /// <summary>
        /// 置入持续牌。顶替掉的旧牌通过 <paramref name="replaced"/> 返回（调用方送进弃牌堆）。
        /// </summary>
        public void SetContinuous(CardInstance card, out CardInstance replaced)
        {
            replaced = Continuous;
            Continuous = card;
        }

        public void ClearContinuous() => Continuous = null;

        /// <summary>弃牌堆洗回卡组。本规则没有抽空惩罚，抽空时由引擎调用这里续牌。</summary>
        public bool ReshuffleDiscardIntoDeck(IRng rng)
        {
            if (Discard.IsEmpty) return false;
            Deck.AddRange(Discard.TakeAll());
            Deck.Shuffle(rng);
            return true;
        }

        /// <summary>按 Uid 在手牌里找牌。</summary>
        public CardInstance FindInHand(int uid) => Hand.Find(uid);
    }
}
