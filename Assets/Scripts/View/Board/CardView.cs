using TCGinFU.Core;
using TCGinFU.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TCGinFU.View
{
    public enum CardHighlight
    {
        正常,
        悬停,
        拖拽中,
        /// <summary>已被选中、正在等后续操作（例如选完「贴充能」等着点目标）</summary>
        选中,
        /// <summary>可以放在这里 / 是个合法目标</summary>
        合法,
        /// <summary>放不了</summary>
        非法,
    }

    /// <summary>拖拽过程中需要外部提供的东西。<see cref="BattleView"/> 实现它。</summary>
    public interface ICardDragContext
    {
        /// <summary>看桌面的那台透视相机。</summary>
        Camera BoardCamera { get; }

        /// <summary>桌面根节点，卡牌拖动时贴着它的平面走。</summary>
        Transform BoardRoot { get; }

        void OnCardBeginDrag(CardView card);
        void OnCardDrag(CardView card, PointerEventData eventData);
        void OnCardEndDrag(CardView card, PointerEventData eventData);
        void OnCardHover(CardView card, bool entered);
        void OnCardClick(CardView card);

        /// <summary>右键卡牌：弹出「打出 / 贴充能」之类的操作菜单。</summary>
        void OnCardContextMenu(CardView card, Vector2 screenPosition);

        /// <summary>左键长按卡牌：看详情。</summary>
        void OnCardLongPress(CardView card);
    }

    /// <summary>
    /// 一张 3D 卡牌。真的有厚度——卡身是个扁盒子，卡面是贴在正面的片，
    /// 所以牌堆叠起来能直接看出有多少张。
    /// <para>
    /// 事件走 Unity 的 <c>PhysicsRaycaster</c> + <see cref="BoxCollider"/>，
    /// 所以 IBeginDragHandler / IPointerClickHandler 这些接口和做 uGUI 时完全一样。
    /// </para>
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class CardView : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerDownHandler, IPointerUpHandler,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        /// <summary>拖起来时离开桌面的高度（设计像素）。</summary>
        /// <summary>拖拽时离开桌面的高度（设计像素）。角色卡走位时也用同一个高度，看着才是一套动作。</summary>
        public const float DragLift = 46f;

        private Transform _body;
        private Transform _faceQuad;
        private Transform _highlight;
        private CardFace _face;
        private Text _nameText;
        private Text _costText;
        private Text _typeText;
        private BoxCollider _collider;
        private ICardDragContext _context;

        private Transform _homeParent;
        private int _homeIndex;

        public Transform Root { get; private set; }
        public CardInstance Card { get; private set; }
        public ZoneView Zone { get; private set; }
        public bool Draggable { get; private set; }
        public bool FaceDown { get; private set; }
        public bool IsDragging { get; private set; }

        /// <summary>当前卡牌尺寸（设计像素）。</summary>
        public Vector2 SizePx { get; private set; }

        public static CardView Create(Transform parent, ICardDragContext context)
        {
            var go = new GameObject("Card", typeof(BoxCollider));
            go.transform.SetParent(parent, false);

            var view = go.AddComponent<CardView>();
            view.Root = go.transform;
            view._context = context;
            view._collider = go.GetComponent<BoxCollider>();

            // 卡身：深色扁盒子，露出来的侧面就是厚度
            view._body = CardMeshFactory.CreateMeshObject(
                "Body", go.transform, CardMeshFactory.Box, new Color(0.08f, 0.09f, 0.12f, 1f));

            // 卡面：贴在正面、略小一圈的片，于是四周留出一圈深色描边
            view._faceQuad = CardMeshFactory.CreateMeshObject(
                "FaceQuad", go.transform, CardMeshFactory.Quad, Color.gray);

            // 高亮：再往前一点的片，平时透明
            view._highlight = CardMeshFactory.CreateMeshObject(
                "Highlight", go.transform, CardMeshFactory.Quad, Color.clear);

            view._face = CardFace.Create(go.transform, Vector2.one, 0f);
            view._nameText = view._face.AddText(
                "Name", "", 15, TextAnchor.UpperCenter, new Vector2(0f, 0.56f), new Vector2(1f, 1f));
            view._costText = view._face.AddText(
                "Cost", "", 13, TextAnchor.MiddleCenter, new Vector2(0f, 0.3f), new Vector2(1f, 0.56f));
            view._typeText = view._face.AddText(
                "Type", "", 12, TextAnchor.LowerCenter, new Vector2(0f, 0f), new Vector2(1f, 0.3f));

            return view;
        }

        /// <param name="costText">
        /// 覆盖费用文字。手牌要传此刻的**真实**费用——「先声夺人」卡面印 5 MP，
        /// 第 1 轮打出却是 0 MP，只显示卡面值玩家会以为 MP 不够而不敢打。
        /// </param>
        public void Bind(
            CardInstance card, ZoneView zone, bool faceDown, bool draggable, Vector2 sizePx,
            string costText = null)
        {
            Card = card;
            Zone = zone;
            Draggable = draggable;

            BindDefinition(card.Definition, faceDown, sizePx, costText);
        }

        /// <summary>
        /// 只按卡面定义摆一张卡，不需要 <see cref="CardInstance"/>，也不进任何区域。
        /// <para>
        /// 离屏预览（<see cref="CardPreviewStage"/>）和详情面板用它：手上往往只有一个
        /// <see cref="CardDefinition"/>，为了画张图去造一个 <c>CardInstance</c> 会白占一个全局 Uid。
        /// </para>
        /// </summary>
        public void BindDefinition(
            CardDefinition def, bool faceDown, Vector2 sizePx, string costText = null)
        {
            FaceDown = faceDown;

            if (SizePx != sizePx)
            {
                SizePx = sizePx;
                ApplySize();
            }

            Color faceColor = faceDown
                ? new Color(0.19f, 0.23f, 0.37f, 1f)
                : ColorOf(def.Category);

            // 先找专属卡图，再按类型兜底，都没有才退回占位色块
            Texture2D art = faceDown
                ? CardArtLibrary.ResolveWithFallback(BackArtId(def), FallbackBack)
                : CardArtLibrary.ResolveWithFallback(def.ArtId, FallbackKeyOf(def));

            CardMeshFactory.SetTexture(_faceQuad, art, faceColor);

            if (faceDown)
            {
                _face.SetVisible(false);
            }
            else
            {
                _face.SetVisible(true);
                _nameText.text = def.Name;
                _costText.text = costText ?? BattleSession.CostText(def);
                _typeText.text = $"{Short(def.Category)}·{Short(def.Kind)}";
            }

            SetHighlight(CardHighlight.正常);
        }

        private void ApplySize()
        {
            float t = CardMeshFactory.CardThickness;
            float front = CardMeshFactory.TowardCamera; // 朝镜头的方向

            _body.localScale = new Vector3(SizePx.x, SizePx.y, t);
            _body.localPosition = Vector3.zero;

            // 卡面贴在朝镜头的那一侧，挪出半个厚度再加一点避免 z-fighting
            _faceQuad.localScale = new Vector3(SizePx.x * 0.94f, SizePx.y * 0.94f, 1f);
            _faceQuad.localPosition = new Vector3(0f, 0f, front * (t * 0.5f + 0.05f));

            // 高亮比卡大一圈、压在卡的背面，于是只露出四周一圈描边
            _highlight.localScale = new Vector3(SizePx.x * 1.06f, SizePx.y * 1.06f, 1f);
            _highlight.localPosition = new Vector3(0f, 0f, -front * (t * 0.5f + 0.05f));

            _face.Root.sizeDelta = SizePx;
            _face.Root.localPosition = new Vector3(0f, 0f, front * (t * 0.5f + 0.15f));

            _collider.size = new Vector3(SizePx.x, SizePx.y, t);
            _collider.center = Vector3.zero;
        }

        public void SetHighlight(CardHighlight state)
        {
            Color color = state switch
            {
                CardHighlight.悬停 => new Color(1f, 1f, 1f, 0.9f),
                CardHighlight.拖拽中 => new Color(1f, 0.95f, 0.5f, 1f),
                CardHighlight.选中 => new Color(1f, 0.82f, 0.2f, 1f),
                CardHighlight.合法 => new Color(0.4f, 1f, 0.5f, 1f),
                CardHighlight.非法 => new Color(1f, 0.35f, 0.35f, 1f),
                _ => Color.clear,
            };

            CardMeshFactory.SetColor(_highlight, color);
            _highlight.gameObject.SetActive(color.a > 0.01f);
        }

        // ──────────────────────────── 拖拽 ────────────────────────────

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!Draggable || _context == null) return;

            // 拖拽优先：手势变成拖，长按就作废，否则拖久一点会弹出详情挡住视线
            _longPress.Cancel();
            IsDragging = true;
            _homeParent = Root.parent;
            _homeIndex = Root.GetSiblingIndex();

            // 拖起来的牌挂到桌面根节点下，并关掉自身碰撞体，
            // 否则射线会一直打中它自己，永远找不到下面的放置目标
            Root.SetParent(_context.BoardRoot, true);
            _collider.enabled = false;

            SetHighlight(CardHighlight.拖拽中);
            _context.OnCardBeginDrag(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsDragging) return;

            if (TryGetBoardPoint(eventData, DragLift, out Vector3 point))
            {
                Root.position = point;
                Root.rotation = _context.BoardRoot.rotation;
            }

            _context.OnCardDrag(this, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!IsDragging) return;

            IsDragging = false;
            _collider.enabled = true;
            SetHighlight(CardHighlight.正常);
            _context.OnCardEndDrag(this, eventData);
        }

        /// <summary>把屏幕坐标投到桌面平面上（抬高 <paramref name="liftPx"/> 像素）。</summary>
        public bool TryGetBoardPoint(PointerEventData eventData, float liftPx, out Vector3 point)
        {
            point = default;

            Camera camera = _context.BoardCamera;
            Transform board = _context.BoardRoot;
            if (camera == null || board == null) return false;

            // 桌面的 forward 是**背离**镜头的（见 CardMeshFactory.TowardCamera），
            // 所以「抬起来」要往它的反方向走。写成 +forward 的话卡会被抬到桌面背后、
            // 被不透明的桌布整个挡住——表现就是「拖动时卡片消失」。
            Vector3 normal = board.forward;
            Vector3 toCamera = normal * CardMeshFactory.TowardCamera;

            // lift 是设计像素，乘根节点缩放换算到世界单位
            Vector3 origin = board.position + toCamera * (liftPx * board.lossyScale.z);

            var plane = new Plane(normal, origin);
            Ray ray = camera.ScreenPointToRay(eventData.position);
            if (!plane.Raycast(ray, out float distance)) return false;

            point = ray.GetPoint(distance);
            return true;
        }

        /// <summary>没被接收时退回原位。</summary>
        public void ReturnHome()
        {
            if (_homeParent == null) return;

            Root.SetParent(_homeParent, false);
            Root.SetSiblingIndex(_homeIndex);
            Zone?.Arrange();
        }

        public void OnPointerEnter(PointerEventData eventData) => _context?.OnCardHover(this, true);

        public void OnPointerExit(PointerEventData eventData) => _context?.OnCardHover(this, false);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (IsDragging) return;

            // 长按已经弹过详情了，这一下抬手不该再当成一次点击
            if (_longPress.Consumed) return;

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                _context?.OnCardContextMenu(this, eventData.position);
                return;
            }

            _context?.OnCardClick(this);
        }

        // ──────────────────────────── 长按看详情 ────────────────────────────

        private readonly LongPress _longPress = new();

        public void OnPointerDown(PointerEventData eventData) => _longPress.OnPointerDown(eventData);

        public void OnPointerUp(PointerEventData eventData) => _longPress.OnPointerUp();

        private void Awake()
            => _longPress.Triggered = _ => _context?.OnCardLongPress(this);

        private void Update() => _longPress.Tick();

        /// <summary>
        /// 内容包的默认卡背。整局只有一套牌背，所以放成静态的，
        /// 由 <c>BattleView</c> 在建视图时从 <c>ContentDatabase</c> 填过来。
        /// </summary>
        public static string DefaultBackArtId { get; set; }

        private static string BackArtId(CardDefinition def)
            => string.IsNullOrEmpty(def.BackArtId) ? DefaultBackArtId : def.BackArtId;

        /// <summary>卡背的兜底键。</summary>
        public const string FallbackBack = "back";

        /// <summary>
        /// 没有专属卡图时按**卡的类型**兜底。
        /// <para>分类刻意粗——兜底图的作用只是「让这张牌看起来像张牌」，
        /// 不是表达卡义。真卡图一到位（<c>art: cards/&lt;卡id&gt;</c>）就会盖过它。</para>
        /// </summary>
        public static string FallbackKeyOf(CardDefinition def) => def.Kind switch
        {
            CardKind.Transform => "transform",
            _ when def.Category == CardCategory.Armor => "guard",
            _ when def.DealsDamage => "attack",
            _ => "spell",
        };

        // ──────────────────────────── 配色与文案 ────────────────────────────

        public static Color ColorOf(CardCategory category) => category switch
        {
            CardCategory.Class => new Color(0.26f, 0.32f, 0.47f, 1f),
            CardCategory.Weapon => new Color(0.46f, 0.28f, 0.24f, 1f),
            CardCategory.Armor => new Color(0.24f, 0.40f, 0.34f, 1f),
            CardCategory.Treasure => new Color(0.43f, 0.36f, 0.19f, 1f),
            _ => new Color(0.3f, 0.3f, 0.3f, 1f),
        };

        public static string Short(CardCategory category) => category switch
        {
            CardCategory.Class => "职业",
            CardCategory.Weapon => "武器",
            CardCategory.Armor => "防具",
            CardCategory.Treasure => "秘宝",
            _ => "?",
        };

        public static string Short(CardKind kind) => kind switch
        {
            CardKind.Skill => "技能",
            CardKind.Timing => "时机",
            CardKind.Continuous => "持续",
            CardKind.Transform => "升变",
            CardKind.Form => "形态",
            CardKind.Hero => "英雄",
            _ => "?",
        };
    }
}
