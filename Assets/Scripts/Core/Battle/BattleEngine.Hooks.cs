using System;
using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 常驻行为的接线：谁此刻在生效、攻击如何过他们的手、「每当……时」何时广播。
    ///
    /// <para>这一层是为**玩家自定义内容**准备的。脚本写的行为和引擎内置逻辑走同一条路径，
    /// 所以 mod 能做到的事和官方卡能做到的事是同一个集合；
    /// 同时每次调用都被异常隔离——一张写坏的 mod 卡只会让它自己的那个钩子失效，不会崩掉整局。</para>
    /// </summary>
    public sealed partial class BattleEngine
    {
        /// <summary>触发器重入深度上限。脚本 A 的受伤触发里打伤害、又触发脚本 B……到这里就不再往下广播。</summary>
        public const int MaxTriggerDepth = 8;

        private int _triggerDepth;

        /// <summary>
        /// 此刻挂在某角色身上、处于生效中的全部常驻行为。
        ///
        /// <para>刻意每次重新枚举而不维护订阅表：持续牌会被顶替、升变会结束、状态会过期，
        /// 维护订阅就要在每个这些地方记得退订，漏一处就是幽灵触发器。
        /// 现场枚举的代价是每次攻击几次字典查询，换来的是**不可能失效**。</para>
        /// </summary>
        public IEnumerable<(ICardBehaviour Behaviour, BehaviourSource Source)> ActiveBehaviours(Combatant host)
        {
            if (host == null || host.IsDown) yield break;

            // 持续区与升变区是玩家独有的；敌人只有状态。
            // 但敌人**必须**也能带脚本化状态，否则「装甲重构：轮末若护盾未破则永久加伤」
            // 这类词条没处落脚，mod 做的 Boss 也只能是一堆裸伤害数字
            if (host is PlayerCharacter player)
            {
                CardInstance continuous = player.Zones.Continuous;
                if (continuous != null)
                {
                    ICardBehaviour b = Behaviours.Resolve(continuous.Definition);
                    if (b != null) yield return (b, BehaviourSource.Continuous);
                }

                if (player.Transform.Active && player.Transform.TransformCard != null)
                {
                    ICardBehaviour b = Behaviours.Resolve(player.Transform.TransformCard.Definition);
                    if (b != null) yield return (b, BehaviourSource.Transform);
                }
            }

            foreach (StatusEffect status in host.Statuses.All.ToList())
            {
                if (string.IsNullOrEmpty(status.BehaviourId)) continue;
                ICardBehaviour b = Behaviours.ResolveById(status.BehaviourId);
                if (b != null) yield return (b, BehaviourSource.Status);
            }
        }

        // ──────────────────────────────── 敌人意图 ────────────────────────────────

        /// <summary>取一条意图挂的脚本行为。意图直接复用它那张牌的行为。</summary>
        internal ICardBehaviour IntentBehaviour(EnemyIntent intent)
            => intent?.Card == null ? null : Behaviours.Resolve(intent.Card);

        /// <summary>
        /// 打出意图挂着的那张敌人牌。
        ///
        /// <para>刻意**复用** <see cref="EffectContext"/> 与 <see cref="ICardBehaviour"/>，
        /// 而不是给意图另起一套钩子：敌人的意图本来就是它手里一套玩家看不到的牌。
        /// 分成两套的话，数据驱动效果、状态、触发器全都要实现两遍，
        /// 而 mod 作者想做自定义敌人还得再学一套 API。</para>
        ///
        /// <para>意图自带的那段伤害（<see cref="EnemyIntent.Damage"/>）已经在调用方结算过了，
        /// 这里跑的是卡面 <see cref="EffectData"/> 与脚本钩子里的**后续效果**：
        /// 吞噬充能、前后排互换、命刻+1、下轮过热之类。</para>
        /// </summary>
        private void PlayIntentCard(Enemy enemy, EnemyIntent intent)
        {
            if (intent?.Card == null) return;

            enemy.Mp = Math.Max(0, enemy.Mp - intent.MpCost);

            // 和玩家出牌发同一条事件：敌人的意图就是敌人的牌，战报和出牌演出
            // 不该因为出牌的是谁而走两套。以前这里不发，于是战报里只看得到
            // 「意图：XXX」和伤害，看不到那张牌真的被打出来了
            Emit(new BattleEvent(BattleEventType.CardPlayed, enemy.Id, _pendingAttacks.FirstOrDefault()?.Target?.Id)
            {
                CardId = intent.Card.Id,
                Card = intent.Card,
                Text = intent.Card.Name,
                Round = State.Round,
            });

            var card = new CardInstance(intent.Card, enemy.Id);
            var ctx = new EffectContext(this, enemy, card)
            {
                ChosenTarget = _pendingAttacks.Count > 0 ? _pendingAttacks[0].Target : null,
            };

            ICardBehaviour behaviour = Behaviours.Resolve(intent.Card);

            if (behaviour != null) Invoke(behaviour, nameof(ICardBehaviour.OnPlay), () => behaviour.OnPlay(ctx));
            Effects.ApplyAll(ctx, intent.Card.Effects);
            if (behaviour != null) Invoke(behaviour, nameof(ICardBehaviour.OnResolved), () => behaviour.OnResolved(ctx));
        }

        // ──────────────────────────────── 费用 ────────────────────────────────

        /// <summary>
        /// 算出一张牌此刻的真实费用。卡面写的只是基准值，
        /// 「第1轮免除5MP」「下一张熵系魔法-5MP」这类词条在这里生效。
        ///
        /// <para>UI 显示费用也该调这里，否则玩家看到的是 5 MP、实际扣 0 MP。</para>
        /// </summary>
        public CostContext ResolveCost(PlayerCharacter actor, CardDefinition card)
        {
            var cost = new CostContext(actor, card, State.Round);
            if (actor == null || card == null) return cost;

            ICardBehaviour own = Behaviours.Resolve(card);
            if (own != null) Invoke(own, nameof(ICardBehaviour.ModifyCost), () => own.ModifyCost(cost, actor));

            foreach ((ICardBehaviour behaviour, BehaviourSource source) in ActiveBehaviours(actor).ToList())
            {
                Invoke(behaviour, source.ToString(), () => behaviour.ModifyCost(cost, actor));
            }

            return cost;
        }

        /// <summary>
        /// 让**全队**的常驻行为过手一次「消耗充能」。
        /// 刻意不限于消耗者自己——卡面写的是「全队角色每当消耗充能指示物时……」，
        /// 只问消耗者会让这张卡对队友完全失效。
        /// </summary>
        private void RunChargeSpendModifiers(ChargeSpendContext spend)
        {
            foreach (PlayerCharacter host in State.LivingPlayers.ToList())
            {
                foreach ((ICardBehaviour behaviour, BehaviourSource source) in ActiveBehaviours(host).ToList())
                {
                    Invoke(behaviour, source.ToString(), () => behaviour.ModifyChargeSpend(spend, host));
                }
            }
        }

        // ──────────────────────────────── 暴击掷骰 ────────────────────────────────

        /// <summary>
        /// 投一次暴击骰，并让常驻行为在**判定之前**过手骰值。
        /// 所有暴击掷骰都必须走这里，否则「鹰眼锁定：掷骰结果+1」只在部分路径上生效。
        /// </summary>
        public CritResult RollCrit(PlayerCharacter actor, CardDefinition card, Combatant target)
        {
            if (card == null || !card.HasCrit) return CritResult.None;

            int resist = target?.CritResist ?? State.LivingEnemies.FirstOrDefault()?.CritResist ?? 0;
            CritResult rolled = CritResolver.Roll(State.Rng, actor.Sheet.Dice, card.Colors, resist);

            var roll = new CritRollContext(rolled, card, target);

            ICardBehaviour own = Behaviours.Resolve(card);
            if (own != null) Invoke(own, nameof(ICardBehaviour.ModifyCritRoll), () => own.ModifyCritRoll(roll, actor));

            foreach ((ICardBehaviour behaviour, BehaviourSource source) in ActiveBehaviours(actor).ToList())
            {
                Invoke(behaviour, source.ToString(), () => behaviour.ModifyCritRoll(roll, actor));
            }

            return roll.Evaluate();
        }

        // ──────────────────────────────── 攻击管线 ────────────────────────────────

        /// <summary>
        /// 让一次攻击依次过攻方与守方的手，然后结算。
        /// <see cref="DealDamage"/> 只是它的便捷包装；需要在结算前后插手的调用方直接用这个。
        /// </summary>
        public DamageResult ResolveAttack(AttackContext attack)
        {
            if (attack?.Target == null || attack.Target.IsDown) return null;

            // 正在打出的那张牌自己的行为。玩家的卡和敌人意图挂的那张牌走**同一个**入口——
            // 「前排物理后排魔法」「已移到前排只受擦伤」和「本次攻击+3」是同一类词条，
            // 没有理由让敌人那边多一个 on_intent_attack
            CardDefinition sourceCard = attack.Card?.Definition ?? attack.Intent?.Card;
            if (sourceCard != null && attack.Source != null)
            {
                ICardBehaviour own = Behaviours.Resolve(sourceCard);
                if (own != null)
                {
                    Invoke(own, sourceCard.Id,
                        () => own.ModifyAttack(attack, attack.Source, AttackSide.Attacker));
                }
            }

            RunModifiers(attack, attack.Source, AttackSide.Attacker);
            RunModifiers(attack, attack.Target, AttackSide.Defender);

            if (attack.Avoided)
            {
                Emit(new BattleEvent(BattleEventType.AttackAvoided, attack.Source?.Id, attack.Target.Id)
                {
                    CardId = attack.Card?.Definition.Id,
                    Round = State.Round,
                });

                return null;
            }

            DamageResult result = DamageResolver.Resolve(
                attack.Source, attack.Target, attack.FinalAmount, attack.Element, attack.Crit,
                attack.IgnoreShield, attack.CritMultiplier);

            attack.Result = result;
            attack.Target.DamageTakenThisRound += result.HpLost;

            // 一次性状态在这一刻用掉：守方的【破绽】（「下一次**受到**的伤害+3」），
            // 以及攻方的「下一次**攻击**伤害+3」——两边都要清，只清守方会让
            // 翠风箭附、盗贼戏法那类增益一直挂着，永远加下去
            ConsumeTriggeredStatuses(attack.Target);
            if (attack.Source != null) ConsumeTriggeredStatuses(attack.Source);

            PublishDamage(attack, result);
            return result;
        }

        private void ConsumeTriggeredStatuses(Combatant combatant)
        {
            foreach (string consumed in combatant.Statuses.ConsumeTriggered())
            {
                Emit(new BattleEvent(BattleEventType.StatusConsumed, combatant.Id, combatant.Id)
                {
                    Text = consumed,
                    Round = State.Round,
                });
            }
        }

        private void RunModifiers(AttackContext attack, Combatant host, AttackSide side)
        {
            if (host == null) return;

            foreach ((ICardBehaviour behaviour, BehaviourSource source) in ActiveBehaviours(host).ToList())
            {
                Invoke(behaviour, source.ToString(), () => behaviour.ModifyAttack(attack, host, side));
            }
        }

        /// <summary>伤害结算后的统计、事件与触发广播。</summary>
        private void PublishDamage(AttackContext attack, DamageResult result)
        {
            Combatant source = attack.Source;
            Combatant target = attack.Target;

            State.Stats.Add(BattleStatKeys.DamageDealt, source?.Id, result.HpLost);
            if (result.IsCrit) State.Stats.Add(BattleStatKeys.CritsDealt, source?.Id);
            if (result.IsSuperCrit) State.Stats.Add(BattleStatKeys.SuperCritsDealt, source?.Id);

            Emit(new BattleEvent(BattleEventType.DamageDealt, source?.Id, target.Id)
            {
                Amount = result.HpLost,
                CardId = attack.Card?.Definition.Id,
                Damage = result,
                Round = State.Round,
            });

            if (result.TargetEnteredCrisis)
            {
                Emit(new BattleEvent(BattleEventType.CrisisEntered, target.Id, target.Id) { Round = State.Round });
            }

            if (result.TargetWentDown)
            {
                Emit(new BattleEvent(BattleEventType.CombatantDown, target.Id, target.Id) { Round = State.Round });
                if (target is Enemy enemy)
                {
                    State.Stats.Add(BattleStatKeys.EnemiesDowned, source?.Id);
                    enemy.ClearIntents();
                }

                State.CheckOutcome();
            }

            // 受伤方的「每当你受到伤害时」：闪避与 0 伤害不触发（老卡「灵智回流」明确写的是受到伤害）
            if (result.HpLost > 0 && target is PlayerCharacter hurt)
            {
                FireTrigger(hurt, BattleTrigger.DamageTaken, new TriggerSignal
                {
                    Other = source,
                    Amount = result.HpLost,
                    Damage = result,
                    Card = attack.Card,
                });
            }

            if (source is PlayerCharacter dealer)
            {
                FireTrigger(dealer, BattleTrigger.DamageDealt, new TriggerSignal
                {
                    Other = target,
                    Amount = result.HpLost,
                    Damage = result,
                    Card = attack.Card,
                });

                if (result.IsCrit)
                {
                    FireTrigger(dealer, BattleTrigger.CritDealt, new TriggerSignal
                    {
                        Other = target,
                        Amount = result.HpLost,
                        Damage = result,
                        Card = attack.Card,
                    });
                }
            }
        }

        // ──────────────────────────────── 触发广播 ────────────────────────────────

        /// <summary>一次触发携带的数据。字段与 <see cref="TriggerContext"/> 一一对应。</summary>
        internal struct TriggerSignal
        {
            public Combatant Other;
            public int Amount;
            public DamageResult Damage;
            public CardInstance Card;
        }

        internal void FireTrigger(Combatant host, BattleTrigger trigger, TriggerSignal signal = default)
        {
            if (host == null) return;
            if (_triggerDepth >= MaxTriggerDepth) return;

            _triggerDepth++;
            try
            {
                foreach ((ICardBehaviour behaviour, BehaviourSource source) in ActiveBehaviours(host).ToList())
                {
                    var ctx = new TriggerContext(this, trigger, host)
                    {
                        Source = source,
                        Other = signal.Other,
                        Amount = signal.Amount,
                        Damage = signal.Damage,
                        Card = signal.Card,
                    };

                    Invoke(behaviour, source.ToString(), () => behaviour.OnTrigger(ctx));
                }
            }
            finally
            {
                _triggerDepth--;
            }
        }

        /// <summary>
        /// 向**全场**广播一个时点，玩家和敌人都收。
        /// 敌人也要收：「装甲重构：轮末若护盾未被击破则永久加伤」挂在敌人身上，
        /// 只广播给玩家的话这条词条永远不会被问到。
        /// </summary>
        internal void FireTriggerAll(BattleTrigger trigger)
        {
            foreach (Combatant combatant in AllLiving().ToList())
            {
                FireTrigger(combatant, trigger);
            }
        }

        /// <summary>
        /// 脚本往战报里写一行。开给脚本的唯一输出口——不给它 print / 文件 / 网络，
        /// 想说什么就说进事件流，这样 mod 的输出和引擎的输出在同一条时间线上。
        /// </summary>
        public void EmitScriptLog(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Emit(new BattleEvent(BattleEventType.ScriptLog) { Text = text, Round = State.Round });
        }

        // ──────────────────────────────── 异常隔离 ────────────────────────────────

        /// <summary>
        /// 调一次行为钩子。玩家自定义脚本随时可能抛异常（下标越界、调了不存在的 API…），
        /// 这里把它关进事件流：那个钩子本次不生效，战斗继续，战报上留一条可见的故障记录。
        /// </summary>
        private void Invoke(ICardBehaviour behaviour, string where, Action call)
        {
            try
            {
                call();
            }
            catch (Exception ex)
            {
                Emit(new BattleEvent(BattleEventType.BehaviourFaulted)
                {
                    Text = $"{behaviour?.Id}@{where}：{ex.Message}",
                    Round = State.Round,
                });
            }
        }
    }
}
