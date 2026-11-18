using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TCGinFU.View
{
    /// <summary>可以被拖拽放置的东西（区域或角色面板）。</summary>
    public interface IDropTarget
    {
        RectTransform Rect { get; }
        bool CanAccept(CardView card);
        void AcceptCard(CardView card);
        void SetDropHighlight(CardHighlight state);
    }

    /// <summary>
    /// 一个场地区域的视图。它只负责「画出边框标题 + 把里面的东西摆好」，
    /// 具体放什么由 <see cref="BattleView"/> 决定——所以新增一种区域
    /// 不用改这个文件。
    /// </summary>
    public sealed class ZoneView : MonoBehaviour, IDropTarget
    {
        private readonly List<RectTransform> _items = new();
        private Image _background;
        private Image _dropHighlight;
        private Text _label;

        public RectTransform Rect { get; private set; }
        public ZoneLayout Layout { get; private set; }

        /// <summary>外部注入的「能不能接这张牌」判定，避免区域自己长出规则。</summary>
        public System.Func<ZoneView, CardView, bool> DropFilter { get; set; }

        public System.Action<ZoneView, CardView> DropHandler { get; set; }

        public static ZoneView Create(Transform parent, ZoneLayout layout)
        {
            RectTransform rect = UiBuilder.CreateRect($"Zone_{layout.key}", parent);
            rect.anchorMin = layout.anchorMin;
            rect.anchorMax = layout.anchorMax;
            rect.offsetMin = layout.offsetMin;
            rect.offsetMax = layout.offsetMax;

            var view = rect.gameObject.AddComponent<ZoneView>();
            view.Rect = rect;
            view.Layout = layout;

            view._background = UiBuilder.CreateImage("Bg", rect, layout.background);
            view._background.rectTransform.Stretch();
            // 区域底板要能接住拖拽的射线，但不要挡住里面的卡
            view._background.raycastTarget = true;

            UiBuilder.CreateBorder(rect, layout.border);

            view._dropHighlight = UiBuilder.CreateImage("DropHighlight", rect, Color.clear, raycast: false);
            view._dropHighlight.rectTransform.Stretch(-2f);

            if (layout.showLabel)
            {
                view._label = UiBuilder.CreateText(
                    "Label", rect, layout.displayName, 13, TextAnchor.UpperLeft,
                    new Color(1f, 1f, 1f, 0.5f));
                view._label.rectTransform.anchorMin = new Vector2(0f, 1f);
                view._label.rectTransform.anchorMax = new Vector2(1f, 1f);
                view._label.rectTransform.pivot = new Vector2(0.5f, 1f);
                view._label.rectTransform.sizeDelta = new Vector2(0f, 18f);
                view._label.rectTransform.anchoredPosition = new Vector2(6f, -2f);
            }

            view.gameObject.SetActive(layout.visible);
            return view;
        }

        public void SetLabel(string text)
        {
            if (_label != null) _label.text = text;
        }

        // ──────────────────────────── 内容与排布 ────────────────────────────

        public void BeginRebuild() => _items.Clear();

        public void Add(RectTransform item)
        {
            item.SetParent(Rect, false);
            _items.Add(item);
        }

        /// <summary>按配置的排布方式摆好所有子项。</summary>
        public void Arrange()
        {
            int count = _items.Count;
            if (count == 0) return;

            Vector2 size = Rect.rect.size;
            Vector2 cardSize = Layout.cardSize;

            switch (Layout.arrange)
            {
                case ZoneArrange.堆叠:
                    ArrangeStack(cardSize);
                    break;
                case ZoneArrange.网格:
                    ArrangeGrid(size, cardSize);
                    break;
                case ZoneArrange.扇形:
                    ArrangeFan(size, cardSize);
                    break;
                default:
                    ArrangeRow(size, cardSize);
                    break;
            }
        }

        private void ArrangeStack(Vector2 cardSize)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                RectTransform item = _items[i];
                item.sizeDelta = cardSize;
                item.anchorMin = item.anchorMax = new Vector2(0.5f, 0.5f);
                item.pivot = new Vector2(0.5f, 0.5f);
                item.localRotation = Quaternion.identity;
                // 只露出一点点错位，表示底下还有牌
                item.anchoredPosition = new Vector2(i * 1.2f, i * -1.2f);
                item.gameObject.SetActive(i >= _items.Count - 6);
            }
        }

        private void ArrangeRow(Vector2 size, Vector2 cardSize)
        {
            int count = _items.Count;
            float spacing = Layout.spacing;
            float totalWidth = count * cardSize.x + (count - 1) * spacing;

            // 放不下就压缩间距（允许负间距让卡片互相叠一点）
            if (totalWidth > size.x && count > 1)
            {
                spacing = (size.x - count * cardSize.x) / (count - 1);
                totalWidth = size.x;
            }

            float startX = -totalWidth / 2f + cardSize.x / 2f;
            for (int i = 0; i < count; i++)
            {
                RectTransform item = _items[i];
                item.sizeDelta = cardSize;
                item.anchorMin = item.anchorMax = new Vector2(0.5f, 0.5f);
                item.pivot = new Vector2(0.5f, 0.5f);
                item.localRotation = Quaternion.identity;
                item.anchoredPosition = new Vector2(startX + i * (cardSize.x + spacing), 0f);
                item.gameObject.SetActive(true);
            }
        }

        private void ArrangeGrid(Vector2 size, Vector2 cardSize)
        {
            int columns = Mathf.Max(1, Layout.gridColumns);
            float spacing = Layout.spacing;

            for (int i = 0; i < _items.Count; i++)
            {
                int column = i % columns;
                int row = i / columns;

                RectTransform item = _items[i];
                item.sizeDelta = cardSize;
                item.anchorMin = item.anchorMax = new Vector2(0f, 1f);
                item.pivot = new Vector2(0f, 1f);
                item.localRotation = Quaternion.identity;
                item.anchoredPosition = new Vector2(
                    column * (cardSize.x + spacing) + spacing,
                    -(row * (cardSize.y + spacing) + spacing + 16f));
                item.gameObject.SetActive(true);
            }
        }

        private void ArrangeFan(Vector2 size, Vector2 cardSize)
        {
            int count = _items.Count;
            if (count == 1)
            {
                ArrangeRow(size, cardSize);
                return;
            }

            float spacing = Layout.spacing;
            float totalWidth = count * cardSize.x + (count - 1) * spacing;
            if (totalWidth > size.x)
            {
                spacing = (size.x - count * cardSize.x) / (count - 1);
                totalWidth = size.x;
            }

            float startX = -totalWidth / 2f + cardSize.x / 2f;
            float half = (count - 1) / 2f;

            for (int i = 0; i < count; i++)
            {
                float offset = i - half;
                float normalized = half == 0f ? 0f : offset / half;

                RectTransform item = _items[i];
                item.sizeDelta = cardSize;
                item.anchorMin = item.anchorMax = new Vector2(0.5f, 0.5f);
                item.pivot = new Vector2(0.5f, 0.5f);
                item.anchoredPosition = new Vector2(
                    startX + i * (cardSize.x + spacing),
                    -Mathf.Abs(normalized) * Layout.fanArc);
                item.localRotation = Quaternion.Euler(0f, 0f, -normalized * Layout.fanAngle);
                item.gameObject.SetActive(true);
            }
        }

        // ──────────────────────────── 放置目标 ────────────────────────────

        public bool CanAccept(CardView card)
            => Layout.acceptsDrop && (DropFilter == null || DropFilter(this, card));

        public void AcceptCard(CardView card) => DropHandler?.Invoke(this, card);

        public void SetDropHighlight(CardHighlight state)
        {
            _dropHighlight.color = state switch
            {
                CardHighlight.合法 => new Color(0.35f, 1f, 0.5f, 0.20f),
                CardHighlight.悬停 => new Color(0.35f, 1f, 0.5f, 0.42f),
                CardHighlight.非法 => new Color(1f, 0.3f, 0.3f, 0.22f),
                _ => Color.clear,
            };
        }
    }
}
