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

        private readonly Queue<(Enemy Enemy, EnemyIntent Intent)> _intentQueue = new();
        private (Enemy Enemy, EnemyIntent Intent)? _currentIntent;
        private List<Combatant> _currentIntentTargets = new();

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
            _currentIntent = null;
            _currentIntentTargets = new List<Combatant>();

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
            _ => new List<Combatant>(),
        };

        private static List<Combatant> Single(Combatant combatant)
            => combatant == null ? new List<Combatant>() : new List<Combatant> { combatant };

        private void ResolveCurrentIntentAndContinue()
        {
            if (_currentIntent.HasValue)
            {
                (Enemy enemy, EnemyIntent intent) = _currentIntent.Value;

                foreach (Combatant original in _currentIntentTargets)
                {
                    // 援护：把承伤目标整体改写为援护者
                    Combatant actual = _covers.TryGetValue(original.Id, out PlayerCharacter coverer) && !coverer.IsDown
                        ? coverer
                        : original;

                    if (actual.IsDown) continue;

                    if (intent.Damage > 0)
                    {
                        DealDamage(enemy, actual, intent.Damage, intent.Element, CritResult.None);
                    }

                    if (intent.InflictedStatus != null && !actual.IsDown)
                    {
                        ApplyStatus(actual, CloneStatus(intent.InflictedStatus));
                    }
                }

                State.CheckOutcome();
            }

            if (State.Outcome != BattleOutcome.Ongoing)
            {
                BeginRoundEnd();
                return;
            }

            OpenNextIntentWindow();
        }

        private static StatusEffect CloneStatus(StatusEffect source) => new(source.Id, source.Duration, source.Stacks)
        {
            DamagePerRound = source.DamagePerRound,
            HealPerRound = source.HealPerRound,
            DotElement = source.DotElement,
            OutgoingDamageBonus = source.OutgoingDamageBonus,
            IncomingDamageReduction = source.IncomingDamageReduction,
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
