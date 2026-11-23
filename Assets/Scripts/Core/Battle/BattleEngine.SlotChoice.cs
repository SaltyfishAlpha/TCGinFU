using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 「卡牌让你移动，但排内站哪由你决定」（裁定 R-17c）。
    ///
    /// <para>效果结算是同步的，中途停不下来，所以选位不在结算中途发生：
    /// <see cref="RequestSlotChoice"/> 只往队列里记一笔，等整张牌结算完
    /// 再由 <see cref="BeginSlotChoiceIfAny"/> 把窗口开出来。
    /// 对「移动 + 抽牌」这种卡两种顺序没有可观察差异，却省掉了给整条效果管线加挂起/恢复。</para>
    ///
    /// <para>这个窗口是**插进**别的窗口中间的：玩家阶段打牌要选位，选完得回到玩家阶段。
    /// 所以把被打断的 <see cref="PendingDecision"/> 整个对象存下来原样还原——
    /// 它的 WaitingOn 里记着谁已经宣布完毕，重建一个会把这些表态全弄丢。</para>
    /// </summary>
    public sealed partial class BattleEngine
    {
        private readonly List<(string CombatantId, Row Row)> _slotChoices = new();
        private PendingDecision _interruptedBySlotChoice;

        /// <summary>
        /// 登记一次「等玩家选落点」。<paramref name="row"/> 是卡面定死的目标排。
        ///
        /// <para>目标不是玩家角色就地不动地直接移动——敌人没有人替它挑位置。</para>
        /// </summary>
        public void RequestSlotChoice(Combatant combatant, Row row)
        {
            if (combatant == null || combatant.IsDown) return;

            if (combatant is not PlayerCharacter)
            {
                MoveRow(combatant, row, free: true);
                return;
            }

            // 那一排里根本没有他能站的地方（满员且挤不动）就别开窗口，
            // 否则玩家对着一个没有合法落点的提示框干瞪眼
            if (LegalSlots(combatant, row).Count == 0) return;

            _slotChoices.Add((combatant.Id, row));
        }

        /// <summary>
        /// 一张牌结算完后调用：队列里还有人要选位就把窗口开出来。
        /// 已经在 ChooseSlot 里了就不重复开——连着两次选位是一个一个来的。
        /// </summary>
        private void BeginSlotChoiceIfAny()
        {
            if (_slotChoices.Count == 0 || Pending.Kind == DecisionKind.ChooseSlot) return;

            _interruptedBySlotChoice = Pending;
            OpenNextSlotChoice();
        }

        private void OpenNextSlotChoice()
        {
            // 登记之后倒下的、或者落点已经没了的，直接跳过
            while (_slotChoices.Count > 0)
            {
                (string combatantId, Row row) = _slotChoices[0];
                Combatant combatant = State.Find(combatantId);

                if (combatant != null && !combatant.IsDown && LegalSlots(combatant, row).Count > 0)
                {
                    Pending = new PendingDecision(DecisionKind.ChooseSlot, new[] { combatantId })
                    {
                        SlotChoiceRow = row,
                    };
                    return;
                }

                _slotChoices.RemoveAt(0);
            }

            RestoreInterrupted();
        }

        private void RestoreInterrupted()
        {
            Pending = _interruptedBySlotChoice ?? PendingDecision.None;
            _interruptedBySlotChoice = null;
        }

        /// <summary>
        /// 某人在某排的合法落点。界面拿它画可选槽位，引擎拿它校验，两边同一个来源。
        /// </summary>
        public IReadOnlyList<int> LegalSlots(Combatant combatant, Row row)
        {
            if (combatant == null) return System.Array.Empty<int>();

            Battlefield field = State.FieldOf(combatant);
            var slots = new List<int>(Battlefield.SlotsPerRow);

            for (int slot = 0; slot < Battlefield.SlotsPerRow; slot++)
            {
                if (field.CanMoveTo(combatant, row, slot)) slots.Add(slot);
            }

            return slots;
        }

        /// <summary>
        /// 玩家**此刻走位走得到**的槽位：既要落点本身合法，也要付得起这一次移动。
        /// 界面画拖拽范围用它，这样高亮出来的格子松手必定能落下去。
        /// </summary>
        public IReadOnlyList<int> AffordableMoveSlots(PlayerCharacter player, Row row)
            => CanPayForMove(player, row, out _, out _)
                ? LegalSlots(player, row)
                : System.Array.Empty<int>();

        private ActionResult HandleChooseSlot(PlayerCharacter actor, ChooseSlotAction action)
        {
            if (Pending.Kind != DecisionKind.ChooseSlot) return ActionResult.Fail("当前不需要选择落点");
            if (!Pending.WaitingOn.Contains(actor.Id)) return ActionResult.Fail("不该由你选择落点");

            Row row = Pending.SlotChoiceRow;
            if (!LegalSlots(actor, row).Contains(action.Slot))
            {
                return ActionResult.Fail($"{action.Slot} 号位不是合法落点");
            }

            // 卡牌效果给的移动：不花 AP，也不占本轮那一次常规走位
            if (!MoveRow(actor, row, action.Slot, free: true))
            {
                return ActionResult.Fail("移动失败");
            }

            _slotChoices.RemoveAt(0);
            OpenNextSlotChoice();

            State.CheckOutcome();
            return ActionResult.Success;
        }

        /// <summary>
        /// 轮末清空还没兑现的选位登记。窗口跨不过轮——这一轮没选就是放弃了，
        /// 留到下一轮会在莫名其妙的时点弹出来。
        /// </summary>
        private void ClearSlotChoices()
        {
            _slotChoices.Clear();
            _interruptedBySlotChoice = null;
        }
    }
}
