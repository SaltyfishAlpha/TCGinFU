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
        private CardDetailPanel _detailPanel;
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
            // 绕 X 轴正向旋转：上边缘（远处的敌人）往 +Z 倒下去，也就是远离位于 −Z 的镜头。
            // 注意桌面的 +Z 是**背离**镜头的，朝镜头的一侧是 −Z（见 CardMeshFactory.TowardCamera）
            root.localRotation = Quaternion.Euler(layout.boardTilt, 0f, 0f);
            root.localPosition = Vector3.zero;
            BoardRoot = root;

            BuildCamera(boardPx * scale);

            // 桌布：比桌面大一圈的面片，压在所有东西下面
            Transform table = CardMeshFactory.CreateMeshObject(
                "Table", root, CardMeshFactory.Quad, layout.tableColor);
            table.localScale = new Vector3(boardPx.x * 1.35f, boardPx.y * 1.3f, 1f);
            // 桌布压在所有东西背后（远离镜头的一侧）
            table.localPosition = new Vector3(0f, 0f, -CardMeshFactory.TowardCamera * 2f);

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

            // 相机必须在 −Z 侧朝 +Z 看。放到 +Z 侧会让整个画面左右镜像，
            // 而且所有面片都变成背面——屏幕会一片漆黑。
            cameraGo.transform.position = new Vector3(
                0f, boardWorldSize.y * layout.cameraHeight, -distance);
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

            // 详情面板最后建：它要盖在牌堆查看面板之上，
            // 这样在弃牌堆里点一张牌也能看详情
            _detailPanel = CardDetailPanel.Create(hudGo.transform);
            // 查看面板里的牌按「当前操作角色」的面板算公式
            _peekPanel.CardClickHandler = card => _detailPanel.Open(card, Session.SelectedPlayer);
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

            // 必须放在重建之后：FillCardZone 里的 Bind 会把高亮重置成「正常」，
            // 之前就是因为这个，选完「贴充能」后那张牌的高亮立刻没了，看着像没生效
            ApplyChargeTargetingHighlights();
        }

        /// <summary>
        /// 选充能目标期间的提示：被选中的那张牌保持高亮，可以贴的队友角色卡亮绿。
        /// 不在选择状态时负责把这些高亮清掉。
        /// </summary>
        private void ApplyChargeTargetingHighlights()
        {
            bool targeting = _pendingChargeCard != null;

            foreach (CombatantView view in _combatantViews.Values)
            {
                bool valid = targeting
                             && view.Combatant is PlayerCharacter
                             && !view.Combatant.IsDown;
                view.SetDropHighlight(valid ? CardHighlight.合法 : CardHighlight.正常);
            }

            if (!targeting) return;

            if (_cardViews.TryGetValue(_pendingChargeCard.Uid, out CardView card))
            {
                card.SetHighlight(CardHighlight.选中);
            }
        }

        private void FillCardZone(ZoneView zone, HashSet<int> live)
        {
            IReadOnlyList<CardInstance> cards = ResolveCards(zone.Layout);
            zone.SetLabel($"{zone.Layout.displayName} ({cards.Count})");

            // 选充能目标期间整张手牌都不可拖：这一步是模态的，
            // 中途还能把别的牌打出去只会让人以为充能没生效
            bool draggable = zone.Layout.draggable
                             && Session.State.Outcome == BattleOutcome.Ongoing
                             && _pendingChargeCard == null;

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
                FillTransformSlot(view, combatant, liveCards);
            }
        }

        /// <summary>
        /// 升变期间把升变牌盖到角色卡上——它就是这一局的新角色卡（§6.2 ②）。
        /// </summary>
        private void FillTransformSlot(CombatantView view, Combatant combatant, HashSet<int> liveCards)
        {
            if (view.TransformSlot == null) return;

            ZoneView slot = view.TransformSlot;
            slot.BeginRebuild();

            if (combatant is PlayerCharacter player
                && player.Transform.Active
                && player.Transform.TransformCard != null)
            {
                CardInstance card = player.Transform.TransformCard;
                CardView cardView = RentCardView(card.Uid);
                cardView.Root.gameObject.SetActive(true);
                cardView.Bind(card, slot, faceDown: false, draggable: false, slot.SlotSize);
                slot.Add(cardView);
                liveCards.Add(card.Uid);
            }

            slot.Arrange();
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

            // 正在等它选目标的那张牌保持选中高亮，别被「鼠标移开」给清掉
            if (_pendingChargeCard != null && ReferenceEquals(card.Card, _pendingChargeCard))
            {
                card.SetHighlight(CardHighlight.选中);
                return;
            }

            card.SetHighlight(entered ? CardHighlight.悬停 : CardHighlight.正常);
        }

        /// <summary>
        /// 左键 = 看详情，**不是**打出。
        /// 棋盘上的 3D 卡塞不下卡面规则文字，而那恰恰是打牌时最需要读的东西；
        /// 而且「点一下就把牌打出去」太容易误触。打出走拖拽。
        /// </summary>
        public void OnCardClick(CardView card)
        {
            if (card.Card == null) return;

            // 看牌是只读的，选充能目标期间也允许——不会打断那条流程。
            // 公式按这张牌所属角色的面板算（卡组/弃牌堆这类区域也能定位到人）
            PlayerCharacter viewer = card.Zone != null
                ? ResolvePlayer(card.Zone.Layout.playerIndex)
                : Session.SelectedPlayer;

            _detailPanel.Open(card.Card, viewer);
        }

        /// <summary>
        /// 选目标是模态的：期间别的操作一律挡掉并说明原因，
        /// 否则「还能照常打牌」会让人以为前一步没生效。
        /// </summary>
        private bool IsBlockedByTargeting(string what)
        {
            if (_pendingChargeCard == null) return false;

            Session.SetMessage(
                $"正在选充能目标（「{_pendingChargeCard.Name}」），不能{what}。"
                + "请点一名<b>队友的角色卡</b>，或用顶栏的「取消充能」。", isWarning: true);
            Refresh();
            return true;
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
            // 正在选充能目标时，点空处**不**取消。
            // 点在角色卡边缘稍微偏一点就会命中区域底板而不是卡，
            // 要是这里顺手取消掉，用户看到的就是「贴充能毫无反应」。
            if (_pendingChargeCard != null)
            {
                Session.SetMessage(
                    $"还在选充能目标（「{_pendingChargeCard.Name}」）——请点一名<b>队友的角色卡</b>，"
                    + "或用顶栏的「取消充能」。", isWarning: true);
                Refresh();
                return;
            }

            if (!zone.Layout.CanPeek) return;

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
            if (IsBlockedByTargeting("再开菜单")) return;
            if (card.Zone == null || !card.Zone.Layout.draggable || card.Card == null) return;

            PlayerCharacter owner = ResolvePlayer(card.Zone.Layout.playerIndex);
            if (owner == null) return;

            // 左键已经改成「看详情」，所以打出得回到菜单里——
            // 否则除了拖拽就没有别的出牌方式了
            CardInstance instance = card.Card;
            string target = BattleSession.NeedsAllyTarget(instance.Definition)
                ? owner.Name
                : Session.State.Find(Session.SelectedEnemyId)?.Name ?? "当前目标";

            var options = new List<(string, System.Action)>
            {
                ($"打出（→ {target}）", () => PlayCard(owner, instance)),
                ("贴充能…", () => BeginChargeTargeting(owner, instance)),
                ("查看详情", () => _detailPanel.Open(instance, owner)),
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
            // 先把「这轮根本贴不了」的情况说清楚，而不是让用户点完目标、点完确认，
            // 最后才在状态栏看到一行拒绝理由
            if (Session.Engine.Pending.Kind != DecisionKind.PlayerActions)
            {
                Session.SetMessage("贴充能只能在玩家阶段。", isWarning: true);
                Refresh();
                return;
            }

            if (owner.HasPlacedChargeThisRound)
            {
                Session.SetMessage($"{owner.Name} 本轮已经贴过充能了（每轮限 1 次）。", isWarning: true);
                Refresh();
                return;
            }

            _pendingChargeCard = card;
            _pendingChargeOwnerId = owner.Id;
            Session.SetMessage($"选择要给谁贴充能（「{card.Name}」）——点一名<b>队友的角色卡</b>，或用顶栏的「取消充能」。");
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
                // 保持选择状态，让用户直接再点一个队友，不用重新走一遍右键菜单
                Session.SetMessage(
                    $"{view.Combatant.Name} 是敌人，充能只能贴给队友。请点一名队友的角色卡。",
                    isWarning: true);
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
            // 选充能目标期间，这个按钮临时变成唯一的取消入口
            if (_pendingChargeCard != null)
            {
                _contextButtonLabel.text = "取消充能";
                _contextButton.interactable = true;
                return;
            }

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
            if (_pendingChargeCard != null)
            {
                CancelChargeTargeting();
                return;
            }

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
