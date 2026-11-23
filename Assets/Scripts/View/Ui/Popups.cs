using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TCGinFU.View
{
    /// <summary>右键卡牌弹出的操作菜单。</summary>
    public sealed class CardContextMenu : MonoBehaviour, IPointerClickHandler
    {
        private RectTransform _window;
        private Text _title;
        private VerticalLayoutGroup _list;
        private readonly List<GameObject> _buttons = new();
        private Action _onCancel;

        public bool IsOpen => gameObject.activeSelf;

        public static CardContextMenu Create(Transform parent)
        {
            // 背景铺满用来接住「点别处关闭」，本身几乎透明
            Image backdrop = UiBuilder.CreateImage("CardContextMenu", parent, new Color(0f, 0f, 0f, 0.25f));
            backdrop.rectTransform.Stretch();

            var menu = backdrop.gameObject.AddComponent<CardContextMenu>();

            Image window = UiBuilder.CreateImage(
                "Window", backdrop.transform, new Color(0.16f, 0.17f, 0.22f, 0.98f));
            menu._window = window.rectTransform;
            menu._window.sizeDelta = new Vector2(176f, 40f);
            // 锚到父物体中心，好让 anchoredPosition 和
            // ScreenPointToLocalPointInRectangle 返回的局部坐标是同一个原点；
            // 轴心放左上角，菜单从鼠标处往右下展开
            menu._window.anchorMin = menu._window.anchorMax = new Vector2(0.5f, 0.5f);
            menu._window.pivot = new Vector2(0f, 1f);
            UiBuilder.CreateBorder(menu._window, new Color(1f, 1f, 1f, 0.28f));

            menu._list = window.gameObject.AddComponent<VerticalLayoutGroup>();
            menu._list.padding = new RectOffset(8, 8, 8, 8);
            menu._list.spacing = 4;
            menu._list.childForceExpandWidth = true;
            menu._list.childForceExpandHeight = false;

            var fitter = window.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            menu._title = UiBuilder.CreateText("Title", window.transform, "", 14, TextAnchor.MiddleCenter);
            menu._title.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            backdrop.gameObject.SetActive(false);
            return menu;
        }

        /// <summary>
        /// 在屏幕坐标 <paramref name="screenPosition"/> 处弹出菜单。
        /// </summary>
        /// <param name="onCancel">
        /// 点别处关掉菜单时回调。出牌前的「几选一」是一条多步流程的中间环节，
        /// 关掉菜单等于放弃这次出牌——没有这个回调的话流程会卡在半截，
        /// 玩家既看不到牌被打出、也没有任何提示说明发生了什么。
        /// </param>
        public void Open(
            string title,
            Vector2 screenPosition,
            IEnumerable<(string Label, Action OnPick)> options,
            Action onCancel = null)
        {
            _title.text = title;
            _onCancel = onCancel;

            foreach (GameObject button in _buttons) Destroy(button);
            _buttons.Clear();

            foreach ((string label, Action onPick) in options)
            {
                Action captured = onPick;
                Button button = UiBuilder.CreateButton(label, _window, 160f, () =>
                {
                    // 先清掉取消回调再关：否则「选了一项」也会被当成「放弃」
                    _onCancel = null;
                    Close();
                    captured?.Invoke();
                });
                _buttons.Add(button.gameObject);
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            // 菜单挂在屏幕空间 Canvas 上，直接用屏幕坐标定位；贴着鼠标右下方展开
            var parentRect = (RectTransform)transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, screenPosition, null, out Vector2 local))
            {
                _window.anchoredPosition = local + new Vector2(8f, -8f);
            }

            ClampIntoParent(parentRect);
        }

        /// <summary>靠近屏幕边缘时把菜单拉回可视范围。</summary>
        private void ClampIntoParent(RectTransform parentRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_window);

            Vector2 size = _window.rect.size;
            Vector2 half = parentRect.rect.size * 0.5f;
            Vector2 position = _window.anchoredPosition;

            // 轴心在左上角：x 向右长 size.x，y 向下长 size.y
            position.x = Mathf.Clamp(position.x, -half.x, Mathf.Max(-half.x, half.x - size.x));
            position.y = Mathf.Clamp(position.y, Mathf.Min(half.y, -half.y + size.y), half.y);
            _window.anchoredPosition = position;
        }

        public void Close()
        {
            Action cancelled = _onCancel;
            _onCancel = null;
            gameObject.SetActive(false);
            cancelled?.Invoke();
        }

        public void OnPointerClick(PointerEventData eventData) => Close();
    }

    /// <summary>确认弹窗。选完充能目标后用它二次确认。</summary>
    public sealed class ConfirmDialog : MonoBehaviour, IPointerClickHandler
    {
        private Text _message;
        private Action _onConfirm;

        public bool IsOpen => gameObject.activeSelf;

        public static ConfirmDialog Create(Transform parent)
        {
            Image backdrop = UiBuilder.CreateImage("ConfirmDialog", parent, new Color(0f, 0f, 0f, 0.55f));
            backdrop.rectTransform.Stretch();

            var dialog = backdrop.gameObject.AddComponent<ConfirmDialog>();

            Image window = UiBuilder.CreateImage(
                "Window", backdrop.transform, new Color(0.16f, 0.17f, 0.22f, 0.99f));
            window.rectTransform.anchorMin = window.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            window.rectTransform.sizeDelta = new Vector2(420f, 150f);
            window.rectTransform.anchoredPosition = Vector2.zero;
            UiBuilder.CreateBorder(window.rectTransform, new Color(1f, 1f, 1f, 0.3f));

            dialog._message = UiBuilder.CreateText("Message", window.transform, "", 16);
            dialog._message.rectTransform.anchorMin = new Vector2(0f, 0.35f);
            dialog._message.rectTransform.anchorMax = Vector2.one;
            dialog._message.rectTransform.offsetMin = new Vector2(16f, 0f);
            dialog._message.rectTransform.offsetMax = new Vector2(-16f, -12f);

            var row = UiBuilder.CreateRect("Buttons", window.transform);
            row.anchorMin = Vector2.zero;
            row.anchorMax = new Vector2(1f, 0.35f);
            row.offsetMin = new Vector2(16f, 14f);
            row.offsetMax = new Vector2(-16f, 0f);

            var group = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 12;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;

            UiBuilder.CreateButton("确定", row, 150f, dialog.Confirm);
            UiBuilder.CreateButton("取消", row, 150f, dialog.Close);

            backdrop.gameObject.SetActive(false);
            return dialog;
        }

        public void Open(string message, Action onConfirm)
        {
            _message.text = message;
            _onConfirm = onConfirm;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        private void Confirm()
        {
            Action callback = _onConfirm;
            Close();
            callback?.Invoke();
        }

        public void Close()
        {
            _onConfirm = null;
            gameObject.SetActive(false);
        }

        public void OnPointerClick(PointerEventData eventData) => Close();
    }
}
