using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 前后排站位（§4.2 / §4.3）。每排 4 个定长槽位——槽位不是摆设，
    /// 「正前方的前排角色」这条相邻规则需要「列」的概念（裁定 R-17）。
    /// </summary>
    public sealed class Battlefield
    {
        public const int SlotsPerRow = 4;

        private readonly Combatant[] _front = new Combatant[SlotsPerRow];
        private readonly Combatant[] _back = new Combatant[SlotsPerRow];

        private Combatant[] RowArray(Row row) => row == Row.Front ? _front : _back;

        public IEnumerable<Combatant> InRow(Row row) => RowArray(row).Where(c => c != null);

        public Combatant At(Row row, int slot)
            => slot >= 0 && slot < SlotsPerRow ? RowArray(row)[slot] : null;

        /// <summary>放入指定排的首个空槽；该排已满返回 false。</summary>
        public bool Place(Combatant combatant, Row row)
        {
            Combatant[] array = RowArray(row);
            for (int slot = 0; slot < SlotsPerRow; slot++)
            {
                if (array[slot] != null) continue;
                array[slot] = combatant;
                combatant.Row = row;
                combatant.Slot = slot;
                return true;
            }

            return false;
        }

        public bool PlaceAt(Combatant combatant, Row row, int slot)
        {
            Combatant[] array = RowArray(row);
            if (slot < 0 || slot >= SlotsPerRow || array[slot] != null) return false;
            array[slot] = combatant;
            combatant.Row = row;
            combatant.Slot = slot;
            return true;
        }

        public void Remove(Combatant combatant)
        {
            Combatant[] array = RowArray(combatant.Row);
            if (combatant.Slot >= 0 && combatant.Slot < SlotsPerRow && array[combatant.Slot] == combatant)
            {
                array[combatant.Slot] = null;
            }
        }

        /// <summary>
        /// 换到对排。优先落在**同列**槽位以保持相邻关系；同列被占则取最近的空槽。
        /// 对排已满返回 false。
        /// </summary>
        public bool SwitchRow(Combatant combatant)
            => MoveTo(combatant, combatant.Row == Row.Front ? Row.Back : Row.Front);

        /// <summary>
        /// 移动到**指定**排。已经在那排了返回 false（什么都没发生），目标排满了也返回 false。
        ///
        /// <para>定向移动和 <see cref="SwitchRow"/> 不能混为一谈：
        /// 「被击退至后排」对已经在后排的人应当无事发生，用切换会把他拉到前排——正好反了。</para>
        /// </summary>
        public bool MoveTo(Combatant combatant, Row target)
        {
            if (combatant == null || combatant.Row == target) return false;

            Combatant[] targetArray = RowArray(target);
            int slot = FindSlot(targetArray, preferred: combatant.Slot);
            if (slot < 0) return false;

            Remove(combatant);
            targetArray[slot] = combatant;
            combatant.Row = target;
            combatant.Slot = slot;
            return true;
        }

        /// <summary>
        /// 前后排整体互换，**保持各自的列**（老卡「相位折跃」）。
        ///
        /// <para>必须原子地做：逐个调 <see cref="SwitchRow"/> 会让先动的人把对排槽位占掉，
        /// 后动的人被挤到别的列，相邻关系（§4.3 的「正前方」）就乱了。</para>
        /// </summary>
        public void SwapRows()
        {
            var moving = new List<(Combatant Combatant, Row To, int Slot)>();

            for (int slot = 0; slot < SlotsPerRow; slot++)
            {
                if (_front[slot] != null) moving.Add((_front[slot], Row.Back, slot));
                if (_back[slot] != null) moving.Add((_back[slot], Row.Front, slot));
            }

            System.Array.Clear(_front, 0, SlotsPerRow);
            System.Array.Clear(_back, 0, SlotsPerRow);

            foreach ((Combatant combatant, Row to, int slot) in moving)
            {
                RowArray(to)[slot] = combatant;
                combatant.Row = to;
                combatant.Slot = slot;
            }
        }

        private static int FindSlot(Combatant[] array, int preferred)
        {
            if (preferred >= 0 && preferred < SlotsPerRow && array[preferred] == null) return preferred;

            for (int distance = 1; distance < SlotsPerRow; distance++)
            {
                int left = preferred - distance;
                if (left >= 0 && array[left] == null) return left;
                int right = preferred + distance;
                if (right < SlotsPerRow && array[right] == null) return right;
            }

            return -1;
        }

        /// <summary>
        /// 相邻判定（§4.3）：
        /// ① 同排左右紧邻；② 后排角色与正前方（同列）的前排角色互为相邻。
        /// </summary>
        public bool AreAdjacent(Combatant a, Combatant b)
        {
            if (a == null || b == null || ReferenceEquals(a, b)) return false;

            if (a.Row == b.Row) return System.Math.Abs(a.Slot - b.Slot) == 1;
            return a.Slot == b.Slot;
        }

        public IEnumerable<Combatant> AdjacentTo(Combatant combatant)
        {
            if (combatant == null) yield break;

            foreach (Combatant other in RowArray(combatant.Row))
            {
                if (other != null && System.Math.Abs(other.Slot - combatant.Slot) == 1) yield return other;
            }

            Row opposite = combatant.Row == Row.Front ? Row.Back : Row.Front;
            Combatant across = At(opposite, combatant.Slot);
            if (across != null) yield return across;
        }
    }
}
