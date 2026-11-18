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
        private Camera _boardCamera;
        private ZonePeekPanel _peekPanel;
        private CardContextMenu _contextMenu;
        private ConfirmDialog _confirmDialog;

        /// <summary>右键选了「贴充能」之后，等待点选目标的那张牌。</summary>
        private CardInstance _pendingChargeCard;
        private string _pendingChargeOwnerId;

        private Text _statusText;
        private Text _messageText;
        private Button _contextButton;
        private Text _contextButtonLabel;
        private IDropTarget _hoveredTarget;

        public BattleSession Session { get; private set; }
        public Canvas Canvas { get; private set; }
        public RectTransform DragLayer { get; private set; }

        private void Start()
        {
            if (layout == null) layout = BattleLayoutAsset.CreateDefault();

            // 旧版布局资产升级后，新加的字段会是零值（Unity 不会套用 C# 字段初始值），
            // 这里兜一下，免得卡牌被缩成 0 看不见
            layout.Normalize();

            Session = new BattleSession { Seed = seed, PartySize = partySize, EnemyPreset = scenario };
            Session.Restart();

            BuildUi();
            Refresh();
        }

        // ──────────────────────────── 搭界面 ────────────────────────────

        private void BuildUi()
        {
            EnsureEventSystem();
            BuildBoardCanvas();
            BuildHudCanvas();
        }

        /// <summary>
        /// 桌面本身是一块**世界空间**的 Canvas，被一台透视相机斜着看——
        /// 这样就有了真实的 3D 透视（靠近手牌的一侧更大、远处的敌人更小），
        /// 同时 CardView / ZoneView 这些 uGUI 组件原样复用，不必重写成 3D 物体。
        /// </summary>
        private void BuildBoardCanvas()
        {
            Vector2 size = layout.referenceResolution;
            float scale = 1f / BattleLayoutAsset.PixelsPerUnit;

            var canvasGo = new GameObject("BoardCanvas", typeof(Canvas), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            Canvas = canvasGo.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.WorldSpace;

            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = size;
            canvasRect.localScale = Vector3.one * scale;
            // 桌面向后躺倒；绕 X 轴转正角度 = 上边缘远离镜头
            canvasRect.localRotation = Quaternion.Euler(layout.boardTilt, 0f, 0f);
            canvasRect.position = Vector3.zero;

            _boardCamera = BuildCamera(size * scale);
            Canvas.worldCamera = _boardCamera;

            Image table = UiBuilder.CreateImage("Table", canvasGo.transform, layout.tableColor);
            table.rectTransform.Stretch(-240f); // 桌布往外铺一圈，避免边缘露出背景

            _board = UiBuilder.CreateRect("Board", canvasGo.transform);
            _board.Stretch();

            foreach (ZoneLayout zoneLayout in layout.zones)
            {
                // 角色也是卡，所以全部区域都用同一套标准卡牌尺寸
                ZoneView zone = ZoneView.Create(_board, zoneLayout, layout.CardSizeFor(zoneLayout));
                zone.DropFilter = ZoneAcceptsCard;
                zone.DropHandler = HandleZoneDrop;
                zone.ClickHandler = HandleZoneClick;
                _zones.Add(zone);
                if (zoneLayout.acceptsDrop && !zoneLayout.IsCombatantZone) _dropTargets.Add(zone);
            }

            // 拖拽层放最后，保证拖起来的牌画在所有区域之上
            DragLayer = UiBuilder.CreateRect("DragLayer", canvasGo.transform);
            DragLayer.Stretch();
            DragLayer.pivot = new Vector2(0.5f, 0.5f);
        }

        private Camera BuildCamera(Vector2 boardWorldSize)
        {
            // 场景里没有别的相机，顺带把 AudioListener 挂这儿，免得 Unity 报缺失
            var cameraGo = new GameObject("BoardCamera", typeof(Camera), typeof(AudioListener));
            cameraGo.transform.SetParent(transform, false);

            var camera = cameraGo.GetComponent<Camera>();
            camera.orthographic = false;
            camera.fieldOfView = layout.cameraFov;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = layout.tableColor * 0.45f;

            // 桌面倾斜后在竖直方向上的投影会变短，所以按 cos 折算需要的取景高度
            float tilt = layout.boardTilt * Mathf.Deg2Rad;
            float visibleHeight = boardWorldSize.y * Mathf.Cos(tilt);
            float neededHeight = Mathf.Max(visibleHeight, boardWorldSize.x / camera.aspect);
            float distance = neededHeight * 0.5f / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            distance *= layout.cameraMargin;

            float height = boardWorldSize.y * layout.cameraHeight;
            cameraGo.transform.position = new Vector3(0f, height, -distance);
            cameraGo.transform.LookAt(Vector3.zero, Vector3.up);

            return camera;
        }

        /// <summary>顶栏、弹窗这些**不**跟着桌面倾斜，单独走屏幕空间 Canvas。</summary>
        private void BuildHudCanvas()
        {
            var hudGo = new GameObject("HudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            hudGo.transform.SetParent(transform, false);

            var hud = hudGo.GetComponent<Canvas>();
            hud.renderMode = RenderMode.ScreenSpaceOverlay;
            hud.sortingOrder = 100;

            var scaler = hudGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = layout.referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            BuildTopBar(hudGo.transform);

            _peekPanel = ZonePeekPanel.Create(hudGo.transform, layout.CardSize);
            _contextMenu = CardContextMenu.Create(hudGo.transform);
            _confirmDialog = ConfirmDialog.Create(hudGo.transform);
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
            UiBuilder.CreateButton("重开", bar.transform, 64f, () => { Session.Restart(); Refresh(); });
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

            var liveCardUids = new HashSet<int>();
            var liveCombatantIds = new HashSet<string>();

            foreach (ZoneView zone in _zones)
            {
                zone.gameObject.SetActive(zone.Layout.visible);
                if (!zone.Layout.visible) continue;

                zone.BeginRebuild();

                if (zone.Layout.IsCombatantZone) FillCombatantZone(zone, liveCombatantIds, liveCardUids);
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
                view.Bind(card, zone, zone.Layout.faceDown, draggable, zone.SlotSize);
                zone.Add(view.Rect);
                live.Add(card.Uid);
            }
        }

        private void FillCombatantZone(ZoneView zone, HashSet<string> live, HashSet<int> liveCards)
        {
            IReadOnlyList<Combatant> combatants = ResolveCombatants(zone.Layout);
            zone.SetLabel(zone.Layout.displayName);

            foreach (Combatant combatant in combatants)
            {
                bool isEnemy = combatant is Enemy;

                if (!_combatantViews.TryGetValue(combatant.Id, out CombatantView view))
                {
                    // 角色本身就是一张卡，尺寸直接用区域的卡位尺寸
                    view = CombatantView.Create(zone.Rect, zone.SlotSize, withChargeStack: !isEnemy);
                    view.DropFilter = CombatantAcceptsCard;
                    view.DropHandler = HandleCombatantDrop;
                    view.ClickHandler = HandleCombatantClick;
                    _combatantViews[combatant.Id] = view;
                    _dropTargets.Add(view);
                    // 充能堆只负责显示，不作为放置目标——充能走右键菜单，
                    // 不必靠「拖到卡的哪一块」来区分打出和充能
                }

                bool selected = isEnemy
                    ? Session.SelectedEnemyId == combatant.Id
                    : Session.SelectedPlayerId == combatant.Id;

                view.gameObject.SetActive(true);
                view.Bind(combatant, isEnemy, selected, zone.SlotSize);
                zone.Add(view.Rect);
                live.Add(combatant.Id);

                FillChargeStrip(view, combatant, liveCards);
            }
        }

        /// <summary>
        /// 把该角色充能区里的牌横置摆到他的角色卡上——实体打牌里给队友充能
        /// 就是在他的角色卡上横放一张牌（§5.2），所以充能不单独占一块场地。
        /// </summary>
        private void FillChargeStrip(CombatantView view, Combatant combatant, HashSet<int> liveCards)
        {
            if (view.ChargeStack == null || combatant is not PlayerCharacter player) return;

            ZoneView strip = view.ChargeStack;
            strip.BeginRebuild();

            foreach (ChargeToken token in player.Zones.Charges.Tokens)
            {
                if (!_cardViews.TryGetValue(token.Card.Uid, out CardView cardView))
                {
                    cardView = RentCardView();
                    _cardViews[token.Card.Uid] = cardView;
                }

                cardView.gameObject.SetActive(true);
                cardView.Bind(token.Card, strip, faceDown: true, draggable: false, strip.SlotSize);
                strip.Add(cardView.Rect);
                liveCards.Add(token.Card.Uid);
            }

            strip.Arrange();
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

        /// <summary>点可查看的叠放区域 → 把里面的牌全部摊开（弃牌堆是公开信息）。</summary>
        private void HandleZoneClick(ZoneView zone)
        {
            if (!zone.Layout.peekable) return;

            IReadOnlyList<CardInstance> cards = ResolveCards(zone.Layout);
            if (cards.Count == 0)
            {
                Session.SetMessage($"{zone.Layout.displayName}是空的。");
                Refresh();
                return;
            }

            PlayerCharacter owner = ResolvePlayer(zone.Layout.playerIndex);
            string title = owner == null
                ? zone.Layout.displayName
                : $"{owner.Name} 的{zone.Layout.displayName}";

            _peekPanel.Open(title, cards, this);
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
            // 正在选充能目标时，点角色 = 选中它并二次确认
            if (_pendingChargeCard != null)
            {
                PickChargeTarget(view);
                return;
            }

            if (view.Combatant is Enemy) Session.SelectedEnemyId = view.Combatant.Id;
            else Session.SelectedPlayerId = view.Combatant.Id;

            Refresh();
        }

        // ──────────────────────────── 右键菜单与充能 ────────────────────────────

        public void OnCardContextMenu(CardView card, Vector2 screenPosition)
        {
            if (card.Zone == null || !card.Zone.Layout.draggable || card.Card == null) return;

            PlayerCharacter owner = ResolvePlayer(card.Zone.Layout.playerIndex);
            if (owner == null) return;

            CardInstance instance = card.Card;
            var options = new List<(string, System.Action)>
            {
                ($"打出（→ {Session.State.Find(Session.SelectedEnemyId)?.Name ?? "当前目标"}）",
                    () => PlayCard(owner, instance)),
                ("贴充能…", () => BeginChargeTargeting(owner, instance)),
            };

            _contextMenu.Open(instance.Name, screenPosition, options);
        }

        private void PlayCard(PlayerCharacter owner, CardInstance card)
        {
            string target = BattleSession.NeedsAllyTarget(card.Definition)
                ? owner.Id
                : Session.SelectedEnemyId;

            Session.Submit(new PlayCardAction(owner.Id, card.Uid, target), $"打出「{card.Name}」。");
            Refresh();
        }

        /// <summary>进入「选充能目标」状态，下一次点角色卡就是选目标。</summary>
        private void BeginChargeTargeting(PlayerCharacter owner, CardInstance card)
        {
            _pendingChargeCard = card;
            _pendingChargeOwnerId = owner.Id;
            Session.SetMessage($"选择要给谁贴充能（「{card.Name}」）——点一名队友的角色卡，或按右键取消。");
            Refresh();
        }

        private void CancelChargeTargeting()
        {
            if (_pendingChargeCard == null) return;

            _pendingChargeCard = null;
            _pendingChargeOwnerId = null;
            Session.SetMessage("已取消贴充能。");
            Refresh();
        }

        private void PickChargeTarget(CombatantView view)
        {
            if (view.Combatant is not PlayerCharacter target)
            {
                Session.SetMessage("充能只能贴给队友的角色卡。", isWarning: true);
                Refresh();
                return;
            }

            CardInstance card = _pendingChargeCard;
            string ownerId = _pendingChargeOwnerId;
            _pendingChargeCard = null;
            _pendingChargeOwnerId = null;

            _confirmDialog.Open(
                $"把「{card.Name}」横置到 {target.Name} 的角色卡上作为充能？\n"
                + $"（{target.Name} 当前充能 {target.Zones.Charges.Count}/{target.Zones.Charges.Capacity}）",
                () =>
                {
                    Session.Submit(
                        new PlaceChargeAction(ownerId, card.Uid, target.Id),
                        $"「{card.Name}」横置到 {target.Name} 身上，作为充能。");
                    Refresh();
                });

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
