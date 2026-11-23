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
        /// 移动到**指定**排，落点槽位自动选（同列优先，同列被占则取最近空槽）。
        /// 已经在那排了返回 false（什么都没发生），目标排满了也返回 false。
        ///
        /// <para>定向移动和 <see cref="SwitchRow"/> 不能混为一谈：
        /// 「被击退至后排」对已经在后排的人应当无事发生，用切换会把他拉到前排——正好反了。</para>
        ///
        /// <para>这个重载是**强制移动**（击退之类）走的路；玩家自己走位要指定落点槽位，
        /// 走 <see cref="MoveTo(Combatant, Row, int)"/>。</para>
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
        /// 移动到**指定排的指定槽位**。跨排换位与同排内改变相对位置都走这里。
        ///
        /// <para>目标槽被队友占着时**挤开**：从目标槽朝最近的空槽方向，把沿途的人整体平移一格。
        /// 这样满员的排也能调整站位，而不是被站位锁死。</para>
        ///
        /// <para>原地不动（同排同槽）返回 false——它不该白扣 1 AP。</para>
        /// </summary>
        public bool MoveTo(Combatant combatant, Row target, int slot)
        {
            if (!CanMoveTo(combatant, target, slot)) return false;

            Combatant[] targetArray = RowArray(target);

            // 先把自己从场上摘掉，腾出来的槽位对「挤开」是可用的空位：
            // 同排从 0 号挪到 2 号时，1、2 号正是朝 0 号方向平移。
            Remove(combatant);

            if (targetArray[slot] != null) Shove(targetArray, slot);

            targetArray[slot] = combatant;
            combatant.Row = target;
            combatant.Slot = slot;
            return true;
        }

        /// <summary>
        /// 落点合法性。View 要在拖拽途中逐槽预判，所以判定得和 <see cref="MoveTo(Combatant, Row, int)"/>
        /// 用同一套规则，不能各写一份。
        /// </summary>
        public bool CanMoveTo(Combatant combatant, Row target, int slot)
        {
            if (combatant == null || slot < 0 || slot >= SlotsPerRow) return false;

            // 原地不动不算移动
            if (combatant.Row == target && combatant.Slot == slot) return false;

            Combatant[] targetArray = RowArray(target);
            if (targetArray[slot] == null) return true;

            // 目标槽有人：得挤得动，也就是这一排（把自己摘掉之后）还有空槽
            int occupied = 0;
            for (int i = 0; i < SlotsPerRow; i++)
            {
                if (targetArray[i] != null && targetArray[i] != combatant) occupied++;
            }

            return occupied < SlotsPerRow;
        }

        /// <summary>
        /// 腾出 <paramref name="slot"/>：朝最近的空槽方向把沿途的人整体平移一格。
        /// 调用前必须确认该排有空槽。
        /// </summary>
        private static void Shove(Combatant[] array, int slot)
        {
            int empty = FindSlot(array, preferred: slot);

            // 空槽在右边就整体右移，在左边就整体左移；FindSlot 保证它不等于 slot 本身
            int step = empty > slot ? -1 : 1;
            for (int i = empty; i != slot; i += step)
            {
                array[i] = array[i + step];
                array[i].Slot = i;
            }

            array[slot] = null;
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
