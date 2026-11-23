using System.Collections.Generic;
using System.Linq;
using TCGinFU.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TCGinFU.View
{
    /// <summary>可以被拖拽放置的东西（区域或角色卡）。</summary>
    public interface IDropTarget
    {
        Transform Root { get; }
        bool CanAccept(CardView card);
        void AcceptCard(CardView card);
        void SetDropHighlight(CardHighlight state);
    }

    /// <summary>一个卡位的位姿（桌面局部像素坐标 + 绕法线的旋转）。</summary>
    internal readonly struct SlotPose
    {
        public readonly Vector3 Position;
        public readonly float Roll;

        public SlotPose(Vector3 position, float roll = 0f)
        {
            Position = position;
            Roll = roll;
        }
    }

    /// <summary>
    /// 桌面上的一块区域。位置、大小来自 <see cref="ZoneLayout"/>，
    /// 自己只负责「画出底板 + 把里面的卡摆好」。
    /// <para>
    /// 叠放时沿桌面法线逐张抬高一个卡厚，所以牌堆是真的会变厚的。
    /// </para>
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ZoneView : MonoBehaviour, IDropTarget, IPointerClickHandler
    {
        /// <summary>区域里摆着的东西。卡和角色卡都是卡，统一按卡位排布。</summary>
        private readonly List<Transform> _items = new();

        /// <summary>
        /// 角色区专用：按 <see cref="Combatant.Slot"/> 定位的 4 个固定卡位（裁定 R-17）。
        ///
        /// <para>不能和 <see cref="_items"/> 一样按加入顺序摆——槽位决定 §4.3 的「正前方同列」，
        /// 紧凑居中会让画面上挨着的两个人在引擎里判不相邻，援护能不能成立就成了玄学。</para>
        /// </summary>
        private readonly Transform[] _slotted = new Transform[Battlefield.SlotsPerRow];

        /// <summary>走位提示用的逐槽高亮框，只在拖拽走位期间亮起。</summary>
        private readonly Transform[] _slotMarks = new Transform[Battlefield.SlotsPerRow];
        private readonly List<Transform> _ghosts = new();
        private Transform _plate;
        private Transform _highlight;
        private Transform _ghostRoot;
        private Text _label;
        private BoxCollider _collider;
        private Vector2 _sizePx;

        public Transform Root { get; private set; }
        public ZoneLayout Layout { get; private set; }

        /// <summary>卡位尺寸（设计像素）。</summary>
        public Vector2 SlotSize { get; private set; }

        public System.Func<ZoneView, CardView, bool> DropFilter { get; set; }
        public System.Action<ZoneView, CardView> DropHandler { get; set; }
        /// <summary>点击回调。带上事件数据，这样「点了这一排的哪一格」才判得出来（选位窗口要用）。</summary>
        public System.Action<ZoneView, PointerEventData> ClickHandler { get; set; }

        /// <summary>
        /// 按布局在桌面上开一块区域。<paramref name="boardPx"/> 是整张桌面的设计像素尺寸。
        /// </summary>
        public static ZoneView Create(Transform parent, ZoneLayout layout, Vector2 slotSize, Vector2 boardPx)
        {
            Vector2 min = Vector2.Scale(layout.anchorMin, boardPx) + layout.offsetMin;
            Vector2 max = Vector2.Scale(layout.anchorMax, boardPx) + layout.offsetMax;
            Vector2 sizePx = Vector2.Max(max - min, Vector2.one);
            Vector2 centerPx = (min + max) * 0.5f - boardPx * 0.5f;

            var go = new GameObject($"Zone_{layout.key}", typeof(BoxCollider));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(centerPx.x, centerPx.y, 0f);
            go.transform.localRotation = Quaternion.identity;

            var view = go.AddComponent<ZoneView>();
            view.Root = go.transform;
            view.Layout = layout;
            view.SlotSize = slotSize;
            view._sizePx = sizePx;

            view._collider = go.GetComponent<BoxCollider>();
            view._collider.size = new Vector3(sizePx.x, sizePx.y, 1f);
            // 完全透明的区域（比如角色卡背后的充能堆）不该吃掉点击
            view._collider.enabled = layout.background.a > 0.001f || layout.acceptsDrop;

            if (layout.background.a > 0.001f)
            {
                view._plate = CardMeshFactory.CreateMeshObject(
                    "Plate", go.transform, CardMeshFactory.Quad, OpaqueOnTable(layout.background));
                view._plate.localScale = new Vector3(sizePx.x, sizePx.y, 1f);
                // 底板压在卡的背面一侧
                view._plate.localPosition = new Vector3(0f, 0f, -CardMeshFactory.TowardCamera * 0.6f);
            }

            view._highlight = CardMeshFactory.CreateMeshObject(
                "Highlight", go.transform, CardMeshFactory.Quad, Color.clear);
            view._highlight.localScale = new Vector3(sizePx.x + 8f, sizePx.y + 8f, 1f);
            view._highlight.localPosition = new Vector3(0f, 0f, -CardMeshFactory.TowardCamera * 0.9f);
            view._highlight.gameObject.SetActive(false);

            view._ghostRoot = new GameObject("Slots").transform;
            view._ghostRoot.SetParent(go.transform, false);

            if (layout.showLabel) view.CreateLabel(sizePx);

            go.SetActive(layout.visible);
            return view;
        }

        /// <summary>
        /// 桌面上的网格一律不透明（Unlit 材质没开混合），所以把半透明的区域底色
        /// 直接压到桌布上算出一个实色。
        /// </summary>
        private static Color OpaqueOnTable(Color background)
        {
            var table = new Color(0.11f, 0.12f, 0.15f, 1f);
            return Color.Lerp(table, new Color(background.r, background.g, background.b, 1f), background.a * 3f);
        }

        private void CreateLabel(Vector2 sizePx)
        {
            float front = CardMeshFactory.TowardCamera;
            CardFace face = CardFace.Create(Root, new Vector2(sizePx.x, 26f), front * 0.4f);
            face.Root.localPosition = new Vector3(0f, sizePx.y * 0.5f + 16f, front * 0.4f);
            _label = face.AddText(
                "Label", Layout.displayName, 15, TextAnchor.MiddleLeft,
                Vector2.zero, Vector2.one, new Color(1f, 1f, 1f, 0.55f));
        }

        public void SetLabel(string text)
        {
            if (_label != null) _label.text = text;
        }

        // ──────────────────────────── 内容与排布 ────────────────────────────

        public void BeginRebuild()
        {
            _items.Clear();
            System.Array.Clear(_slotted, 0, _slotted.Length);
        }

        public void Add(CardView card) => AddItem(card.Root);

        /// <summary>把角色卡放进它在引擎里的那个槽位，而不是追加到队尾。</summary>
        public void AddCombatant(CombatantView combatant, int slot)
        {
            combatant.Root.SetParent(Root, false);
            if (slot >= 0 && slot < _slotted.Length) _slotted[slot] = combatant.Root;
        }

        private void AddItem(Transform item)
        {
            item.SetParent(Root, false);
            _items.Add(item);
        }

        // ──────────────────────── 角色区的固定槽位 ────────────────────────

        /// <summary>槽位中心在区域局部坐标里的 X。拖拽判定落点、画预览都用它。</summary>
        public float LocalXForSlot(int slot)
        {
            float step = RowStep(Battlefield.SlotsPerRow, SlotSize.x, out float startX);
            return startX + Mathf.Clamp(slot, 0, Battlefield.SlotsPerRow - 1) * step;
        }

        /// <summary>槽位中心的世界坐标。<see cref="BattleView"/> 把拖拽中的角色卡吸附到这儿。</summary>
        public Vector3 WorldPositionForSlot(int slot)
            => Root.TransformPoint(new Vector3(LocalXForSlot(slot), 0f, SlotLift));

        /// <summary>世界坐标落在哪个槽位上。取横向最近的那个，所以区域里任何一点都有归属。</summary>
        public int SlotAtWorldPoint(Vector3 world)
        {
            float x = Root.InverseTransformPoint(world).x;

            int nearest = 0;
            float best = float.MaxValue;
            for (int slot = 0; slot < Battlefield.SlotsPerRow; slot++)
            {
                float distance = Mathf.Abs(LocalXForSlot(slot) - x);
                if (distance >= best) continue;
                best = distance;
                nearest = slot;
            }

            return nearest;
        }

        private static float SlotLift => CardMeshFactory.CardThickness * CardMeshFactory.TowardCamera * 0.5f;

        /// <summary>
        /// 真卡**只按实际张数**排布，所以手牌永远是居中的，间距随张数动态收放。
        ///
        /// <para>以前这里按 <c>max(张数, reservedSlots)</c> 算卡位，手牌的
        /// <c>reservedSlots = 5</c> 就把 5 个槽固定钉死了：手里只有 2 张时，
        /// 两张牌贴在最左边两格上，右边空着三格——这不是卡牌游戏的样子。</para>
        /// </summary>
        public void Arrange()
        {
            if (Layout.IsCombatantZone)
            {
                ArrangeSlotted();
                return;
            }

            if (_items.Count > 0)
            {
                List<SlotPose> poses = ComputePoses(_items.Count);

                for (int i = 0; i < _items.Count; i++)
                {
                    Apply(_items[i], poses[i], animate: false);
                    _items[i].gameObject.SetActive(true);
                }
            }

            SyncGhosts();
        }

        /// <summary>
        /// 角色区：每人落在自己的槽位上，空槽画占位虚框。
        /// 卡位数固定为 4，所以同一个槽位在前后排永远对齐成一列。
        /// </summary>
        private void ArrangeSlotted()
        {
            List<SlotPose> poses = ComputePoses(Battlefield.SlotsPerRow);

            for (int slot = 0; slot < _slotted.Length; slot++)
            {
                Transform item = _slotted[slot];
                if (item == null) continue;

                // 走位要滑过去，不然玩家看不出自己动过
                Apply(item, poses[slot], animate: true);
                item.gameObject.SetActive(true);
            }

            SyncSlotGhosts(poses);
        }

        /// <summary>
        /// 空槽的占位虚框。和卡牌区不同，这里**一直**画——
        /// 玩家得随时看得出「哪里还能站人」，走位才有的可想。
        /// </summary>
        private void SyncSlotGhosts(List<SlotPose> poses)
        {
            while (_ghosts.Count < Battlefield.SlotsPerRow)
            {
                Transform ghost = CardMeshFactory.CreateMeshObject(
                    $"Slot{_ghosts.Count}", _ghostRoot, CardMeshFactory.Quad,
                    new Color(0.16f, 0.17f, 0.21f, 1f));
                _ghosts.Add(ghost);
            }

            for (int i = 0; i < _ghosts.Count; i++)
            {
                bool show = i < Battlefield.SlotsPerRow && _slotted[i] == null;
                _ghosts[i].gameObject.SetActive(show);
                if (!show) continue;

                _ghosts[i].localPosition = new Vector3(
                    poses[i].Position.x, poses[i].Position.y, -CardMeshFactory.TowardCamera * 0.3f);
                _ghosts[i].localRotation = Quaternion.identity;
                _ghosts[i].localScale = new Vector3(SlotSize.x, SlotSize.y, 1f);
            }
        }

        /// <summary>
        /// 只给角色卡做滑行动画：走位是「某个人从前排挪到了后排」，不滑过去玩家看不出自己动过。
        /// 手牌每次 Refresh 都整体重排，补间只会让扇形抖个不停，所以卡牌仍然直接落位。
        /// </summary>
        private static void Apply(Transform item, SlotPose pose, bool animate)
            => SlideTween.Apply(item, pose.Position, Quaternion.Euler(0f, 0f, pose.Roll), animate);

        /// <summary>
        /// 空卡位**只在区域一张牌都没有时**出现，用来说明「这儿是放牌的地方」。
        /// 一旦有牌，真卡自己就标出了区域的位置和大小，占位的轮廓反而是噪音。
        ///
        /// <para>区域本身不会因此塌陷：底板（<c>_plate</c>）的尺寸来自布局的锚点，
        /// 跟区域里有几张牌没有关系。</para>
        /// </summary>
        private void SyncGhosts()
        {
            int slotCount = _items.Count == 0 ? Layout.reservedSlots : 0;
            List<SlotPose> poses = slotCount > 0 ? ComputePoses(slotCount) : null;

            while (_ghosts.Count < slotCount)
            {
                Transform ghost = CardMeshFactory.CreateMeshObject(
                    $"Slot{_ghosts.Count}", _ghostRoot, CardMeshFactory.Quad,
                    new Color(0.16f, 0.17f, 0.21f, 1f));
                ghost.localScale = new Vector3(SlotSize.x, SlotSize.y, 1f);
                _ghosts.Add(ghost);
            }

            for (int i = 0; i < _ghosts.Count; i++)
            {
                bool show = i < slotCount;
                _ghosts[i].gameObject.SetActive(show);
                if (!show) continue;

                SlotPose pose = poses[i];
                // 空卡位压在桌面上，别和真卡抢同一个高度
                _ghosts[i].localPosition = new Vector3(
                    pose.Position.x, pose.Position.y, -CardMeshFactory.TowardCamera * 0.3f);
                _ghosts[i].localRotation = Quaternion.Euler(0f, 0f, pose.Roll);
                _ghosts[i].localScale = new Vector3(SlotSize.x, SlotSize.y, 1f);
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

        /// <summary>牌堆：逐张沿桌面法线抬高一个卡厚，堆得越高说明牌越多。</summary>
        private List<SlotPose> StackPoses(int count)
        {
            float t = CardMeshFactory.CardThickness * CardMeshFactory.TowardCamera;
            var poses = new List<SlotPose>(count);
            for (int i = 0; i < count; i++)
            {
                // 每张再往上挪一丁点，侧面看过去像真实牌堆那样有参差
                poses.Add(new SlotPose(new Vector3(i * 0.35f, i * 0.35f, i * t)));
            }

            return poses;
        }

        private float RowStep(int count, float slotWidth, out float startX)
        {
            float spacing = Layout.spacing;
            float total = count * slotWidth + (count - 1) * spacing;

            if (total > _sizePx.x && count > 1)
            {
                spacing = (_sizePx.x - count * slotWidth) / (count - 1);
                total = _sizePx.x;
            }

            startX = -total / 2f + slotWidth / 2f;
            return slotWidth + spacing;
        }

        private List<SlotPose> RowPoses(int count)
        {
            float step = RowStep(count, SlotSize.x, out float startX);
            float t = CardMeshFactory.CardThickness * CardMeshFactory.TowardCamera;

            var poses = new List<SlotPose>(count);
            for (int i = 0; i < count; i++)
            {
                poses.Add(new SlotPose(new Vector3(startX + i * step, 0f, t * 0.5f)));
            }

            return poses;
        }

        private List<SlotPose> FanPoses(int count)
        {
            if (count == 1) return RowPoses(1);

            float step = RowStep(count, SlotSize.x, out float startX);
            float half = (count - 1) / 2f;
            float t = CardMeshFactory.CardThickness * CardMeshFactory.TowardCamera;

            var poses = new List<SlotPose>(count);
            for (int i = 0; i < count; i++)
            {
                float normalized = half == 0f ? 0f : (i - half) / half;
                poses.Add(new SlotPose(
                    new Vector3(
                        startX + i * step,
                        -Mathf.Abs(normalized) * Layout.fanArc,
                        // 右边的牌压在左边的上面，和真实扇形持牌一致
                        t * (0.5f + i * 1.05f)),
                    -normalized * Layout.fanAngle));
            }

            return poses;
        }

        private List<SlotPose> GridPoses(int count)
        {
            int columns = Mathf.Max(1, Layout.gridColumns);
            float spacing = Layout.spacing;
            float t = CardMeshFactory.CardThickness * CardMeshFactory.TowardCamera;

            var poses = new List<SlotPose>(count);
            for (int i = 0; i < count; i++)
            {
                int column = i % columns;
                int row = i / columns;
                poses.Add(new SlotPose(new Vector3(
                    -_sizePx.x / 2f + spacing + SlotSize.x / 2f + column * (SlotSize.x + spacing),
                    _sizePx.y / 2f - spacing - SlotSize.y / 2f - row * (SlotSize.y + spacing),
                    t * 0.5f)));
            }

            return poses;
        }

        /// <summary>
        /// 横置叠放：牌旋转 90° 塞在角色卡**背后**（法线负方向），一张比一张低，
        /// 对应实体打牌里把充能牌横着塞到角色卡下面。
        /// </summary>
        private List<SlotPose> SidewaysPoses(int count)
        {
            float step = Mathf.Max(6f, Layout.stackStep);
            // 充能牌塞在角色卡**背后**，所以是远离镜头的方向
            float t = CardMeshFactory.CardThickness * -CardMeshFactory.TowardCamera;

            var poses = new List<SlotPose>(count);
            for (int i = 0; i < count; i++)
            {
                poses.Add(new SlotPose(new Vector3(0f, -i * step, t * (i + 1)), 90f));
            }

            return poses;
        }

        // ──────────────────────────── 放置目标 ────────────────────────────

        public bool CanAccept(CardView card)
            => Layout.acceptsDrop && (DropFilter == null || DropFilter(this, card));

        public void AcceptCard(CardView card) => DropHandler?.Invoke(this, card);

        public void SetDropHighlight(CardHighlight state)
        {
            Color color = state switch
            {
                CardHighlight.合法 => new Color(0.22f, 0.45f, 0.28f, 1f),
                CardHighlight.悬停 => new Color(0.35f, 0.85f, 0.45f, 1f),
                CardHighlight.非法 => new Color(0.5f, 0.2f, 0.2f, 1f),
                _ => Color.clear,
            };

            CardMeshFactory.SetColor(_highlight, color);
            _highlight.gameObject.SetActive(color.a > 0.01f);
        }

        // ──────────────────────── 走位的范围与落点提示 ────────────────────────

        /// <summary>
        /// 画走位提示：<paramref name="legalSlots"/> 里的槽位标成「可以站」，
        /// <paramref name="previewSlot"/> 那一格标成「松手就落这儿」。
        ///
        /// <para>提示必须逐槽画而不是整排画——「这一排能去」和「这一格能站」是两件事，
        /// 满员的排只有挤得动的那几格合法。</para>
        /// </summary>
        public void ShowSlotHints(IReadOnlyList<int> legalSlots, int previewSlot = -1)
        {
            List<SlotPose> poses = ComputePoses(Battlefield.SlotsPerRow);
            EnsureSlotMarks();

            for (int slot = 0; slot < _slotMarks.Length; slot++)
            {
                bool legal = legalSlots != null && legalSlots.Contains(slot);
                Color color = slot == previewSlot && legal
                    ? new Color(0.35f, 0.85f, 0.45f, 1f)
                    : legal ? new Color(0.22f, 0.45f, 0.28f, 1f) : Color.clear;

                bool show = color.a > 0.01f;
                _slotMarks[slot].gameObject.SetActive(show);
                if (!show) continue;

                CardMeshFactory.SetColor(_slotMarks[slot], color);
                _slotMarks[slot].localPosition = new Vector3(
                    poses[slot].Position.x, poses[slot].Position.y, -CardMeshFactory.TowardCamera * 0.95f);
                _slotMarks[slot].localScale = new Vector3(SlotSize.x + 8f, SlotSize.y + 8f, 1f);
            }
        }

        public void ClearSlotHints()
        {
            if (_slotMarks[0] == null) return;
            foreach (Transform mark in _slotMarks) mark.gameObject.SetActive(false);
        }

        private void EnsureSlotMarks()
        {
            for (int slot = 0; slot < _slotMarks.Length; slot++)
            {
                _slotMarks[slot] ??= CardMeshFactory.CreateMeshObject(
                    $"SlotHint{slot}", _ghostRoot, CardMeshFactory.Quad, Color.clear);
            }
        }

        public void OnPointerClick(PointerEventData eventData) => ClickHandler?.Invoke(this, eventData);
    }
}
