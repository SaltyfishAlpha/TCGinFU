using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>默认执行器的公共骨架：展开目标 → 逐个检查条件 → 作用。</summary>
    public abstract class EffectHandlerBase : IEffectHandler
    {
        public abstract EffectKind Kind { get; }

        public void Apply(EffectContext ctx, EffectData data)
        {
            IReadOnlyList<Combatant> targets = ctx.ResolveTargets(data.Target);
            foreach (Combatant target in targets)
            {
                if (!ctx.ConditionMet(data.Condition, target)) continue;
                ApplyTo(ctx, data, target);
            }
        }

        protected abstract void ApplyTo(EffectContext ctx, EffectData data, Combatant target);
    }

    public sealed class DamageEffectHandler : EffectHandlerBase
    {
        public override EffectKind Kind => EffectKind.Damage;

        protected override void ApplyTo(EffectContext ctx, EffectData data, Combatant target)
        {
            CritResult crit = data.RollsOwnCrit ? ctx.RollCritFor(target) : ctx.Crit;
            ctx.Engine.DealDamage(ctx.Caster, target, ctx.Resolve(data.Amount), data.Element, crit, ctx.Card);
        }
    }

    public sealed class HealEffectHandler : EffectHandlerBase
    {
        public override EffectKind Kind => EffectKind.Heal;

        protected override void ApplyTo(EffectContext ctx, EffectData data, Combatant target)
            => ctx.Engine.Heal(target, ctx.Resolve(data.Amount), ctx.Card);
    }

    public sealed class ShieldEffectHandler : EffectHandlerBase
    {
        public override EffectKind Kind => EffectKind.GainShield;

        protected override void ApplyTo(EffectContext ctx, EffectData data, Combatant target)
            => ctx.Engine.GrantShield(target, data.ShieldKind, ctx.Resolve(data.Amount), ctx.Card);
    }

    public sealed class RestoreMpEffectHandler : EffectHandlerBase
    {
        public override EffectKind Kind => EffectKind.RestoreMp;

        protected override void ApplyTo(EffectContext ctx, EffectData data, Combatant target)
        {
            if (target is not PlayerCharacter pc) return;
            ctx.Engine.RestoreMp(pc, ctx.Resolve(data.Amount));
        }
    }

    public sealed class GainApEffectHandler : EffectHandlerBase
    {
        public override EffectKind Kind => EffectKind.GainAp;

        protected override void ApplyTo(EffectContext ctx, EffectData data, Combatant target)
        {
            if (target is not PlayerCharacter pc) return;
            ctx.Engine.GrantAp(pc, ctx.Resolve(data.Amount));
        }
    }

    public sealed class DrawEffectHandler : EffectHandlerBase
    {
        public override EffectKind Kind => EffectKind.Draw;

        protected override void ApplyTo(EffectContext ctx, EffectData data, Combatant target)
        {
            if (target is not PlayerCharacter pc) return;
            ctx.Engine.DrawCards(pc, ctx.Resolve(data.Amount));
        }
    }

    public sealed class GainChargeEffectHandler : EffectHandlerBase
    {
        public override EffectKind Kind => EffectKind.GainCharge;

        protected override void ApplyTo(EffectContext ctx, EffectData data, Combatant target)
        {
            if (target is not PlayerCharacter pc) return;

            // 从自己牌堆顶取牌作为充能指示物；牌堆空则这条效果落空。
            // 敌人没有牌堆（它的牌是个备选池，不走抽弃），所以这条效果对敌人无效
            if (ctx.CasterPlayer == null) return;

            int count = ctx.Resolve(data.Amount);
            for (int i = 0; i < count; i++)
            {
                CardInstance card = ctx.Engine.DrawFromDeck(ctx.CasterPlayer);
                if (card == null) break;
                ctx.Engine.PlaceCharge(pc, card);
            }
        }
    }

    public sealed class ApplyStatusEffectHandler : EffectHandlerBase
    {
        public override EffectKind Kind => EffectKind.ApplyStatus;

        protected override void ApplyTo(EffectContext ctx, EffectData data, Combatant target)
        {
            if (string.IsNullOrEmpty(data.StatusId)) return;
            int stacks = ctx.Resolve(data.Amount);
            ctx.Engine.ApplyStatus(
                target,
                ctx.Engine.Statuses.Create(data.StatusId, data.Duration, stacks < 1 ? 1 : stacks));
        }
    }

    public sealed class MoveRowEffectHandler : EffectHandlerBase
    {
        public override EffectKind Kind => EffectKind.MoveRow;

        protected override void ApplyTo(EffectContext ctx, EffectData data, Combatant target)
        {
            if (data.RowTarget.HasValue) ctx.Engine.MoveRow(target, data.RowTarget.Value, free: true);
            else ctx.Engine.SwitchRow(target, free: true);
        }
    }

    /// <summary>
    /// 「**可**免费移动1次」。与 <see cref="MoveRowEffectHandler"/> 的区别是它不立刻移动——
    /// 卡面写「可」，动不动、什么时候动由玩家决定。
    /// </summary>
    public sealed class GrantFreeMoveEffectHandler : EffectHandlerBase
    {
        public override EffectKind Kind => EffectKind.GrantFreeMove;

        protected override void ApplyTo(EffectContext ctx, EffectData data, Combatant target)
        {
            if (target is not PlayerCharacter pc) return;
            int count = ctx.Resolve(data.Amount);
            ctx.Engine.GrantFreeMove(pc, count < 1 ? 1 : count);
        }
    }

    /// <summary>前后排整体互换（老 Boss 意图「相位折跃」）。目标作用域决定换的是哪一边。</summary>
    public sealed class SwapRowsEffectHandler : IEffectHandler
    {
        public EffectKind Kind => EffectKind.SwapRows;

        public void Apply(EffectContext ctx, EffectData data)
        {
            bool playerSide = data.Target.Scope is TargetScope.Self or TargetScope.AllAllies
                or TargetScope.AdjacentAllies or TargetScope.SingleAlly;

            ctx.Engine.SwapRows(playerSide);
        }
    }

    /// <summary>援护：把本次结算的承伤目标整体改写为施放者（裁定 R-12）。</summary>
    public sealed class CoverEffectHandler : IEffectHandler
    {
        public EffectKind Kind => EffectKind.Cover;

        public void Apply(EffectContext ctx, EffectData data)
            // 援护是玩家之间的事（§4.3 的「相邻队友」），敌人没有这个动作
            => ctx.Engine.RegisterCover(ctx.CasterPlayer, ctx.CurrentTarget, ctx.Card);
    }
}
