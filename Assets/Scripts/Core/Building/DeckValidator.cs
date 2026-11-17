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

        public static DeckValidationResult Validate(DeckList deck)
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
                        + (def.Kind == CardKind.Transform ? "（同名升变牌限 1 张）" : ""));
                }
            }

            ValidateFormDecks(deck, result);
            return result;
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

                CardDefinition stray = form.FirstOrDefault(c => c.Kind != CardKind.Form && c.Kind != CardKind.Hero);
                if (stray != null)
                {
                    result.Errors.Add($"「{transform.Name}」的形态组里混入了非形态牌「{stray.Name}」");
                }
            }

            foreach (string orphan in deck.FormDecks.Keys.Where(id => !transformIds.Contains(id)))
            {
                result.Errors.Add($"形态额外卡组 {orphan} 没有对应的升变牌编入主卡组");
            }
        }
    }
}
