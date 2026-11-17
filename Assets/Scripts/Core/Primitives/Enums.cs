namespace TCGinFU.Core
{
    /// <summary>四色属性（规则书 §2.2）。卡牌颜色与之一一对应。</summary>
    public enum AttributeColor
    {
        /// <summary>力量 · 红</summary>
        Might,
        /// <summary>洞察 · 蓝</summary>
        Insight,
        /// <summary>敏捷 · 绿</summary>
        Dexterity,
        /// <summary>意志 · 紫</summary>
        Willpower,
    }

    /// <summary>属性骰等级。枚举值即规则书「计算面板数值时取 6/8/10/12」的那个数。</summary>
    public enum DieRank
    {
        D6 = 6,
        D8 = 8,
        D10 = 10,
        D12 = 12,
    }

    /// <summary>九大伤害属性（规则书 §7.4）。</summary>
    public enum DamageElement
    {
        Physical,
        Wind,
        Lightning,
        Dark,
        Earth,
        Fire,
        Ice,
        Light,
        Poison,
    }

    /// <summary>对某一伤害属性的亲和度。</summary>
    public enum Affinity
    {
        Normal,
        /// <summary>易损（弱点）：最终伤害 ×2</summary>
        Vulnerable,
        /// <summary>抗性：最终伤害 ÷2 向下取整</summary>
        Resistant,
        /// <summary>免疫：伤害降为 0</summary>
        Immune,
        /// <summary>吸收：伤害转为治疗</summary>
        Absorb,
    }

    /// <summary>护盾种类。属性到护盾的映射见裁定 R-02。</summary>
    public enum ShieldKind
    {
        Physical,
        Magical,
    }

    /// <summary>卡牌四大来源（规则书 §7.1）。</summary>
    public enum CardCategory
    {
        /// <summary>职业牌（含升变牌）</summary>
        Class,
        Weapon,
        Armor,
        /// <summary>秘宝牌</summary>
        Treasure,
    }

    /// <summary>卡牌在结算上的细分形态（规则书 §7.1 / §3.3 / §6）。</summary>
    public enum CardKind
    {
        /// <summary>技能牌：耗 AP 与 MP 直接结算</summary>
        Skill,
        /// <summary>时机牌：留在手中，在敌人阶段或队友行动时响应打出</summary>
        Timing,
        /// <summary>持续牌：置入持续区提供常驻增益</summary>
        Continuous,
        /// <summary>升变牌</summary>
        Transform,
        /// <summary>形态额外卡组中的牌</summary>
        Form,
        /// <summary>英雄技能牌（10 级精通）</summary>
        Hero,
    }

    /// <summary>升变牌的触发方式（规则书 §6.1）。</summary>
    public enum TransformTrigger
    {
        /// <summary>条件型：卡面写有前置条件</summary>
        Conditional,
        /// <summary>即发型：在手即可开启</summary>
        Immediate,
    }

    /// <summary>战场排位。</summary>
    public enum Row
    {
        Front,
        Back,
    }

    /// <summary>标准战斗轮次的 5 个阶段（规则书 §8）。</summary>
    public enum RoundPhase
    {
        /// <summary>① 轮始与意图阶段</summary>
        RoundStart,
        /// <summary>② 补牌阶段</summary>
        Draw,
        /// <summary>③ 玩家阶段</summary>
        PlayerPhase,
        /// <summary>④ 敌人阶段</summary>
        EnemyPhase,
        /// <summary>⑤ 轮末结算阶段</summary>
        RoundEnd,
        /// <summary>战斗已结束</summary>
        Finished,
    }

    /// <summary>消耗充能指示物的三选一用途（规则书 §5.2）。</summary>
    public enum ChargeUse
    {
        /// <summary>抵扣 5 点 MP</summary>
        ConvertToMp,
        /// <summary>本回合 +1 AP</summary>
        ConvertToAp,
        /// <summary>支付卡面「消耗 X 个充能」词条</summary>
        PayCardCost,
    }

    /// <summary>装备牌等级阶梯（规则书 §7.2）。</summary>
    public enum EquipTier
    {
        Tier1 = 1,
        Tier2 = 2,
        Tier3 = 3,
    }

    /// <summary>敌人级别，决定意图可见度与默认暴击抵抗（规则书 §7.3）。</summary>
    public enum EnemyRank
    {
        /// <summary>普通小怪：意图完全公开，暴击抵抗 4</summary>
        Normal,
        /// <summary>精英：意图半公开，暴击抵抗 6</summary>
        Elite,
        /// <summary>首领：暴击抵抗 7–8</summary>
        Boss,
    }

    /// <summary>战斗结局。</summary>
    public enum BattleOutcome
    {
        Ongoing,
        Victory,
        Defeat,
    }
}
