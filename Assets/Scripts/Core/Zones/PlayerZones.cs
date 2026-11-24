using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 角色个人面板的全部区域（§4.1）。离场的牌统一进<b>除外区</b>（<see cref="Exile"/>）：
    /// 卡面写「消耗」的一次性牌打出后直接进，升变牌与它那套形态牌则在弹出升变时整套进。
    /// </summary>
    public sealed class PlayerZones
    {
        /// <summary>
        /// 每张升变牌对应一组 10 张形态额外卡组，按升变牌的定义 Id 索引（§3.1）。
        /// 这是**备着的卡池**，跟场上那块 <see cref="FormDeck"/> 不是一回事：
        /// 发动升变时从这里取出对应的一组，洗进 <see cref="FormDeck"/> 才算上场。
        /// </summary>
        private readonly Dictionary<string, CardPile> _formDecks = new();

        public PlayerZones(int chargeCapacity)
        {
            Charges = new ChargeZone(chargeCapacity);
        }

        /// <summary>
        /// 主卡组区。**升变期间它不再被形态牌顶替**——形态牌进 <see cref="FormDeck"/>，
        /// 这摞原封不动地留着，只是这期间不从它这里抽牌。
        /// </summary>
        public CardPile Deck { get; } = new("卡组");

        /// <summary>升变卡组区：升变期间实际抽牌的那一摞形态牌。不在升变中时是空的。</summary>
        public CardPile FormDeck { get; } = new("升变卡组");

        public CardPile Hand { get; } = new("手牌");

        public CardPile Discard { get; } = new("弃牌堆");

        /// <summary>升变期间打出的形态牌堆在这里，退出升变时随形态牌一起移出本局（裁定 R-14）。</summary>
        public CardPile FormDiscard { get; } = new("形态弃牌堆");

        /// <summary>
        /// 墓地的全貌：形态弃牌 + 主弃牌。两者在规则上是两摞（退出升变时形态牌要整体移出本局），
        /// 但对外——界面展示、以及将来「从墓地回收」这类同时够得着两边的效果——算同一个墓地。
        /// 形态牌排在前面：升变期间刚打出去的就是它们，叠放区域要露在最上面。
        /// </summary>
        public IEnumerable<CardInstance> AllDiscards => FormDiscard.Concat(Discard);

        /// <summary>
        /// 除外区：移出本局的牌，不会再以任何方式回到卡组、手牌或弃牌堆。
        /// <para>两条来源：卡面写「消耗」的一次性牌（<see cref="CardDefinition.ExileOnResolve"/>）
        /// 打出结算完直接进；升变牌与它那 10 张形态牌在弹出升变时整套进（§6.2 ⑤，裁定 R-14）。</para>
        /// </summary>
        public CardPile Exile { get; } = new("除外区");

        /// <summary>持续区，上限 1 张。被顶替的旧持续牌进弃牌堆。</summary>
        public CardInstance Continuous { get; private set; }

        public ChargeZone Charges { get; }

        /// <summary>升变期间暂存到场外的原手牌（§6.2 ①）。主卡组不进暂存，它留在原位。</summary>
        public CardPile StashedHand { get; } = new("暂存手牌");

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

        /// <summary>弃牌堆洗回主卡组。本规则没有抽空惩罚，抽空时由引擎调用这里续牌。</summary>
        public bool ReshuffleDiscardIntoDeck(IRng rng) => Reshuffle(Discard, Deck, rng);

        /// <summary>
        /// 形态弃牌堆洗回升变卡组。升变期间那 10 张形态牌**同样循环**，
        /// 和主卡组一个道理——只是两套牌各自循环，互不相通。
        /// </summary>
        public bool ReshuffleFormDiscardIntoFormDeck(IRng rng) => Reshuffle(FormDiscard, FormDeck, rng);

        /// <returns>弃牌堆是空的（没得续）时返回 false。</returns>
        private static bool Reshuffle(CardPile discard, CardPile deck, IRng rng)
        {
            if (discard.IsEmpty) return false;
            deck.AddRange(discard.TakeAll());
            deck.Shuffle(rng);
            return true;
        }

        /// <summary>按 Uid 在手牌里找牌。</summary>
        public CardInstance FindInHand(int uid) => Hand.Find(uid);
    }
}
