using System.Collections.Generic;
using TCGinFU.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TCGinFU.View
{
    /// <summary>
    /// 牌堆查看面板：点弃牌堆 / 持续区 / 已完成升变区这类叠放区域时弹出，
    /// 把里面的牌全部摊开。实体打牌时这些都是可以随时翻看的公开信息。
    /// <para>卡牌视图是面板自己的一套，不碰 <see cref="BattleView"/> 的对象池。</para>
    /// </summary>
    public sealed class ZonePeekPanel : MonoBehaviour, IPointerClickHandler
    {
        private const int Columns = 6;

        private readonly List<CardView> _cards = new();
        private RectTransform _grid;
        private Text _title;
        private Vector2 _cardSize;

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

            panel._grid = UiBuilder.CreateRect("Grid", scroll);
            panel._grid.anchorMin = new Vector2(0f, 1f);
            panel._grid.anchorMax = new Vector2(1f, 1f);
            panel._grid.pivot = new Vector2(0.5f, 1f);
            scrollRect.content = panel._grid;

            backdrop.gameObject.SetActive(false);
            return panel;
        }

        public void Open(string title, IReadOnlyList<CardInstance> cards, ICardDragContext context)
        {
            _title.text = $"{title}　共 {cards.Count} 张";
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            while (_cards.Count < cards.Count)
            {
                _cards.Add(CardView.Create(_grid, context));
            }

            const float spacing = 12f;
            int rows = Mathf.Max(1, Mathf.CeilToInt(cards.Count / (float)Columns));
            _grid.sizeDelta = new Vector2(0f, rows * (_cardSize.y + spacing) + spacing);

            for (int i = 0; i < _cards.Count; i++)
            {
                CardView view = _cards[i];
                if (i >= cards.Count)
                {
                    view.gameObject.SetActive(false);
                    continue;
                }

                view.gameObject.SetActive(true);
                // 查看面板里的牌一律正面、不可拖
                view.Bind(cards[i], zone: null, faceDown: false, draggable: false, _cardSize);

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
        }

        public void Close() => gameObject.SetActive(false);

        /// <summary>
        /// 点背景空白处关闭。窗体本身是带射线的子物体，点在窗体上不会冒泡到这里。
        /// </summary>
        public void OnPointerClick(PointerEventData eventData) => Close();
    }
}
