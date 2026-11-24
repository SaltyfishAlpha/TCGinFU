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

        /// <summary>
        /// 容量 = max(玩家人数 - 1, 2)，所以要等全队到齐才能确定。
        /// <see cref="BattleSetup"/> 在建局时按实际人数统一重设，避免调用方手填错。
        /// </summary>
        public int Capacity { get; private set; }

        internal void SetCapacity(int capacity)
        {
            Capacity = capacity;

            // 缩容时把超出的最早几个退还给原持有者，由调用方处理
            while (_tokens.Count > Capacity) _tokens.RemoveAt(0);
        }

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

        /// <summary>
        /// 把一个刚 <see cref="Pop"/> 出去、但结算下来「不进弃牌堆」的充能放回**原位**
        /// （最早置入端），而不是当成新贴的追加到末尾——它逻辑上从没离开过充能区，
        /// 顺序一变，下次溢出顶替谁就跟着变了。
        ///
        /// <para>放回时可能已经满了：只会是 Pop 与放回之间有钩子又贴了充能。
        /// 被顶出去的是**那次贴入**（末尾），不是这个从没离开过的。</para>
        /// </summary>
        public void Restore(ChargeToken token, out ChargeToken evicted)
        {
            evicted = null;
            _tokens.Insert(0, token);
            if (_tokens.Count <= Capacity) return;

            evicted = _tokens[^1];
            _tokens.RemoveAt(_tokens.Count - 1);
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
