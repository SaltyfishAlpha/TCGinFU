using System.Collections.Generic;
using System.Linq;
using TCGinFU.Content;
using TCGinFU.Core;

namespace TCGinFU.Sandbox
{
    public enum SandboxEnemy
    {
        训练木桩,
        哥布林,
        石像鬼,
        深渊领主,
        哥布林小队,
    }

    /// <summary>
    /// 沙盒的一局对战：握着引擎，外加一点「当前选中谁、选中什么目标」这类纯 GUI 状态。
    /// 规则判断一概不做——能不能打交给引擎回答，所以这里不会和引擎产生第二套规则。
    /// </summary>
    public sealed class SandboxSession
    {
        private readonly HashSet<int> _discardSelection = new();

        public int Seed = 20251117;
        public int PartySize = 2;
        public SandboxEnemy EnemyPreset = SandboxEnemy.哥布林;

        public BattleEngine Engine { get; private set; }
        public BattleState State => Engine.State;

        /// <summary>当前在操作的角色。</summary>
        public string SelectedPlayerId { get; set; }

        /// <summary>出牌时指定的敌方目标。</summary>
        public string SelectedEnemyId { get; set; }

        /// <summary>贴充能时放进谁的充能区。</summary>
        public string ChargeTargetId { get; set; }

        /// <summary>最近一次动作的结果，显示在状态栏。</summary>
        public string Message { get; private set; } = "点「重开一局」开始。";

        public bool LastActionFailed { get; private set; }

        public SandboxSession() => Restart();

        public void Restart()
        {
            IRng rng = new XorShift32Rng(Seed);

            var party = new List<(PlayerCharacter, Row)>();
            string[] names = { "护卫", "弓手", "术士", "游侠" };
            for (int i = 0; i < PartySize; i++)
            {
                party.Add((
                    SampleDeck.CreatePlayer($"p{i}", rng, names[i % names.Length]),
                    i == 0 ? Row.Front : Row.Back));
            }

            var enemies = BuildEnemies().Select(e => (e, Row.Front)).ToList();

            BattleState state = BattleSetup.Create(rng, party, enemies);
            Engine = new BattleEngine(
                state,
                EffectRegistry.CreateDefault(),
                SampleBehaviours.CreateRegistry(),
                SampleDeck.CreateStatusRegistry());

            Engine.Start();

            SelectedPlayerId = state.Players[0].Id;
            ChargeTargetId = SelectedPlayerId;
            SelectedEnemyId = state.Enemies[0].Id;
            _discardSelection.Clear();
            Message = $"第 {state.Round} 轮开始。";
            LastActionFailed = false;
        }

        private IEnumerable<Enemy> BuildEnemies() => EnemyPreset switch
        {
            SandboxEnemy.训练木桩 => new[] { SampleEnemies.TrainingDummy() },
            SandboxEnemy.哥布林 => new[] { SampleEnemies.Goblin(hp: 80) },
            SandboxEnemy.石像鬼 => new[] { SampleEnemies.Gargoyle() },
            SandboxEnemy.深渊领主 => new[] { SampleEnemies.AbyssLord() },
            SandboxEnemy.哥布林小队 => SampleEnemies.GoblinPack(3),
            _ => new[] { SampleEnemies.TrainingDummy() },
        };

        public PlayerCharacter SelectedPlayer => State.FindPlayer(SelectedPlayerId);

        public bool IsWaitingOn(string playerId) => Engine.Pending.WaitingOn.Contains(playerId);

        /// <summary>
        /// 提交动作并记录结果。被拒绝的动作不会改动任何状态——
        /// 所以沙盒可以直接乐观提交，失败了把原因显示出来就行。
        /// </summary>
        public void Submit(IBattleAction action, string successText = null)
        {
            ActionResult result = Engine.Submit(action);
            LastActionFailed = !result.Ok;
            Message = result.Ok ? successText ?? "已执行。" : result.Reason;
        }

        // ── 轮末弃牌的多选 ──

        public bool IsSelectedForDiscard(int uid) => _discardSelection.Contains(uid);

        public void ToggleDiscard(int uid)
        {
            if (!_discardSelection.Remove(uid)) _discardSelection.Add(uid);
        }

        public int DiscardSelectionCount => _discardSelection.Count;

        public void ConfirmDiscard(PlayerCharacter player)
        {
            Submit(new DiscardToHandLimitAction(player.Id, _discardSelection.ToList()), "已弃牌。");
            if (!LastActionFailed) _discardSelection.Clear();
        }

