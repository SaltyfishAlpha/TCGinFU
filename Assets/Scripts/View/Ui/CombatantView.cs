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
    /// 一名参战者的面板。既是显示，也是拖拽的放置目标——
    /// 把攻击牌拖到敌人身上就等于指定它为目标。
    /// </summary>
    public sealed class CombatantView : MonoBehaviour, IDropTarget, IPointerClickHandler
    {
        private Image _background;
        private Image _dropHighlight;
        private Image _selectionRing;
        private Image _hpFill;
        private Text _nameText;
        private Text _statsText;
        private Text _intentText;

        public RectTransform Rect { get; private set; }
        public Combatant Combatant { get; private set; }

        public System.Func<CombatantView, CardView, bool> DropFilter { get; set; }
        public System.Action<CombatantView, CardView> DropHandler { get; set; }
        public System.Action<CombatantView> ClickHandler { get; set; }

        public static CombatantView Create(Transform parent)
        {
            RectTransform rect = UiBuilder.CreateRect("Combatant", parent);
            var view = rect.gameObject.AddComponent<CombatantView>();
            view.Rect = rect;

            view._background = UiBuilder.CreateImage("Bg", rect, Color.gray);
            view._background.rectTransform.Stretch();

            view._selectionRing = UiBuilder.CreateImage("Selection", rect, Color.clear, raycast: false);
            view._selectionRing.rectTransform.Stretch(-3f);
            view._selectionRing.transform.SetAsFirstSibling();

            view._dropHighlight = UiBuilder.CreateImage("DropHighlight", rect, Color.clear, raycast: false);
            view._dropHighlight.rectTransform.Stretch();

            view._nameText = UiBuilder.CreateText("Name", rect, "", 16, TextAnchor.UpperLeft);
            view._nameText.rectTransform.anchorMin = new Vector2(0f, 1f);
            view._nameText.rectTransform.anchorMax = new Vector2(1f, 1f);
            view._nameText.rectTransform.pivot = new Vector2(0.5f, 1f);
            view._nameText.rectTransform.sizeDelta = new Vector2(-12f, 22f);
            view._nameText.rectTransform.anchoredPosition = new Vector2(0f, -4f);

            // HP 条：底槽 + 填充
            Image track = UiBuilder.CreateImage("HpTrack", rect, new Color(0f, 0f, 0f, 0.45f), raycast: false);
            track.rectTransform.anchorMin = new Vector2(0f, 1f);
            track.rectTransform.anchorMax = new Vector2(1f, 1f);
            track.rectTransform.pivot = new Vector2(0.5f, 1f);
            track.rectTransform.sizeDelta = new Vector2(-12f, 10f);
            track.rectTransform.anchoredPosition = new Vector2(0f, -28f);

            view._hpFill = UiBuilder.CreateImage("HpFill", track.rectTransform, Color.green, raycast: false);
            view._hpFill.rectTransform.Stretch();
            view._hpFill.type = Image.Type.Filled;
            view._hpFill.fillMethod = Image.FillMethod.Horizontal;
            view._hpFill.fillOrigin = (int)Image.OriginHorizontal.Left;

            view._statsText = UiBuilder.CreateText("Stats", rect, "", 13, TextAnchor.UpperLeft);
            view._statsText.rectTransform.anchorMin = Vector2.zero;
            view._statsText.rectTransform.anchorMax = Vector2.one;
            view._statsText.rectTransform.offsetMin = new Vector2(6f, 4f);
            view._statsText.rectTransform.offsetMax = new Vector2(-6f, -42f);

            view._intentText = UiBuilder.CreateText(
                "Intent", rect, "", 12, TextAnchor.LowerLeft, new Color(1f, 0.85f, 0.5f));
            view._intentText.rectTransform.anchorMin = Vector2.zero;
            view._intentText.rectTransform.anchorMax = new Vector2(1f, 0f);
            view._intentText.rectTransform.pivot = new Vector2(0.5f, 0f);
            view._intentText.rectTransform.sizeDelta = new Vector2(-12f, 34f);
            view._intentText.rectTransform.anchoredPosition = new Vector2(0f, 4f);

            return view;
        }

        public void Bind(Combatant combatant, bool isEnemy, bool selected, Vector2 size)
        {
            Combatant = combatant;
            Rect.sizeDelta = size;

            bool crisis = combatant is PlayerCharacter pc && pc.IsInCrisis;
            _background.color = UiBuilder.TeamColor(isEnemy, combatant.IsDown, crisis);

            _selectionRing.color = selected ? new Color(1f, 0.9f, 0.3f, 0.75f) : Color.clear;

            float ratio = combatant.MaxHp <= 0 ? 0f : Mathf.Clamp01((float)combatant.Hp / combatant.MaxHp);
            _hpFill.fillAmount = ratio;
            _hpFill.color = ratio > 0.5f
                ? new Color(0.35f, 0.75f, 0.35f)
                : ratio > 0.25f
                    ? new Color(0.85f, 0.7f, 0.25f)
                    : new Color(0.85f, 0.3f, 0.3f);

            string rowTag = combatant.Row == Row.Front ? "前" : "后";
            _nameText.text = $"{combatant.Name}  <color=#aaaaaa>[{rowTag}{combatant.Slot}]</color>";

            _statsText.text = BuildStats(combatant);
            _intentText.text = BuildIntent(combatant);
        }

        private static string BuildStats(Combatant combatant)
        {
            var builder = new StringBuilder();
            builder.Append($"HP {combatant.Hp}/{combatant.MaxHp}");

            if (combatant is PlayerCharacter player)
            {
                builder.Append($"　MP {player.Mp}/{player.MaxMp}　AP {player.Ap}\n");
                builder.Append($"盾 物{player.Shields.Physical}/魔{player.Shields.Magical}");
                builder.Append($"　手{player.Zones.Hand.Count} 组{player.Zones.Deck.Count}");
                builder.Append($"　充能{player.Zones.Charges.Count}/{player.Zones.Charges.Capacity}");
                if (player.IsInCrisis) builder.Append("\n<color=#ff8080>危机</color>");
                if (player.Transform.Active) builder.Append($"　<color=#ffd24d>升变剩{player.Transform.RoundsRemaining}轮</color>");
            }
            else if (combatant is Enemy enemy)
            {
                builder.Append($"　抵抗 {enemy.CritResist}\n");
                builder.Append(BattleSession.AffinitySummary(enemy));
                if (enemy.Shields.Physical > 0 || enemy.Shields.Magical > 0)
                {
                    builder.Append($"\n盾 物{enemy.Shields.Physical}/魔{enemy.Shields.Magical}");
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
                ? $"▸ {intent.Label} {intent.Damage} 点{intent.Element.DisplayName()}"
                : $"▸ {intent.Label}（数值隐藏）"));
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
