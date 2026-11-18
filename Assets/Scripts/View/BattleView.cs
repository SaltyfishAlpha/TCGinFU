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
    /// 战斗界面总控。棋盘是真正的 3D：每张卡都是带厚度的网格物体，
    /// 所以牌堆叠起来看得出有多少张。整个场面按 <see cref="BattleLayoutAsset"/> 搭出来，
    /// 不依赖预制体——改布局只改资产。
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
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private Transform _cardGraveyard;
        private ZonePeekPanel _peekPanel;
        private CardContextMenu _contextMenu;
        private ConfirmDialog _confirmDialog;
        private IDropTarget _hoveredTarget;

        private CardInstance _pendingChargeCard;
        private string _pendingChargeOwnerId;

        private Text _statusText;
        private Text _messageText;
        private Button _contextButton;
        private Text _contextButtonLabel;

        public BattleSession Session { get; private set; }
        public Camera BoardCamera { get; private set; }
        public Transform BoardRoot { get; private set; }

        private void Start()
        {
            if (layout == null) layout = BattleLayoutAsset.CreateDefault();
            layout.Normalize();

            Session = new BattleSession { Seed = seed, PartySize = partySize, EnemyPreset = scenario };
            Session.Restart();

            BuildScene();
            Refresh();
        }

        // ──────────────────────────── 搭场面 ────────────────────────────

        private void BuildScene()
        {
            EnsureEventSystem();
            BuildBoard();
            BuildHud();
        }

        /// <summary>
        /// 桌面根节点缩放成 1/PixelsPerUnit，于是它底下所有坐标都可以直接用
        /// **设计像素**，只在这一处换算到世界单位。
        /// </summary>
        private void BuildBoard()
        {
            Vector2 boardPx = layout.referenceResolution;
            float scale = 1f / BattleLayoutAsset.PixelsPerUnit;

            var root = new GameObject("Board").transform;
            root.SetParent(transform, false);
            root.localScale = Vector3.one * scale;
            // 绕 X 轴负向旋转：上边缘（远处的敌人）往镜头反方向倒下去，
            // 桌面法线仍然朝向镜头那一侧
            root.localRotation = Quaternion.Euler(-layout.boardTilt, 0f, 0f);
            root.localPosition = Vector3.zero;
            BoardRoot = root;

            BuildCamera(boardPx * scale);

            // 桌布：比桌面大一圈的面片，压在所有东西下面
            Transform table = CardMeshFactory.CreateMeshObject(
                "Table", root, CardMeshFactory.Quad, layout.tableColor);
            table.localScale = new Vector3(boardPx.x * 1.35f, boardPx.y * 1.3f, 1f);
            table.localPosition = new Vector3(0f, 0f, -2f);

            foreach (ZoneLayout zoneLayout in layout.zones)
            {
                ZoneView zone = ZoneView.Create(root, zoneLayout, layout.CardSizeFor(zoneLayout), boardPx);
                zone.DropFilter = ZoneAcceptsCard;
                zone.DropHandler = HandleZoneDrop;
                zone.ClickHandler = HandleZoneClick;
                _zones.Add(zone);
                if (zoneLayout.acceptsDrop && !zoneLayout.IsCombatantZone) _dropTargets.Add(zone);
            }

            // 回收池里的卡挂这儿，别留在区域里影响排布
            _cardGraveyard = new GameObject("CardPool").transform;
            _cardGraveyard.SetParent(root, false);
            _cardGraveyard.gameObject.SetActive(false);
        }

        private void BuildCamera(Vector2 boardWorldSize)
        {
            // 场景里没有别的相机，顺带把 AudioListener 挂这儿
            var cameraGo = new GameObject("BoardCamera", typeof(Camera), typeof(AudioListener));
            cameraGo.transform.SetParent(transform, false);

            var camera = cameraGo.GetComponent<Camera>();
            camera.orthographic = false;
            camera.fieldOfView = layout.cameraFov;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = layout.tableColor * 0.4f;

            // 3D 卡牌靠碰撞体拾取，所以必须挂 PhysicsRaycaster，
            // 否则 IPointerClickHandler / IBeginDragHandler 一个都不会触发
            var raycaster = cameraGo.AddComponent<PhysicsRaycaster>();
            raycaster.eventMask = ~0;

            float tilt = layout.boardTilt * Mathf.Deg2Rad;
            float visibleHeight = boardWorldSize.y * Mathf.Cos(tilt);
            float needed = Mathf.Max(visibleHeight, boardWorldSize.x / Mathf.Max(0.1f, camera.aspect));
            float distance = needed * 0.5f / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            distance *= layout.cameraMargin;

            cameraGo.transform.position = new Vector3(0f, boardWorldSize.y * layout.cameraHeight, distance);
            cameraGo.transform.LookAt(Vector3.zero, Vector3.up);

            BoardCamera = camera;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            // 本工程 Active Input Handling 设的是 Input System Package，
            // 老的 StandaloneInputModule 会在运行时抛异常，必须用这个
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        /// <summary>顶栏与弹窗走屏幕空间，不跟着桌面倾斜——倾斜的文字没法读。</summary>
        private void BuildHud()
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

        public void Refresh()
        {
            BattleState state = Session.State;
            PendingDecision pending = Session.Engine.Pending;

            _statusText.text =
                $"第 {state.Round} 轮 · {PhaseName(state.Phase)} · 命刻 {state.SceneClock}"
                + (state.Outcome == BattleOutcome.Ongoing
                    ? ""
                    : state.Outcome == BattleOutcome.Victory
                        ? "　<color=#7dff7d>★ 胜利</color>"
                        : "　<color=#ff7d7d>✖ 失败</color>");

            _messageText.text = Session.LastActionFailed
                ? $"<color=#ff9a9a>{Session.Message}</color>"
                : Session.Message;

            UpdateContextButton(pending);

            var liveCards = new HashSet<int>();
            var liveCombatants = new HashSet<string>();

            foreach (ZoneView zone in _zones)
            {
                zone.Root.gameObject.SetActive(zone.Layout.visible);
                if (!zone.Layout.visible) continue;

                zone.BeginRebuild();

                if (zone.Layout.IsCombatantZone) FillCombatantZone(zone, liveCombatants, liveCards);
                else FillCardZone(zone, liveCards);

                zone.Arrange();
            }

            Prune(liveCards, liveCombatants);
        }

        private void FillCardZone(ZoneView zone, HashSet<int> live)
        {
            IReadOnlyList<CardInstance> cards = ResolveCards(zone.Layout);
            zone.SetLabel($"{zone.Layout.displayName} ({cards.Count})");

            bool draggable = zone.Layout.draggable && Session.State.Outcome == BattleOutcome.Ongoing;

            foreach (CardInstance card in cards)
            {
                CardView view = RentCardView(card.Uid);
                view.Root.gameObject.SetActive(true);
                view.Bind(card, zone, zone.Layout.faceDown, draggable, zone.SlotSize);
                zone.Add(view);
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
                    view = CombatantView.Create(zone.Root, zone.SlotSize, withChargeStack: !isEnemy);
                    view.DropFilter = CombatantAcceptsCard;
                    view.DropHandler = HandleCombatantDrop;
                    view.ClickHandler = HandleCombatantClick;
                    _combatantViews[combatant.Id] = view;
                    _dropTargets.Add(view);
                }

                bool selected = isEnemy
                    ? Session.SelectedEnemyId == combatant.Id
                    : Session.SelectedPlayerId == combatant.Id;

                view.Root.gameObject.SetActive(true);
                view.Bind(combatant, isEnemy, selected);
                zone.AddCombatant(view);
                live.Add(combatant.Id);

                FillChargeStack(view, combatant, liveCards);
            }
        }

        /// <summary>把该角色的充能牌横置摆到他的角色卡背后。</summary>
        private void FillChargeStack(CombatantView view, Combatant combatant, HashSet<int> liveCards)
        {
            if (view.ChargeStack == null || combatant is not PlayerCharacter player) return;

            ZoneView stack = view.ChargeStack;
            stack.BeginRebuild();

            foreach (ChargeToken token in player.Zones.Charges.Tokens)
            {
                CardView cardView = RentCardView(token.Card.Uid);
                cardView.Root.gameObject.SetActive(true);
                cardView.Bind(token.Card, stack, faceDown: true, draggable: false, stack.SlotSize);
                stack.Add(cardView);
                liveCards.Add(token.Card.Uid);
            }

            stack.Arrange();
        }

        private CardView RentCardView(int uid)
        {
            if (_cardViews.TryGetValue(uid, out CardView existing)) return existing;

            CardView view;
            if (_cardPool.Count > 0)
            {
                view = _cardPool[^1];
                _cardPool.RemoveAt(_cardPool.Count - 1);
            }
            else
            {
                view = CardView.Create(BoardRoot, this);
            }

            _cardViews[uid] = view;
            return view;
        }

        private void Prune(HashSet<int> liveCards, HashSet<string> liveCombatants)
        {
            foreach (int uid in _cardViews.Keys.Where(uid => !liveCards.Contains(uid)).ToList())
            {
                CardView view = _cardViews[uid];
                _cardViews.Remove(uid);
                view.Root.SetParent(_cardGraveyard, false);
                view.Root.gameObject.SetActive(false);
                _cardPool.Add(view);
            }

            foreach (string id in _combatantViews.Keys.Where(id => !liveCombatants.Contains(id)).ToList())
            {
                _combatantViews[id].Root.gameObject.SetActive(false);
            }
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
            _contextMenu.Close();

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
        /// 射线找鼠标下方的放置目标。拖拽中的卡自己的碰撞体已经关掉，
        /// 所以不会挡住下面的目标。
        /// </summary>
        private IDropTarget FindTargetUnder(PointerEventData eventData)
        {
            if (BoardCamera == null) return null;

            Ray ray = BoardCamera.ScreenPointToRay(eventData.position);
            int count = Physics.RaycastNonAlloc(ray, _hits, 1000f);
            if (count == 0) return null;

            System.Array.Sort(_hits, 0, count, RaycastDistanceComparer.Instance);

            for (int i = 0; i < count; i++)
            {
                Transform hit = _hits[i].collider.transform;

                // 角色卡优先于它底下的区域底板
                var combatant = hit.GetComponentInParent<CombatantView>();
                if (combatant != null && _dropTargets.Contains(combatant)) return combatant;

                var zone = hit.GetComponentInParent<ZoneView>();
                if (zone != null && _dropTargets.Contains(zone)) return zone;
            }

            return null;
        }

        private sealed class RaycastDistanceComparer : IComparer<RaycastHit>
        {
            public static readonly RaycastDistanceComparer Instance = new();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        public void OnCardHover(CardView card, bool entered)
        {
            if (card.IsDragging) return;
            card.SetHighlight(entered ? CardHighlight.悬停 : CardHighlight.正常);
        }

        public void OnCardClick(CardView card)
        {
            if (!card.Draggable || card.Zone == null) return;

            PlayerCharacter owner = ResolvePlayer(card.Zone.Layout.playerIndex);
            if (owner == null) return;

            PlayCard(owner, card.Card);
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

            CardDefinition def = card.Card.Definition;
            bool needsEnemy = def.Effects.Any(e => e.Target.Scope == TargetScope.SingleEnemy);
            bool needsAlly = def.Effects.Any(e => e.Target.Scope == TargetScope.SingleAlly);

            if (needsEnemy) return view.Combatant is Enemy;
            if (needsAlly) return view.Combatant is PlayerCharacter;
            return true;
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

        /// <summary>点可查看的叠放区域 → 把里面的牌全部摊开（弃牌堆是公开信息）。</summary>
        private void HandleZoneClick(ZoneView zone)
        {
            if (_pendingChargeCard != null)
            {
                CancelChargeTargeting();
                return;
            }

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

            _peekPanel.Open(title, cards);
        }

        private void HandleCombatantClick(CombatantView view)
        {
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

        private void BeginChargeTargeting(PlayerCharacter owner, CardInstance card)
        {
            _pendingChargeCard = card;
            _pendingChargeOwnerId = owner.Id;
            Session.SetMessage($"选择要给谁贴充能（「{card.Name}」）——点一名队友的角色卡，或点棋盘空处取消。");
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
            _contextButtonLabel.text = pending.Kind switch
            {
                DecisionKind.TransformDeclaration => "不升变",
                DecisionKind.PlayerActions => "结束行动",
                DecisionKind.EnemyResponse => "放弃响应",
                DecisionKind.HandLimitDiscard => "自动弃牌",
                _ => "—",
            };

            _contextButton.interactable = pending.Kind != DecisionKind.None
                                          && Session.State.Outcome == BattleOutcome.Ongoing;
        }

        private void SubmitContextAction()
        {
            PlayerCharacter player = Session.SelectedPlayer;
            PendingDecision pending = Session.Engine.Pending;

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
}
