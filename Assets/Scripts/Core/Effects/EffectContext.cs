using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 一次「打出一张牌」的结算上下文。数据驱动的 <see cref="IEffectHandler"/> 和
    /// 脚本化的 <see cref="ICardBehaviour"/> 共用同一个上下文与同一套引擎写入口，
    /// 两套机制不会各走一条结算路径。
    /// </summary>
    public sealed class EffectContext
    {
        public EffectContext(BattleEngine engine, PlayerCharacter caster, CardInstance card)
        {
            Engine = engine;
            Caster = caster;
            Card = card;
        }

        public BattleEngine Engine { get; }
        public BattleState State => Engine.State;
        public PlayerCharacter Caster { get; }
        public CardInstance Card { get; }
        public CardDefinition Definition => Card.Definition;

        /// <summary>玩家在动作里显式指定的目标（SingleEnemy / SingleAlly 用）。</summary>
        public Combatant ChosenTarget { get; internal set; }

        /// <summary>响应窗口里「本次结算的既定目标」，供援护与时机牌引用。</summary>
        public Combatant CurrentTarget { get; internal set; }

        public CritResult Crit { get; internal set; } = CritResult.None;

        /// <summary>本次打出消耗掉的充能数。</summary>
        public int ChargeSpent { get; internal set; }

        /// <summary>Behaviour 可以往这里塞临时标记，供同一张牌的后续效果读取。</summary>
        public Dictionary<string, int> Scratch { get; } = new();

        public ValueContext Values => Caster.ValueContext(ChargeSpent);

        public int Resolve(ValueExpr expr) => expr.Resolve(Values);

        /// <summary>EffectData 的附加条件是否成立。</summary>
        public bool ConditionMet(EffectCondition condition, Combatant target) => condition switch
        {
            EffectCondition.Always => true,
            EffectCondition.OnCrit => Crit.IsCrit,
            EffectCondition.OnSuperCrit => Crit.IsSuperCrit,
            EffectCondition.IfCasterInCrisis => Caster.IsInCrisis,
            EffectCondition.IfChargeSpent => ChargeSpent > 0,
            EffectCondition.IfTargetInCrisis => target is PlayerCharacter pc && pc.IsInCrisis,
            _ => true,
        };

        /// <summary>把 <see cref="TargetSelector"/> 展开成具体目标列表（§4.3 的相邻定义在这里生效）。</summary>
        public IReadOnlyList<Combatant> ResolveTargets(TargetSelector selector)
        {
            switch (selector.Scope)
            {
                case TargetScope.Self:
                    return new Combatant[] { Caster };

                case TargetScope.SingleEnemy:
                case TargetScope.SingleAlly:
                    return ChosenTarget != null && !ChosenTarget.IsDown
                        ? new[] { ChosenTarget }
                        : System.Array.Empty<Combatant>();

                case TargetScope.AllEnemies:
                    return State.LivingEnemies.Cast<Combatant>().ToList();

                case TargetScope.FrontRowEnemies:
                    return State.LivingEnemies.Where(e => e.Row == Row.Front).Cast<Combatant>().ToList();

                case TargetScope.BackRowEnemies:
                    return State.LivingEnemies.Where(e => e.Row == Row.Back).Cast<Combatant>().ToList();

                case TargetScope.AllAllies:
                    return State.LivingPlayers.Cast<Combatant>().ToList();

                case TargetScope.AdjacentAllies:
                    return State.PlayerField.AdjacentTo(Caster).Where(c => !c.IsDown).ToList();

                case TargetScope.CurrentTarget:
                    return CurrentTarget != null ? new[] { CurrentTarget } : System.Array.Empty<Combatant>();

                default:
                    return System.Array.Empty<Combatant>();
            }
        }
    }
}
