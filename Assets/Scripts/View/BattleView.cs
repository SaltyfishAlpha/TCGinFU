using System.Collections.Generic;
using System.Linq;
using TCGinFU.Content.Loading;
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

        [SerializeField] private BattleScenario scenario = BattleScenario.命刻之战;

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
        private CardPreviewStage _previewStage;
        private CardShowcase _showcase;
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

            Session = new BattleSession { Seed = seed, EnemyPreset = scenario };
            Session.Restart();

            // 内容包没加载起来就不开局：把报告直接摊在屏幕上，而不是搭一张空桌子。
            // 静默退回到另一套内容会让「内容包坏了」看起来像「游戏能玩」。
            if (!Session.IsLoaded)
            {
                BuildLoadFailureScreen();
                return;
            }

            // 整局只有一套牌背与一组兜底图，从内容包取一次就够
            CardView.DefaultBackArtId = ContentScenario.LastLoaded?.DefaultCardBackArtId;
            CardArtLibrary.Fallbacks = ContentScenario.LastLoaded?.ArtFallbacks;
            // 卡面图跟着内容包走（玩家包在前、官方包在后），包里没有才退回包体内的兜底图
            CardArtLibrary.SearchRoots = ContentPaths.ResolveAll();

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

            // 离屏摄影棚那一层归预览相机，桌面相机看不见它（见 CardPreviewStage）
            camera.cullingMask = ~(1 << CardPreviewStage.PreviewLayer);

            // 3D 卡牌靠碰撞体拾取，所以必须挂 PhysicsRaycaster，
            // 否则 IPointerClickHandler / IBeginDragHandler 一个都不会触发
            var raycaster = cameraGo.AddComponent<PhysicsRaycaster>();
            raycaster.eventMask = camera.cullingMask;

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
            _previewStage = CardPreviewStage.Create(transform);
            _detailPanel = CardDetailPanel.Create(hudGo.transform, _previewStage);

            // 演出单开一座棚子：和详情面板共用的话，打牌时正好开着详情
            // 就会两边抢同一张 RenderTexture
            _showcase = CardShowcase.Create(hudGo.transform, CardPreviewStage.Create(transform));
            // 查看面板里的牌按「当前操作角色」的面板算公式
            _peekPanel.CardClickHandler = card => _detailPanel.Open(card, Session.SelectedPlayer);
        }

        /// <summary>
        /// 内容包加载失败时的整屏报告。只有一块可滚动的文本和两个按钮，
        /// 不建棋盘、不建相机——此时 <c>Session.State</c> 是 null，碰什么都会空引用。
        ///
        /// <para>报告里带着出错的文件与行号，玩家改坏自己的 JSON 之后照着改就行。</para>
        /// </summary>
        private void BuildLoadFailureScreen()
        {
            EnsureEventSystem();

            var hudGo = new GameObject("LoadFailureCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            hudGo.transform.SetParent(transform, false);

            var canvas = hudGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            var scaler = hudGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = layout.referenceResolution;

            Image backdrop = UiBuilder.CreateImage("Backdrop", hudGo.transform, new Color(0.06f, 0.05f, 0.08f, 1f));
            backdrop.rectTransform.anchorMin = Vector2.zero;
            backdrop.rectTransform.anchorMax = Vector2.one;
            backdrop.rectTransform.sizeDelta = Vector2.zero;

            Text title = UiBuilder.CreateText(
                "Title", backdrop.transform, "内容包加载失败，无法开局", 26,
                TextAnchor.UpperLeft, new Color(1f, 0.6f, 0.6f));
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -24f);
            title.rectTransform.sizeDelta = new Vector2(-48f, 40f);

            Text report = UiBuilder.CreateText(
                "Report", backdrop.transform, Session.Message, 15, TextAnchor.UpperLeft);
            report.rectTransform.anchorMin = new Vector2(0f, 0f);
            report.rectTransform.anchorMax = new Vector2(1f, 1f);
            report.rectTransform.offsetMin = new Vector2(24f, 64f);
            report.rectTransform.offsetMax = new Vector2(-24f, -72f);
            report.horizontalOverflow = HorizontalWrapMode.Wrap;
            report.verticalOverflow = VerticalWrapMode.Truncate;

            var bar = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bar.transform.SetParent(backdrop.transform, false);
            var barRect = (RectTransform)bar.transform;
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = new Vector2(0f, 14f);
            barRect.sizeDelta = new Vector2(-48f, 36f);

            var group = bar.GetComponent<HorizontalLayoutGroup>();
            group.spacing = 8;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;

            // 改完 JSON 不必重开游戏：重试会重新读一遍内容包
            UiBuilder.CreateButton("重试", bar.transform, 96f, RetryLoad);
            UiBuilder.CreateButton("退出", bar.transform, 96f, AppExit.Quit);
        }

        /// <summary>重新读内容包。成功就把这块报告拆掉、照常开局。</summary>
        private void RetryLoad()
        {
            Session.Restart();
            if (!Session.IsLoaded)
            {
                // 还是坏的：换一份报告继续显示
                Transform canvas = transform.Find("LoadFailureCanvas");
                if (canvas != null) Destroy(canvas.gameObject);
                BuildLoadFailureScreen();
                return;
            }

            Transform failure = transform.Find("LoadFailureCanvas");
            if (failure != null) Destroy(failure.gameObject);

            CardView.DefaultBackArtId = ContentScenario.LastLoaded?.DefaultCardBackArtId;
            CardArtLibrary.Fallbacks = ContentScenario.LastLoaded?.ArtFallbacks;
            CardArtLibrary.SearchRoots = ContentPaths.ResolveAll();

            BuildScene();
            Refresh();
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

            // 走位与升变**不在**顶栏：
            // 走位 = 把角色卡拖到对排 + 确认；升变 = 把升变牌拖到自己的角色卡上。
            // 两者都是棋盘上的动作，放进顶栏按钮会让它们和「重开」「退出」这类
            // 全局调试入口混在一起，玩家反而找不到。顶栏只留阶段推进与调试。

            UiBuilder.CreateButton("自动出牌", bar.transform, 86f, () => { Session.AutoPlay(); Refresh(); });
            UiBuilder.CreateButton("跳过本步", bar.transform, 86f, () => { Session.SkipPending(); Refresh(); });
            UiBuilder.CreateButton("自动打完", bar.transform, 86f, () => { Session.SkipToEnd(); Refresh(); });
            UiBuilder.CreateButton("重开", bar.transform, 64f, () => { Session.Restart(); Refresh(); });

            // 退出键（Esc）之外再给一个看得见的出口：构建产物是全屏的，
            // 万一键盘焦点出问题，没有可点的退出按钮就只能用任务管理器
            UiBuilder.CreateButton("退出", bar.transform, 64f, AppExit.Quit);
        }

        // ──────────────────────────── 刷新 ────────────────────────────

        // 读到哪儿了。引擎的事件表是只追加的，所以记一个游标就能拿到「这次刷新之间新发生的事」
        private BattleEngine _eventSource;
        private int _eventCursor;

        /// <summary>
        /// 把这次刷新之间新打出的牌送去演出。
        ///
        /// <para>状态走 <see cref="Refresh"/> 整体重建，**演出走事件**——
        /// 「现在手里有哪些牌」是状态，「刚才打出了什么」只有事件说得清：
        /// 等 Refresh 跑完，那张牌早就离开手牌进弃牌堆了。</para>
        ///
        /// <para>玩家出牌和敌人意图发的是同一条 <see cref="BattleEventType.CardPlayed"/>，
        /// 所以这里不必分两种情况。</para>
        /// </summary>
        private void DrainPlayedCards()
        {
            if (_showcase == null) return;

            // 重开一局会换掉引擎，游标得跟着归零，否则新一局的前几张牌会被当成「已经看过了」
            if (!ReferenceEquals(_eventSource, Session.Engine))
            {
                _eventSource = Session.Engine;
                _eventCursor = 0;
                _showcase.Clear();
            }

            IReadOnlyList<BattleEvent> events = Session.Engine.Events;
            for (; _eventCursor < events.Count; _eventCursor++)
            {
                BattleEvent e = events[_eventCursor];
                if (e.Type != BattleEventType.CardPlayed) continue;

                _showcase.Enqueue(e.Card, Session.State?.Find(e.ActorId)?.Name);
            }
        }

        public void Refresh()
        {
            BattleState state = Session.State;
            PendingDecision pending = Session.Engine.Pending;

            // 每次刷新都重新指过去：重开一局会换掉引擎，只在建面板时设一次就会留下陈旧引用
            _detailPanel.Engine = Session.Engine;

            DrainPlayedCards();

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

                // 正面朝上的牌显示持有者此刻的真实费用（含减免）；
                // 背面朝上的牌本来就不显示费用，不必白算一次
                string costText = zone.Layout.faceDown
                    ? null
                    : BattleSession.CostText(card.Definition, Session.Engine, Session.State.FindPlayer(card.OwnerId));

                view.Bind(card, zone, zone.Layout.faceDown, draggable, zone.SlotSize, costText);
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
                    view.ContextHandler = v => _detailPanel.Open(v.Combatant);
                    view.LongPressHandler = v => _detailPanel.Open(v.Combatant);
                    view.BoardPointResolver = ResolveBoardPoint;
                    view.BeginDragHandler = OnCombatantBeginDrag;
                    view.DragHandler = OnCombatantDrag;
                    view.EndDragHandler = OnCombatantEndDrag;
                    _combatantViews[combatant.Id] = view;
                    _dropTargets.Add(view);
                }

                bool selected = isEnemy
                    ? Session.SelectedEnemyId == combatant.Id
                    : Session.SelectedPlayerId == combatant.Id;

                // 只有自己人、而且此刻真的走得动，才拖得起来——
                // 拖得动却放不下去是最让人困惑的交互
                view.Draggable = !isEnemy
                                 && combatant is PlayerCharacter movable
                                 && CanMoveRow(movable);

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
        /// <summary>
        /// 轻点一下**不**做任何会改变局面的事。打出走拖拽，看详情走长按——
        /// 「点一下就把牌打出去」太容易误触，而误触在这种回合制里是不可撤销的。
        /// </summary>
        public void OnCardClick(CardView card)
        {
            if (card.Card == null || card.FaceDown) return;

            Session.SetMessage($"「{card.Card.Name}」：长按看详情，拖到目标上打出。");
            Refresh();
        }

        /// <summary>长按 = 看详情。和角色卡是同一个手势（见 <see cref="LongPress"/>）。</summary>
        public void OnCardLongPress(CardView card)
        {
            if (card.Card == null) return;

            // 背面朝上的牌是暗信息：卡组、以及横置在角色卡上的充能牌。
            // 能点开看等于白给对手情报，也违背实体打牌的常识。
            if (card.FaceDown)
            {
                Session.SetMessage(
                    card.Zone != null && card.Zone.Layout.source == ZoneSource.充能区
                        ? "充能指示物是背面朝下的，看不到是哪张牌。"
                        : "卡组是背面朝上的暗信息，不能查看。",
                    isWarning: true);
                Refresh();
                return;
            }

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
            bool transformWindow = Session.Engine.Pending.Kind == DecisionKind.TransformDeclaration;

            // 升变牌和别的牌一样是从手里打出去的——拖到**自己**的角色卡上，
            // 因为升变牌就是这一局里的新角色卡（§6.2 ②）
            if (def.Kind == CardKind.Transform)
            {
                if (!transformWindow) return false;

                PlayerCharacter owner = ResolvePlayer(card.Zone.Layout.playerIndex);
                if (owner == null || !ReferenceEquals(view.Combatant, owner)) return false;

                // 条件没达成的升变牌直接标成不可放置，而不是拖过去才被引擎一句话挡回来
                return Session.Engine.TransformCandidates(owner).Any(c => c.Uid == card.Card.Uid);
            }

            // 升变窗口里只能发动升变，别的牌直接标成不可放置，
            // 省得玩家拖过去才被一句报错挡回来
            if (transformWindow) return false;

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

            bool transform = card.Card.Definition.Kind == CardKind.Transform;

            PlayCardWithChoices(
                player, card.Card, view.Combatant.Id,
                transform
                    ? $"{player.Name} 发动升变：{card.Card.Name}。"
                    : $"打出「{card.Card.Name}」→ {view.Combatant.Name}。");
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

            PlayCardWithChoices(owner, card, target, $"打出「{card.Name}」。");
        }

        // ──────────────────────────── 出牌前的玩家选择 ────────────────────────────

        /// <summary>
        /// 打出一张牌。卡面要玩家做选择的（二选一、选元素、从牌堆里挑牌），
        /// 先把问题一个个问完，再带着答案提交。
        ///
        /// <para>这里是**唯一**的出牌入口，右键菜单与拖拽都走它——
        /// 两条路各写一份的话，补一张要选择的卡就得记得改两处，迟早漏掉一处，
        /// 表现出来就是「拖着打能选、右键打不能选」这种说不清的差异。</para>
        /// </summary>
        private void PlayCardWithChoices(PlayerCharacter owner, CardInstance card, string targetId, string message)
        {
            IReadOnlyList<ChoicePrompt> prompts = CardChoiceCatalog.For(card.Definition.Id);
            if (prompts.Count == 0)
            {
                SubmitPlay(owner, card, targetId, message, null, null);
                return;
            }

            AskPrompt(owner, card, targetId, message, prompts, 0,
                new Dictionary<string, int>(), new List<int>());
        }

        /// <summary>按顺序问第 <paramref name="index"/> 个问题；问完就提交。</summary>
        private void AskPrompt(
            PlayerCharacter owner,
            CardInstance card,
            string targetId,
            string message,
            IReadOnlyList<ChoicePrompt> prompts,
            int index,
            Dictionary<string, int> answers,
            List<int> pickedUids)
        {
            // 条件不成立的问题直接跳过（「没选涂油就不问是哪个元素」）
            while (index < prompts.Count && prompts[index].When != null && !prompts[index].When(answers))
            {
                index++;
            }

            if (index >= prompts.Count)
            {
                SubmitPlay(owner, card, targetId, message, answers, pickedUids);
                return;
            }

            ChoicePrompt prompt = prompts[index];

            void Next() => AskPrompt(owner, card, targetId, message, prompts, index + 1, answers, pickedUids);
            void Cancel()
            {
                Session.SetMessage($"已取消打出「{card.Name}」。");
                Refresh();
            }

            if (prompt.IsPick)
            {
                AskCardPick(owner, card, prompt, pickedUids, Next, Cancel);
                return;
            }

            var options = new List<(string, System.Action)>();
            foreach ((string label, int value) in prompt.Options)
            {
                if (prompt.OptionAvailable != null && !prompt.OptionAvailable(owner, value)) continue;

                int captured = value;
                options.Add((label, () =>
                {
                    answers[prompt.Key] = captured;
                    Next();
                }));
            }

            // 一个选项都没有（条件全不满足）就别弹空菜单，交给引擎/脚本的默认值
            if (options.Count == 0)
            {
                Next();
                return;
            }

            // 菜单弹在屏幕中部偏上：拖拽出牌时鼠标停在角色卡上，
            // 跟着鼠标弹会把刚放下的那张牌挡住
            _contextMenu.Open(
                prompt.Title,
                new Vector2(Screen.width * 0.5f - 90f, Screen.height * 0.6f),
                options,
                Cancel);
        }

        /// <summary>从手牌 / 弃牌堆里挑牌。</summary>
        private void AskCardPick(
            PlayerCharacter owner,
            CardInstance card,
            ChoicePrompt prompt,
            List<int> pickedUids,
            System.Action next,
            System.Action cancel)
        {
            List<CardInstance> candidates = prompt.PickFrom switch
            {
                ChoiceSource.手牌 => owner.Zones.Hand.ToList(),
                ChoiceSource.弃牌堆 => owner.Zones.Discard.ToList(),
                _ => new List<CardInstance>(),
            };

            // 正在打出的这张牌自己不在候选里——它马上就要离开手牌了，
            // 让玩家把它选成「要贴成充能的那张」只会得到一个说不清的结果
            candidates.RemoveAll(c => c.Uid == card.Uid);

            if (candidates.Count == 0)
            {
                // 没得选不等于打不出（「最多2张」允许一张都不贴），
                // 空不空由卡面自己处理，界面只是没什么可问的
                next();
                return;
            }

            string source = prompt.PickFrom == ChoiceSource.手牌 ? "手牌" : "弃牌堆";
            _peekPanel.OpenForSelection(
                $"{card.Name}　{prompt.Title}（{owner.Name} 的{source}）",
                candidates,
                prompt.MinPick,
                prompt.MaxPick,
                picked =>
                {
                    pickedUids.Clear();
                    foreach (CardInstance c in picked) pickedUids.Add(c.Uid);
                    next();
                },
                cancel);
        }

        private void SubmitPlay(
            PlayerCharacter owner,
            CardInstance card,
            string targetId,
            string message,
            Dictionary<string, int> answers,
            List<int> pickedUids)
        {
            // 「为卡面词条付充能」目前只有 ChargeForMp 这一个入口，
            // 而它的本意是「拿充能抵 MP」——这条链是错的（见 ui_todo_handoff §2）。
            // 在牌效那边把入口理清之前，先照着它走：调合·万能灵药的 MP 费用是 0，
            // 所以借道抵扣不会产生副作用，而 ctx.charge_spent 能拿到正确的数字。
            int chargeForMp = answers != null && answers.TryGetValue("charge", out int charges) ? charges : 0;

            var action = new PlayCardAction(owner.Id, card.Uid, targetId, chargeForMp);

            if (answers != null)
            {
                foreach (KeyValuePair<string, int> answer in answers) action.With(answer.Key, answer.Value);
            }

            if (pickedUids != null && pickedUids.Count > 0) action.WithMany("card", pickedUids);

            Session.Submit(action, message);
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

        // ──────────────────────────── 拖拽走位 ────────────────────────────

        /// <summary>此刻这名角色走不走得动（时机 + 代价都算上）。</summary>
        private bool CanMoveRow(PlayerCharacter player)
        {
            PendingDecision pending = Session.Engine.Pending;
            bool free = player.FreeMoves > 0;

            // 攒着的免费移动在响应窗口里也能兑现（「翻滚披风」那次机动就是为了赶在敌人落手前挪开）
            bool timingOk = pending.Kind == DecisionKind.PlayerActions
                            || (free && pending.Kind == DecisionKind.EnemyResponse);

            return timingOk
                   && pending.WaitingOn.Contains(player.Id)
                   && !player.IsDown
                   && Session.State.Outcome == BattleOutcome.Ongoing
                   && (free || (!player.HasMovedThisRound && player.Ap >= 1));
        }

        /// <summary>把屏幕坐标投到桌面平面上，供角色卡跟着鼠标走。</summary>
        private Vector3? ResolveBoardPoint(CombatantView view, PointerEventData eventData)
        {
            if (BoardCamera == null || BoardRoot == null) return null;

            Vector3 normal = BoardRoot.forward;
            Vector3 origin = BoardRoot.position
                             + normal * CardMeshFactory.TowardCamera
                             * (CardView.DragLift * BoardRoot.lossyScale.z);

            var plane = new Plane(normal, origin);
            Ray ray = BoardCamera.ScreenPointToRay(eventData.position);
            return plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : null;
        }

        private void OnCombatantBeginDrag(CombatantView view)
        {
            _contextMenu.Close();

            // 拖起来的角色卡挂到桌面根节点下，否则它还受所在区域的排布摆布
            view.Root.SetParent(BoardRoot, true);
            HighlightMoveTarget(view, CardHighlight.合法);
        }

        private void OnCombatantDrag(CombatantView view, PointerEventData eventData)
        {
            ZoneView target = FindRowZoneUnder(eventData);
            HighlightMoveTarget(view, IsOppositeRowZone(view, target) ? CardHighlight.悬停 : CardHighlight.合法);
        }

        private void OnCombatantEndDrag(CombatantView view, PointerEventData eventData)
        {
            HighlightMoveTarget(view, CardHighlight.正常);

            ZoneView target = FindRowZoneUnder(eventData);
            bool moved = IsOppositeRowZone(view, target);

            // 无论确认与否都先 Refresh：角色卡现在挂在桌面根节点下，
            // 不放回去的话它会一直飘在鼠标松开的位置
            Refresh();

            if (!moved || view.Combatant is not PlayerCharacter player) return;

            string destination = player.Row == Row.Front ? "后排" : "前排";
            string cost = player.FreeMoves > 0 ? $"免费（剩 {player.FreeMoves} 次）" : "消耗 1 AP";

            _confirmDialog.Open($"把 {player.Name} 移动到{destination}？\n{cost}", () =>
            {
                Session.Submit(new MoveRowAction(player.Id), $"{player.Name} 移动到{destination}。");
                Refresh();
            });
        }

        private void HighlightMoveTarget(CombatantView view, CardHighlight state)
        {
            if (view.Combatant is not PlayerCharacter player) return;

            Row target = player.Row == Row.Front ? Row.Back : Row.Front;
            foreach (ZoneView zone in _zones)
            {
                if (RowZoneOf(zone) == target && IsOwnSideZone(zone)) zone.SetDropHighlight(state);
            }
        }

        private bool IsOppositeRowZone(CombatantView view, ZoneView zone)
        {
            if (zone == null || view.Combatant is not PlayerCharacter player) return false;
            if (!IsOwnSideZone(zone)) return false;

            Row target = player.Row == Row.Front ? Row.Back : Row.Front;
            return RowZoneOf(zone) == target;
        }

        private static bool IsOwnSideZone(ZoneView zone)
            => zone.Layout.source is ZoneSource.我方前排 or ZoneSource.我方后排;

        private static Row? RowZoneOf(ZoneView zone) => zone.Layout.source switch
        {
            ZoneSource.我方前排 => Row.Front,
            ZoneSource.我方后排 => Row.Back,
            _ => null,
        };

        /// <summary>射线找鼠标下方的前/后排区域底板。</summary>
        private ZoneView FindRowZoneUnder(PointerEventData eventData)
        {
            if (BoardCamera == null) return null;

            Ray ray = BoardCamera.ScreenPointToRay(eventData.position);
            int count = Physics.RaycastNonAlloc(ray, _hits, 1000f);
            if (count == 0) return null;

            System.Array.Sort(_hits, 0, count, RaycastDistanceComparer.Instance);

            for (int i = 0; i < count; i++)
            {
                var zone = _hits[i].collider.transform.GetComponentInParent<ZoneView>();
                if (zone != null && RowZoneOf(zone).HasValue) return zone;
            }

            return null;
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
