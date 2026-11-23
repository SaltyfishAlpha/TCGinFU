using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    public sealed class DeckValidationResult
    {
        public List<string> Errors { get; } = new();
        public bool IsValid => Errors.Count == 0;

        public override string ToString()
            => IsValid ? "构筑合法" : string.Join("\n", Errors);
    }

    /// <summary>
    /// 主卡组构筑校验（§3.2）。一次返回**全部**违规项而不是第一条——
    /// 构筑界面要一次性把问题都标红，而不是让玩家改一条试一次。
    /// </summary>
    public static class DeckValidator
    {
        private static readonly (CardCategory Category, string Label, int Min, int Max)[] CategoryRules =
        {
            (CardCategory.Class, "职业牌（含升变牌）", 12, 16),
            (CardCategory.Weapon, "武器牌", 3, 8),
            (CardCategory.Armor, "防具牌", 3, 8),
            (CardCategory.Treasure, "秘宝牌", 0, 6),
        };

        /// <param name="sheet">
        /// 可选。传入时额外校验与角色相关的规则（英雄技能牌的 10 级精通要求）；
        /// 不传则只做纯卡表层面的校验，方便在没有具体角色的编辑器里用。
        /// </param>
        public static DeckValidationResult Validate(DeckList deck, CharacterSheet sheet = null)
        {
            var result = new DeckValidationResult();
            if (deck == null)
            {
                result.Errors.Add("卡表为空");
                return result;
            }

            if (deck.MainDeck.Count != DeckList.MainDeckSize)
            {
                result.Errors.Add($"主卡组必须恒定 {DeckList.MainDeckSize} 张，当前 {deck.MainDeck.Count} 张");
            }

            foreach ((CardCategory category, string label, int min, int max) in CategoryRules)
            {
                int count = deck.CountOf(category);
                if (count < min || count > max)
                {
                    result.Errors.Add($"{label}应为 {min}–{max} 张，当前 {count} 张");
                }
            }

            // 同名上限：普通牌 3 张，升变牌 1 张
            foreach (IGrouping<string, CardDefinition> group in deck.MainDeck.GroupBy(c => c.Id))
            {
                CardDefinition def = group.First();
                if (group.Count() > def.CopyLimit)
                {
                    result.Errors.Add(
                        $"「{def.Name}」最多编入 {def.CopyLimit} 张，当前 {group.Count()} 张"
                        + (def.Role == CardRole.Transform ? "（同名升变牌限 1 张）" : ""));
                }
            }

            ValidateFormDecks(deck, result);
            if (sheet != null) ValidateAgainstCharacter(deck, sheet, result);
            return result;
        }

        /// <summary>§3.3：英雄技能牌只有在该职业达到 10 级精通时才能获得。</summary>
        private static void ValidateAgainstCharacter(DeckList deck, CharacterSheet sheet, DeckValidationResult result)
        {
            IEnumerable<CardDefinition> allCards = deck.MainDeck.Concat(deck.FormDecks.Values.SelectMany(f => f));

            foreach (CardDefinition hero in allCards.Where(c => c.IsHeroSkill).Distinct())
            {
                if (hero.ClassId == null)
                {
                    result.Errors.Add($"英雄技能牌「{hero.Name}」没有标注所属职业");
                    continue;
                }

                if (!sheet.HasClass(hero.ClassId))
                {
                    result.Errors.Add($"英雄技能牌「{hero.Name}」要求职业 {hero.ClassId}，但角色没有该职业");
                }
                else if (sheet.LevelOf(hero.ClassId) < ClassLevel.MaxPerClass)
                {
                    result.Errors.Add(
                        $"英雄技能牌「{hero.Name}」要求 {hero.ClassId} 达到 {ClassLevel.MaxPerClass} 级精通，"
                        + $"当前 {sheet.LevelOf(hero.ClassId)} 级");
                }
            }

            foreach (CardDefinition transform in deck.TransformCards.Distinct())
            {
                if (transform.ClassId != null && !sheet.HasClass(transform.ClassId))
                {
                    result.Errors.Add($"升变牌「{transform.Name}」要求职业 {transform.ClassId}，但角色没有该职业");
                }
            }
        }

        private static void ValidateFormDecks(DeckList deck, DeckValidationResult result)
        {
            var transformIds = new HashSet<string>(deck.TransformCards.Select(c => c.Id));

            // 每编入 1 张升变牌，场外须对应配置 1 组形态额外卡组
            foreach (CardDefinition transform in deck.TransformCards)
            {
                if (!deck.FormDecks.TryGetValue(transform.Id, out List<CardDefinition> form))
                {
                    result.Errors.Add($"升变牌「{transform.Name}」缺少对应的形态额外卡组");
                    continue;
                }

                if (form.Count != DeckList.FormDeckSize)
                {
                    result.Errors.Add(
                        $"「{transform.Name}」的形态额外卡组须为 {DeckList.FormDeckSize} 张，当前 {form.Count} 张");
                }

                // 英雄技能牌是职业牌上的一个标记，可以编进形态组，不算混入
                CardDefinition stray = form.FirstOrDefault(c => c.Role != CardRole.Form && !c.IsHeroSkill);
                if (stray != null)
                {
                    result.Errors.Add($"「{transform.Name}」的形态组里混入了非形态牌「{stray.Name}」");
                }

                // 一组形态牌只能出自同一个形态。没标 formId 的（测试构造卡、英雄技能牌）跳过
                string[] formIds = form
                    .Where(c => c.Role == CardRole.Form && !string.IsNullOrEmpty(c.FormId))
                    .Select(c => c.FormId)
                    .Distinct()
                    .ToArray();

                if (formIds.Length > 1)
                {
                    result.Errors.Add(
                        $"「{transform.Name}」的形态组混了 {formIds.Length} 个形态的牌：{string.Join(" / ", formIds)}");
                }
            }

            foreach (string orphan in deck.FormDecks.Keys.Where(id => !transformIds.Contains(id)))
            {
                result.Errors.Add($"形态额外卡组 {orphan} 没有对应的升变牌编入主卡组");
            }
        }
    }
}
