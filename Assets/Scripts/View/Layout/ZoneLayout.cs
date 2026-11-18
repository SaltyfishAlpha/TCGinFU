using System;
using UnityEngine;

namespace TCGinFU.View
{
    /// <summary>
    /// 一个区域显示什么。新增区域类型时在这里加一项，然后在
    /// <see cref="BattleView"/> 的内容解析里补一个分支即可。
    /// </summary>
    public enum ZoneSource
    {
        手牌,
        卡组,
        弃牌堆,
        /// <summary>独立充能区。默认布局不用它——充能按实体打牌的做法横置在角色卡上。</summary>
        充能区,
        持续区,
        形态卡组,
        已完成升变区,
        我方前排,
        我方后排,
        敌方前排,
        敌方后排,
        /// <summary>空容器。预留给还没接数据的新区域，摆位置时先占个坑。</summary>
        空,
    }

    /// <summary>区域内的排布方式。</summary>
    public enum ZoneArrange
    {
        /// <summary>横排平铺，超出宽度时自动压缩间距</summary>
        横排,
        /// <summary>扇形展开，手牌用</summary>
        扇形,
        /// <summary>叠放，只露最上面一张，牌堆/弃牌堆用</summary>
        堆叠,
        /// <summary>网格</summary>
        网格,
        /// <summary>
        /// 横置叠放：卡牌旋转 90° 压在角色卡下面，一张比一张低，只露出下缘。
        /// 对应实体打牌里「把牌横着塞到角色卡下面表示充能」。
        /// </summary>
        横置,
    }

    /// <summary>
    /// 一个场地区域的配置。全部是数据——位置、大小、排布、能不能拖进拖出，
    /// 都在 Inspector 里改，不用动代码。增删区域就是增删这个列表的元素。
    /// </summary>
    [Serializable]
    public sealed class ZoneLayout
    {
        [Header("标识")]
        [Tooltip("区域唯一键，用来在代码里找到它。可以随便起名")]
        public string key = "zone";

        [Tooltip("显示在区域左上角的标题")]
        public string displayName = "区域";

        public bool visible = true;

        [Header("内容")]
        public ZoneSource source = ZoneSource.手牌;

        [Tooltip("该区域属于第几名队友。-1 表示「当前操作的角色」，会跟着选中目标走")]
        public int playerIndex = -1;

        [Header("位置与大小（锚点为 0–1 的相对坐标，分辨率自适应）")]
        public Vector2 anchorMin = new(0.1f, 0f);
        public Vector2 anchorMax = new(0.9f, 0.2f);

        [Tooltip("在锚点矩形基础上的像素偏移，左下")]
        public Vector2 offsetMin = Vector2.zero;

        [Tooltip("在锚点矩形基础上的像素偏移，右上")]
        public Vector2 offsetMax = Vector2.zero;

        [Header("排布")]
        public ZoneArrange arrange = ZoneArrange.横排;

        [Tooltip("卡牌缩放。实际尺寸 = 布局资产里的标准卡牌尺寸 × 这个值，比例恒定不变形。"
                 + "角色卡同样走这个值——棋盘上的角色本身就是一张卡")]
        [Range(0.2f, 2f)]
        public float cardScale = 1f;

        [Tooltip("横置叠放时每张往下错开的像素")]
        public float stackStep = 16f;

        public float spacing = 12f;

        [Tooltip("扇形排布的总张开角度（度）")]
        public float fanAngle = 14f;

        [Tooltip("扇形排布的弧线下沉高度（像素）")]
        public float fanArc = 26f;

        [Range(1, 12)]
        public int gridColumns = 4;

        [Tooltip("至少预留几个卡位。没有牌时也会画出空卡位，保证区域不塌、牌位不跳")]
        [Range(0, 12)]
        public int reservedSlots = 1;

        [Header("交互")]
        [Tooltip("这个区域的卡牌能不能拖")]
        public bool draggable;

        [Tooltip("能不能把卡牌拖到这个区域上")]
        public bool acceptsDrop;

        [Tooltip("背面朝上。卡组、充能指示物用")]
        public bool faceDown;

        [Tooltip("能不能点开查看里面所有的牌。弃牌堆、已完成升变区这类公开信息应当打开")]
        public bool peekable;

        public bool showLabel = true;

        [Header("外观")]
        public Color background = new(1f, 1f, 1f, 0.05f);
        public Color border = new(1f, 1f, 1f, 0.18f);

        /// <summary>这个区域装的是角色而不是卡牌。</summary>
        public bool IsCombatantZone => source is ZoneSource.我方前排 or ZoneSource.我方后排
            or ZoneSource.敌方前排 or ZoneSource.敌方后排;

        public bool IsEnemyZone => source is ZoneSource.敌方前排 or ZoneSource.敌方后排;

        public ZoneLayout Clone() => (ZoneLayout)MemberwiseClone();
    }
}
