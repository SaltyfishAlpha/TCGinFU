using System;
using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 战斗规则引擎：同步、单线程、无协程。
    /// 需要玩家做决定的地方把 <see cref="Pending"/> 挂出来停住，等外部提交下一个动作，
    /// 所以同一套引擎既能驱动 UI 也能跑 AI 自对局和单元测试。
    /// </summary>
    public sealed partial class BattleEngine
    {
        private readonly List<BattleEvent> _events = new();

        /// <summary>本次攻击的援护登记：原目标 Id → 代为承伤者（裁定 R-12）。</summary>
        private readonly Dictionary<string, PlayerCharacter> _covers = new();

        /// <summary>本次攻击的分摊登记：原目标 Id → 与他对半分的那位（B-14）。</summary>
        private readonly Dictionary<string, PlayerCharacter> _shares = new();

        private readonly Queue<(Enemy Enemy, EnemyIntent Intent)> _intentQueue = new();
        private (Enemy Enemy, EnemyIntent Intent)? _currentIntent;
        private List<Combatant> _currentIntentTargets = new();

        /// <summary>当前响应窗口里**尚未结算**的那几次攻击。时机牌改的就是它们。</summary>
        private List<AttackContext> _pendingAttacks = new();

        public BattleEngine(
            BattleState state,
            EffectRegistry effects = null,
            BehaviourRegistry behaviours = null,
            StatusRegistry statuses = null)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Effects = effects ?? EffectRegistry.CreateDefault();
            Behaviours = behaviours ?? new BehaviourRegistry();
            Statuses = statuses ?? new StatusRegistry();
        }

        public BattleState State { get; }
        public EffectRegistry Effects { get; }
        public BehaviourRegistry Behaviours { get; }
        public StatusRegistry Statuses { get; }

        public IReadOnlyList<BattleEvent> Events => _events;
        public PendingDecision Pending { get; private set; } = PendingDecision.None;

        /// <summary>当前响应窗口里敌人意图的目标（已解析仇恨，未计援护）。</summary>
        public IReadOnlyList<Combatant> CurrentIntentTargets => _currentIntentTargets;

        /// <summary>
        /// 当前响应窗口里即将发生的那几次攻击，可被时机牌改写（减伤 / 闪避 / 免暴击 / 改属性）。
        /// 响应窗口之外为空。
        /// </summary>
        public IReadOnlyList<AttackContext> PendingAttacks => _pendingAttacks;

        /// <summary>已经替某人登记了分摊的那位队友，没有则为 null。界面用它显示「已分摊」。</summary>
        public PlayerCharacter SharerFor(Combatant target)
            => target != null && _shares.TryGetValue(target.Id, out PlayerCharacter sharer) ? sharer : null;

        /// <summary>取针对某个目标的那次待结算攻击。时机牌「使目标……」用它定位。</summary>
        public AttackContext PendingAttackOn(Combatant target)
            => target == null ? null : _pendingAttacks.FirstOrDefault(a => ReferenceEquals(a.Target, target));

        public event Action<BattleEvent> EventEmitted;

        internal void Emit(BattleEvent battleEvent)
        {
            _events.Add(battleEvent);
            EventEmitted?.Invoke(battleEvent);
        }

        // ──────────────────────────────── 流程 ────────────────────────────────

        public void Start()
        {
            if (State.Round != 0) throw new InvalidOperationException("战斗已经开始过了");

            Emit(new BattleEvent(BattleEventType.BattleStarted));
            BeginRound();
        }

        /// <summary>① 轮始与意图阶段（§8 ①）。</summary>
        private void BeginRound()
        {
            State.Round++;
            State.Phase = RoundPhase.RoundStart;
            Emit(new BattleEvent(BattleEventType.RoundStarted) { Round = State.Round, Amount = State.Round });

            foreach (PlayerCharacter player in State.LivingPlayers.ToList())
            {
                player.BeginRound();
                Emit(new BattleEvent(BattleEventType.ShieldRefreshed, player.Id, player.Id)
                {
                    Amount = player.Shields.Physical,
                    Round = State.Round,
                });
            }

            // 结算「退出升变」（§8 ①）
            foreach (PlayerCharacter player in State.Players.Where(p => p.Transform.ShouldExit).ToList())
            {
                ExitTransform(player);
            }

            // 轮始触发在意图揭示**之前**：持续牌「轮始获得 X」这类词条要先于意图落地
            FireTriggerAll(BattleTrigger.RoundStart);

            foreach (Enemy enemy in State.LivingEnemies.ToList())
            {
                enemy.RevealIntents(State);
                foreach (EnemyIntent intent in enemy.Intents)
                {
                    Emit(new BattleEvent(BattleEventType.IntentRevealed, enemy.Id)
                    {
                        Text = intent.Label,
                        Amount = intent.Damage,
                        Round = State.Round,
                    });
                }
            }

            List<string> canTransform = State.LivingPlayers
                .Where(p => TransformCandidates(p).Count > 0)
                .Select(p => p.Id)
                .ToList();

            Pending = new PendingDecision(DecisionKind.TransformDeclaration, canTransform);
            if (Pending.IsResolved) ProceedToDraw();
        }

        /// <summary>② 补牌阶段（§8 ②）。</summary>
        private void ProceedToDraw()
        {
            State.Phase = RoundPhase.Draw;
            Emit(new BattleEvent(BattleEventType.PhaseChanged) { Text = nameof(RoundPhase.Draw), Round = State.Round });

            foreach (PlayerCharacter player in State.LivingPlayers.ToList())
            {
                DrawCards(player, DrawCountFor(player));
            }

            State.Phase = RoundPhase.PlayerPhase;
            Emit(new BattleEvent(BattleEventType.PhaseChanged)
            {
                Text = nameof(RoundPhase.PlayerPhase),
                Round = State.Round,
            });

            Pending = new PendingDecision(DecisionKind.PlayerActions, State.LivingPlayers.Select(p => p.Id));
            if (Pending.IsResolved) ProceedToEnemyPhase();
        }

        /// <summary>第 1 轮 5 张，之后每轮 3 张；升变首轮形态组抽 5，次轮把剩余形态牌全抽（§8 ② / §6.2）。</summary>
        public int DrawCountFor(PlayerCharacter player)
        {
            if (player.Transform.IsFirstFormRound) return 5;
            if (player.Transform.IsSecondFormRound) return player.Zones.Deck.Count;
            return State.Round == 1 ? 5 : 3;
        }

        /// <summary>④ 敌人阶段（§8 ④）。</summary>
        private void ProceedToEnemyPhase()
        {
            State.Phase = RoundPhase.EnemyPhase;
            Emit(new BattleEvent(BattleEventType.PhaseChanged)
            {
                Text = nameof(RoundPhase.EnemyPhase),
                Round = State.Round,
            });

            _intentQueue.Clear();
            foreach (Enemy enemy in State.Enemies.Where(e => !e.IsDown))
            {
                foreach (EnemyIntent intent in enemy.Intents)
                {
                    _intentQueue.Enqueue((enemy, intent));
                }
            }

            OpenNextIntentWindow();
        }

        private void OpenNextIntentWindow()
        {
            _covers.Clear();
            _shares.Clear();
            _currentIntent = null;
            _currentIntentTargets = new List<Combatant>();
            _pendingAttacks = new List<AttackContext>();

            while (_intentQueue.Count > 0 && _intentQueue.Peek().Enemy.IsDown)
            {
                _intentQueue.Dequeue();
            }

            if (_intentQueue.Count == 0 || State.Outcome != BattleOutcome.Ongoing)
            {
                BeginRoundEnd();
                return;
            }

            (Enemy enemy, EnemyIntent intent) = _intentQueue.Dequeue();
            _currentIntent = (enemy, intent);
            _currentIntentTargets = ResolveIntentTargets(intent);

            // 先把这次攻击摆出来，响应窗口里的时机牌才有东西可改
            _pendingAttacks = _currentIntentTargets
                .Select(t => new AttackContext(enemy, t, intent.Damage, intent.Element, CritResult.None)
                {
                    Intent = intent,
                })
                .ToList();

            // 宣告要在开窗**之前**发：玩家是靠这条事件看到首领打的是哪张牌的，
            // 而他得在响应窗口里据此决定打不打时机牌
            Emit(new BattleEvent(BattleEventType.IntentDeclared, enemy.Id, _pendingAttacks.FirstOrDefault()?.Target?.Id)
            {
                CardId = intent.Card?.Id,
                Card = intent.Card,
                Text = intent.Label,
                Amount = intent.Damage,
                Round = State.Round,
            });

            // 裁定 R-11：每条意图结算前开一个响应窗口，全体 Pass 后才结算
            Pending = new PendingDecision(DecisionKind.EnemyResponse, State.LivingPlayers.Select(p => p.Id))
            {
                Intent = intent,
                IntentSourceId = enemy.Id,
            };

            if (Pending.IsResolved) ResolveCurrentIntentAndContinue();
        }

        private List<Combatant> ResolveIntentTargets(EnemyIntent intent) => intent.Targeting switch
        {
            IntentTargeting.DefaultSingle => Single(State.DefaultAggroTarget()),
            IntentTargeting.FrontRowAoe => State.LivingPlayersInRow(Row.Front).Cast<Combatant>().ToList(),
            IntentTargeting.BackRowAoe => State.LivingPlayersInRow(Row.Back).Cast<Combatant>().ToList(),
            IntentTargeting.AllPlayers => State.LivingPlayers.Cast<Combatant>().ToList(),
            IntentTargeting.DesignatedRowSingle => Single(
                State.LivingPlayersInRow(intent.DesignatedRow).OrderBy(p => p.Slot).FirstOrDefault()
                ?? State.DefaultAggroTarget()),
            IntentTargeting.HighestHpSingle => Single(
                State.LivingPlayers.OrderByDescending(p => p.Hp).ThenBy(p => p.Slot).FirstOrDefault()),
            _ => new List<Combatant>(),
        };

        private static List<Combatant> Single(Combatant combatant)
            => combatant == null ? new List<Combatant>() : new List<Combatant> { combatant };

        private void ResolveCurrentIntentAndContinue()
        {
            if (_currentIntent.HasValue)
            {
                (Enemy enemy, EnemyIntent intent) = _currentIntent.Value;

                // 范围意图在**结算时**才定谁挨打（裁定 R-24），
                // 所以响应窗口里的走位能真的躲开，也能真的撞上去
                if (!intent.LocksTarget) RebuildPendingAttacksForCurrentRow(enemy, intent);

                foreach (AttackContext attack in ApplyCoversAndShares())
                {
                    if (attack.Target.IsDown) continue;

                    // 时机牌可能已经把这次攻击改没了（看破闪避的完全闪避）
                    if (attack.Avoided)
                    {
                        Emit(new BattleEvent(BattleEventType.AttackAvoided, enemy.Id, attack.Target.Id)
                        {
                            Text = intent.Label,
                            Round = State.Round,
                        });

                        continue;
                    }

                    if (attack.BaseAmount > 0) ResolveAttack(attack);

                    // 分摊出来的那半份只分伤害，不分状态（见 AttackContext.SharedFrom）
                    if (intent.InflictedStatus != null && !attack.IsSharedHalf && !attack.Target.IsDown)
                    {
                        ApplyStatus(attack.Target, CloneStatus(intent.InflictedStatus));
                    }
                }

                // 意图挂着的那张「敌人的牌」在这里打出：
                // 和玩家出牌走同一条管线，同样的 EffectData、同样的 ICardBehaviour 钩子
                PlayIntentCard(enemy, intent);

                State.CheckOutcome();
            }

            if (State.Outcome != BattleOutcome.Ongoing)
            {
                BeginRoundEnd();
                return;
            }

            OpenNextIntentWindow();
        }

        /// <summary>
        /// 把响应窗口里登记的援护与分摊落到待结算的攻击上，产出真正要结算的那一串。
        ///
        /// <para>援护改写目标，分摊则把一次攻击**拆成两次**——拆出来的半份是一个独立的
        /// <see cref="AttackContext"/>，自己走一遍 <see cref="ResolveAttack"/>，
        /// 于是「分别抵扣护盾」「各自的减伤钩子」都是顺带成立的，不必在伤害管线里特判。</para>
        ///
        /// <para>只拆 <see cref="AttackContext.BaseAmount"/>：响应窗口里为原目标打出的减伤
        /// 留在原目标那一半上——那张牌保护的是他，不该顺带便宜了分摊者。
        /// 分不匀时（基础值为奇数）原目标多承担 1 点。</para>
        /// </summary>
        private List<AttackContext> ApplyCoversAndShares()
        {
            var resolving = new List<AttackContext>(_pendingAttacks.Count);

            foreach (AttackContext attack in _pendingAttacks)
            {
                Combatant original = attack.Target;
                resolving.Add(attack);

                // 援护：把承伤目标整体改写为援护者
                if (_covers.TryGetValue(original.Id, out PlayerCharacter coverer) && !coverer.IsDown)
                {
                    attack.Target = coverer;
                    continue; // 援护与分摊互斥，登记时已经拦过一道
                }

                if (!_shares.TryGetValue(original.Id, out PlayerCharacter sharer) || sharer.IsDown) continue;
                if (attack.BaseAmount <= 0) continue;

                int half = attack.BaseAmount / 2;
                attack.BaseAmount -= half;

                resolving.Add(new AttackContext(attack.Source, sharer, half, attack.Element, attack.Crit)
                {
                    Intent = attack.Intent,
                    Card = attack.Card,
                    SharedFrom = original,
                });
            }

            return resolving;
        }

        /// <summary>
        /// 按**此刻**的站位重新解析范围意图的目标（裁定 R-24）。
        ///
        /// <para>已经被时机牌处理过的那几次攻击要原样留着——玩家为某个目标付出的减伤
        /// 不该因为重新解析而丢掉。而刚挪进来的人拿到的是一次**全新**的攻击：
        /// 别人打的时机牌保护不了他，这正是「挪进来要承担后果」的意思。</para>
        /// </summary>
        private void RebuildPendingAttacksForCurrentRow(Enemy enemy, EnemyIntent intent)
        {
            List<Combatant> current = ResolveIntentTargets(intent);
            _currentIntentTargets = current;

            var rebuilt = new List<AttackContext>(current.Count);

            foreach (Combatant target in current)
            {
                AttackContext existing = _pendingAttacks.FirstOrDefault(a => ReferenceEquals(a.Target, target));

                rebuilt.Add(existing ?? new AttackContext(
                    enemy, target, intent.Damage, intent.Element, CritResult.None)
                {
                    Intent = intent,
                });
            }

            _pendingAttacks = rebuilt;
        }

        private static StatusEffect CloneStatus(StatusEffect source) => new(source.Id, source.Duration, source.Stacks)
        {
            DamagePerRound = source.DamagePerRound,
            HealPerRound = source.HealPerRound,
            DotElement = source.DotElement,
            OutgoingDamageBonus = source.OutgoingDamageBonus,
            IncomingDamageReduction = source.IncomingDamageReduction,
            IncomingDamageBonus = source.IncomingDamageBonus,
            ConsumedOnTrigger = source.ConsumedOnTrigger,
            BehaviourId = source.BehaviourId,
            CritResistDelta = source.CritResistDelta,
            IncomingMultiplierNumerator = source.IncomingMultiplierNumerator,
            IncomingMultiplierDenominator = source.IncomingMultiplierDenominator,
        };

        /// <summary>⑤ 轮末结算阶段（§8 ⑤）。</summary>
        private void BeginRoundEnd()
        {
            State.Phase = RoundPhase.RoundEnd;
            Emit(new BattleEvent(BattleEventType.PhaseChanged) { Text = nameof(RoundPhase.RoundEnd), Round = State.Round });

            State.SceneClock++;
            Emit(new BattleEvent(BattleEventType.SceneClockAdvanced) { Amount = State.SceneClock, Round = State.Round });

            foreach (Combatant combatant in AllLiving().ToList())
            {
                foreach (StatusEffect status in combatant.Statuses.All.ToList())
                {
                    if (status.DamagePerRound > 0)
                    {
                        DealDamage(null, combatant, status.DamagePerRound * status.Stacks,
                            status.DotElement, CritResult.None);
                    }

                    if (status.HealPerRound > 0)
                    {
                        Heal(combatant, status.HealPerRound * status.Stacks);
                    }
                }
            }

            // 轮末触发在持续伤害之后、状态递减之前——脚本还能看到本轮挂着的状态
            FireTriggerAll(BattleTrigger.RoundEnd);

            foreach (Combatant combatant in AllCombatants())
            {
                foreach (string expired in combatant.Statuses.TickAndPrune())
                {
                    Emit(new BattleEvent(BattleEventType.StatusExpired, combatant.Id, combatant.Id)
                    {
                        Text = expired,
                        Round = State.Round,
                    });
                }
            }

            State.CheckOutcome();
            if (State.Outcome != BattleOutcome.Ongoing)
            {
                Finish();
                return;
            }

            List<string> overLimit = State.LivingPlayers
                .Where(p => RequiredDiscards(p) > 0)
                .Select(p => p.Id)
                .ToList();

            Pending = new PendingDecision(DecisionKind.HandLimitDiscard, overLimit);
            if (Pending.IsResolved) FinishRound();
        }

        public const int HandLimit = 7;

        /// <summary>该角色轮末需要弃几张（§8 ⑤，裁定 R-15：只在轮末检查）。</summary>
        public int RequiredDiscards(PlayerCharacter player)
            => Math.Max(0, player.Zones.Hand.Count - HandLimit);

        private void FinishRound()
        {
            // 没兑现的选位登记不跨轮：留到下一轮会在莫名其妙的时点弹出来
            ClearSlotChoices();

            foreach (PlayerCharacter player in State.Players)
            {
                player.Transform.TickRoundEnd();
                player.EndRound();
            }

            foreach (Enemy enemy in State.Enemies)
            {
                enemy.RollDamageStats();
                enemy.ClearIntents();
            }

            State.CheckOutcome();
            if (State.Outcome == BattleOutcome.Ongoing) BeginRound();
            else Finish();
        }

        private void Finish()
        {
            State.Phase = RoundPhase.Finished;
            Pending = PendingDecision.None;
            Emit(new BattleEvent(BattleEventType.BattleEnded) { Text = State.Outcome.ToString(), Round = State.Round });
        }

        private IEnumerable<Combatant> AllCombatants()
            => State.Players.Cast<Combatant>().Concat(State.Enemies);

        private IEnumerable<Combatant> AllLiving()
            => AllCombatants().Where(c => !c.IsDown);
    }
}
