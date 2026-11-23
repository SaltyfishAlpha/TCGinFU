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

    /// <summary>
    /// 卡牌的**类别**（规则书 §7.1）：这张牌从哪来、谁能编入。
    /// 与 <see cref="CardPlay"/> 是两个互不相干的维度。
    /// <para>§2.3 的「角色专属牌」还没实现，见 known_issues。</para>
    /// </summary>
    public enum CardCategory
    {
        /// <summary>职业牌。升变牌与英雄技能牌都归在这一类下，它们是标记不是类别。</summary>
        Class,
        Weapon,
        Armor,
        /// <summary>秘宝牌</summary>
        Treasure,
    }

    /// <summary>
    /// 卡牌的**打法**（规则书 §7.1）：这张牌什么时候打、打出后去哪。只有三种。
    ///
    /// <para>和 <see cref="CardRole"/> 是两个互不相干的维度——升变牌、形态牌、敌人意图
    /// 不是第四、第五种打法，形态牌仍然有自己的打法（烟幕替身是「形态牌 · 时机」）。</para>
    /// </summary>
    public enum CardPlay
    {
        /// <summary>技能牌：玩家阶段主动打出，耗 AP / MP，当场结算后进入弃牌堆</summary>
        Skill,
        /// <summary>时机牌：留在手中，在敌人阶段或队友行动时作为响应打出</summary>
        Timing,
        /// <summary>持续牌：置入持续区，常驻生效（§4.1）</summary>
        Continuous,
    }

    /// <summary>
    /// 打法之外的标记（规则书 §7.1「不属于打法的标记」）。和 <see cref="CardPlay"/>、
    /// <see cref="CardCategory"/> 都是独立维度。
    /// </summary>
    public enum CardRole
    {
        /// <summary>普通牌：按自己的打法结算</summary>
        Normal,

        /// <summary>
        /// 升变牌（§6）。本身不按打法结算，而是走升变宣告，
        /// 所以它的 <see cref="CardDefinition.Play"/> 是 null。
        /// </summary>
        Transform,

        /// <summary>
        /// 形态牌：属于某张升变牌的形态额外卡组，只在升变期间抽到（§6.2）。
        /// <b>仍然有自己的打法</b>——「在哪个牌堆」和「什么时候打」是两件事。
        /// </summary>
        Form,

        /// <summary>
        /// 敌人意图（§8 ④）。敌人的意图就是它手里一套玩家看不到的牌，
        /// 按阶段从备选池里挑一张宣告出来，结算沿用同一套卡牌效果体系。
        /// <para>不是玩家卡牌：不能编入卡组，不进手牌，也不按打法结算
        /// （<see cref="CardDefinition.Play"/> 同样是 null）。</para>
        /// </summary>
        EnemyIntent,
    }

    /// <summary>
    /// 一张牌要玩家指定什么目标。与 <see cref="TargetScope"/> 不是一回事：
    /// 那个说的是「某条效果作用到谁」，这个说的是「出牌时界面该让玩家点谁」。
    /// </summary>
    public enum CardTargeting
    {
        /// <summary>按卡面 effects 推导（有 SingleEnemy 就指敌人，有 SingleAlly 就指队友）</summary>
        Auto,
        /// <summary>不需要指定</summary>
        None,
        Enemy,
        Ally,
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
