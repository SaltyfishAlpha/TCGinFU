namespace TCGinFU.Core
{
    /// <summary>
    /// 角色的升变状态（§6.2）。升变持续整整 2 轮：
    /// 进入轮 <see cref="RoundsRemaining"/>=2（抽 5 张形态牌），
    /// 维持轮 =1（把形态组剩余 5 张全抽上手），
    /// 第 3 轮轮始 =0 时退出并归还原卡组。
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

        /// <summary>升变的第 1 轮（抽 5 张）。</summary>
        public bool IsFirstFormRound => Active && RoundsRemaining == 2;

        /// <summary>升变的第 2 轮（形态组剩余牌全抽）。</summary>
        public bool IsSecondFormRound => Active && RoundsRemaining == 1;

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
