using System.Linq;
using TCGinFU.Core;
using TCGinFU.Presentation;
using UnityEngine;

namespace TCGinFU.Sandbox
{
    /// <summary>
    /// 沙盒界面。刻意用 IMGUI 而不是 uGUI：同一份绘制代码在 EditorWindow 和
    /// Play 模式的 OnGUI 里都能跑，不用拼 Canvas / 预制体 / 事件系统，
    /// 也就没有一堆 GUID 和布局可以配错。等规则定稿再做正式的 uGUI 表现层。
    /// </summary>
    public static class SandboxGui
    {
        private static Vector2 _logScroll;
        private static Vector2 _handScroll;
        private static bool _logAtBottom = true;

        public static void Draw(BattleSession session, float width, float height)
        {
            if (session == null) return;

            // 内容包坏了就把加载报告摊开，别留一张空白面板让人以为是沙盒自己崩了
            if (!session.IsLoaded)
            {
                DrawLoadFailure(session, height);
                return;
            }

            DrawToolbar(session);
            DrawStatusBar(session);

            GUILayout.BeginHorizontal();
            {
                float columnHeight = Mathf.Max(160f, height - 330f);

                GUILayout.BeginVertical(GUILayout.Width(width * 0.3f));
                DrawEnemies(session, columnHeight);
                GUILayout.EndVertical();

                GUILayout.BeginVertical(GUILayout.Width(width * 0.33f));
                DrawParty(session, columnHeight);
                GUILayout.EndVertical();

                GUILayout.BeginVertical();
                DrawLog(session, columnHeight);
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();

            DrawPendingPanel(session);
        }

        private static void DrawLoadFailure(BattleSession session, float height)
        {
            GUILayout.Label("内容包加载失败", GUI.skin.box);

            GUILayout.BeginHorizontal();
            {
                GUILayout.Label("种子", GUILayout.Width(30));
                string seedText = GUILayout.TextField(session.Seed.ToString(), GUILayout.Width(80));
                if (int.TryParse(seedText, out int seed)) session.Seed = seed;

                if (GUILayout.Button("重试", GUILayout.Width(80))) session.Restart();
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();

            _logScroll = GUILayout.BeginScrollView(_logScroll, GUILayout.Height(Mathf.Max(120f, height - 90f)));
            GUILayout.TextArea(session.Message);
            GUILayout.EndScrollView();
        }

        // ──────────────────────────── 顶栏 ────────────────────────────

        private static void DrawToolbar(BattleSession session)
        {
            GUILayout.BeginHorizontal();
            {
                GUILayout.Label("种子", GUILayout.Width(30));
                string seedText = GUILayout.TextField(session.Seed.ToString(), GUILayout.Width(80));
                if (int.TryParse(seedText, out int seed)) session.Seed = seed;

                // 阵容由内容包决定（命刻之战固定是米阔阔 + 维汀），所以没有队伍人数可调。
                // 以后多出第二个对局时，这里按 BattleScenario 摆一排切换按钮即可。
                GUILayout.Space(8);
                GUILayout.Label("对局", GUILayout.Width(30));
                GUILayout.Label(session.EnemyPreset.ToString(), GUILayout.Width(80));

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("重开一局", GUILayout.Width(80))) session.Restart();
                if (GUILayout.Button("自动出牌", GUILayout.Width(80))) session.AutoPlay();
                if (GUILayout.Button("跳过本步", GUILayout.Width(80))) session.SkipPending();
                if (GUILayout.Button("自动打完", GUILayout.Width(80))) session.SkipToEnd();
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawStatusBar(BattleSession session)
        {
            BattleState state = session.State;
            PendingDecision pending = session.Engine.Pending;

            GUILayout.BeginHorizontal("box");
            {
                GUILayout.Label(
                    $"第 {state.Round} 轮 · {PhaseName(state.Phase)} · 命刻 {state.SceneClock}",
                    GUILayout.Width(220));

                string waiting = pending.WaitingOn.Count == 0
                    ? "—"
                    : string.Join("、", pending.WaitingOn.Select(id => state.Find(id)?.Name ?? id));
                GUILayout.Label($"等待：{DecisionName(pending.Kind)}（{waiting}）");

                GUILayout.FlexibleSpace();

                if (state.Outcome != BattleOutcome.Ongoing)
                {
                    GUILayout.Label(state.Outcome == BattleOutcome.Victory ? "★ 胜利" : "✖ 失败");
                }
            }
            GUILayout.EndHorizontal();

            Color previous = GUI.color;
            if (session.LastActionFailed) GUI.color = new Color(1f, 0.6f, 0.6f);
            GUILayout.Label($"▸ {session.Message}");
            GUI.color = previous;
        }

        // ──────────────────────────── 敌人 ────────────────────────────

        private static void DrawEnemies(BattleSession session, float height)
        {
            GUILayout.Label("敌人", GUILayout.Height(18));
            GUILayout.BeginVertical("box", GUILayout.Height(height));

            foreach (Enemy enemy in session.State.Enemies)
            {
                GUILayout.BeginVertical("box");
                {
                    GUILayout.BeginHorizontal();
                    {
                        bool selected = session.SelectedEnemyId == enemy.Id;
                        GUI.enabled = !enemy.IsDown;
                        if (GUILayout.Toggle(selected, selected ? "◉" : "○", "Button", GUILayout.Width(26))
                            && !selected)
                        {
                            session.SelectedEnemyId = enemy.Id;
                        }

                        GUI.enabled = true;
                        GUILayout.Label($"{enemy.Name}（{RankName(enemy.Rank)}）");
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Label(enemy.IsDown
                        ? "已倒下"
                        : $"HP {enemy.Hp}/{enemy.MaxHp}　暴击抵抗 {enemy.CritResist}");
                    GUILayout.Label(BattleSession.AffinitySummary(enemy));

                    if (enemy.Shields.Physical > 0 || enemy.Shields.Magical > 0)
                    {
                        GUILayout.Label($"盾 物{enemy.Shields.Physical} / 魔{enemy.Shields.Magical}");
                    }

                    foreach (StatusEffect status in enemy.Statuses.All)
                    {
                        GUILayout.Label($"· 状态 {status}");
                    }

                    if (!enemy.IsDown && enemy.Intents.Count > 0)
                    {
                        GUILayout.Label(enemy.IntentFullyVisible ? "意图：" : "意图（半公开）：");
                        foreach (EnemyIntent intent in enemy.Intents)
                        {
                            GUILayout.Label(enemy.IntentFullyVisible
                                ? $"  ▸ {intent.Label} {intent.Damage} 点{intent.Element.DisplayName()}"
                                : $"  ▸ {intent.Label}（数值隐藏）");
                        }
                    }
                }
                GUILayout.EndVertical();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        // ──────────────────────────── 我方 ────────────────────────────

        private static void DrawParty(BattleSession session, float height)
        {
            GUILayout.Label("我方小队", GUILayout.Height(18));
            GUILayout.BeginVertical("box", GUILayout.Height(height));

            foreach (PlayerCharacter player in session.State.Players)
            {
                GUILayout.BeginVertical("box");
                {
                    GUILayout.BeginHorizontal();
                    {
                        bool selected = session.SelectedPlayerId == player.Id;
                        if (GUILayout.Toggle(selected, selected ? "◉" : "○", "Button", GUILayout.Width(26))
                            && !selected)
                        {
                            session.SelectedPlayerId = player.Id;
                            session.ChargeTargetId = player.Id;
                        }

                        string flags = "";
                        if (player.IsDown) flags += " [倒下]";
                        else if (player.IsInCrisis) flags += " [危机]";
                        if (player.Transform.Active) flags += $" [升变 剩{player.Transform.RoundsRemaining}轮]";
                        if (session.IsWaitingOn(player.Id)) flags += " ←待行动";

                        GUILayout.Label($"{player.Name}（{(player.Row == Row.Front ? "前排" : "后排")}{player.Slot}）{flags}");
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Label(
                        $"HP {player.Hp}/{player.MaxHp}　MP {player.Mp}/{player.MaxMp}　AP {player.Ap}");
                    GUILayout.Label(
                        $"盾 物{player.Shields.Physical}（自动{player.Shields.AutoPhysical}）"
                        + $" / 魔{player.Shields.Magical}（自动{player.Shields.AutoMagical}）");
                    GUILayout.Label(
                        $"手牌 {player.Zones.Hand.Count}　卡组 {player.Zones.Deck.Count}　弃牌 {player.Zones.Discard.Count}");
                    GUILayout.Label(
                        $"充能 {player.Zones.Charges.Count}/{player.Zones.Charges.Capacity}"
                        + $"　持续区 {(player.Zones.Continuous?.Name ?? "空")}");

                    foreach (StatusEffect status in player.Statuses.All)
                    {
                        GUILayout.Label($"· 状态 {status}");
                    }
                }
                GUILayout.EndVertical();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        // ──────────────────────────── 战报 ────────────────────────────

        private static void DrawLog(BattleSession session, float height)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("战报", GUILayout.Height(18));
            GUILayout.FlexibleSpace();
            _logAtBottom = GUILayout.Toggle(_logAtBottom, "自动滚到底", GUILayout.Width(90));
            GUILayout.EndHorizontal();

            if (_logAtBottom) _logScroll.y = float.MaxValue;

            _logScroll = GUILayout.BeginScrollView(_logScroll, "box", GUILayout.Height(height));
            GUILayout.Label(session.Log);
            GUILayout.EndScrollView();
        }

        // ──────────────────────────── 决策面板 ────────────────────────────

        private static void DrawPendingPanel(BattleSession session)
        {
            PlayerCharacter player = session.SelectedPlayer;
            if (player == null) return;

            BattleEngine engine = session.Engine;
            DecisionKind kind = engine.Pending.Kind;

            GUILayout.BeginVertical("box");
            GUILayout.Label($"【{player.Name}】{DecisionName(kind)}");

            switch (kind)
            {
                case DecisionKind.TransformDeclaration:
                    DrawTransformPanel(session, player);
                    break;
                case DecisionKind.PlayerActions:
                    DrawPlayerActionPanel(session, player);
                    break;
                case DecisionKind.EnemyResponse:
                    DrawResponsePanel(session, player);
                    break;
                case DecisionKind.HandLimitDiscard:
                    DrawDiscardPanel(session, player);
                    break;
                default:
                    GUILayout.Label("战斗已结束，点「重开一局」再来。");
                    break;
            }

            GUILayout.EndVertical();
        }

        private static void DrawTransformPanel(BattleSession session, PlayerCharacter player)
        {
            var candidates = session.Engine.TransformCandidates(player).ToList();

            GUILayout.BeginHorizontal();
            if (candidates.Count == 0)
            {
                GUILayout.Label("没有可发动的升变牌（条件未达成，或牌不在手上）。");
            }
            else
            {
                foreach (CardInstance card in candidates)
                {
                    if (GUILayout.Button($"升变：{card.Name}", GUILayout.Width(160)))
                    {
                        session.Submit(
                            new DeclareTransformAction(player.Id, card.Uid), $"{player.Name} 发动升变！");
                    }
                }
            }

            GUILayout.FlexibleSpace();
            GUI.enabled = session.IsWaitingOn(player.Id);
            if (GUILayout.Button("本轮不升变", GUILayout.Width(110)))
            {
                session.Submit(new DeclineTransformAction(player.Id), "已跳过升变。");
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            DrawHand(session, player, playable: false);
        }

        private static void DrawPlayerActionPanel(BattleSession session, PlayerCharacter player)
        {
            GUILayout.BeginHorizontal();
            {
                GUILayout.Label($"目标：{session.State.Find(session.SelectedEnemyId)?.Name ?? "未选"}",
                    GUILayout.Width(140));

                GUILayout.Label("充能贴给：", GUILayout.Width(66));
                foreach (PlayerCharacter ally in session.State.Players)
                {
                    bool on = session.ChargeTargetId == ally.Id;
                    if (GUILayout.Toggle(on, ally.Name, "Button", GUILayout.Width(60)) && !on)
                    {
                        session.ChargeTargetId = ally.Id;
                    }
                }

                GUILayout.FlexibleSpace();

                GUI.enabled = session.IsWaitingOn(player.Id);
                if (GUILayout.Button("消耗充能 +1AP", GUILayout.Width(110)))
                {
                    session.Submit(new SpendChargeForApAction(player.Id), "充能换成了 1 点 AP。");
                }

                if (GUILayout.Button("走位（1AP）", GUILayout.Width(100)))
                {
                    session.Submit(new MoveRowAction(player.Id), $"{player.Name} 换了排位。");
                }

                if (GUILayout.Button("结束行动", GUILayout.Width(90)))
                {
                    session.Submit(new DeclareReadyAction(player.Id), $"{player.Name} 宣布行动完毕。");
                }

                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();

            DrawHand(session, player, playable: true);
        }

        private static void DrawResponsePanel(BattleSession session, PlayerCharacter player)
        {
            EnemyIntent intent = session.Engine.Pending.Intent;
            string source = session.State.Find(session.Engine.Pending.IntentSourceId)?.Name ?? "敌人";
            string targets = string.Join("、", session.Engine.CurrentIntentTargets.Select(t => t.Name));

            GUILayout.BeginHorizontal();
            {
                GUILayout.Label(intent == null
                    ? "等待结算。"
                    : $"{source} 即将「{intent.Label}」{intent.Damage} 点{intent.Element.DisplayName()} → {targets}");

                GUILayout.FlexibleSpace();
                GUI.enabled = session.IsWaitingOn(player.Id);
                if (GUILayout.Button("放弃响应", GUILayout.Width(90)))
                {
                    session.Submit(new PassResponseAction(player.Id), $"{player.Name} 放弃响应。");
                }

                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("只有时机牌与防具牌能在这里打出：");
            DrawHand(session, player, playable: true);
        }

        private static void DrawDiscardPanel(BattleSession session, PlayerCharacter player)
        {
            int required = session.Engine.RequiredDiscards(player);

            GUILayout.BeginHorizontal();
            {
                GUILayout.Label($"手牌上限 7 张，需要弃置 {required} 张（已选 {session.DiscardSelectionCount} 张）。");
                GUILayout.FlexibleSpace();

                GUI.enabled = session.DiscardSelectionCount == required && required > 0;
                if (GUILayout.Button("确认弃牌", GUILayout.Width(90))) session.ConfirmDiscard(player);
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();

            _handScroll = GUILayout.BeginScrollView(_handScroll, GUILayout.Height(96));
            GUILayout.BeginHorizontal();
            foreach (CardInstance card in player.Zones.Hand.ToList())
            {
                bool selected = session.IsSelectedForDiscard(card.Uid);
                if (GUILayout.Toggle(selected, $"{(selected ? "✓ " : "")}{card.Name}", "Button",
                        GUILayout.Width(110), GUILayout.Height(40)) != selected)
                {
                    session.ToggleDiscard(card.Uid);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        private static void DrawHand(BattleSession session, PlayerCharacter player, bool playable)
        {
            _handScroll = GUILayout.BeginScrollView(_handScroll, GUILayout.Height(104));
            GUILayout.BeginHorizontal();

            if (player.Zones.Hand.Count == 0) GUILayout.Label("（手牌为空）");

            foreach (CardInstance card in player.Zones.Hand.ToList())
            {
                CardDefinition def = card.Definition;

                GUILayout.BeginVertical("box", GUILayout.Width(126));
                {
                    GUILayout.Label(def.Name);
                    GUILayout.Label(BattleSession.CostText(def));
                    GUILayout.Label($"{CategoryName(def.Category)}·{KindName(def.Kind)}");

                    GUI.enabled = playable && session.IsWaitingOn(player.Id);

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("打出"))
                    {
                        string target = BattleSession.NeedsAllyTarget(def) ? player.Id : session.SelectedEnemyId;
                        session.Submit(
                            new PlayCardAction(player.Id, card.Uid, target), $"打出「{def.Name}」。");
                    }

                    if (GUILayout.Button("充能", GUILayout.Width(44)))
                    {
                        session.Submit(
                            new PlaceChargeAction(player.Id, card.Uid, session.ChargeTargetId),
                            $"「{def.Name}」贴成了充能。");
                    }

                    GUILayout.EndHorizontal();
                    GUI.enabled = true;
                }
                GUILayout.EndVertical();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        // ──────────────────────────── 文案 ────────────────────────────

        private static string PhaseName(RoundPhase phase) => phase switch
        {
            RoundPhase.RoundStart => "① 轮始与意图",
            RoundPhase.Draw => "② 补牌",
            RoundPhase.PlayerPhase => "③ 玩家阶段",
            RoundPhase.EnemyPhase => "④ 敌人阶段",
            RoundPhase.RoundEnd => "⑤ 轮末结算",
            RoundPhase.Finished => "战斗结束",
            _ => phase.ToString(),
        };

        private static string DecisionName(DecisionKind kind) => kind switch
        {
            DecisionKind.TransformDeclaration => "宣告升变",
            DecisionKind.PlayerActions => "玩家行动",
            DecisionKind.EnemyResponse => "响应窗口",
            DecisionKind.HandLimitDiscard => "轮末弃牌",
            _ => "无",
        };

        private static string RankName(EnemyRank rank) => rank switch
        {
            EnemyRank.Normal => "小怪",
            EnemyRank.Elite => "精英",
            EnemyRank.Boss => "首领",
            _ => rank.ToString(),
        };

        private static string CategoryName(CardCategory category) => category switch
        {
            CardCategory.Class => "职业",
            CardCategory.Weapon => "武器",
            CardCategory.Armor => "防具",
            CardCategory.Treasure => "秘宝",
            _ => category.ToString(),
        };

        private static string KindName(CardKind kind) => kind switch
        {
            CardKind.Skill => "技能",
            CardKind.Timing => "时机",
            CardKind.Continuous => "持续",
            CardKind.Transform => "升变",
            CardKind.Form => "形态",
            CardKind.Hero => "英雄",
            _ => kind.ToString(),
        };
    }
}
