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
        /// <summary>标准大卡 / 美式扑克的高宽比：2.5 × 3.5 英寸。</summary>
        public const float PokerAspect = 3.5f / 2.5f;

        [Tooltip("设计分辨率。Canvas 按它等比缩放")]
        public Vector2 referenceResolution = new(1920f, 1080f);

        [Tooltip("标准卡牌宽度（像素）。高度按美式扑克比例 1:1.4 自动算出，不可单独改")]
        [Range(40f, 400f)]
        public float cardWidth = 120f;

        [Tooltip("桌面底色")]
        public Color tableColor = new(0.11f, 0.12f, 0.15f, 1f);

        [Tooltip("全部区域。顺序即渲染层级，靠后的盖在上面")]
        public List<ZoneLayout> zones = new();

        /// <summary>
        /// 标准卡牌尺寸。**高度恒按扑克比例算**，所以无论怎么缩放都不会变形——
        /// 各区域只能通过 <see cref="ZoneLayout.cardScale"/> 整体缩放。
        /// </summary>
        public Vector2 CardSize => new(cardWidth, cardWidth * PokerAspect);

        public Vector2 CardSizeFor(ZoneLayout zone) => CardSize * Mathf.Max(0.05f, zone.cardScale);

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
                // 敌方按**敌人自己的视角**排：它们的后排在更远处，也就是屏幕更上方
                new()
                {
                    key = "enemy_back", displayName = "敌方后排", source = ZoneSource.敌方后排,
                    anchorMin = new Vector2(0.12f, 0.80f), anchorMax = new Vector2(0.88f, 0.955f),
                    arrange = ZoneArrange.横排, combatantSize = new Vector2(200f, 118f), spacing = 18f,
                    reservedSlots = 2, acceptsDrop = true,
                    background = new Color(0.6f, 0.2f, 0.2f, 0.05f),
                },
                new()
                {
                    key = "enemy_front", displayName = "敌方前排", source = ZoneSource.敌方前排,
                    anchorMin = new Vector2(0.12f, 0.615f), anchorMax = new Vector2(0.88f, 0.79f),
                    arrange = ZoneArrange.横排, combatantSize = new Vector2(200f, 140f), spacing = 18f,
                    reservedSlots = 2, acceptsDrop = true,
                    background = new Color(0.6f, 0.2f, 0.2f, 0.08f),
                },

                // 我方按**玩家视角**排：前排靠近敌人（上），后排靠近自己（下）
                new()
                {
                    key = "party_front", displayName = "我方前排", source = ZoneSource.我方前排,
                    anchorMin = new Vector2(0.12f, 0.425f), anchorMax = new Vector2(0.88f, 0.60f),
                    arrange = ZoneArrange.横排, combatantSize = new Vector2(220f, 150f), spacing = 18f,
                    reservedSlots = 2, acceptsDrop = true,
                    background = new Color(0.2f, 0.45f, 0.7f, 0.08f),
                },
                new()
                {
                    key = "party_back", displayName = "我方后排", source = ZoneSource.我方后排,
                    anchorMin = new Vector2(0.12f, 0.24f), anchorMax = new Vector2(0.88f, 0.415f),
                    arrange = ZoneArrange.横排, combatantSize = new Vector2(220f, 150f), spacing = 18f,
                    reservedSlots = 2, acceptsDrop = true,
                    background = new Color(0.2f, 0.45f, 0.7f, 0.06f),
                },

                new()
                {
                    key = "deck", displayName = "卡组", source = ZoneSource.卡组,
                    anchorMin = new Vector2(0.008f, 0.03f), anchorMax = new Vector2(0.105f, 0.23f),
                    arrange = ZoneArrange.堆叠, cardScale = 0.95f, faceDown = true, reservedSlots = 1,
                },
                new()
                {
                    key = "hand", displayName = "手牌", source = ZoneSource.手牌,
                    anchorMin = new Vector2(0.115f, 0.015f), anchorMax = new Vector2(0.885f, 0.235f),
                    arrange = ZoneArrange.扇形, cardScale = 1.1f, spacing = 14f,
                    draggable = true, reservedSlots = 5,
                    background = new Color(1f, 1f, 1f, 0.04f),
                },
                new()
                {
                    key = "continuous", displayName = "持续区", source = ZoneSource.持续区,
                    anchorMin = new Vector2(0.895f, 0.245f), anchorMax = new Vector2(0.992f, 0.44f),
                    arrange = ZoneArrange.堆叠, cardScale = 0.95f, reservedSlots = 1,
                    background = new Color(0.5f, 0.3f, 0.8f, 0.09f),
                },
                new()
                {
                    key = "discard", displayName = "弃牌堆", source = ZoneSource.弃牌堆,
                    anchorMin = new Vector2(0.895f, 0.03f), anchorMax = new Vector2(0.992f, 0.23f),
                    arrange = ZoneArrange.堆叠, cardScale = 0.95f, reservedSlots = 1,
                },
            };

            return asset;
        }

        /// <summary>
        /// 修掉明显无效的值。
        /// <para>
        /// 必须有这一步：Unity 给**已存在的资产**补上新加的字段时，用的是类型的零值
        /// （float = 0、Vector2 = (0,0)），而不是 C# 里写的字段初始值。
        /// 所以旧资产升级后 cardScale 会变成 0，卡牌直接缩没。
        /// </para>
        /// </summary>
        public void Normalize()
        {
            if (cardWidth < 40f) cardWidth = 120f;
            cardWidth = Mathf.Clamp(cardWidth, 40f, 400f);

            foreach (ZoneLayout zone in zones.Where(z => z != null))
            {
                if (zone.cardScale <= 0.01f) zone.cardScale = 1f;
                zone.cardScale = Mathf.Clamp(zone.cardScale, 0.2f, 2f);

                if (zone.IsCombatantZone && (zone.combatantSize.x < 1f || zone.combatantSize.y < 1f))
                {
                    zone.combatantSize = new Vector2(210f, 150f);
                }

                zone.reservedSlots = Mathf.Clamp(zone.reservedSlots, 0, 12);
            }
        }

        private void OnValidate() => Normalize();
    }
}