        /// <summary>一键跳过当前这个决策点，方便快速推进到想看的回合。</summary>
        public void SkipPending()
        {
            PendingDecision pending = Engine.Pending;
            foreach (string id in pending.WaitingOn.ToList())
            {
                PlayerCharacter player = State.FindPlayer(id);
                switch (pending.Kind)
                {
                    case DecisionKind.TransformDeclaration:
                        Engine.Submit(new DeclineTransformAction(id));
                        break;
                    case DecisionKind.PlayerActions:
                        Engine.Submit(new DeclareReadyAction(id));
                        break;
                    case DecisionKind.EnemyResponse:
                        Engine.Submit(new PassResponseAction(id));
                        break;
                    case DecisionKind.HandLimitDiscard:
                        Engine.Submit(new DiscardToHandLimitAction(
                            id,
                            player.Zones.Hand.Take(Engine.RequiredDiscards(player))
                                .Select(c => c.Uid).ToList()));
                        break;
                }
            }

            _discardSelection.Clear();
            Message = "已跳过当前决策点。";
            LastActionFailed = false;
        }

        /// <summary>
        /// 沙盒用的傻瓜 AI：给所有还没行动的角色「能打就打」，有升变就开。
        /// 不做任何策略判断，纯粹为了让自动推进能真的演一局，而不是站着被打死。
        /// </summary>
        public void AutoPlay()
        {
            if (Engine.Pending.Kind == DecisionKind.TransformDeclaration)
            {
                foreach (string id in Engine.Pending.WaitingOn.ToList())
                {
                    PlayerCharacter player = State.FindPlayer(id);
                    CardInstance candidate = Engine.TransformCandidates(player).FirstOrDefault();
                    if (candidate != null) Engine.Submit(new DeclareTransformAction(id, candidate.Uid));
                }

                Message = "自动处理了升变宣告。";
                LastActionFailed = false;
                return;
            }

            if (Engine.Pending.Kind != DecisionKind.PlayerActions) return;

            foreach (string id in Engine.Pending.WaitingOn.ToList())
            {
                PlayerCharacter player = State.FindPlayer(id);
                if (player == null) continue;

                // guard 防止某张牌反复「打出成功但没消耗任何资源」时空转
                for (int guard = 0; guard < 30; guard++)
                {
                    if (!Engine.Pending.WaitingOn.Contains(id)) break;
                    if (!TryPlayAnyCard(player)) break;
                }
            }

            Message = "自动出牌完毕。";
            LastActionFailed = false;
        }

        private bool TryPlayAnyCard(PlayerCharacter player)
        {
            foreach (CardInstance card in player.Zones.Hand.ToList())
            {
                string target = NeedsAllyTarget(card.Definition)
                    ? player.Id
                    : State.LivingEnemies.FirstOrDefault()?.Id;

                if (Engine.Submit(new PlayCardAction(player.Id, card.Uid, target)).Ok) return true;
            }

            return false;
        }

        /// <summary>这张牌是指向队友还是指向敌人。</summary>
        public static bool NeedsAllyTarget(CardDefinition def)
            => def.Effects.Any(e => e.Target.Scope == TargetScope.SingleAlly);

        /// <summary>
        /// 自动打完整局：每个决策点先自动出牌，再推进。
        /// 跳满上限说明某个阶段没推进——那是引擎有问题，不是这里该兜的。
        /// </summary>
        public void SkipToEnd(int maxSteps = 2000)
        {
            int steps = 0;
            while (State.Outcome == BattleOutcome.Ongoing && steps++ < maxSteps)
            {
                AutoPlay();
                SkipPending();
            }

            Message = State.Outcome == BattleOutcome.Ongoing
                ? $"推进 {maxSteps} 步后仍未结束，可能有规则卡住了。"
                : $"战斗结束：{State.Outcome}（共 {State.Round} 轮）";
            LastActionFailed = State.Outcome == BattleOutcome.Ongoing;
        }

        /// <summary>完整战报文本。</summary>
        public string Log => BattleLog.Format(Engine);

        /// <summary>敌人的弱点 / 抗性摘要，显示在面板上。</summary>
        public static string AffinitySummary(Enemy enemy)
        {
            var parts = new List<string>();
            foreach (DamageElement element in System.Enum.GetValues(typeof(DamageElement)))
            {
                Affinity affinity = enemy.Affinities[element];
                if (affinity == Affinity.Normal) continue;

                string tag = affinity switch
                {
                    Affinity.Vulnerable => "弱",
                    Affinity.Resistant => "抗",
                    Affinity.Immune => "免",
                    Affinity.Absorb => "吸",
                    _ => "",
                };
                parts.Add($"{tag}{element.DisplayName()}");
            }

            return parts.Count == 0 ? "无克制" : string.Join(" ", parts);
        }

        /// <summary>卡面费用摘要。</summary>
        public static string CostText(CardDefinition def)
        {
            var parts = new List<string>();
            if (def.ApCost > 0) parts.Add($"{def.ApCost}AP");
            if (def.MpCost > 0) parts.Add($"{def.MpCost}MP");
            if (def.ChargeCost > 0) parts.Add($"{def.ChargeCost}充能");
            if (def.HasCrit) parts.Add("【暴击】");
            return parts.Count == 0 ? "免费" : string.Join(" ", parts);
        }
    }
}
