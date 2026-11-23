using System.Collections.Generic;
using System.Linq;
using TCGinFU.Content.Loading;
using TCGinFU.Core;

namespace TCGinFU.Presentation
{
    /// <summary>
    /// 可开的对局。目前只有内容包里的「命刻之战」一种；
    /// 以后每新增一个内容包阵容，在这里加一个枚举值并在
    /// <see cref="BattleSession.Restart"/> 里接上对应的加载入口即可。
    /// </summary>
    public enum BattleScenario
    {
        /// <summary>内容包里的真实数据：米阔阔 + 维汀 对 克洛诺斯-IV型</summary>
        命刻之战,
    }

    /// <summary>
    /// 一局对战的驱动层：握着引擎，外加一点「当前选中谁、选中什么目标」这类纯 GUI 状态。
    /// 规则判断一概不做——能不能打交给引擎回答，所以这里不会和引擎产生第二套规则。
    /// </summary>
    public sealed class BattleSession
    {
        private readonly HashSet<int> _discardSelection = new();

        public int Seed = 20251117;
        public BattleScenario EnemyPreset = BattleScenario.命刻之战;

        /// <summary>内容包加载失败时为 null——此时不开局，只显示 <see cref="Message"/> 里的加载报告。</summary>
        public BattleEngine Engine { get; private set; }

        public BattleState State => Engine?.State;

        /// <summary>内容包是否加载成功。false 时整个棋盘都不该建出来。</summary>
        public bool IsLoaded => Engine != null;

        /// <summary>当前在操作的角色。</summary>
        public string SelectedPlayerId { get; set; }

        /// <summary>出牌时指定的敌方目标。</summary>
        public string SelectedEnemyId { get; set; }

        /// <summary>贴充能时放进谁的充能区。</summary>
        public string ChargeTargetId { get; set; }

        /// <summary>最近一次动作的结果，显示在状态栏。</summary>
        public string Message { get; private set; } = "点「重开一局」开始。";

        public bool LastActionFailed { get; private set; }

        public BattleSession() => Restart();

        /// <summary>
        /// 用内容包里的真实数据开局（见 data_architecture.md）。
        ///
        /// <para>内容包缺失或有错时**不开局**，只把加载报告写进 <see cref="Message"/>。
        /// 这里曾经会静默退回一套 C# 样例卡组，那样「内容包坏了」看起来会像「游戏能玩」——
        /// 玩家改坏自己的 JSON 之后反而更难发现问题出在哪。</para>
        /// </summary>
        public void Restart()
        {
            Engine = null;
            _discardSelection.Clear();

            BattleEngine engine = ContentScenario.CreateClockworkCollapse(Seed, out LoadReport report);
            if (engine == null)
            {
                SelectedPlayerId = null;
                SelectedEnemyId = null;
                ChargeTargetId = null;
                SetMessage("内容包加载失败，无法开局：\n" + report, isWarning: true);
                return;
            }

            Engine = engine;
            SelectedPlayerId = State.Players[0].Id;
            ChargeTargetId = SelectedPlayerId;
            SelectedEnemyId = State.Enemies[0].Id;
            Message = $"{EnemyPreset} · 第 {State.Round} 轮开始。";
            LastActionFailed = false;
        }

        public PlayerCharacter SelectedPlayer => State?.FindPlayer(SelectedPlayerId);

        public bool IsWaitingOn(string playerId) => Engine.Pending.WaitingOn.Contains(playerId);

        /// <summary>给状态栏写一条提示。表现层做了点什么、但并没有提交动作时用。</summary>
        public void SetMessage(string text, bool isWarning = false)
        {
            Message = text;
            LastActionFailed = isWarning;
        }

        /// <summary>
        /// 提交动作并记录结果。被拒绝的动作不会改动任何状态——
        /// 所以界面可以直接乐观提交，失败了把原因显示出来就行。
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

        /// <summary>完整战报文本。没开起局时就是那份加载报告。</summary>
        public string Log => IsLoaded ? BattleLog.Format(Engine) : Message;

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

        /// <summary>低于卡面基准的费用（已减免）。</summary>
        public const string DiscountColor = "#7dff7d";

        /// <summary>高于卡面基准的费用（被加价）。</summary>
        public const string SurchargeColor = "#ff9a9a";

        /// <summary>卡面费用摘要（卡面基准值，不含任何折扣）。</summary>
        public static string CostText(CardDefinition def) => Compose(def, def.ApCost, def.MpCost, def.ChargeCost, false);

        /// <summary>
        /// 某个角色此刻打这张牌的**真实**费用摘要，被改过的那一项染色。
        ///
        /// <para>必须有这个重载：「先声夺人」卡面印着 5 MP，但第 1 轮打出是 0 MP。
        /// 界面只显示卡面基准值的话，玩家看到的费用和实际扣的对不上，
        /// 还会以为 MP 不够而不敢打。</para>
        /// </summary>
        public static string CostText(CardDefinition def, BattleEngine engine, PlayerCharacter actor)
        {
            if (engine == null || actor == null) return CostText(def);

            CostContext cost = engine.ResolveCost(actor, def);
            return Compose(def, cost.Ap, cost.Mp, cost.Charge, compareToBase: true);
        }

        private static string Compose(CardDefinition def, int ap, int mp, int charge, bool compareToBase)
        {
            var parts = new List<string>();

            Append(parts, ap, compareToBase ? def.ApCost : ap, "AP");
            Append(parts, mp, compareToBase ? def.MpCost : mp, "MP");
            Append(parts, charge, compareToBase ? def.ChargeCost : charge, "充能");

            // 一分钱不花的牌印「0」而不是「免费」：和炉石一样，费用位永远是个数字。
            // 写成「免费」会让它和其它牌的费用没法一眼比较，也容易被看成「这里没加载出来」。
            if (parts.Count == 0) parts.Add("0");

            if (def.HasCrit) parts.Add("【暴击】");
            return string.Join(" ", parts);
        }

        /// <summary>
        /// 费用为 0 且没被改过时整项省略（省得每张牌都挂一串「0AP 0充能」），
        /// 但**只要被改过就一定显示**——减免到 0 恰恰是最该让玩家看见的那一次。
        /// </summary>
        private static void Append(List<string> parts, int value, int baseValue, string unit)
        {
            if (value == 0 && value == baseValue) return;

            string text = $"{value}{unit}";

            if (value < baseValue) parts.Add($"<color={DiscountColor}>{text}</color>");
            else if (value > baseValue) parts.Add($"<color={SurchargeColor}>{text}</color>");
            else parts.Add(text);
        }
    }
}
