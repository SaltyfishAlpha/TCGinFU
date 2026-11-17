using System;
using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>
    /// 充能区里的一个指示物。必须同时记住卡与**原持有者**——
    /// 溢出顶替和消耗后归位都要回到原持有者的弃牌堆，而不是充能区主人的（§4.1 / §5.2）。
    /// </summary>
    public sealed class ChargeToken
    {
        public ChargeToken(CardInstance card)
        {
            Card = card ?? throw new ArgumentNullException(nameof(card));
            OwnerId = card.OwnerId;
        }

        public CardInstance Card { get; }
        public string OwnerId { get; }

        public override string ToString() => $"充能[{Card}←{OwnerId}]";
    }

    /// <summary>
    /// 角色的充能区（§4.1）。容量 = max(玩家人数 - 1, 2)，满了继续贴会把**最早**置入的顶出去。
    /// </summary>
    public sealed class ChargeZone
    {
        private readonly List<ChargeToken> _tokens = new();

        public ChargeZone(int capacity) => Capacity = capacity;

        public int Capacity { get; }
        public int Count => _tokens.Count;
        public IReadOnlyList<ChargeToken> Tokens => _tokens;

        /// <summary>充能区容量公式。</summary>
        public static int CapacityFor(int playerCount) => Math.Max(playerCount - 1, 2);

        /// <summary>
        /// 置入 1 个充能。若因容量溢出顶替了最早的一个，通过 <paramref name="evicted"/> 返回它，
        /// 由调用方送进其原持有者的弃牌堆。
        /// </summary>
        public void Push(ChargeToken token, out ChargeToken evicted)
        {
            evicted = null;
            if (_tokens.Count >= Capacity)
            {
                evicted = _tokens[0];
                _tokens.RemoveAt(0);
            }

            _tokens.Add(token);
        }

        /// <summary>消耗 1 个充能（取最早置入的）。没有可用充能返回 null。</summary>
        public ChargeToken Pop()
        {
            if (_tokens.Count == 0) return null;
            ChargeToken token = _tokens[0];
            _tokens.RemoveAt(0);
            return token;
        }

        public override string ToString() => $"充能区 {Count}/{Capacity}";
    }
}
