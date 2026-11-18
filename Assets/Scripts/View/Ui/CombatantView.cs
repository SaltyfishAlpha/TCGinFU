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
    /// 棋盘上的一名参战者——**它本身就是一张角色卡**，和手牌同一个扑克比例，
    /// 版式也按实体卡来：顶上名条、中间插画位、下面数值栏、底部意图栏。
    /// 同时是拖拽的放置目标：把攻击牌拖到敌人卡上就等于指定它为目标。
    /// </summary>
    public sealed class CombatantView : MonoBehaviour, IDropTarget, IPointerClickHandler
    {
        private Image _background;
        private Image _artBand;
        private Image _dropHighlight;
        private Image _selectionRing;
        private Image _hpFill;
        private Text _nameText;
        private Text _statsText;
        private Text _intentText;

        public RectTransform Rect { get; private set; }
        public Combatant Combatant { get; private set; }

        /// <summary>
        /// 塞在角色卡**背后**的充能牌堆。实体打牌里给队友充能就是把一张牌横着
        /// 塞到他的角色卡下面，只露出下缘一条（§5.2），所以充能不单独占场地。
        /// 玩家角色才有。
        /// </summary>
        public ZoneView ChargeStack { get; private set; }

        public System.Func<CombatantView, CardView, bool> DropFilter { get; set; }
        public System.Action<CombatantView, CardView> DropHandler { get; set; }
        public System.Action<CombatantView> ClickHandler { get; set; }

        public static CombatantView Create(Transform parent, Vector2 cardSize, bool withChargeStack)
        {
            RectTransform rect = UiBuilder.CreateRect("CharacterCard", parent);
            var view = rect.gameObject.AddComponent<CombatantView>();
            view.Rect = rect;

            // 充能牌堆**最先**创建 → 兄弟序最小 → 画在角色卡底板之下，
            // 被不透明的卡面挡住大半，只露出往下错开的那几条边
            if (withChargeStack)
            {
                view.ChargeStack = ZoneView.Create(rect, new ZoneLayout
                {
                    key = "charge_stack",
                    displayName = "充能",
                    source = ZoneSource.充能区,
                    arrange = ZoneArrange.横置,
                    acceptsDrop = true,
                    faceDown = true,
                    showLabel = false,
                    reservedSlots = 1,
                    stackStep = Mathf.Max(10f, cardSize.y * 0.085f),
                    cardScale = 1f,
                    // 贴着卡的下半部分，往下延伸出卡外一截
                    anchorMin = new Vector2(0.5f, 0f),
                    anchorMax = new Vector2(0.5f, 0f),
                    offsetMin = new Vector2(-cardSize.x * 0.5f, -cardSize.y * 0.42f),
                    offsetMax = new Vector2(cardSize.x * 0.5f, cardSize.y * 0.12f),
                    background = Color.clear,
                    border = Color.clear,
                }, cardSize * 0.78f);
            }

            view._background = UiBuilder.CreateImage("CardFace", rect, Color.gray);
            view._background.rectTransform.Stretch();

            view._selectionRing = UiBuilder.CreateImage("Selection", rect, Color.clear, raycast: false);
            view._selectionRing.rectTransform.Stretch(-3f);

            // ── 名条 ──
            view._nameText = UiBuilder.CreateText("Name", rect, "", 14, TextAnchor.MiddleCenter);
            view._nameText.rectTransform.anchorMin = new Vector2(0f, 0.86f);
            view._nameText.rectTransform.anchorMax = new Vector2(1f, 1f);
            view._nameText.rectTransform.offsetMin = new Vector2(4f, 0f);
            view._nameText.rectTransform.offsetMax = new Vector2(-4f, -2f);

            // ── 插画位（暂时是一块纯色，将来换立绘）──
            view._artBand = UiBuilder.CreateImage("Art", rect, new Color(0f, 0f, 0f, 0.22f), raycast: false);
            view._artBand.rectTransform.anchorMin = new Vector2(0.06f, 0.46f);
            view._artBand.rectTransform.anchorMax = new Vector2(0.94f, 0.85f);
            view._artBand.rectTransform.offsetMin = Vector2.zero;
            view._artBand.rectTransform.offsetMax = Vector2.zero;

            // ── HP 条 ──
            Image track = UiBuilder.CreateImage("HpTrack", rect, new Color(0f, 0f, 0f, 0.5f), raycast: false);
            track.rectTransform.anchorMin = new Vector2(0.06f, 0.40f);
            track.rectTransform.anchorMax = new Vector2(0.94f, 0.45f);
            track.rectTransform.offsetMin = Vector2.zero;
            track.rectTransform.offsetMax = Vector2.zero;

            view._hpFill = UiBuilder.CreateImage("HpFill", track.rectTransform, Color.green, raycast: false);
            view._hpFill.rectTransform.Stretch();
            view._hpFill.type = Image.Type.Filled;
            view._hpFill.fillMethod = Image.FillMethod.Horizontal;
            view._hpFill.fillOrigin = (int)Image.OriginHorizontal.Left;

            // ── 数值栏 ──
            view._statsText = UiBuilder.CreateText("Stats", rect, "", 11, TextAnchor.UpperLeft);
            view._statsText.rectTransform.anchorMin = new Vector2(0.06f, 0.14f);
            view._statsText.rectTransform.anchorMax = new Vector2(0.94f, 0.39f);
            view._statsText.rectTransform.offsetMin = Vector2.zero;
            view._statsText.rectTransform.offsetMax = Vector2.zero;

            // ── 意图栏 ──
            view._intentText = UiBuilder.CreateText(
                "Intent", rect, "", 11, TextAnchor.LowerLeft, new Color(1f, 0.85f, 0.5f));
            view._intentText.rectTransform.anchorMin = new Vector2(0.06f, 0.01f);
            view._intentText.rectTransform.anchorMax = new Vector2(0.94f, 0.14f);
            view._intentText.rectTransform.offsetMin = Vector2.zero;
            view._intentText.rectTransform.offsetMax = Vector2.zero;

            // 放置高亮盖在卡面之上
            view._dropHighlight = UiBuilder.CreateImage("DropHighlight", rect, Color.clear, raycast: false);
            view._dropHighlight.rectTransform.Stretch();

            return view;
        }

        public void Bind(Combatant combatant, bool isEnemy, bool selected, Vector2 size)
        {
            Combatant = combatant;
            Rect.sizeDelta = size;

            bool crisis = combatant is PlayerCharacter pc && pc.IsInCrisis;
            _background.color = UiBuilder.TeamColor(isEnemy, combatant.IsDown, crisis);
            _selectionRing.color = selected ? new Color(1f, 0.9f, 0.3f, 0.8f) : Color.clear;

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
            _dropHighlight.color = state switch
            {
                CardHighlight.合法 => new Color(0.4f, 1f, 0.5f, 0.18f),
                CardHighlight.悬停 => new Color(0.4f, 1f, 0.5f, 0.42f),
                CardHighlight.非法 => new Color(1f, 0.3f, 0.3f, 0.2f),
                _ => Color.clear,
            };
        }

        public void OnPointerClick(PointerEventData eventData) => ClickHandler?.Invoke(this);
    }
}
