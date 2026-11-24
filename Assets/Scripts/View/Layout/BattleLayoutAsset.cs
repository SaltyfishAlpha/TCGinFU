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

        [Header("3D 视角")]
        [Tooltip("桌面向后倾斜的角度。0 = 正对镜头的平面视角，越大越像趴在桌子上看")]
        [Range(0f, 60f)]
        public float boardTilt = 20f;

        [Tooltip("相机视野角度。越小透视越弱、越接近平面")]
        [Range(20f, 70f)]
        public float cameraFov = 38f;

        [Tooltip("取景余量。1 = 刚好装下桌面，调大则留白更多")]
        [Range(1f, 2f)]
        public float cameraMargin = 1.28f;

        [Tooltip("相机在桌面上方的抬高量，按桌面高度的比例。抬高后更俯视")]
        [Range(-0.5f, 0.8f)]
        public float cameraHeight = 0.18f;

        /// <summary>设计像素 → 世界单位的换算。桌面宽 1920 像素即 19.2 个单位。</summary>
        public const float PixelsPerUnit = 100f;

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

            // 五条横带（敌后 / 敌前 / 我前 / 我后 / 手牌）平分可用高度。
            // 角色也是卡，所以每条带的高度按标准卡高留，再加一点余量给横置的充能牌。
            asset.zones = new List<ZoneLayout>
            {
                // 敌方按**敌人自己的视角**排：它们的后排在更远处，也就是屏幕更上方
                new()
                {
                    key = "enemy_back", displayName = "敌方后排", source = ZoneSource.敌方后排,
                    anchorMin = new Vector2(0.14f, 0.805f), anchorMax = new Vector2(0.86f, 0.985f),
                    arrange = ZoneArrange.横排, cardScale = 0.92f, spacing = 22f,
                    reservedSlots = 1, acceptsDrop = true,
                    background = new Color(0.6f, 0.2f, 0.2f, 0.05f),
                },
                new()
                {
                    key = "enemy_front", displayName = "敌方前排", source = ZoneSource.敌方前排,
                    anchorMin = new Vector2(0.14f, 0.608f), anchorMax = new Vector2(0.86f, 0.788f),
                    arrange = ZoneArrange.横排, cardScale = 0.92f, spacing = 22f,
                    reservedSlots = 1, acceptsDrop = true,
                    background = new Color(0.6f, 0.2f, 0.2f, 0.08f),
                },

                // 我方按**玩家视角**排：前排靠近敌人（上），后排靠近自己（下）
                new()
                {
                    key = "party_front", displayName = "我方前排", source = ZoneSource.我方前排,
                    anchorMin = new Vector2(0.14f, 0.411f), anchorMax = new Vector2(0.86f, 0.591f),
                    arrange = ZoneArrange.横排, cardScale = 0.92f, spacing = 22f,
                    reservedSlots = 1, acceptsDrop = true,
                    background = new Color(0.2f, 0.45f, 0.7f, 0.08f),
                },
                new()
                {
                    key = "party_back", displayName = "我方后排", source = ZoneSource.我方后排,
                    anchorMin = new Vector2(0.14f, 0.214f), anchorMax = new Vector2(0.86f, 0.394f),
                    arrange = ZoneArrange.横排, cardScale = 0.92f, spacing = 22f,
                    reservedSlots = 1, acceptsDrop = true,
                    background = new Color(0.2f, 0.45f, 0.7f, 0.06f),
                },

                new()
                {
                    key = "hand", displayName = "手牌", source = ZoneSource.手牌,
                    anchorMin = new Vector2(0.135f, 0.012f), anchorMax = new Vector2(0.865f, 0.197f),
                    arrange = ZoneArrange.扇形, cardScale = 1.0f, spacing = 14f,
                    // 手牌只在空手时画一个占位：有牌时扇形自己居中，摆几张就占几张的地方
                    draggable = true, reservedSlots = 1,
                    background = new Color(1f, 1f, 1f, 0.04f),
                },

                new()
                {
                    // 主卡组与升变卡组共用这一块地：升变期间这里装的是形态牌，
                    // 标题会自己换成「升变卡组」（见 BattleView.ResolveZoneTitle）
                    key = "deck", displayName = "卡组", source = ZoneSource.卡组,
                    anchorMin = new Vector2(0.01f, 0.012f), anchorMax = new Vector2(0.125f, 0.197f),
                    arrange = ZoneArrange.堆叠, cardScale = 0.95f, faceDown = true, reservedSlots = 1,
                },
                new()
                {
                    key = "discard", displayName = "弃牌堆", source = ZoneSource.弃牌堆,
                    anchorMin = new Vector2(0.875f, 0.012f), anchorMax = new Vector2(0.99f, 0.197f),
                    arrange = ZoneArrange.堆叠, cardScale = 0.95f, reservedSlots = 1, peekable = true,
                },
                new()
                {
                    key = "continuous", displayName = "持续区", source = ZoneSource.持续区,
                    anchorMin = new Vector2(0.875f, 0.214f), anchorMax = new Vector2(0.99f, 0.394f),
                    arrange = ZoneArrange.堆叠, cardScale = 0.95f, reservedSlots = 1, peekable = true,
                    background = new Color(0.5f, 0.3f, 0.8f, 0.09f),
                },
                // 除外区（§4.1）：卡面写「消耗」的一次性牌打出后进这儿，升变结束的整套
                // 升变牌与形态牌也进这儿。**默认可见**——它原来叫「已完成升变区」、
                // 平时一直空着所以藏了起来，接了一次性牌之后常规牌局里就会用到。
                new()
                {
                    key = "exile", displayName = "除外区", source = ZoneSource.除外区,
                    anchorMin = new Vector2(0.01f, 0.214f), anchorMax = new Vector2(0.125f, 0.394f),
                    arrange = ZoneArrange.堆叠, cardScale = 0.95f, reservedSlots = 1,
                    background = new Color(0.8f, 0.6f, 0.2f, 0.07f),
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

            // 旧资产升级时这些新字段会是 0，给回可用的默认值
            if (cameraFov < 20f) cameraFov = 38f;
            if (cameraMargin < 1f) cameraMargin = 1.28f;
            boardTilt = Mathf.Clamp(boardTilt, 0f, 60f);
            cameraHeight = Mathf.Clamp(cameraHeight, -0.5f, 0.8f);

            foreach (ZoneLayout zone in zones.Where(z => z != null))
            {
                if (zone.cardScale <= 0.01f) zone.cardScale = 1f;
                zone.cardScale = Mathf.Clamp(zone.cardScale, 0.2f, 2f);

                if (zone.stackStep <= 0.01f) zone.stackStep = 16f;

                zone.reservedSlots = Mathf.Clamp(zone.reservedSlots, 0, 4);
            }
        }

        private void OnValidate() => Normalize();
    }
}
