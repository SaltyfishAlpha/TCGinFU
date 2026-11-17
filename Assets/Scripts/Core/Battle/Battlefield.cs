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
        {
            Row target = combatant.Row == Row.Front ? Row.Back : Row.Front;
            Combatant[] targetArray = RowArray(target);

            int slot = FindSlot(targetArray, preferred: combatant.Slot);
            if (slot < 0) return false;

            Remove(combatant);
            targetArray[slot] = combatant;
            combatant.Row = target;
            combatant.Slot = slot;
            return true;
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
