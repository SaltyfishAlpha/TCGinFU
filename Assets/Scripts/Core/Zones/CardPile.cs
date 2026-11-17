using System;
using System.Collections;
using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>
    /// 一叠有序的牌。索引 0 是**顶部**（下一张被抽的牌）。
    /// 主卡组、手牌、弃牌堆、形态卡组都用它。
    /// </summary>
    public sealed class CardPile : IReadOnlyList<CardInstance>
    {
        private readonly List<CardInstance> _cards = new();

        public CardPile(string name) => Name = name;

        public CardPile(string name, IEnumerable<CardInstance> cards) : this(name)
            => _cards.AddRange(cards);

        public string Name { get; }
        public int Count => _cards.Count;
        public bool IsEmpty => _cards.Count == 0;
        public CardInstance this[int index] => _cards[index];

        public void AddTop(CardInstance card) => _cards.Insert(0, card);

        public void AddBottom(CardInstance card) => _cards.Add(card);

        public void AddRange(IEnumerable<CardInstance> cards) => _cards.AddRange(cards);

        /// <summary>抽顶部 1 张；牌堆为空返回 null（本规则没有抽空惩罚，由调用方决定怎么处理）。</summary>
        public CardInstance DrawTop()
        {
            if (_cards.Count == 0) return null;
            CardInstance card = _cards[0];
            _cards.RemoveAt(0);
            return card;
        }

        /// <summary>最多抽 count 张，不足则有多少抽多少。</summary>
        public List<CardInstance> DrawTop(int count)
        {
            var drawn = new List<CardInstance>(Math.Max(count, 0));
            for (int i = 0; i < count; i++)
            {
                CardInstance card = DrawTop();
                if (card == null) break;
                drawn.Add(card);
            }

            return drawn;
        }

        public bool Remove(CardInstance card) => _cards.Remove(card);

        public CardInstance Find(int uid) => _cards.Find(c => c.Uid == uid);

        public bool Contains(CardInstance card) => _cards.Contains(card);

        /// <summary>取走全部牌并清空本堆。</summary>
        public List<CardInstance> TakeAll()
        {
            var all = new List<CardInstance>(_cards);
            _cards.Clear();
            return all;
        }

        public void Clear() => _cards.Clear();

        public void Shuffle(IRng rng) => rng.Shuffle(_cards);

        public IEnumerator<CardInstance> GetEnumerator() => _cards.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"{Name}({Count})";
    }
}
