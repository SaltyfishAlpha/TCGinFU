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

    /// <summary>一个卡位的位置与旋转。</summary>
    internal readonly struct SlotPose
    {
        public readonly Vector2 Position;
        public readonly float Rotation;

        public SlotPose(Vector2 position, float rotation = 0f)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    /// <summary>
    /// 一个场地区域的视图。它只负责「画出边框标题 + 把里面的东西摆好」，
    /// 具体放什么由 <see cref="BattleView"/> 决定——所以新增一种区域
    /// 不用改这个文件。
    /// </summary>
    public sealed class ZoneView : MonoBehaviour, IDropTarget
    {
        private readonly List<RectTransform> _items = new();
        private readonly List<RectTransform> _ghosts = new();
        private Image _background;
        private Image _dropHighlight;
        private Text _label;
        private RectTransform _ghostRoot;

        public RectTransform Rect { get; private set; }
        public ZoneLayout Layout { get; private set; }

        /// <summary>卡位尺寸。卡牌区取标准卡牌尺寸×缩放，角色区取角色面板尺寸。</summary>
        public Vector2 SlotSize { get; private set; }

        /// <summary>外部注入的「能不能接这张牌」判定，避免区域自己长出规则。</summary>
        public System.Func<ZoneView, CardView, bool> DropFilter { get; set; }

        public System.Action<ZoneView, CardView> DropHandler { get; set; }

        public static ZoneView Create(Transform parent, ZoneLayout layout, Vector2 slotSize)
        {
            RectTransform rect = UiBuilder.CreateRect($"Zone_{layout.key}", parent);
            rect.anchorMin = layout.anchorMin;
            rect.anchorMax = layout.anchorMax;
            rect.offsetMin = layout.offsetMin;
            rect.offsetMax = layout.offsetMax;

            var view = rect.gameObject.AddComponent<ZoneView>();
            view.Rect = rect;
            view.Layout = layout;
            view.SlotSize = slotSize;

            view._background = UiBuilder.CreateImage("Bg", rect, layout.background);
            view._background.rectTransform.Stretch();
            view._background.raycastTarget = true;

            UiBuilder.CreateBorder(rect, layout.border);

            // 空卡位画在最底下，真卡盖在上面
            view._ghostRoot = UiBuilder.CreateRect("Slots", rect);
            view._ghostRoot.Stretch();

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

        /// <summary>
        /// 按配置摆好所有子项。即使一张牌都没有，也会按 reservedSlots 画出空卡位——
        /// 这样区域不会塌，牌位也不会随张数变化乱跳。
        /// </summary>
        public void Arrange()
        {
            WarnIfTooSmall();

            int slotCount = Mathf.Max(_items.Count, Layout.reservedSlots);
            if (slotCount == 0)
            {
                SyncGhosts(0, null);
                return;
            }

            List<SlotPose> poses = ComputePoses(slotCount);

            for (int i = 0; i < _items.Count && i < poses.Count; i++)
            {
                Apply(_items[i], poses[i]);
                _items[i].gameObject.SetActive(Layout.arrange != ZoneArrange.堆叠 || i >= _items.Count - 6);
            }

            SyncGhosts(slotCount, poses);
        }

        private bool _warnedTooSmall;

        /// <summary>
        /// 区域必须随时装得下一个卡位。改锚点很容易把某块调得过小，
        /// 与其让卡牌默默画到框外，不如直接报出来。只报一次。
        /// </summary>
        private void WarnIfTooSmall()
        {
            if (_warnedTooSmall) return;

            Vector2 size = Rect.rect.size;
            if (size.x <= 0f || size.y <= 0f) return; // 布局还没算完，下一帧再说

            float needHeight = Layout.arrange == ZoneArrange.横置 ? SlotSize.x : SlotSize.y;
            float needWidth = Layout.arrange == ZoneArrange.横置 ? SlotSize.y : SlotSize.x;

            if (size.x + 0.5f >= needWidth && size.y + 0.5f >= needHeight) return;

            _warnedTooSmall = true;
            Debug.LogWarning(
                $"[战斗布局] 区域「{Layout.displayName}」({Layout.key}) 实际大小 {size.x:F0}×{size.y:F0}，"
                + $"装不下一个卡位 {needWidth:F0}×{needHeight:F0}。"
                + "请在布局资产里调大它的锚点范围，或调小 cardScale / combatantSize。", this);
        }

        private void Apply(RectTransform item, SlotPose pose)
        {
            item.sizeDelta = SlotSize;
            item.anchorMin = item.anchorMax = new Vector2(0.5f, 0.5f);
            item.pivot = new Vector2(0.5f, 0.5f);
            item.anchoredPosition = pose.Position;
            item.localRotation = Quaternion.Euler(0f, 0f, pose.Rotation);
        }

        /// <summary>空卡位：只显示没有被真卡占住的那些。</summary>
        private void SyncGhosts(int slotCount, List<SlotPose> poses)
        {
            while (_ghosts.Count < slotCount)
            {
                Image ghost = UiBuilder.CreateImage(
                    $"Slot{_ghosts.Count}", _ghostRoot, new Color(1f, 1f, 1f, 0.045f), raycast: false);
                UiBuilder.CreateBorder(ghost.rectTransform, new Color(1f, 1f, 1f, 0.10f), 1.5f);
                _ghosts.Add(ghost.rectTransform);
            }

            for (int i = 0; i < _ghosts.Count; i++)
            {
                bool show = poses != null && i < slotCount && i >= _items.Count;
                _ghosts[i].gameObject.SetActive(show);
                if (show) Apply(_ghosts[i], poses[i]);
            }
        }

        private List<SlotPose> ComputePoses(int count) => Layout.arrange switch
        {
            ZoneArrange.堆叠 => StackPoses(count),
            ZoneArrange.网格 => GridPoses(count),
            ZoneArrange.扇形 => FanPoses(count),
            ZoneArrange.横置 => SidewaysPoses(count),
            _ => RowPoses(count),
        };

        private List<SlotPose> StackPoses(int count)
        {
            var poses = new List<SlotPose>(count);
            for (int i = 0; i < count; i++) poses.Add(new SlotPose(new Vector2(i * 1.2f, i * -1.2f)));
            return poses;
        }

        /// <summary>横排时的步进。放不下就压缩间距（允许负间距让卡互相叠一点）。</summary>
        private float RowStep(int count, float slotWidth, out float startX)
        {
            float spacing = Layout.spacing;
            float width = Rect.rect.width;
            float total = count * slotWidth + (count - 1) * spacing;

            if (total > width && count > 1)
            {
                spacing = (width - count * slotWidth) / (count - 1);
                total = width;
            }

            startX = -total / 2f + slotWidth / 2f;
            return slotWidth + spacing;
        }

        private List<SlotPose> RowPoses(int count)
        {
            float step = RowStep(count, SlotSize.x, out float startX);
            var poses = new List<SlotPose>(count);
            for (int i = 0; i < count; i++) poses.Add(new SlotPose(new Vector2(startX + i * step, 0f)));
            return poses;
        }

        private List<SlotPose> FanPoses(int count)
        {
            if (count == 1) return RowPoses(1);

            float step = RowStep(count, SlotSize.x, out float startX);
            float half = (count - 1) / 2f;

            var poses = new List<SlotPose>(count);
            for (int i = 0; i < count; i++)
            {
                float normalized = half == 0f ? 0f : (i - half) / half;
                poses.Add(new SlotPose(
                    new Vector2(startX + i * step, -Mathf.Abs(normalized) * Layout.fanArc),
                    -normalized * Layout.fanAngle));
            }

            return poses;
        }

        private List<SlotPose> GridPoses(int count)
        {
            int columns = Mathf.Max(1, Layout.gridColumns);
            float spacing = Layout.spacing;
            Vector2 size = Rect.rect.size;

            var poses = new List<SlotPose>(count);
            for (int i = 0; i < count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                poses.Add(new SlotPose(new Vector2(
                    -size.x / 2f + spacing + SlotSize.x / 2f + column * (SlotSize.x + spacing),
                    size.y / 2f - 18f - spacing - SlotSize.y / 2f - row * (SlotSize.y + spacing))));
            }

            return poses;
        }

        /// <summary>
        /// 横置叠放：对应实体打牌里「在角色卡上横放一张牌表示充能」。
        /// 卡旋转 90°，依次往右压住一部分，只露出边缘。
        /// </summary>
        private List<SlotPose> SidewaysPoses(int count)
        {
            // 旋转后卡牌在水平方向的占位是它的高度
            float rotatedWidth = SlotSize.y;
            float step = Mathf.Max(12f, rotatedWidth * 0.3f + Layout.spacing);
            float total = rotatedWidth + (count - 1) * step;
            float startX = -total / 2f + rotatedWidth / 2f;

            var poses = new List<SlotPose>(count);
            for (int i = 0; i < count; i++) poses.Add(new SlotPose(new Vector2(startX + i * step, 0f), 90f));
            return poses;
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
