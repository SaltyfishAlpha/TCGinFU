using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TCGinFU.View
{
    /// <summary>
    /// 整张战斗桌面的布局。改这个资产就能挪动 / 缩放 / 增删区域，
    /// 不需要改代码，也不需要动场景——<see cref="BattleView"/> 每次都按它重建界面。
    /// </summary>
    [CreateAssetMenu(menuName = "TCGinFU/战斗布局", fileName = "BattleLayout")]
    public sealed class BattleLayoutAsset : ScriptableObject
    {
        [Tooltip("设计分辨率。Canvas 按它等比缩放")]
        public Vector2 referenceResolution = new(1920f, 1080f);

        [Tooltip("桌面底色")]
        public Color tableColor = new(0.11f, 0.12f, 0.15f, 1f);

        [Tooltip("全部区域。顺序即渲染层级，靠后的盖在上面")]
        public List<ZoneLayout> zones = new();

        public ZoneLayout Find(string key) => zones.FirstOrDefault(z => z.key == key);

        /// <summary>
        /// 代码生成的默认布局。场景生成器在找不到布局资产时用它建一份，
        /// 之后你在 Inspector 里随便改。
        /// </summary>
        public static BattleLayoutAsset CreateDefault()
        {
            var asset = CreateInstance<BattleLayoutAsset>();
            asset.zones = new List<ZoneLayout>
            {
                new()
                {
                    key = "enemy_front", displayName = "敌方前排", source = ZoneSource.敌方前排,
                    anchorMin = new Vector2(0.12f, 0.70f), anchorMax = new Vector2(0.88f, 0.96f),
                    arrange = ZoneArrange.横排, cardSize = new Vector2(190f, 150f), spacing = 18f,
                    acceptsDrop = true, background = new Color(0.6f, 0.2f, 0.2f, 0.07f),
                },
                new()
                {
                    key = "enemy_back", displayName = "敌方后排", source = ZoneSource.敌方后排,
                    anchorMin = new Vector2(0.12f, 0.575f), anchorMax = new Vector2(0.88f, 0.69f),
                    arrange = ZoneArrange.横排, cardSize = new Vector2(190f, 110f), spacing = 18f,
                    acceptsDrop = true, background = new Color(0.6f, 0.2f, 0.2f, 0.05f),
                },
                new()
                {
                    key = "party_front", displayName = "我方前排", source = ZoneSource.我方前排,
                    anchorMin = new Vector2(0.12f, 0.395f), anchorMax = new Vector2(0.88f, 0.565f),
                    arrange = ZoneArrange.横排, cardSize = new Vector2(200f, 140f), spacing = 18f,
                    acceptsDrop = true, background = new Color(0.2f, 0.45f, 0.7f, 0.07f),
                },
                new()
                {
                    key = "party_back", displayName = "我方后排", source = ZoneSource.我方后排,
                    anchorMin = new Vector2(0.12f, 0.225f), anchorMax = new Vector2(0.88f, 0.385f),
                    arrange = ZoneArrange.横排, cardSize = new Vector2(200f, 140f), spacing = 18f,
                    acceptsDrop = true, background = new Color(0.2f, 0.45f, 0.7f, 0.05f),
                },
                new()
                {
                    key = "deck", displayName = "卡组", source = ZoneSource.卡组,
                    anchorMin = new Vector2(0.005f, 0.02f), anchorMax = new Vector2(0.105f, 0.21f),
                    arrange = ZoneArrange.堆叠, cardSize = new Vector2(110f, 154f), faceDown = true,
                },
                new()
                {
                    key = "charge", displayName = "充能区", source = ZoneSource.充能区,
                    anchorMin = new Vector2(0.005f, 0.225f), anchorMax = new Vector2(0.105f, 0.40f),
                    arrange = ZoneArrange.横排, cardSize = new Vector2(64f, 90f), spacing = 4f,
                    faceDown = true, acceptsDrop = true,
                    background = new Color(0.9f, 0.75f, 0.2f, 0.09f),
                },
                new()
                {
                    key = "hand", displayName = "手牌", source = ZoneSource.手牌,
                    anchorMin = new Vector2(0.115f, 0.01f), anchorMax = new Vector2(0.885f, 0.215f),
                    arrange = ZoneArrange.扇形, cardSize = new Vector2(130f, 182f), spacing = 14f,
                    draggable = true, background = new Color(1f, 1f, 1f, 0.04f),
                },
                new()
                {
                    key = "continuous", displayName = "持续区", source = ZoneSource.持续区,
                    anchorMin = new Vector2(0.895f, 0.225f), anchorMax = new Vector2(0.995f, 0.40f),
                    arrange = ZoneArrange.堆叠, cardSize = new Vector2(110f, 154f),
                    background = new Color(0.5f, 0.3f, 0.8f, 0.09f),
                },
                new()
                {
                    key = "discard", displayName = "弃牌堆", source = ZoneSource.弃牌堆,
                    anchorMin = new Vector2(0.895f, 0.02f), anchorMax = new Vector2(0.995f, 0.21f),
                    arrange = ZoneArrange.堆叠, cardSize = new Vector2(110f, 154f),
                },
            };

            return asset;
        }
    }
}
