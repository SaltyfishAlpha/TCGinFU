using System;
using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 「卡牌让你移动，但落点由你决定」（裁定 R-17c）。
    ///
    /// <para>选位是**结算中途**停下来的：效果一条条往下走，走到需要选落点的那条就挂起，
    /// 玩家选完之后从**下一条**接着走。不能图省事挪到整张牌结算完之后——
    /// 「移动到目标敌人正前方，然后造成伤害」这类卡，伤害要按移动**之后**的站位算，
    /// 顺序错了结果就是错的。</para>
    ///
    /// <para>挂起的是三样东西：剩下没结算的效果、结算上下文、以及结算完之后的收尾
    /// （OnResolved / 卡牌归宿 / 触发器）。被打断的那个 <see cref="PendingDecision"/>
    /// 整个对象存下来原样还原——它的 WaitingOn 里记着谁已经宣布完毕，重建一个会把这些表态全弄丢。</para>
    /// </summary>
    public sealed partial class BattleEngine
    {
        /// <summary>挂起的那次结算。同一时刻只会有一个：选位窗口开着时别的动作全被挡掉。</summary>
        private sealed class SuspendedResolution
        {
            public EffectContext Ctx;
            public IReadOnlyList<EffectData> Effects;
            public int ResumeFrom;
            public Action Tail;
            public PendingDecision Interrupted;
        }

        private readonly List<(string CombatantId, MoveRange Range)> _slotChoices = new();
        private SuspendedResolution _suspended;

        /// <summary>
        /// 登记一次「等玩家选落点」。<paramref name="range"/> 是卡面对落点的限定。
        ///
        /// <para>以下三种情况不开窗口，就地按范围自动落位——没人可问，但移动本身仍然要发生：
        /// 目标不是玩家角色、这次结算停不下来（敌人意图）、范围内只剩一个格子（没得选）。</para>
        /// </summary>
        public void RequestSlotChoice(EffectContext ctx, Combatant combatant, MoveRange range)
        {
            if (combatant == null || combatant.IsDown) return;

            List<(Row Row, int Slot)> options = LegalDestinations(combatant, range);
            if (options.Count == 0) return;

            bool canAsk = ctx is { CanSuspend: true } && combatant is PlayerCharacter;
            if (!canAsk || options.Count == 1)
            {
                // 没人可问（敌人、或结算停不下来）、或者压根没得选：就地落位，但移动本身仍然要发生
                (Row row, int slot) = NearestDestination(combatant, options);
                MoveRow(combatant, row, slot, free: true);
                return;
            }

            _slotChoices.Add((combatant.Id, range));
            ctx.Suspended = true;
        }

        /// <summary>自动落位时挑哪一格：优先不换排，其次离原列最近——别让站位凭空乱跳。</summary>
        private static (Row Row, int Slot) NearestDestination(
            Combatant combatant, List<(Row Row, int Slot)> options)
            => options
                .OrderBy(d => d.Row == combatant.Row ? 0 : 1)
                .ThenBy(d => Math.Abs(d.Slot - combatant.Slot))
                .First();

        /// <summary>
        /// 把一次结算挂起到选位窗口上。调用方负责在此之后**立刻返回**，
        /// 不要再往下走收尾——收尾被存进 <paramref name="tail"/> 里，等恢复时才跑。
        /// </summary>
        private void SuspendForSlotChoice(
            EffectContext ctx, IReadOnlyList<EffectData> effects, int resumeFrom, Action tail)
        {
            _suspended = new SuspendedResolution
            {
                Ctx = ctx,
                Effects = effects,
                ResumeFrom = resumeFrom,
                Tail = tail,
                Interrupted = Pending,
            };

            OpenNextSlotChoice();
        }

        private void OpenNextSlotChoice()
        {
            // 登记之后倒下的、或者落点已经没了的，直接跳过
            while (_slotChoices.Count > 0)
            {
                (string combatantId, MoveRange range) = _slotChoices[0];
                Combatant combatant = State.Find(combatantId);

                if (combatant != null && !combatant.IsDown && LegalDestinations(combatant, range).Count > 0)
                {
                    Pending = new PendingDecision(DecisionKind.ChooseSlot, new[] { combatantId })
                    {
                        SlotChoiceRange = range,
                    };
                    return;
                }

                _slotChoices.RemoveAt(0);
            }

            ResumeSuspended();
        }

        /// <summary>选位都选完了：从挂起点往下接着结算，然后跑收尾。</summary>
        private void ResumeSuspended()
        {
            SuspendedResolution resumed = _suspended;
            _suspended = null;

            if (resumed == null)
            {
                Pending = PendingDecision.None;
                return;
            }

            // 先还原被打断的窗口，再往下走：后续效果和收尾可能自己要改 Pending
            // （比如触发器推进到下一个阶段），那些改动应当盖过这里的还原
            Pending = resumed.Interrupted;

            int again = Effects.ApplyAll(resumed.Ctx, resumed.Effects, resumed.ResumeFrom);
            if (again >= 0)
            {
                // 后面还有要选位的效果，再挂一次
                SuspendForSlotChoice(resumed.Ctx, resumed.Effects, again + 1, resumed.Tail);
                return;
            }

            resumed.Tail?.Invoke();
        }

        /// <summary>
        /// 某人在某排的合法落点（只看站位，不看付不付得起）。
        /// 界面画可选槽位、引擎校验提交，两边同一个来源。
        /// </summary>
        public IReadOnlyList<int> LegalSlots(Combatant combatant, Row row)
        {
            if (combatant == null) return Array.Empty<int>();

            Battlefield field = State.FieldOf(combatant);
            var slots = new List<int>(Battlefield.SlotsPerRow);

            for (int slot = 0; slot < Battlefield.SlotsPerRow; slot++)
            {
                if (field.CanMoveTo(combatant, row, slot)) slots.Add(slot);
            }

            return slots;
        }

        /// <summary>某人在某排里、范围又允许的那些落点。</summary>
        public IReadOnlyList<int> LegalSlots(Combatant combatant, Row row, MoveRange range)
            => LegalSlots(combatant, row).Where(s => range.Allows(combatant.Row, row, s)).ToList();

        /// <summary>范围允许的全部落点，含排在内（范围可能跨两排）。</summary>
        public List<(Row Row, int Slot)> LegalDestinations(Combatant combatant, MoveRange range)
        {
            var all = new List<(Row, int)>();
            foreach (Row row in new[] { Row.Front, Row.Back })
            {
                foreach (int slot in LegalSlots(combatant, row, range)) all.Add((row, slot));
            }

            return all;
        }

        /// <summary>
        /// 玩家**此刻走位走得到**的槽位：既要落点本身合法，也要付得起这一次移动。
        /// 界面画拖拽范围用它，这样高亮出来的格子松手必定能落下去。
        /// </summary>
        public IReadOnlyList<int> AffordableMoveSlots(PlayerCharacter player, Row row)
            => LegalSlots(player, row)
                .Where(slot => CanPayForMove(player, row, slot, out _, out _))
                .ToList();

        private ActionResult HandleChooseSlot(PlayerCharacter actor, ChooseSlotAction action)
        {
            if (Pending.Kind != DecisionKind.ChooseSlot) return ActionResult.Fail("当前不需要选择落点");
            if (!Pending.WaitingOn.Contains(actor.Id)) return ActionResult.Fail("不该由你选择落点");

            MoveRange range = Pending.SlotChoiceRange;
            Row row = action.Row ?? range.Row ?? Opposite(actor.Row);

            if (!range.Allows(actor.Row, row, action.Slot)
                || !State.FieldOf(actor).CanMoveTo(actor, row, action.Slot))
            {
                return ActionResult.Fail($"这不是合法落点（可去：{range}）");
            }

            // 卡牌效果给的移动：不花 AP，也不占本轮那一次常规走位
            if (!MoveRow(actor, row, action.Slot, free: true)) return ActionResult.Fail("移动失败");

            _slotChoices.RemoveAt(0);
            OpenNextSlotChoice();

            State.CheckOutcome();
            return ActionResult.Success;
        }

        /// <summary>
        /// 轮末清空还没兑现的选位登记。窗口跨不过轮——
        /// 留到下一轮会在莫名其妙的时点弹出来。
        /// </summary>
        private void ClearSlotChoices()
        {
            _slotChoices.Clear();
            _suspended = null;
        }
    }
}
