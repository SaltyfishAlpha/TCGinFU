using System;
using System.Collections.Generic;
using System.Linq;
using TCGinFU.Core;
using UnityEngine;

namespace TCGinFU.Data
{
    /// <summary>主卡组里的一项：某张卡 × 几张。</summary>
    [Serializable]
    public sealed class DeckEntry
    {
        public CardDefinitionAsset card;

        [Min(1)]
        [Tooltip("编入张数。普通牌最多 3 张，同名升变牌限 1 张（§3.2）")]
        public int count = 1;
    }

    /// <summary>一张升变牌 + 它那组 10 张形态额外卡组（§3.1）。</summary>
    [Serializable]
    public sealed class FormDeckEntry
    {
        [Tooltip("对应的升变牌，必须同时编在主卡组里")]
        public CardDefinitionAsset transformCard;

        [Tooltip("形态额外卡组，固定 10 张，不计入主卡组 30 张")]
        public List<CardDefinitionAsset> cards = new();
    }

    /// <summary>战斗外构筑出来的一份卡表资产。</summary>
    [CreateAssetMenu(menuName = "TCGinFU/卡组", fileName = "Deck_")]
    public sealed class DeckListAsset : ScriptableObject
    {
        [Tooltip("主卡组，恒定 30 张")]
        [SerializeField] private List<DeckEntry> mainDeck = new();

        [Tooltip("每张升变牌各配一组形态额外卡组")]
        [SerializeField] private List<FormDeckEntry> formDecks = new();

        public DeckList ToDeckList()
        {
            var deck = new DeckList();

            foreach (DeckEntry entry in mainDeck.Where(e => e?.card != null))
            {
                CardDefinition definition = entry.card.ToDefinition();
                for (int i = 0; i < entry.count; i++) deck.MainDeck.Add(definition);
            }

            foreach (FormDeckEntry entry in formDecks.Where(e => e?.transformCard != null))
            {
                deck.FormDecks[entry.transformCard.Id] = entry.cards
                    .Where(c => c != null)
                    .Select(c => c.ToDefinition())
                    .ToList();
            }

            return deck;
        }

        /// <summary>按 §3.2 校验，返回全部违规项。</summary>
        public DeckValidationResult Validate() => DeckValidator.Validate(ToDeckList());

        [ContextMenu("校验构筑")]
        private void LogValidation()
        {
            DeckValidationResult result = Validate();
            if (result.IsValid) Debug.Log($"[{name}] 构筑合法", this);
            else Debug.LogWarning($"[{name}] 构筑不合法：\n{result}", this);
        }
    }
}
