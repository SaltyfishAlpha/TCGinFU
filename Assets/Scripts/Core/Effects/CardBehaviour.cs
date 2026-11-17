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

        public bool IsRegistered(string id) => id != null && _behaviours.ContainsKey(id);
    }
}
