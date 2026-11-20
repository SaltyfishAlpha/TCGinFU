using System.Linq;
using System.Text;
using TCGinFU.Core;
using TCGinFU.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TCGinFU.View
{
    /// <summary>
    /// 棋盘上的一名参战者——**一张有厚度的 3D 角色卡**，和手牌同一个扑克比例。
    /// 版式按实体角色卡排：名条 → 插画位 → HP 条 → 数值栏 → 意图栏。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class CombatantView : MonoBehaviour, IDropTarget, IPointerClickHandler
    {
        private Transform _body;
        private Transform _faceQuad;
        private Transform _highlight;
        private Transform _selectionRing;
        private CardFace _face;
        private Image _hpFill;
        private Text _nameText;
        private Text _statsText;
        private Text _intentText;
        private BoxCollider _collider;
        private Vector2 _sizePx;

        public Transform Root { get; private set; }
        public Combatant Combatant { get; private set; }

        /// <summary>
        /// 塞在角色卡**背后**的充能牌堆：与角色卡等大的牌横置（旋转 90°）压在
        /// 卡中心偏下的位置，一张比一张低（§5.2）。玩家角色才有。
        /// <para>只负责显示，不是放置目标——充能走右键菜单。</para>
        /// </summary>
        public ZoneView ChargeStack { get; private set; }

        /// <summary>
        /// 升变期间盖在角色卡**上方**的那张升变牌——它就是这一局里的新角色卡（§6.2 ②）。
        /// <para>
        /// 往上错开一截而不是严丝合缝盖住：升变牌挡住名条和插画位就够表达「换了形态」，
        /// 下面的 HP 条、数值栏、意图栏还得看得见，否则升变期间就没法打牌了。
        /// </para>
        /// </summary>
        public ZoneView TransformSlot { get; private set; }

        public System.Func<CombatantView, CardView, bool> DropFilter { get; set; }
        public System.Action<CombatantView, CardView> DropHandler { get; set; }
        public System.Action<CombatantView> ClickHandler { get; set; }

        /// <summary>右键角色卡 = 看详情。左键留给选中目标，否则每切一次目标都弹窗。</summary>
        public System.Action<CombatantView> ContextHandler { get; set; }

        public static CombatantView Create(Transform parent, Vector2 sizePx, bool withChargeStack)
        {
            var go = new GameObject("CharacterCard", typeof(BoxCollider));
            go.transform.SetParent(parent, false);

            var view = go.AddComponent<CombatantView>();
            view.Root = go.transform;
            view._sizePx = sizePx;
            view._collider = go.GetComponent<BoxCollider>();

            float t = CardMeshFactory.CardThickness;
            float front = CardMeshFactory.TowardCamera;

            // 选中环与放置高亮都比卡大一圈、压在卡背后，只露出四周一圈
            view._selectionRing = CardMeshFactory.CreateMeshObject(
                "Selection", go.transform, CardMeshFactory.Quad, Color.clear);
            view._selectionRing.localScale = new Vector3(sizePx.x * 1.08f, sizePx.y * 1.06f, 1f);
            view._selectionRing.localPosition = new Vector3(0f, 0f, -front * (t * 0.5f + 0.05f));
            view._selectionRing.gameObject.SetActive(false);

            view._body = CardMeshFactory.CreateMeshObject(
                "Body", go.transform, CardMeshFactory.Box, new Color(0.07f, 0.08f, 0.1f, 1f));
            view._body.localScale = new Vector3(sizePx.x, sizePx.y, t);

            view._faceQuad = CardMeshFactory.CreateMeshObject(
                "FaceQuad", go.transform, CardMeshFactory.Quad, Color.gray);
            view._faceQuad.localScale = new Vector3(sizePx.x * 0.94f, sizePx.y * 0.95f, 1f);
            view._faceQuad.localPosition = new Vector3(0f, 0f, front * (t * 0.5f + 0.05f));

            view._highlight = CardMeshFactory.CreateMeshObject(
                "DropHighlight", go.transform, CardMeshFactory.Quad, Color.clear);
            view._highlight.localScale = new Vector3(sizePx.x * 1.02f, sizePx.y * 1.02f, 1f);
            view._highlight.localPosition = new Vector3(0f, 0f, -front * (t * 0.5f + 0.1f));
            view._highlight.gameObject.SetActive(false);

            view._collider.size = new Vector3(sizePx.x, sizePx.y, t);

            view.BuildFace(sizePx, t);

            if (withChargeStack)
            {
                view.BuildChargeStack(sizePx);
                view.BuildTransformSlot(sizePx);
            }

            return view;
        }

        private void BuildFace(Vector2 sizePx, float thickness)
        {
            _face = CardFace.Create(Root, sizePx, CardMeshFactory.TowardCamera * (thickness * 0.5f + 0.15f));

            _nameText = _face.AddText(
                "Name", "", 14, TextAnchor.MiddleCenter, new Vector2(0f, 0.86f), new Vector2(1f, 1f));

            _face.AddImage("Art", new Color(0f, 0f, 0f, 0.25f),
                new Vector2(0.06f, 0.46f), new Vector2(0.94f, 0.85f));

            Image track = _face.AddImage("HpTrack", new Color(0f, 0f, 0f, 0.55f),
                new Vector2(0.06f, 0.40f), new Vector2(0.94f, 0.45f));

            _hpFill = UiBuilder.CreateImage("HpFill", track.rectTransform, Color.green, raycast: false);
            _hpFill.rectTransform.Stretch();
            _hpFill.type = Image.Type.Filled;
            _hpFill.fillMethod = Image.FillMethod.Horizontal;
            _hpFill.fillOrigin = (int)Image.OriginHorizontal.Left;

            _statsText = _face.AddText(
                "Stats", "", 11, TextAnchor.UpperLeft, new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.39f));

            _intentText = _face.AddText(
                "Intent", "", 11, TextAnchor.LowerLeft, new Vector2(0.06f, 0.01f), new Vector2(0.94f, 0.14f),
                new Color(1f, 0.85f, 0.5f));
        }

        private void BuildChargeStack(Vector2 sizePx)
        {
            // 充能牌与角色卡等大，横置在卡中心的下方
            const float centerOffset = 0.3f;

            ChargeStack = ZoneView.Create(Root, new ZoneLayout
            {
                key = "charge_stack",
                displayName = "充能",
                source = ZoneSource.充能区,
                arrange = ZoneArrange.横置,
                acceptsDrop = false, // 充能走右键菜单
                faceDown = true,
                showLabel = false,
                reservedSlots = 0,
                stackStep = Mathf.Max(10f, sizePx.y * 0.09f),
                cardScale = 1f,
                anchorMin = new Vector2(0.5f, 0.5f),
                anchorMax = new Vector2(0.5f, 0.5f),
                offsetMin = new Vector2(-sizePx.x * 0.5f, -sizePx.y * (0.5f + centerOffset)),
                offsetMax = new Vector2(sizePx.x * 0.5f, sizePx.y * (0.5f - centerOffset)),
                background = Color.clear,
                border = Color.clear,
            }, sizePx, Vector2.zero);
        }

        private void BuildTransformSlot(Vector2 sizePx)
        {
            // 往上错开 22%：盖住名条与插画位，留出下半截的 HP 条和数值栏
            const float riseRatio = 0.22f;

            TransformSlot = ZoneView.Create(Root, new ZoneLayout
            {
                key = "transform_overlay",
                displayName = "升变",
                source = ZoneSource.空,
                arrange = ZoneArrange.堆叠,
                acceptsDrop = false,
                showLabel = false,
                reservedSlots = 0,
                cardScale = 1f,
                anchorMin = new Vector2(0.5f, 0.5f),
                anchorMax = new Vector2(0.5f, 0.5f),
                offsetMin = new Vector2(-sizePx.x * 0.5f, -sizePx.y * (0.5f - riseRatio)),
                offsetMax = new Vector2(sizePx.x * 0.5f, sizePx.y * (0.5f + riseRatio)),
                background = Color.clear,
                border = Color.clear,
            }, sizePx, Vector2.zero);

            // 推到角色卡前面去，否则会被不透明的卡面挡住
            TransformSlot.Root.localPosition += new Vector3(
                0f, 0f, CardMeshFactory.TowardCamera * CardMeshFactory.CardThickness * 3f);
        }

        public void Bind(Combatant combatant, bool isEnemy, bool selected)
        {
            Combatant = combatant;

            bool crisis = combatant is PlayerCharacter pc && pc.IsInCrisis;
            CardMeshFactory.SetColor(_faceQuad, UiBuilder.TeamColor(isEnemy, combatant.IsDown, crisis));

            CardMeshFactory.SetColor(_selectionRing, new Color(1f, 0.85f, 0.25f, 1f));
            _selectionRing.gameObject.SetActive(selected);

            float ratio = combatant.MaxHp <= 0 ? 0f : Mathf.Clamp01((float)combatant.Hp / combatant.MaxHp);
            _hpFill.fillAmount = ratio;
            _hpFill.color = ratio > 0.5f
                ? new Color(0.35f, 0.75f, 0.35f)
                : ratio > 0.25f
                    ? new Color(0.85f, 0.7f, 0.25f)
                    : new Color(0.85f, 0.3f, 0.3f);

            string rowTag = combatant.Row == Row.Front ? "前" : "后";
            _nameText.text = $"{combatant.Name} <color=#aaaaaa>{rowTag}{combatant.Slot}</color>";
            _statsText.text = BuildStats(combatant);
            _intentText.text = BuildIntent(combatant);
        }

        private static string BuildStats(Combatant combatant)
        {
            var builder = new StringBuilder();
            builder.Append($"HP {combatant.Hp}/{combatant.MaxHp}\n");

            if (combatant is PlayerCharacter player)
            {
                builder.Append($"MP {player.Mp}  AP {player.Ap}\n");
                builder.Append($"盾 物{player.Shields.Physical} 魔{player.Shields.Magical}\n");
                builder.Append($"手{player.Zones.Hand.Count} 充{player.Zones.Charges.Count}/{player.Zones.Charges.Capacity}");
                if (player.IsInCrisis) builder.Append(" <color=#ff8080>危机</color>");
                if (player.Transform.Active) builder.Append($" <color=#ffd24d>升变{player.Transform.RoundsRemaining}</color>");
            }
            else if (combatant is Enemy enemy)
            {
                builder.Append($"抵抗 {enemy.CritResist}\n");
                builder.Append(BattleSession.AffinitySummary(enemy));
                if (enemy.Shields.Physical > 0 || enemy.Shields.Magical > 0)
                {
                    builder.Append($"\n盾 物{enemy.Shields.Physical} 魔{enemy.Shields.Magical}");
                }
            }

            string statuses = string.Join(" ", combatant.Statuses.All.Select(s => s.ToString()));
            if (!string.IsNullOrEmpty(statuses)) builder.Append($"\n<color=#b0a0ff>{statuses}</color>");

            return builder.ToString();
        }

        private static string BuildIntent(Combatant combatant)
        {
            if (combatant is not Enemy enemy || enemy.IsDown || enemy.Intents.Count == 0) return "";

            return string.Join("\n", enemy.Intents.Select(intent => enemy.IntentFullyVisible
                ? $"▸{intent.Label} {intent.Damage}{intent.Element.DisplayName()}"
                : $"▸{intent.Label}(?)"));
        }

        public bool CanAccept(CardView card)
            => Combatant != null && !Combatant.IsDown && (DropFilter == null || DropFilter(this, card));

        public void AcceptCard(CardView card) => DropHandler?.Invoke(this, card);

        public void SetDropHighlight(CardHighlight state)
        {
            Color color = state switch
            {
                CardHighlight.合法 => new Color(0.25f, 0.6f, 0.32f, 1f),
                CardHighlight.悬停 => new Color(0.4f, 1f, 0.5f, 1f),
                CardHighlight.非法 => new Color(0.6f, 0.22f, 0.22f, 1f),
                _ => Color.clear,
            };

            CardMeshFactory.SetColor(_highlight, color);
            _highlight.gameObject.SetActive(color.a > 0.01f);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right) ContextHandler?.Invoke(this);
            else ClickHandler?.Invoke(this);
        }
    }
}
