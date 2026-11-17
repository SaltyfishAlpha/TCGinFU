using System;
using System.Threading;

namespace TCGinFU.Core
{
    /// <summary>
    /// 对局中流转的一张具体卡。同名卡会有多张 instance，各自 Uid 不同——
    /// 充能溢出要归还「该卡原持有者」的弃牌堆，所以必须能区分同名牌（§4.1）。
    /// </summary>
    public sealed class CardInstance
    {
        private static int _nextUid;

        public CardInstance(CardDefinition definition, string ownerId)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            OwnerId = ownerId ?? throw new ArgumentNullException(nameof(ownerId));
            Uid = Interlocked.Increment(ref _nextUid);
        }

        public int Uid { get; }
        public CardDefinition Definition { get; }

        /// <summary>原持有者。卡被贴到队友充能区后依然指向原主人。</summary>
        public string OwnerId { get; }

        /// <summary>是否属于形态额外卡组（退出升变时要整体移出本局）。</summary>
        public bool IsFormCard => Definition.Kind == CardKind.Form;

        public string Name => Definition.Name;

        public override string ToString() => $"{Definition.Name}#{Uid}";
    }
}
