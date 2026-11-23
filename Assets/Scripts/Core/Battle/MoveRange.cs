namespace TCGinFU.Core
{
    /// <summary>
    /// 一次移动允许的落点范围。
    ///
    /// <para>卡面对「移动到哪」的限定花样很多，把它写死成某一种是过度拟合：
    /// 「在前后排之间移动 1 次」要求换排，「打出后移动 1 次」哪都能去，
    /// 「移动到目标敌人正前方」指定到具体一格。所以移动接口一律带这个范围参数，
    /// 由卡面决定松紧，引擎只负责照办。</para>
    ///
    /// <para>范围在**兑现时**求值而不是授予时：攒着的免费移动从授予到兑现之间，
    /// 这个人可能已经被击退过了，「换排」指的是兑现那一刻的对排。</para>
    /// </summary>
    public readonly struct MoveRange
    {
        private MoveRange(Row? row, int? slot, bool mustChangeRow)
        {
            Row = row;
            Slot = slot;
            MustChangeRow = mustChangeRow;
        }

        /// <summary>任意排的任意槽位。「打出后移动 1 次」这类没有限定的卡面。</summary>
        public static MoveRange Any => default;

        /// <summary>必须换排，排内落点任意。「在前后排之间移动 1 次」。</summary>
        public static MoveRange RowSwitch => new(null, null, mustChangeRow: true);

        /// <summary>指定排，排内落点任意。「移动到后排」。</summary>
        public static MoveRange Into(Row row) => new(row, null, mustChangeRow: false);

        /// <summary>指定到具体一格。「移动到目标敌人正前方」。</summary>
        public static MoveRange Exactly(Row row, int slot) => new(row, slot, mustChangeRow: false);

        /// <summary>限定到这一排；null 表示两排都行。</summary>
        public Row? Row { get; }

        /// <summary>限定到这一格；null 表示排内任意。</summary>
        public int? Slot { get; }

        /// <summary>落点必须与出发时不同排。</summary>
        public bool MustChangeRow { get; }

        /// <summary>
        /// 这个落点在范围内吗。
        ///
        /// <para><paramref name="from"/> 是**出发时**所在的排，必须由调用方显式给出而不是从
        /// 角色身上现读——兑现免费移动时记账发生在移动之后，那时候角色已经站在目标排了，
        /// 现读会让「必须换排」拿目标排和自己比，永远判不通过。</para>
        /// </summary>
        public bool Allows(Row from, Row row, int slot)
        {
            if (Row.HasValue && row != Row.Value) return false;
            if (Slot.HasValue && slot != Slot.Value) return false;
            if (MustChangeRow && row == from) return false;
            return true;
        }

        /// <summary>这一排里有没有范围允许的格子。界面拿它决定要不要把整排标成可去。</summary>
        public bool AllowsAnyIn(Row from, Row row)
        {
            if (Row.HasValue && row != Row.Value) return false;
            if (MustChangeRow && row == from) return false;
            return true;
        }

        public override string ToString()
        {
            string row = Row.HasValue ? (Row.Value == Core.Row.Front ? "前排" : "后排")
                : MustChangeRow ? "对排" : "任意排";

            return Slot.HasValue ? $"{row} {Slot.Value} 号位" : $"{row}内任意位置";
        }
    }
}
