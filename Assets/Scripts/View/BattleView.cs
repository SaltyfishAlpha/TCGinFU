using System.Collections.Generic;
using System.Linq;
using TCGinFU.Core;
using TCGinFU.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace TCGinFU.View
{
    /// <summary>
    /// 战斗界面总控。整个界面由代码按 <see cref="BattleLayoutAsset"/> 建出来，
    /// 不依赖预制体——改布局只改资产，不用动场景也不用动代码。
    /// </summary>
    [AddComponentMenu("TCGinFU/战斗界面")]
    public sealed class BattleView : MonoBehaviour, ICardDragContext
    {
        [Header("布局")]
        [Tooltip("场地区域配置。留空会在运行时生成一份默认布局")]
        [SerializeField] private BattleLayoutAsset layout;

        [Header("开局参数")]
        [SerializeField] private int seed = 20251117;

        [Range(1, 4)]
        [SerializeField] private int partySize = 2;

        [SerializeField] private BattleScenario scenario = BattleScenario.哥布林;

        private readonly List<IDropTarget> _dropTargets = new();
        private readonly List<ZoneView> _zones = new();
        private readonly Dictionary<int, CardView> _cardViews = new();
        private readonly Dictionary<string, CombatantView> _combatantViews = new();
        private readonly List<CardView> _cardPool = new();

        private RectTransform _board;
        private RectTransform _logPanel;
        private Text _statusText;
        private Text _messageText;
        private Text _logText;
        private Button _contextButton;
        private Text _contextButtonLabel;
        private IDropTarget _hoveredTarget;

        public BattleSession Session { get; private set; }
        public Canvas Canvas { get; private set; }
        public RectTransform DragLayer { get; private set; }

        private void Start()
        {
            if (layout == null) layout = BattleLayoutAsset.CreateDefault();

            Session = new BattleSession { Seed = seed, PartySize = partySize, EnemyPreset = scenario };
            Session.Restart();

            BuildUi();
            Refresh();
        }

        // ──────────────────────────── 搭界面 ────────────────────────────

        private void BuildUi()
        {
            EnsureEventSystem();

            var canvasGo = new GameObject("BattleCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            Canvas = canvasGo.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = layout.referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Image table = UiBuilder.CreateImage("Table", canvasGo.transform, layout.tableColor);
            table.rectTransform.Stretch();

            _board = UiBuilder.CreateRect("Board", canvasGo.transform);
            _board.Stretch();
            _board.offsetMax = new Vector2(0f, -44f); // 给顶栏让出位置

            foreach (ZoneLayout zoneLayout in layout.zones)
            {
                ZoneView zone = ZoneView.Create(_board, zoneLayout);
                zone.DropFilter = ZoneAcceptsCard;
                zone.DropHandler = HandleZoneDrop;
                _zones.Add(zone);
                if (zoneLayout.acceptsDrop && !zoneLayout.IsCombatantZone) _dropTargets.Add(zone);
            }

            BuildTopBar(canvasGo.transform);
            BuildLogPanel(canvasGo.transform);

            // 拖拽层放最后，保证拖起来的牌画在所有区域之上
            DragLayer = UiBuilder.CreateRect("DragLayer", canvasGo.transform);
            DragLayer.Stretch();
            DragLayer.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            // 本工程 Active Input Handling 设的是 Input System Package，
            // 老的 StandaloneInputModule 会在运行时抛异常，必须用这个。
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        private void BuildTopBar(Transform parent)
        {
            Image bar = UiBuilder.CreateImage("TopBar", parent, new Color(0f, 0f, 0f, 0.55f));
            bar.rectTransform.anchorMin = new Vector2(0f, 1f);
            bar.rectTransform.anchorMax = new Vector2(1f, 1f);
            bar.rectTransform.pivot = new Vector2(0.5f, 1f);
            bar.rectTransform.sizeDelta = new Vector2(0f, 44f);

            var group = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.padding = new RectOffset(10, 10, 7, 7);
            group.spacing = 6;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;

            _statusText = UiBuilder.CreateText("Status", bar.transform, "", 15, TextAnchor.MiddleLeft);
            _statusText.gameObject.AddComponent<LayoutElement>().preferredWidth = 380f;

            _messageText = UiBuilder.CreateText(
                "Message", bar.transform, "", 14, TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 1f));
            _messageText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _contextButton = UiBuilder.CreateButton("结束行动", bar.transform, 96f, SubmitContextAction);
            _contextButtonLabel = _contextButton.GetComponentInChildren<Text>();

            UiBuilder.CreateButton("自动出牌", bar.transform, 86f, () => { Session.AutoPlay(); Refresh(); });
            UiBuilder.CreateButton("跳过本步", bar.transform, 86f, () => { Session.SkipPending(); Refresh(); });
            UiBuilder.CreateButton("自动打完", bar.transform, 86f, () => { Session.SkipToEnd(); Refresh(); });
            UiBuilder.CreateButton("战报", bar.transform, 64f,
                () => _logPanel.gameObject.SetActive(!_logPanel.gameObject.activeSelf));
            UiBuilder.CreateButton("重开", bar.transform, 64f, () => { Session.Restart(); Refresh(); });
        }

        private void BuildLogPanel(Transform parent)
        {
            Image panel = UiBuilder.CreateImage("LogPanel", parent, new Color(0f, 0f, 0f, 0.82f));
            _logPanel = panel.rectTransform;
            _logPanel.anchorMin = new Vector2(1f, 0f);
            _logPanel.anchorMax = new Vector2(1f, 1f);
            _logPanel.pivot = new Vector2(1f, 0.5f);
            _logPanel.sizeDelta = new Vector2(360f, -48f);
            _logPanel.anchoredPosition = new Vector2(0f, -24f);

            _logText = UiBuilder.CreateText("LogText", _logPanel, "", 13, TextAnchor.LowerLeft);
            _logText.rectTransform.Stretch(8f);

            _logPanel.gameObject.SetActive(false);
        }

        // ──────────────────────────── 刷新 ────────────────────────────

        /// <summary>按引擎当前状态重建所有区域的内容。每次动作之后调一次。</summary>
        public void Refresh()
        {
            BattleState state = Session.State;
            PendingDecision pending = Session.Engine.Pending;

            _statusText.text =
                $"第 {state.Round} 轮 · {PhaseName(state.Phase)} · 命刻 {state.SceneClock}"
                + (state.Outcome == BattleOutcome.Ongoing
                    ? ""
                    : state.Outcome == BattleOutcome.Victory ? "　<color=#7dff7d>★ 胜利</color>" : "　<color=#ff7d7d>✖ 失败</color>");

            _messageText.text = Session.LastActionFailed
                ? $"<color=#ff9a9a>{Session.Message}</color>"
                : Session.Message;

            UpdateContextButton(pending);

            if (_logPanel.gameObject.activeSelf)
            {
                string[] lines = Session.Log.Split('\n');
                _logText.text = string.Join("\n", lines.Skip(Mathf.Max(0, lines.Length - 46)));
            }

            var liveCardUids = new HashSet<int>();
            var liveCombatantIds = new HashSet<string>();

            foreach (ZoneView zone in _zones)
            {
                zone.gameObject.SetActive(zone.Layout.visible);
                if (!zone.Layout.visible) continue;

                zone.BeginRebuild();

                if (zone.Layout.IsCombatantZone) FillCombatantZone(zone, liveCombatantIds);
                else FillCardZone(zone, liveCardUids);

                zone.Arrange();
            }

            Prune(liveCardUids, liveCombatantIds);
        }

        private void FillCardZone(ZoneView zone, HashSet<int> live)
        {
            IReadOnlyList<CardInstance> cards = ResolveCards(zone.Layout);
            zone.SetLabel($"{zone.Layout.displayName} ({cards.Count})");

            bool draggable = zone.Layout.draggable && Session.State.Outcome == BattleOutcome.Ongoing;

            foreach (CardInstance card in cards)
            {
                if (!_cardViews.TryGetValue(card.Uid, out CardView view))
                {
                    view = RentCardView();
                    _cardViews[card.Uid] = view;
                }

                view.gameObject.SetActive(true);
                view.Bind(card, zone, zone.Layout.faceDown, draggable, zone.Layout.cardSize);
                zone.Add(view.Rect);
                live.Add(card.Uid);
            }
        }

        private void FillCombatantZone(ZoneView zone, HashSet<string> live)
        {
            IReadOnlyList<Combatant> combatants = ResolveCombatants(zone.Layout);
            zone.SetLabel(zone.Layout.displayName);

            foreach (Combatant combatant in combatants)
            {
                if (!_combatantViews.TryGetValue(combatant.Id, out CombatantView view))
                {
                    view = CombatantView.Create(zone.Rect);
                    view.DropFilter = CombatantAcceptsCard;
                    view.DropHandler = HandleCombatantDrop;
                    view.ClickHandler = HandleCombatantClick;
                    _combatantViews[combatant.Id] = view;
                    _dropTargets.Add(view);
                }

                bool isEnemy = combatant is Enemy;
                bool selected = isEnemy
                    ? Session.SelectedEnemyId == combatant.Id
                    : Session.SelectedPlayerId == combatant.Id;

                view.gameObject.SetActive(true);
                view.Bind(combatant, isEnemy, selected, zone.Layout.cardSize);
                zone.Add(view.Rect);
                live.Add(combatant.Id);
            }
        }

        private void Prune(HashSet<int> liveCards, HashSet<string> liveCombatants)
        {
            foreach (int uid in _cardViews.Keys.Where(uid => !liveCards.Contains(uid)).ToList())
            {
                CardView view = _cardViews[uid];
                _cardViews.Remove(uid);
                view.gameObject.SetActive(false);
                view.Rect.SetParent(_board, false);
                _cardPool.Add(view);
            }

            foreach (string id in _combatantViews.Keys.Where(id => !liveCombatants.Contains(id)).ToList())
            {
                _combatantViews[id].gameObject.SetActive(false);
            }
        }

        private CardView RentCardView()
        {
            if (_cardPool.Count == 0) return CardView.Create(_board, this);

            CardView view = _cardPool[^1];
            _cardPool.RemoveAt(_cardPool.Count - 1);
            return view;
        }

        // ──────────────────────────── 区域内容解析 ────────────────────────────

        private PlayerCharacter ResolvePlayer(int index)
        {
            IReadOnlyList<PlayerCharacter> players = Session.State.Players;
            if (index < 0) return Session.SelectedPlayer ?? players.FirstOrDefault();
            return index < players.Count ? players[index] : null;
        }

        private IReadOnlyList<CardInstance> ResolveCards(ZoneLayout zoneLayout)
        {
            PlayerCharacter player = ResolvePlayer(zoneLayout.playerIndex);
            if (player == null) return System.Array.Empty<CardInstance>();

            PlayerZones zones = player.Zones;
            return zoneLayout.source switch
            {
                ZoneSource.手牌 => zones.Hand.ToList(),
                ZoneSource.卡组 => zones.Deck.ToList(),
                ZoneSource.弃牌堆 => zones.Discard.ToList(),
                ZoneSource.充能区 => zones.Charges.Tokens.Select(t => t.Card).ToList(),
                ZoneSource.持续区 => zones.Continuous != null
                    ? new List<CardInstance> { zones.Continuous }
                    : (IReadOnlyList<CardInstance>)System.Array.Empty<CardInstance>(),
                ZoneSource.形态卡组 => zones.FormDecks.Values.SelectMany(p => p).ToList(),
                ZoneSource.已完成升变区 => zones.CompletedTransform.ToList(),
                _ => System.Array.Empty<CardInstance>(),
            };
        }

        private IReadOnlyList<Combatant> ResolveCombatants(ZoneLayout zoneLayout) => zoneLayout.source switch
        {
            ZoneSource.我方前排 => Session.State.Players.Where(p => p.Row == Row.Front).Cast<Combatant>().ToList(),
            ZoneSource.我方后排 => Session.State.Players.Where(p => p.Row == Row.Back).Cast<Combatant>().ToList(),
            ZoneSource.敌方前排 => Session.State.Enemies.Where(e => e.Row == Row.Front).Cast<Combatant>().ToList(),
            ZoneSource.敌方后排 => Session.State.Enemies.Where(e => e.Row == Row.Back).Cast<Combatant>().ToList(),
            _ => System.Array.Empty<Combatant>(),
        };

        // ──────────────────────────── 拖拽与放置 ────────────────────────────

        public void OnCardBeginDrag(CardView card)
        {
            foreach (IDropTarget target in _dropTargets)
            {
                target.SetDropHighlight(target.CanAccept(card) ? CardHighlight.合法 : CardHighlight.正常);
            }
        }

        public void OnCardDrag(CardView card, PointerEventData eventData)
        {
            IDropTarget hovered = FindTargetUnder(eventData);
            if (ReferenceEquals(hovered, _hoveredTarget)) return;

            if (_hoveredTarget != null)
            {
                _hoveredTarget.SetDropHighlight(
                    _hoveredTarget.CanAccept(card) ? CardHighlight.合法 : CardHighlight.正常);
            }

            _hoveredTarget = hovered;

            if (_hoveredTarget != null)
            {
                bool ok = _hoveredTarget.CanAccept(card);
                _hoveredTarget.SetDropHighlight(ok ? CardHighlight.悬停 : CardHighlight.非法);
                card.SetHighlight(ok ? CardHighlight.合法 : CardHighlight.非法);
            }
            else
            {
                card.SetHighlight(CardHighlight.拖拽中);
            }
        }

        public void OnCardEndDrag(CardView card, PointerEventData eventData)
        {
            IDropTarget target = FindTargetUnder(eventData);

            foreach (IDropTarget drop in _dropTargets) drop.SetDropHighlight(CardHighlight.正常);
            _hoveredTarget = null;

            if (target != null && target.CanAccept(card)) target.AcceptCard(card);
            else card.ReturnHome();

            Refresh();
        }

        /// <summary>
        /// 用矩形命中而不是 GraphicRaycast：拖拽中的卡本身会挡住射线，
        /// 而且区域底板是否勾了 raycastTarget 不该影响能不能放。
        /// </summary>
        private IDropTarget FindTargetUnder(PointerEventData eventData)
        {
            Camera camera = Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Canvas.worldCamera;

            // 倒序遍历：后注册的（角色面板）压在区域底板上面
            for (int i = _dropTargets.Count - 1; i >= 0; i--)
            {
                IDropTarget target = _dropTargets[i];
                if (target.Rect == null || !target.Rect.gameObject.activeInHierarchy) continue;

                if (RectTransformUtility.RectangleContainsScreenPoint(target.Rect, eventData.position, camera))
                {
                    return target;
                }
            }

            return null;
        }

        public void OnCardHover(CardView card, bool entered)
        {
            if (card.IsDragging) return;
            card.SetHighlight(entered ? CardHighlight.悬停 : CardHighlight.正常);
        }

        public void OnCardClick(CardView card)
        {
            // 点一下也能出牌：用当前选中的目标，省得每次都拖
            if (!card.Draggable || card.Zone == null) return;

            PlayerCharacter player = ResolvePlayer(card.Zone.Layout.playerIndex);
            if (player == null) return;

            string target = BattleSession.NeedsAllyTarget(card.Definition())
                ? player.Id
                : Session.SelectedEnemyId;

            Session.Submit(new PlayCardAction(player.Id, card.Card.Uid, target), $"打出「{card.Card.Name}」。");
            Refresh();
        }

        private bool ZoneAcceptsCard(ZoneView zone, CardView card)
        {
            if (card.Zone == null || zone == card.Zone) return false;
            return zone.Layout.source == ZoneSource.充能区;
        }

        private void HandleZoneDrop(ZoneView zone, CardView card)
        {
            PlayerCharacter owner = ResolvePlayer(card.Zone.Layout.playerIndex);
            PlayerCharacter zoneOwner = ResolvePlayer(zone.Layout.playerIndex);
            if (owner == null || zoneOwner == null)
            {
                card.ReturnHome();
                return;
            }

            Session.Submit(
                new PlaceChargeAction(owner.Id, card.Card.Uid, zoneOwner.Id),
                $"「{card.Card.Name}」贴成了充能。");
        }

        private bool CombatantAcceptsCard(CombatantView view, CardView card)
        {
            if (card.Zone == null || !card.Zone.Layout.draggable) return false;

            CardDefinition def = card.Definition();
            bool needsEnemy = def.Effects.Any(e => e.Target.Scope == TargetScope.SingleEnemy);
            bool needsAlly = def.Effects.Any(e => e.Target.Scope == TargetScope.SingleAlly);

            if (needsEnemy) return view.Combatant is Enemy;
            if (needsAlly) return view.Combatant is PlayerCharacter;
            return true; // 不需要指定目标的牌，拖到谁身上都算打出
        }

        private void HandleCombatantDrop(CombatantView view, CardView card)
        {
            PlayerCharacter player = ResolvePlayer(card.Zone.Layout.playerIndex);
            if (player == null)
            {
                card.ReturnHome();
                return;
            }

            if (view.Combatant is Enemy) Session.SelectedEnemyId = view.Combatant.Id;

            Session.Submit(
                new PlayCardAction(player.Id, card.Card.Uid, view.Combatant.Id),
                $"打出「{card.Card.Name}」→ {view.Combatant.Name}。");
        }

        private void HandleCombatantClick(CombatantView view)
        {
            if (view.Combatant is Enemy) Session.SelectedEnemyId = view.Combatant.Id;
            else Session.SelectedPlayerId = view.Combatant.Id;

            Refresh();
        }

        // ──────────────────────────── 顶栏的那个随阶段变的按钮 ────────────────────────────

        private void UpdateContextButton(PendingDecision pending)
        {
            string label = pending.Kind switch
            {
                DecisionKind.TransformDeclaration => "不升变",
                DecisionKind.PlayerActions => "结束行动",
                DecisionKind.EnemyResponse => "放弃响应",
                DecisionKind.HandLimitDiscard => "自动弃牌",
                _ => "—",
            };

            _contextButtonLabel.text = label;
            _contextButton.interactable = pending.Kind != DecisionKind.None
                                          && Session.State.Outcome == BattleOutcome.Ongoing;
        }

        private void SubmitContextAction()
        {
            PlayerCharacter player = Session.SelectedPlayer;
            PendingDecision pending = Session.Engine.Pending;

            // 当前选中的角色不在待办列表里时，直接处理第一个在等的人，省得用户先去点头像
            if (player == null || !pending.WaitingOn.Contains(player.Id))
            {
                string first = pending.WaitingOn.FirstOrDefault();
                if (first != null)
                {
                    Session.SelectedPlayerId = first;
                    player = Session.State.FindPlayer(first);
                }
            }

            if (player == null)
            {
                Session.SkipPending();
                Refresh();
                return;
            }

            switch (pending.Kind)
            {
                case DecisionKind.TransformDeclaration:
                    Session.Submit(new DeclineTransformAction(player.Id), "已跳过升变。");
                    break;
                case DecisionKind.PlayerActions:
                    Session.Submit(new DeclareReadyAction(player.Id), $"{player.Name} 宣布行动完毕。");
                    break;
                case DecisionKind.EnemyResponse:
                    Session.Submit(new PassResponseAction(player.Id), $"{player.Name} 放弃响应。");
                    break;
                case DecisionKind.HandLimitDiscard:
                    Session.SkipPending();
                    break;
            }

            Refresh();
        }

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
    }

    internal static class CardViewExtensions
    {
        public static CardDefinition Definition(this CardView view) => view.Card.Definition;
    }
}
