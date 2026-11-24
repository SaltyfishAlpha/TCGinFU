namespace TCGinFU.Core
{
    /// <summary>
    /// 角色的升变状态（§6.2）。升变持续整整 2 轮：
    /// 进入轮 <see cref="RoundsRemaining"/>=2，维持轮 =1，两轮都从升变卡组抽 5 张；
    /// 第 3 轮轮始 =0 时退出，升变牌与形态牌整套进除外区、取回暂存的原手牌。
    /// </summary>
    public sealed class TransformState
    {
        public bool Active { get; private set; }
        public int RoundsRemaining { get; private set; }
        public CardInstance TransformCard { get; private set; }
        public string ClassId { get; private set; }

        /// <summary>本局是否已经升变过（一局一次，用完的升变牌移出本局）。</summary>
        public bool HasTransformedThisBattle { get; private set; }

        /// <summary>第 3 轮保底跳出是否已用掉（裁定 R-13：仅第 3 轮提供一次，错过不补）。</summary>
        public bool UsedThirdRoundGuarantee { get; internal set; }

        /// <summary>轮始时应当结算退出。</summary>
        public bool ShouldExit => Active && RoundsRemaining <= 0;

        internal void Enter(CardInstance transformCard, string classId)
        {
            Active = true;
            RoundsRemaining = 2;
            TransformCard = transformCard;
            ClassId = classId;
            HasTransformedThisBattle = true;
        }

        internal void TickRoundEnd()
        {
            if (Active && RoundsRemaining > 0) RoundsRemaining--;
        }

        internal void Exit()
        {
            Active = false;
            RoundsRemaining = 0;
            TransformCard = null;
            ClassId = null;
        }

        public override string ToString()
            => Active ? $"升变中（{ClassId}，剩 {RoundsRemaining} 轮）" : "未升变";
    }
}
