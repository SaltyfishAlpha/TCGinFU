using System.Collections.Generic;
using TCGinFU.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TCGinFU.View
{
    /// <summary>
    /// 查看面板里的一张卡。棋盘上的卡是 3D 网格，但这个面板挂在屏幕空间 HUD 上，
    /// 所以单独做一个平面版本——翻看牌堆不需要厚度。
    /// </summary>
    public sealed class PeekCardView : MonoBehaviour, IPointerClickHandler
    {
        private Image _background;
        private Text _nameText;
        private Text _costText;
        private Text _typeText;
        private GameObject _selectedMark;
        private Text _orderText;

        public RectTransform Rect { get; private set; }
        public CardInstance Card { get; private set; }

        /// <summary>点一张牌看详情，和棋盘上的左键一致。</summary>
        public System.Action<CardInstance> ClickHandler { get; set; }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Card != null) ClickHandler?.Invoke(Card);
        }

        public static PeekCardView Create(Transform parent)
        {
            RectTransform rect = UiBuilder.CreateRect("PeekCard", parent);
            var view = rect.gameObject.AddComponent<PeekCardView>();
            view.Rect = rect;

            view._background = UiBuilder.CreateImage("Bg", rect, Color.gray);
            view._background.rectTransform.Stretch();

            view._nameText = UiBuilder.CreateText("Name", rect, "", 14, TextAnchor.UpperCenter);
            view._nameText.rectTransform.anchorMin = new Vector2(0f, 0.56f);
            view._nameText.rectTransform.anchorMax = Vector2.one;
            view._nameText.rectTransform.offsetMin = new Vector2(4f, 0f);
            view._nameText.rectTransform.offsetMax = new Vector2(-4f, -4f);

            view._costText = UiBuilder.CreateText("Cost", rect, "", 12, TextAnchor.MiddleCenter);
            view._costText.rectTransform.anchorMin = new Vector2(0f, 0.3f);
            view._costText.rectTransform.anchorMax = new Vector2(1f, 0.56f);
            view._costText.rectTransform.offsetMin = Vector2.zero;
            view._costText.rectTransform.offsetMax = Vector2.zero;

            view._typeText = UiBuilder.CreateText("Type", rect, "", 11, TextAnchor.LowerCenter);
            view._typeText.rectTransform.anchorMin = Vector2.zero;
            view._typeText.rectTransform.anchorMax = new Vector2(1f, 0.3f);
            view._typeText.rectTransform.offsetMin = new Vector2(2f, 4f);
            view._typeText.rectTransform.offsetMax = new Vector2(-2f, 0f);

            // 和棋盘上的卡一样描边：底下铺了插画之后，白字在浅色图上会糊掉
            foreach (Text text in new[] { view._nameText, view._costText, view._typeText })
            {
                var outline = text.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(1.2f, -1.2f);
            }

            // 选中标记：一圈亮绿描边 + 右上角的序号。
            // 只画描边的话，「最多选2张」时看不出先选的是哪张——
            // 多选时顺序会影响玩家的判断（比如想换掉先选的那张）
            RectTransform mark = UiBuilder.CreateRect("Selected", rect);
            mark.Stretch();
            UiBuilder.CreateBorder(mark, new Color(0.49f, 1f, 0.49f, 1f), 3f);
            view._orderText = UiBuilder.CreateText("Order", mark, "", 16, TextAnchor.UpperRight);
            view._orderText.rectTransform.Stretch(4f);
            view._selectedMark = mark.gameObject;
            view._selectedMark.SetActive(false);

            return view;
        }

        /// <summary><paramref name="order"/> 从 1 起；0 表示未选中。</summary>
        public void SetSelected(int order)
        {
            if (_selectedMark == null) return;
            _selectedMark.SetActive(order > 0);
            _orderText.text = order > 0 ? order.ToString() : "";
        }

        public void Bind(CardInstance card, Vector2 size)
        {
            Card = card;
            CardDefinition def = card.Definition;
            Rect.sizeDelta = size;

            // 摊开看的牌也要有卡图——此前这里只画了来源色块，
            // 于是棋盘上有图、点开弃牌堆全是纯色，看着像两套不同的牌
            _background.sprite = CardArtLibrary.ResolveSprite(def.ArtId, CardView.FallbackKeyOf(def));
            _background.color = _background.sprite != null ? Color.white : CardView.ColorOf(def.Category);
            _background.preserveAspect = false;

            _nameText.text = def.Name;
            _costText.text = Presentation.BattleSession.CostText(def);
            _typeText.text = CardView.TypeLine(def);
        }
    }

    /// <summary>
    /// 牌堆查看面板：点弃牌堆 / 持续区 / 除外区这类叠放区域时弹出，
    /// 把里面的牌全部摊开。实体打牌时这些都是可以随时翻看的公开信息。
    /// </summary>
    public sealed class ZonePeekPanel : MonoBehaviour, IPointerClickHandler
    {
        private const int Columns = 6;

        private readonly List<PeekCardView> _cards = new();
        private readonly List<CardInstance> _shown = new();
        private readonly List<CardInstance> _picked = new();
        private RectTransform _grid;
        private RectTransform _scroll;
        private RectTransform _footer;
        private Text _title;
        private Text _hint;
        private Button _confirmButton;
        private Vector2 _cardSize;

        private bool _selecting;
        private int _minPick;
        private int _maxPick;
        private System.Action<IReadOnlyList<CardInstance>> _onConfirm;
        private System.Action _onCancel;

        /// <summary>点查看面板里的某张牌时回调，由 <see cref="BattleView"/> 接到详情面板上。</summary>
        public System.Action<CardInstance> CardClickHandler { get; set; }

        public bool IsOpen => gameObject.activeSelf;

        public static ZonePeekPanel Create(Transform parent, Vector2 cardSize)
        {
            Image backdrop = UiBuilder.CreateImage("ZonePeek", parent, new Color(0f, 0f, 0f, 0.78f));
            backdrop.rectTransform.Stretch();

            var panel = backdrop.gameObject.AddComponent<ZonePeekPanel>();
            panel._cardSize = cardSize * 0.9f;

            Image window = UiBuilder.CreateImage(
                "Window", backdrop.transform, new Color(0.14f, 0.15f, 0.19f, 0.98f));
            window.rectTransform.anchorMin = new Vector2(0.14f, 0.14f);
            window.rectTransform.anchorMax = new Vector2(0.86f, 0.86f);
            window.rectTransform.offsetMin = Vector2.zero;
            window.rectTransform.offsetMax = Vector2.zero;
            UiBuilder.CreateBorder(window.rectTransform, new Color(1f, 1f, 1f, 0.22f));

            panel._title = UiBuilder.CreateText("Title", window.transform, "", 18, TextAnchor.MiddleLeft);
            panel._title.rectTransform.anchorMin = new Vector2(0f, 1f);
            panel._title.rectTransform.anchorMax = new Vector2(1f, 1f);
            panel._title.rectTransform.pivot = new Vector2(0.5f, 1f);
            panel._title.rectTransform.sizeDelta = new Vector2(-24f, 34f);
            panel._title.rectTransform.anchoredPosition = new Vector2(12f, -6f);

            UiBuilder.CreateButton("关闭", window.transform, 80f, panel.Close)
                .GetComponent<RectTransform>().SetAsLastSibling();

            // 关闭按钮用的是 LayoutElement 尺寸，这里没有布局组，手动摆到右上角
            RectTransform closeRect = window.transform.GetChild(window.transform.childCount - 1)
                .GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.sizeDelta = new Vector2(80f, 30f);
            closeRect.anchoredPosition = new Vector2(-10f, -6f);

            var scroll = UiBuilder.CreateRect("Scroll", window.transform);
            scroll.anchorMin = Vector2.zero;
            scroll.anchorMax = Vector2.one;
            scroll.offsetMin = new Vector2(12f, 12f);
            scroll.offsetMax = new Vector2(-12f, -44f);

            var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scroll.gameObject.AddComponent<RectMask2D>();

            // 同 CardDetailPanel：滚动区得有个能接射线的东西，
            // 否则鼠标停在卡与卡之间的空隙上滚轮就没反应
            Image catcher = UiBuilder.CreateImage("ScrollCatcher", scroll, Color.clear);
            catcher.rectTransform.Stretch();

            panel._scroll = scroll;
            panel._grid = UiBuilder.CreateRect("Grid", scroll);
            panel._grid.anchorMin = new Vector2(0f, 1f);
            panel._grid.anchorMax = new Vector2(1f, 1f);
            panel._grid.pivot = new Vector2(0.5f, 1f);
            scrollRect.content = panel._grid;

            panel.BuildFooter(window.transform);

            backdrop.gameObject.SetActive(false);
            return panel;
        }

        /// <summary>选择模式下才出现的底栏：左边说明还要选几张，右边确定/取消。</summary>
        private void BuildFooter(Transform window)
        {
            _footer = UiBuilder.CreateRect("Footer", window);
            _footer.anchorMin = Vector2.zero;
            _footer.anchorMax = new Vector2(1f, 0f);
            _footer.pivot = new Vector2(0.5f, 0f);
            _footer.sizeDelta = new Vector2(-24f, 34f);
            _footer.anchoredPosition = new Vector2(0f, 8f);

            var group = _footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 10;
            group.childAlignment = TextAnchor.MiddleRight;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;

            _hint = UiBuilder.CreateText("Hint", _footer, "", 15, TextAnchor.MiddleLeft);
            _hint.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            _confirmButton = UiBuilder.CreateButton("确定", _footer, 100f, ConfirmSelection);
            UiBuilder.CreateButton("取消", _footer, 100f, CancelSelection);

            _footer.gameObject.SetActive(false);
        }

        /// <summary>只看不选：弃牌堆这类公开信息随时可以摊开。</summary>
        public void Open(string title, IReadOnlyList<CardInstance> cards)
        {
            _selecting = false;
            _onConfirm = null;
            _onCancel = null;
            Layout(title, cards);
        }

        /// <summary>
        /// 选择模式：摊开候选牌，让玩家点选，结果回传给 <paramref name="onConfirm"/>。
        ///
        /// <para>面板本身**不判断这张牌选了合不合规**——合法性在引擎里，
        /// 这里只负责「选够张数」这一条纯界面约束（见表现层架构 §1）。</para>
        /// </summary>
        public void OpenForSelection(
            string title,
            IReadOnlyList<CardInstance> cards,
            int minPick,
            int maxPick,
            System.Action<IReadOnlyList<CardInstance>> onConfirm,
            System.Action onCancel)
        {
            _selecting = true;
            _minPick = minPick;
            _maxPick = Mathf.Max(1, maxPick);
            _onConfirm = onConfirm;
            _onCancel = onCancel;
            _picked.Clear();

            Layout(title, cards);
        }

        private void Layout(string title, IReadOnlyList<CardInstance> cards)
        {
            _shown.Clear();
            _shown.AddRange(cards);

            _title.text = $"{title}　共 {cards.Count} 张";
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            _footer.gameObject.SetActive(_selecting);
            // 底栏挡住最后一行卡，所以选择模式下滚动区要让出这块高度
            _scroll.offsetMin = new Vector2(12f, _selecting ? 50f : 12f);

            while (_cards.Count < cards.Count)
            {
                _cards.Add(PeekCardView.Create(_grid));
            }

            const float spacing = 12f;
            int rows = Mathf.Max(1, Mathf.CeilToInt(cards.Count / (float)Columns));
            _grid.sizeDelta = new Vector2(0f, rows * (_cardSize.y + spacing) + spacing);

            for (int i = 0; i < _cards.Count; i++)
            {
                PeekCardView view = _cards[i];
                if (i >= cards.Count)
                {
                    view.gameObject.SetActive(false);
                    continue;
                }

                CardInstance card = cards[i];
                view.gameObject.SetActive(true);
                view.ClickHandler = _selecting ? Toggle : CardClickHandler;
                view.Bind(card, _cardSize);
                view.SetSelected(0);

                int column = i % Columns;
                int row = i / Columns;

                RectTransform rect = view.Rect;
                rect.SetParent(_grid, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.localRotation = Quaternion.identity;
                rect.anchoredPosition = new Vector2(
                    spacing + column * (_cardSize.x + spacing),
                    -(spacing + row * (_cardSize.y + spacing)));
            }

            if (_selecting) RefreshSelectionState();
        }

        private void Toggle(CardInstance card)
        {
            if (_picked.Remove(card))
            {
                RefreshSelectionState();
                return;
            }

            // 选满之后再点新的一张：挤掉最早选的那张，而不是干脆不响应。
            // 「只选1张」的卡占绝大多数，若不响应，玩家只能先点一下取消再点新的，
            // 而「点一下取消」这件事界面上根本没有提示。
            if (_picked.Count >= _maxPick) _picked.RemoveAt(0);
            _picked.Add(card);
            RefreshSelectionState();
        }

        private void RefreshSelectionState()
        {
            for (int i = 0; i < _cards.Count && i < _shown.Count; i++)
            {
                _cards[i].SetSelected(_picked.IndexOf(_shown[i]) + 1);
            }

            bool enough = _picked.Count >= _minPick;
            _confirmButton.interactable = enough;
            _hint.text = enough
                ? $"已选 {_picked.Count}/{_maxPick} 张"
                : $"<color=#ff9a9a>已选 {_picked.Count}/{_maxPick} 张，至少要选 {_minPick} 张</color>";
        }

        private void ConfirmSelection()
        {
            if (_picked.Count < _minPick) return;

            var result = new List<CardInstance>(_picked);
            System.Action<IReadOnlyList<CardInstance>> callback = _onConfirm;
            // 先收尾再回调：回调里可能会立刻把面板重新打开（多步选择）
            Dismiss();
            callback?.Invoke(result);
        }

        private void CancelSelection()
        {
            System.Action callback = _onCancel;
            Dismiss();
            callback?.Invoke();
        }

        private void Dismiss()
        {
            _selecting = false;
            _onConfirm = null;
            _onCancel = null;
            _picked.Clear();
            gameObject.SetActive(false);
        }

        public void Close()
        {
            // 选择模式下关闭 = 放弃这次出牌，必须把取消回调走完，
            // 否则调用方会一直以为面板还开着，后续操作全被当成「还在选牌」
            if (_selecting)
            {
                CancelSelection();
                return;
            }

            gameObject.SetActive(false);
        }

        /// <summary>
        /// 点背景空白处关闭。窗体本身是带射线的子物体，点在窗体上不会冒泡到这里。
        /// </summary>
        public void OnPointerClick(PointerEventData eventData) => Close();
    }
}
