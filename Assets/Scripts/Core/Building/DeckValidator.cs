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
        // §3.2（2026-11-22 起）：主卡组只约束总张数与同名上限，**不设类别配比**。
        // 把基础攻防牌整体优化出卡组是允许的构筑选择，由卡牌强度自然调节。

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

            // 形态牌只能待在形态卡组里（§7.1）
            foreach (CardDefinition form in deck.MainDeck.Where(c => c.Role == CardRole.Form).Distinct())
            {
                result.Errors.Add($"形态牌「{form.Name}」不能编入主卡组");
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

        /// <summary>
        /// §7.1：职业牌只能编进学过该职业的角色卡组；
        /// §3.3：英雄技能牌还要求该职业达到 10 级精通。
        /// </summary>
        private static void ValidateAgainstCharacter(DeckList deck, CharacterSheet sheet, DeckValidationResult result)
        {
            IEnumerable<CardDefinition> allCards = deck.MainDeck.Concat(deck.FormDecks.Values.SelectMany(f => f));

            // 主卡组里的每张职业牌（含升变牌）都要是角色学过的职业。
            // 形态卡组不单独查：它跟着升变牌走，升变牌合法形态组就合法
            foreach (CardDefinition card in deck.MainDeck
                         .Where(c => c.Category == CardCategory.Class && c.ClassId != null)
                         .Distinct())
            {
                if (!sheet.HasClass(card.ClassId))
                {
                    result.Errors.Add($"职业牌「{card.Name}」属于 {card.ClassId}，但角色没有该职业");
                }
            }

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

                // 形态牌的职业要和它所在的升变牌一致。
                // 以后主线升变若要共用形态牌（design_backlog O-4），再放开这条
                if (transform.ClassId != null)
                {
                    foreach (CardDefinition off in form
                                 .Where(c => c.Role == CardRole.Form && c.ClassId != transform.ClassId)
                                 .Distinct())
                    {
                        result.Errors.Add(
                            $"「{transform.Name}」（{transform.ClassId}）的形态组里混了"
                            + $"{off.ClassId ?? "无职业"}的形态牌「{off.Name}」");
                    }
                }
            }

            foreach (string orphan in deck.FormDecks.Keys.Where(id => !transformIds.Contains(id)))
            {
                result.Errors.Add($"形态额外卡组 {orphan} 没有对应的升变牌编入主卡组");
            }
        }
    }
}
