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
        public EffectContext(BattleEngine engine, Combatant caster, CardInstance card)
        {
            Engine = engine;
            Caster = caster;
            Card = card;
        }

        public BattleEngine Engine { get; }
        public BattleState State => Engine.State;

        /// <summary>
        /// 打出这张牌的人。**可能是敌人**——敌人的意图就是它手里一套玩家看不到的牌，
        /// 和玩家出牌走同一条结算路径、同一套钩子。给敌人另起一套平行接口的话，
        /// 数据驱动效果、状态、触发器全都要再实现一遍，mod 还得学两套 API。
        /// </summary>
        public Combatant Caster { get; }

        /// <summary>施放者是玩家时返回它，敌人返回 null。只对玩家生效的效果用它做守卫。</summary>
        public PlayerCharacter CasterPlayer => Caster as PlayerCharacter;
        public CardInstance Card { get; }
        public CardDefinition Definition => Card.Definition;

        /// <summary>玩家在动作里显式指定的目标（SingleEnemy / SingleAlly 用）。</summary>
        public Combatant ChosenTarget { get; internal set; }

        /// <summary>响应窗口里「本次结算的既定目标」，供援护与时机牌引用。</summary>
        public Combatant CurrentTarget { get; internal set; }

        public CritResult Crit { get; internal set; } = CritResult.None;

        /// <summary>
        /// 响应窗口里「本次即将结算的攻击」，时机牌改它来实现闪避 / 减伤 / 免暴击。
        /// 不在响应窗口、或既定目标已不在这次意图里时为 null。
        /// </summary>
        public AttackContext CurrentAttack => Engine.PendingAttackOn(CurrentTarget);

        /// <summary>本次打出消耗掉的充能数。</summary>
        public int ChargeSpent { get; internal set; }

        /// <summary>
        /// 这次结算**停得下来**吗——也就是外层驱动支不支持「停在半路，等玩家做个选择，再从下一条效果接着往下走」。
        ///
        /// <para>玩家出牌（<c>HandlePlayCard</c>）支持；敌人意图结算不支持，
        /// 因为意图是在「逐条意图结算」的循环里跑的，停在半路得连那层循环一起挂起。
        /// 不支持时需要选择的效果就地取缺省值，不会把结算卡死。</para>
        /// </summary>
        public bool CanSuspend { get; internal set; }

        /// <summary>
        /// 当前这条效果要求结算挂起。由 <see cref="EffectRegistry.ApplyAll"/> 在每条效果之后检查——
        /// 所以挂起发生在**整条效果做完之后**，同一条效果的多个目标能一起排队，不会漏掉后面的目标。
        /// </summary>
        public bool Suspended { get; internal set; }

        /// <summary>
        /// 本次打出**之前**施放者本轮已经打过几张牌。
        /// 「若本轮已打出过其他牌」（瞬影突袭）读它——读角色身上的计数会把这张牌自己算进去。
        /// </summary>
        public int CardsPlayedBefore { get; internal set; }

        /// <summary>
        /// 本次打出到目前为止，数据驱动的 <see cref="EffectKind.Damage"/> 一共打掉了多少 HP。
        ///
        /// <para>「回复等同于本次实际伤害一半的HP」（暗言术·活力汲取）这类卡要的是**战果**，
        /// 不是卡面基础值——护盾、克制、暴击都会让两者差很远。
        /// 有了它，伤害照旧留在数据层，行为层只管读结果。</para>
        /// </summary>
        public int DamageDealt { get; internal set; }

        /// <summary>Behaviour 可以往这里塞临时标记，供同一张牌的后续效果读取。</summary>
        public Dictionary<string, int> Scratch { get; } = new();

        /// <summary>
        /// 玩家在界面上做的额外选择（<see cref="PlayCardAction.Choices"/>）。
        /// 界面还没做出来时这里是空的，脚本要能在没有选择时退回一个确定的默认值。
        /// </summary>
        public IReadOnlyDictionary<string, int> Choices { get; internal set; }
            = new Dictionary<string, int>();

        /// <summary>取一个选择，没有就返回 <paramref name="fallback"/>。</summary>
        public int Choice(string key, int fallback = 0)
            => key != null && Choices.TryGetValue(key, out int value) ? value : fallback;

        /// <summary>
        /// 这张牌已经被行为层安置到别处了，引擎不要再把它送进弃牌堆/持续区。
        ///
        /// <para>老卡「顺风充能」写的是「将**此牌**直接置入充能区」——
        /// 它结算完不进弃牌堆，而是变成了一个充能指示物。不给行为层这个开关的话，
        /// 同一张牌会既躺在充能区又躺在弃牌堆里。</para>
        /// </summary>
        public bool CardRehomed { get; set; }

        public ValueContext Values => Caster.ValueContext(ChargeSpent);

        /// <summary>
        /// 为单独一段攻击重投一次暴击骰（<see cref="EffectData.RollsOwnCrit"/>）。
        /// 牌面没有【暴击】标识的卡不投，返回 <see cref="CritResult.None"/>。
        /// </summary>
        public CritResult RollCritFor(Combatant target)
            => Engine.RollCrit(CasterPlayer, Definition, target ?? ChosenTarget, ChargeSpent);

        public int Resolve(ValueExpr expr) => expr.Resolve(Values);

        /// <summary>EffectData 的附加条件是否成立。</summary>
        public bool ConditionMet(EffectCondition condition, Combatant target) => condition switch
        {
            EffectCondition.Always => true,
            EffectCondition.OnCrit => Crit.IsCrit,
            EffectCondition.OnSuperCrit => Crit.IsSuperCrit,
            EffectCondition.IfCasterInCrisis => CasterPlayer is { IsInCrisis: true },
            EffectCondition.IfChargeSpent => ChargeSpent > 0,
            EffectCondition.IfTargetInCrisis => target is PlayerCharacter pc && pc.IsInCrisis,
            _ => true,
        };

        /// <summary>
        /// 施放者的**对立面**。敌人出牌时「敌方」指的是玩家——
        /// 从玩家视角写死的话，敌人的范围牌会打到自己人身上。
        /// </summary>
        private IEnumerable<Combatant> Opponents => Caster is Enemy
            ? State.LivingPlayers.Cast<Combatant>()
            : State.LivingEnemies.Cast<Combatant>();

        /// <summary>施放者的**同阵营**（含自己）。</summary>
        private IEnumerable<Combatant> Allies => Caster is Enemy
            ? State.LivingEnemies.Cast<Combatant>()
            : State.LivingPlayers.Cast<Combatant>();

        private Battlefield OwnField => State.FieldOf(Caster);

        /// <summary>把 <see cref="TargetSelector"/> 展开成具体目标列表（§4.3 的相邻定义在这里生效）。</summary>
        public IReadOnlyList<Combatant> ResolveTargets(TargetSelector selector)
        {
            switch (selector.Scope)
            {
                case TargetScope.Self:
                    return new[] { Caster };

                case TargetScope.SingleEnemy:
                case TargetScope.SingleAlly:
                    return ChosenTarget != null && !ChosenTarget.IsDown
                        ? new[] { ChosenTarget }
                        : System.Array.Empty<Combatant>();

                case TargetScope.AllEnemies:
                    return Opponents.ToList();

                case TargetScope.FrontRowEnemies:
                    return Opponents.Where(c => c.Row == Row.Front).ToList();

                case TargetScope.BackRowEnemies:
                    return Opponents.Where(c => c.Row == Row.Back).ToList();

                case TargetScope.AllAllies:
                    return Allies.ToList();

                case TargetScope.AdjacentAllies:
                    return OwnField.AdjacentTo(Caster).Where(c => !c.IsDown).ToList();

                case TargetScope.CurrentTarget:
                    return CurrentTarget != null ? new[] { CurrentTarget } : System.Array.Empty<Combatant>();

                default:
                    return System.Array.Empty<Combatant>();
            }
        }
    }
}
