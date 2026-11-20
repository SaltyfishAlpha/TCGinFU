using System.Collections.Generic;
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
    /// 卡牌详情面板：左键点任意一张看得见的牌都会弹出它。
    /// <para>
    /// 棋盘上的 3D 卡太小，塞不下卡面规则文字；而规则文字恰恰是打牌时最需要读的东西。
    /// 所以「看牌」和「打牌」分开：左键看详情，拖拽才是打出。
    /// </para>
    /// </summary>
    public sealed class CardDetailPanel : MonoBehaviour, IPointerClickHandler
    {
        private Text _title;
        private Text _subtitle;
        private Text _body;
        private Image _accent;
        private RectTransform _content;

        public bool IsOpen => gameObject.activeSelf;

        public static CardDetailPanel Create(Transform parent)
        {
            Image backdrop = UiBuilder.CreateImage("CardDetail", parent, new Color(0f, 0f, 0f, 0.62f));
            backdrop.rectTransform.Stretch();

            var panel = backdrop.gameObject.AddComponent<CardDetailPanel>();

            Image window = UiBuilder.CreateImage(
                "Window", backdrop.transform, new Color(0.15f, 0.16f, 0.20f, 0.99f));
            window.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            window.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            window.rectTransform.sizeDelta = new Vector2(560f, 540f);
            window.rectTransform.anchoredPosition = Vector2.zero;
            UiBuilder.CreateBorder(window.rectTransform, new Color(1f, 1f, 1f, 0.26f));

            // 顶部色条：按卡牌来源上色，和棋盘上那张卡对得上
            panel._accent = UiBuilder.CreateImage("Accent", window.transform, Color.gray, raycast: false);
            panel._accent.rectTransform.anchorMin = new Vector2(0f, 1f);
            panel._accent.rectTransform.anchorMax = new Vector2(1f, 1f);
            panel._accent.rectTransform.pivot = new Vector2(0.5f, 1f);
            panel._accent.rectTransform.sizeDelta = new Vector2(0f, 64f);

            panel._title = UiBuilder.CreateText(
                "Title", panel._accent.rectTransform, "", 24, TextAnchor.MiddleLeft);
            panel._title.rectTransform.Stretch();
            panel._title.rectTransform.offsetMin = new Vector2(18f, 0f);
            panel._title.rectTransform.offsetMax = new Vector2(-18f, 0f);

            panel._subtitle = UiBuilder.CreateText(
                "Subtitle", window.transform, "", 15, TextAnchor.UpperLeft,
                new Color(0.82f, 0.86f, 0.95f));
            panel._subtitle.rectTransform.anchorMin = new Vector2(0f, 1f);
            panel._subtitle.rectTransform.anchorMax = new Vector2(1f, 1f);
            panel._subtitle.rectTransform.pivot = new Vector2(0.5f, 1f);
            panel._subtitle.rectTransform.sizeDelta = new Vector2(-36f, 54f);
            panel._subtitle.rectTransform.anchoredPosition = new Vector2(0f, -72f);

            // 正文可滚动：真实卡池里的规则文字有的很长
            var scroll = UiBuilder.CreateRect("Scroll", window.transform);
            scroll.anchorMin = Vector2.zero;
            scroll.anchorMax = Vector2.one;
            scroll.offsetMin = new Vector2(18f, 54f);
            scroll.offsetMax = new Vector2(-18f, -132f);

            var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scroll.gameObject.AddComponent<RectMask2D>();

            // 刻意**不用** VerticalLayoutGroup + ContentSizeFitter：
            // 它们和手动锚点是两套互相覆盖的机制，结果是宽度还没定下来就去量高度，
            // 文字既不换行又被 RectMask2D 裁掉。这里改成自己算：
            // 宽度由锚点拉伸确定 → 强制刷新一次 → 按真实宽度取 preferredHeight。
            panel._content = UiBuilder.CreateRect("Content", scroll);
            panel._content.anchorMin = new Vector2(0f, 1f);
            panel._content.anchorMax = new Vector2(1f, 1f);
            panel._content.pivot = new Vector2(0.5f, 1f);
            panel._content.anchoredPosition = Vector2.zero;
            scrollRect.content = panel._content;

            panel._body = UiBuilder.CreateText("Body", panel._content, "", 16, TextAnchor.UpperLeft);
            panel._body.rectTransform.anchorMin = Vector2.zero;
            panel._body.rectTransform.anchorMax = Vector2.one;
            panel._body.rectTransform.offsetMin = Vector2.zero;
            panel._body.rectTransform.offsetMax = Vector2.zero;

            Button close = UiBuilder.CreateButton("关闭", window.transform, 110f, panel.Close);
            RectTransform closeRect = close.GetComponent<RectTransform>();
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(0.5f, 0f);
            closeRect.pivot = new Vector2(0.5f, 0f);
            closeRect.sizeDelta = new Vector2(110f, 32f);
            closeRect.anchoredPosition = new Vector2(0f, 12f);

            backdrop.gameObject.SetActive(false);
            return panel;
        }

        /// <param name="viewer">
        /// 用来把公式算成具体数值的角色。传 null 则只显示公式。
        /// </param>
        public void Open(CardInstance card, PlayerCharacter viewer = null)
        {
            if (card?.Definition == null) return;
            Open(card.Definition, viewer);
        }

        public void Open(CardDefinition def, PlayerCharacter viewer = null)
        {
            if (def == null) return;

            _accent.color = CardView.ColorOf(def.Category);
            _title.text = def.Name;
            _subtitle.text = BuildSubtitle(def);
            _body.text = BuildBody(def, viewer);

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            ResizeContent();
        }

        /// <summary>
        /// 先让宽度按锚点定下来，再按这个宽度量换行后的高度。
        /// 顺序反了就会量到「一整行不换行」的高度，正文被裁掉一截。
        /// </summary>
        private void ResizeContent()
        {
            _content.sizeDelta = new Vector2(0f, 0f);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);

            float height = _body.preferredHeight + 12f;
            _content.sizeDelta = new Vector2(0f, height);
            _content.anchoredPosition = Vector2.zero;
        }

        /// <summary>棋盘上的角色详情。复用同一个面板——版式（标题 / 副标题 / 正文）正好合适。</summary>
        public void Open(Combatant combatant)
        {
            if (combatant == null) return;

            bool isEnemy = combatant is Enemy;
            bool crisis = combatant is PlayerCharacter pc && pc.IsInCrisis;
            _accent.color = UiBuilder.TeamColor(isEnemy, combatant.IsDown, crisis);

            _title.text = combatant.Name;
            _subtitle.text = BuildCombatantSubtitle(combatant);
            _body.text = BuildCombatantBody(combatant);

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            ResizeContent();
        }

        private static string BuildCombatantSubtitle(Combatant combatant)
        {
            string row = combatant.Row == Row.Front ? "前排" : "后排";
            var parts = new List<string> { $"{row} {combatant.Slot} 号位" };

            if (combatant is PlayerCharacter player)
            {
                parts.Add($"总 {player.Sheet.TotalLevel} 级");
                parts.Add(string.Join(" / ", player.Sheet.Classes.Select(c => $"{c.ClassId} {c.Level}")));
            }
            else if (combatant is Enemy enemy)
            {
                parts.Add(enemy.Rank switch
                {
                    EnemyRank.Normal => "普通小怪",
                    EnemyRank.Elite => "精英",
                    EnemyRank.Boss => "首领",
                    _ => enemy.Rank.ToString(),
                });
            }

            if (combatant.IsDown) parts.Add("<color=#ff8080>已倒下</color>");
            return string.Join("　|　", parts);
        }

        private static string BuildCombatantBody(Combatant combatant)
        {
            var builder = new StringBuilder();

            builder.AppendLine("<b>当前状态</b>");
            builder.AppendLine($"· HP {combatant.Hp} / {combatant.MaxHp}");
            builder.AppendLine(
                $"· 护盾　物理 {combatant.Shields.Physical}"
                + $"（其中自动 {combatant.Shields.AutoPhysical}，轮末清零）"
                + $"　魔法 {combatant.Shields.Magical}"
                + $"（其中自动 {combatant.Shields.AutoMagical}）");

            if (combatant is PlayerCharacter player) AppendPlayer(builder, player);
            else if (combatant is Enemy enemy) AppendEnemy(builder, enemy);

            if (combatant.Statuses.All.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("<b>状态</b>");
                foreach (StatusEffect status in combatant.Statuses.All) builder.AppendLine($"· {status}");
            }

            return builder.ToString().TrimEnd();
        }

        private static void AppendPlayer(StringBuilder builder, PlayerCharacter player)
        {
            CharacterSheet sheet = player.Sheet;

            builder.AppendLine($"· MP {player.Mp} / {player.MaxMp}　AP {player.Ap}");
            builder.AppendLine(
                $"· 危机值 {sheet.CrisisThreshold}"
                + (player.IsInCrisis ? "　<color=#ff8080>已进入危机</color>" : ""));

            builder.AppendLine();
            builder.AppendLine("<b>属性骰</b>");
            builder.AppendLine(
                $"· 力量 d{sheet.Dice.Face(AttributeColor.Might)}"
                + $"　洞察 d{sheet.Dice.Face(AttributeColor.Insight)}"
                + $"　敏捷 d{sheet.Dice.Face(AttributeColor.Dexterity)}"
                + $"　意志 d{sheet.Dice.Face(AttributeColor.Willpower)}");

            builder.AppendLine();
            builder.AppendLine("<b>派生数值</b>　<color=#999999>（规则书 §2.1 的公式）</color>");
            builder.AppendLine(
                $"· 最大 HP = 总等级 {sheet.TotalLevel} + 力量骰面 {sheet.Dice.Face(AttributeColor.Might)} × 5"
                + $"{BonusText(sheet.HpBonus)} = {sheet.MaxHp}");
            builder.AppendLine(
                $"· 最大 MP = 总等级 {sheet.TotalLevel} + 意志骰面 {sheet.Dice.Face(AttributeColor.Willpower)} × 5"
                + $"{BonusText(sheet.MpBonus)} = {sheet.MaxMp}");
            builder.AppendLine(
                $"· 每轮自动护盾　物理 {sheet.AutoPhysicalShield}（敏捷 ÷ 2）"
                + $"　魔法 {sheet.AutoMagicShield}（洞察 ÷ 2）");
            builder.AppendLine($"· 等级加值 L_bonus = {sheet.LevelBonus}　装备阶梯 {player.Tier}");

            builder.AppendLine();
            builder.AppendLine("<b>装备与形态基准</b>");
            builder.AppendLine(
                $"· 武器 {player.Weapon.Name}　本阶伤害 {player.Weapon.DamageAt(player.Tier)}"
                + $"　→ B_atk = {player.FormAttackBase}");
            builder.AppendLine(
                $"· 防具 {player.Armor.Name}　本阶护盾 {player.Armor.ShieldAt(player.Tier)}"
                + $"　→ B_def = {player.FormDefenseBase}");

            builder.AppendLine();
            builder.AppendLine("<b>区域</b>");
            builder.AppendLine(
                $"· 手牌 {player.Zones.Hand.Count}　卡组 {player.Zones.Deck.Count}"
                + $"　弃牌堆 {player.Zones.Discard.Count}"
                + $"　已完成升变 {player.Zones.CompletedTransform.Count}");
            builder.AppendLine(
                $"· 充能 {player.Zones.Charges.Count} / {player.Zones.Charges.Capacity}"
                + $"　持续区 {player.Zones.Continuous?.Name ?? "空"}");

            if (player.Transform.Active)
            {
                builder.AppendLine();
                builder.AppendLine(
                    $"<b>升变中</b>　<color=#ffd24d>{player.Transform.TransformCard?.Name}"
                    + $"（{player.Transform.ClassId}，剩 {player.Transform.RoundsRemaining} 轮）</color>");
            }

            builder.AppendLine();
            builder.AppendLine(
                $"<b>本局输出</b>　本轮 {player.DamageDealtThisRound}　上轮 {player.DamageDealtLastRound}"
                + "　<color=#999999>（前排全空时，仇恨指向上轮输出最高者）</color>");
        }

        private static void AppendEnemy(StringBuilder builder, Enemy enemy)
        {
            builder.AppendLine($"· 暴击抵抗 {enemy.CritResist}");
            builder.AppendLine(
                $"· 意图可见度　{(enemy.IntentFullyVisible ? "完全公开" : "半公开（数值与目标隐藏）")}");

            builder.AppendLine();
            builder.AppendLine("<b>属性克制</b>");
            builder.AppendLine("· " + BattleSession.AffinitySummary(enemy));

            if (enemy.Intents.Count == 0) return;

            builder.AppendLine();
            builder.AppendLine("<b>本轮意图</b>　<color=#999999>（自上而下依次结算）</color>");
            foreach (EnemyIntent intent in enemy.Intents)
            {
                builder.AppendLine(enemy.IntentFullyVisible
                    ? $"· {intent.Label}　{intent.Damage} 点{intent.Element.DisplayName()}"
                      + $"　→ {IntentTargetName(intent)}"
                    : $"· {intent.Label}（数值与目标隐藏）");
            }
        }

        private static string IntentTargetName(EnemyIntent intent) => intent.Targeting switch
        {
            IntentTargeting.DefaultSingle => "常规单体（优先前排）",
            IntentTargeting.FrontRowAoe => "前排全体",
            IntentTargeting.BackRowAoe => "后排全体",
            IntentTargeting.AllPlayers => "我方全体",
            IntentTargeting.DesignatedRowSingle =>
                $"{(intent.DesignatedRow == Row.Front ? "前" : "后")}排单体",
            _ => "不指向我方",
        };

        private static string BonusText(int bonus)
            => bonus == 0 ? "" : bonus > 0 ? $" + 加值 {bonus}" : $" − 补正 {-bonus}";

        private static string BuildSubtitle(CardDefinition def)
        {
            var parts = new List<string>
            {
                $"{CardView.Short(def.Category)} · {CardView.Short(def.Kind)}",
                BattleSession.CostText(def),
            };

            if (def.Colors.Count > 0)
            {
                parts.Add(string.Join(" / ", def.Colors.Select(ColorName)));
            }

            if (!string.IsNullOrEmpty(def.ClassId)) parts.Add(def.ClassId);

            return string.Join("　|　", parts);
        }

        private static string BuildBody(CardDefinition def, PlayerCharacter viewer)
        {
            var builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(def.RulesText))
            {
                builder.AppendLine(def.RulesText.Trim());
                builder.AppendLine();
            }

            if (def.Kind == CardKind.Transform && !string.IsNullOrWhiteSpace(def.TransformConditionText))
            {
                builder.AppendLine($"<b>升变前置条件</b>\n{def.TransformConditionText.Trim()}");
                builder.AppendLine();
            }

            // 结算效果是引擎真正执行的东西，和卡面文字分开列——
            // 两者对不上时一眼能看出来是数据写错了
            if (def.Effects.Count > 0)
            {
                builder.AppendLine(viewer == null
                    ? "<b>结算效果</b>"
                    : $"<b>结算效果</b>　<color=#999999>（按 {viewer.Name} 当前面板计算）</color>");
                foreach (EffectData effect in def.Effects) builder.AppendLine("· " + Describe(effect, viewer));
                builder.AppendLine();
            }

            if (!string.IsNullOrEmpty(def.BehaviourId))
            {
                builder.AppendLine(
                    $"<color=#b0a0ff>附带脚本化效果：{def.BehaviourId}</color>");
                builder.AppendLine();
            }

            if (def.WeaponBaseDamage > 0)
            {
                builder.AppendLine($"武器一阶基础伤害 {def.WeaponBaseDamage}"
                                   + (def.TwoHanded ? "（双手）" : "（单手）"));
            }

            if (def.ArmorBaseShield > 0) builder.AppendLine($"防具一阶基础护盾 {def.ArmorBaseShield}");

            if (builder.Length == 0) builder.Append("<color=#999999>（这张牌没有填任何说明）</color>");

            return builder.ToString().TrimEnd();
        }

        private static string Describe(EffectData effect, PlayerCharacter viewer)
        {
            string amount = DescribeAmount(effect.Amount, viewer);
            string target = TargetName(effect.Target.Scope);

            string core = effect.Kind switch
            {
                EffectKind.Damage => $"对{target}造成 {amount} 点{effect.Element.DisplayName()}伤害",
                EffectKind.Heal => $"治疗{target} {amount} 点",
                EffectKind.GainShield =>
                    $"给{target} {amount} 点{(effect.ShieldKind == ShieldKind.Physical ? "物理" : "魔法")}护盾",
                EffectKind.RestoreMp => $"{target}回复 {amount} 点 MP",
                EffectKind.GainAp => $"{target}获得 {amount} 点 AP",
                EffectKind.Draw => $"抽 {amount} 张牌",
                EffectKind.Discard => $"弃 {amount} 张牌",
                EffectKind.GainCharge => $"产生 {amount} 个充能",
                EffectKind.ApplyStatus =>
                    $"给{target}施加「{effect.StatusId}」×{amount}"
                    + (effect.Duration < 0 ? "（本局持续）" : $"（{effect.Duration} 轮）"),
                EffectKind.MoveRow => $"{target}改变排位",
                EffectKind.Cover => "替该目标承受这次伤害",
                _ => $"{effect.Kind} {amount} → {target}",
            };

            return effect.Condition == EffectCondition.Always
                ? core
                : $"{core}　<color=#ffd24d>[{ConditionName(effect.Condition)}]</color>";
        }

        /// <summary>
        /// 把 <see cref="ValueExpr"/> 写成人能读的公式，并在能算的时候补上当前数值。
        /// 形态牌的意义就在于同一张卡在 5 级和 50 级算出不同的数——
        /// 只显示 <c>FormAttack+4</c> 对玩家毫无信息量。
        /// </summary>
        private static string DescribeAmount(ValueExpr expr, PlayerCharacter viewer)
        {
            // 固定值本身就是结果，再补一遍「当前 N」是废话
            if (expr.Source == ValueSource.Flat) return FormulaText(expr);

            string formula = FormulaText(expr);
            if (viewer == null) return formula;

            int value = expr.Resolve(viewer.ValueContext());
            return $"{formula} <color=#ffd24d>= {value}</color>";
        }

        private static string FormulaText(ValueExpr expr)
        {
            if (expr.Source == ValueSource.Flat) return expr.Add.ToString();

            var builder = new StringBuilder(SourceName(expr.Source));
            if (expr.Times != 1) builder.Append($" × {expr.Times}");
            if (expr.DividedBy != 1) builder.Append($" ÷ {expr.DividedBy}");
            if (expr.Add > 0) builder.Append($" + {expr.Add}");
            else if (expr.Add < 0) builder.Append($" − {-expr.Add}");

            return builder.ToString();
        }

        private static string SourceName(ValueSource source) => source switch
        {
            ValueSource.FormAttack => "B_atk（武器伤害 + 等级加值）",
            ValueSource.FormDefense => "B_def（防具护盾 + 等级加值）",
            ValueSource.WeaponDamage => "武器伤害",
            ValueSource.ArmorShield => "防具护盾",
            ValueSource.LevelBonus => "等级加值",
            ValueSource.CasterTotalLevel => "角色总等级",
            ValueSource.ChargeSpent => "本次消耗的充能数",
            _ => source.ToString(),
        };

        private static string TargetName(TargetScope scope) => scope switch
        {
            TargetScope.Self => "自己",
            TargetScope.SingleEnemy => "单个敌人",
            TargetScope.AllEnemies => "全体敌人",
            TargetScope.FrontRowEnemies => "敌方前排",
            TargetScope.BackRowEnemies => "敌方后排",
            TargetScope.SingleAlly => "单名队友",
            TargetScope.AllAllies => "全体队友",
            TargetScope.AdjacentAllies => "相邻队友",
            TargetScope.CurrentTarget => "本次结算的目标",
            _ => scope.ToString(),
        };

        private static string ConditionName(EffectCondition condition) => condition switch
        {
            EffectCondition.OnCrit => "暴击时",
            EffectCondition.OnSuperCrit => "超暴击时",
            EffectCondition.IfCasterInCrisis => "自身危机时",
            EffectCondition.IfChargeSpent => "消耗过充能时",
            EffectCondition.IfTargetInCrisis => "目标危机时",
            _ => condition.ToString(),
        };

        private static string ColorName(AttributeColor color) => color switch
        {
            AttributeColor.Might => "<color=#ff7b6b>红·力量</color>",
            AttributeColor.Insight => "<color=#6bb4ff>蓝·洞察</color>",
            AttributeColor.Dexterity => "<color=#7bdf8a>绿·敏捷</color>",
            AttributeColor.Willpower => "<color=#c08bff>紫·意志</color>",
            _ => color.ToString(),
        };

        public void Close() => gameObject.SetActive(false);

        /// <summary>点窗体以外的地方关闭。窗体是带射线的子物体，点在上面不会冒泡到这里。</summary>
        public void OnPointerClick(PointerEventData eventData) => Close();
    }
}
