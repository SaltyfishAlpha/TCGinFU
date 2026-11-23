using System;
using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>
    /// 卡牌的脚本化钩子。常规卡靠 <see cref="EffectData"/> 就够了；
    /// 英雄技能牌、条件型升变牌这类需要自定义判定的卡挂一个 Behaviour，
    /// 在固定的三个位置插桩，而不是另起一套结算路径。
    /// </summary>
    public interface ICardBehaviour
    {
        string Id { get; }

        /// <summary>数据效果之外的额外前置条件。返回 false 则该牌不可打出。</summary>
        bool CanPlay(EffectContext ctx, out string reason);

        /// <summary>数据效果**之前**执行。</summary>
        void OnPlay(EffectContext ctx);

        /// <summary>数据效果**之后**执行。</summary>
        void OnResolved(EffectContext ctx);

        /// <summary>条件型升变牌的前置条件判定（§6.1）。非升变牌忽略。</summary>
        bool TransformConditionMet(BattleState state, PlayerCharacter owner);

        // ──── 以下是常驻钩子：只要这段行为处于「生效中」就会被问到，和它自己打没打出去无关 ────

        /// <summary>
        /// 在伤害真正结算之前过手一次攻击，可以改 <see cref="AttackContext"/> 的任何字段。
        /// <paramref name="side"/> 说明是以攻方还是守方身份被问到——同一张持续牌两边的词条往往不同。
        /// </summary>
        void ModifyAttack(AttackContext attack, Combatant host, AttackSide side);

        /// <summary>「每当……时」类词条。引擎在 <see cref="BattleTrigger"/> 的各时点广播。</summary>
        void OnTrigger(TriggerContext trigger);

        /// <summary>骰子已投、暴击尚未判定时过手一次，用于「掷骰结果+1」这类词条。</summary>
        void ModifyCritRoll(CritRollContext roll, Combatant host);

        /// <summary>
        /// 校验费用之前过手一次，用于「免除 N MP」「下一张某类牌 -5 MP」这类词条。
        /// <b>必须无副作用</b>——见 <see cref="CostContext"/>。
        /// </summary>
        void ModifyCost(CostContext cost, PlayerCharacter host);

        /// <summary>
        /// 充能离开充能区、尚未进弃牌堆时过手一次，用于「消耗后不进弃牌堆」这类词条。
        /// <para>注意它是**全队广播**的：有卡面写的是「全队角色每当消耗充能时……」。</para>
        /// </summary>
        void ModifyChargeSpend(ChargeSpendContext spend, PlayerCharacter host);

    }

    /// <summary>默认全空实现，派生类只覆盖需要的钩子。</summary>
    public abstract class CardBehaviour : ICardBehaviour
    {
        public abstract string Id { get; }

        public virtual bool CanPlay(EffectContext ctx, out string reason)
        {
            reason = null;
            return true;
        }

        public virtual void OnPlay(EffectContext ctx) { }

        public virtual void OnResolved(EffectContext ctx) { }

        public virtual bool TransformConditionMet(BattleState state, PlayerCharacter owner) => true;

        public virtual void ModifyAttack(AttackContext attack, Combatant host, AttackSide side) { }

        public virtual void OnTrigger(TriggerContext trigger) { }

        public virtual void ModifyCritRoll(CritRollContext roll, Combatant host) { }

        public virtual void ModifyCost(CostContext cost, PlayerCharacter host) { }

        public virtual void ModifyChargeSpend(ChargeSpendContext spend, PlayerCharacter host) { }

    }

    public sealed class BehaviourRegistry
    {
        private readonly Dictionary<string, ICardBehaviour> _behaviours = new(StringComparer.Ordinal);

        public void Register(ICardBehaviour behaviour)
        {
            if (behaviour == null) throw new ArgumentNullException(nameof(behaviour));
            _behaviours[behaviour.Id] = behaviour;
        }

        /// <summary>按卡面的 BehaviourId 取钩子。没挂钩子或 Id 未注册都返回 null（视为纯数据卡）。</summary>
        public ICardBehaviour Resolve(CardDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.BehaviourId)) return null;
            return _behaviours.TryGetValue(definition.BehaviourId, out ICardBehaviour behaviour) ? behaviour : null;
        }

        /// <summary>按 Id 直接取钩子（状态挂的行为走这里，它没有 CardDefinition）。</summary>
        public ICardBehaviour ResolveById(string id)
            => id != null && _behaviours.TryGetValue(id, out ICardBehaviour behaviour) ? behaviour : null;

        public bool IsRegistered(string id) => id != null && _behaviours.ContainsKey(id);

        public IReadOnlyCollection<string> RegisteredIds => _behaviours.Keys;
    }
}
