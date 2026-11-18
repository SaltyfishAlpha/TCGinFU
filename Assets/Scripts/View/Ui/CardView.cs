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
        /// <summary>可以放在这里</summary>
        合法,
        /// <summary>放不了</summary>
        非法,
    }

    /// <summary>拖拽过程中需要外部提供的东西。<see cref="BattleView"/> 实现它。</summary>
    public interface ICardDragContext
    {
        Canvas Canvas { get; }
        RectTransform DragLayer { get; }

        void OnCardBeginDrag(CardView card);
        void OnCardDrag(CardView card, PointerEventData eventData);
        void OnCardEndDrag(CardView card, PointerEventData eventData);
        void OnCardHover(CardView card, bool entered);
        void OnCardClick(CardView card);
    }

    /// <summary>
    /// 一张牌的视图。整张牌由代码拼出来（底板 + 三行文字 + 高亮边框），
    /// 不用预制体，所以改卡面长相只要改这一个文件。
    /// </summary>
    public sealed class CardView : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private Image _background;
        private Image _highlight;
        private Text _nameText;
        private Text _costText;
        private Text _typeText;
        private ICardDragContext _context;

        public RectTransform Rect { get; private set; }
        public CardInstance Card { get; private set; }
        public ZoneView Zone { get; private set; }
        public bool Draggable { get; private set; }
        public bool FaceDown { get; private set; }
        public bool IsDragging { get; private set; }

        /// <summary>拖拽开始前记录的归位信息，放不下时原样退回。</summary>
        private Transform _homeParent;
        private int _homeIndex;

        public static CardView Create(Transform parent, ICardDragContext context)
        {
            RectTransform rect = UiBuilder.CreateRect("Card", parent);
            var view = rect.gameObject.AddComponent<CardView>();
            view.Rect = rect;
            view._context = context;

            view._background = UiBuilder.CreateImage("Bg", rect, Color.white);
            view._background.rectTransform.Stretch();

            view._highlight = UiBuilder.CreateImage("Highlight", rect, Color.clear, raycast: false);
            view._highlight.rectTransform.Stretch(-3f);
            view._highlight.transform.SetAsFirstSibling();

            view._nameText = UiBuilder.CreateText("Name", rect, "", 15, TextAnchor.UpperCenter);
            view._nameText.rectTransform.anchorMin = new Vector2(0f, 0.55f);
            view._nameText.rectTransform.anchorMax = new Vector2(1f, 1f);
            view._nameText.rectTransform.offsetMin = new Vector2(4f, 0f);
            view._nameText.rectTransform.offsetMax = new Vector2(-4f, -6f);

            view._costText = UiBuilder.CreateText("Cost", rect, "", 13, TextAnchor.MiddleCenter);
            view._costText.rectTransform.anchorMin = new Vector2(0f, 0.28f);
            view._costText.rectTransform.anchorMax = new Vector2(1f, 0.55f);
            view._costText.rectTransform.offsetMin = Vector2.zero;
            view._costText.rectTransform.offsetMax = Vector2.zero;

            view._typeText = UiBuilder.CreateText("Type", rect, "", 12, TextAnchor.LowerCenter);
            view._typeText.rectTransform.anchorMin = Vector2.zero;
            view._typeText.rectTransform.anchorMax = new Vector2(1f, 0.28f);
            view._typeText.rectTransform.offsetMin = new Vector2(2f, 4f);
            view._typeText.rectTransform.offsetMax = new Vector2(-2f, 0f);

            return view;
        }

        public void Bind(CardInstance card, ZoneView zone, bool faceDown, bool draggable, Vector2 size)
        {
            Card = card;
            Zone = zone;
            FaceDown = faceDown;
            Draggable = draggable;
            Rect.sizeDelta = size;

            if (faceDown)
            {
                _background.color = new Color(0.2f, 0.24f, 0.38f, 1f);
                _nameText.text = "";
                _costText.text = "";
                _typeText.text = "";
                SetHighlight(CardHighlight.正常);
                return;
            }

            CardDefinition def = card.Definition;
            _background.color = ColorOf(def.Category);
            _nameText.text = def.Name;
            _costText.text = BattleSession.CostText(def);
            _typeText.text = $"{Short(def.Category)}·{Short(def.Kind)}";
            SetHighlight(CardHighlight.正常);
        }

        public void SetHighlight(CardHighlight state)
        {
            _highlight.color = state switch
            {
                CardHighlight.悬停 => new Color(1f, 1f, 1f, 0.55f),
                CardHighlight.拖拽中 => new Color(1f, 0.95f, 0.5f, 0.8f),
                CardHighlight.合法 => new Color(0.4f, 1f, 0.5f, 0.85f),
                CardHighlight.非法 => new Color(1f, 0.35f, 0.35f, 0.85f),
                _ => Color.clear,
            };
        }

        // ──────────────────────────── 拖拽 ────────────────────────────

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!Draggable || _context == null) return;

            IsDragging = true;
            _homeParent = Rect.parent;
            _homeIndex = Rect.GetSiblingIndex();

            Rect.SetParent(_context.DragLayer, true);
            Rect.localRotation = Quaternion.identity;
            SetHighlight(CardHighlight.拖拽中);
            _context.OnCardBeginDrag(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsDragging) return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _context.DragLayer, eventData.position,
                    _context.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _context.Canvas.worldCamera,
                    out Vector2 local))
            {
                Rect.localPosition = local;
            }

            _context.OnCardDrag(this, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!IsDragging) return;

            IsDragging = false;
            SetHighlight(CardHighlight.正常);
            _context.OnCardEndDrag(this, eventData);
        }

        /// <summary>没被接收时退回原位。</summary>
        public void ReturnHome()
        {
            if (_homeParent == null) return;
            Rect.SetParent(_homeParent, false);
            Rect.SetSiblingIndex(_homeIndex);
            Zone?.Arrange();
        }

        public void OnPointerEnter(PointerEventData eventData) => _context?.OnCardHover(this, true);

        public void OnPointerExit(PointerEventData eventData) => _context?.OnCardHover(this, false);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (IsDragging) return;
            _context?.OnCardClick(this);
        }

        // ──────────────────────────── 配色与文案 ────────────────────────────

        private static Color ColorOf(CardCategory category) => category switch
        {
            CardCategory.Class => new Color(0.26f, 0.32f, 0.47f, 1f),
            CardCategory.Weapon => new Color(0.46f, 0.28f, 0.24f, 1f),
            CardCategory.Armor => new Color(0.24f, 0.40f, 0.34f, 1f),
            CardCategory.Treasure => new Color(0.43f, 0.36f, 0.19f, 1f),
            _ => new Color(0.3f, 0.3f, 0.3f, 1f),
        };

        private static string Short(CardCategory category) => category switch
        {
            CardCategory.Class => "职业",
            CardCategory.Weapon => "武器",
            CardCategory.Armor => "防具",
            CardCategory.Treasure => "秘宝",
            _ => "?",
        };

        private static string Short(CardKind kind) => kind switch
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
