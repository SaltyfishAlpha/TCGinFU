namespace TCGinFU.Core
{
    public static class DamageElementExtensions
    {
        /// <summary>
        /// 伤害属性吃哪一种护盾。裁定 R-02：仅「物理」走物理盾，其余八属性走魔法盾。
        /// 要调整毒/风等属性的归类，改这一处即可。
        /// </summary>
        public static ShieldKind ToShieldKind(this DamageElement element)
            => element == DamageElement.Physical ? ShieldKind.Physical : ShieldKind.Magical;

        /// <summary>九大伤害属性的中文名（§7.4）。战报与 UI 都用它，不要直接打印枚举名。</summary>
        public static string DisplayName(this DamageElement element) => element switch
        {
            DamageElement.Physical => "物理",
            DamageElement.Wind => "风",
            DamageElement.Lightning => "雷",
            DamageElement.Dark => "暗",
            DamageElement.Earth => "地",
            DamageElement.Fire => "火",
            DamageElement.Ice => "冰",
            DamageElement.Light => "光",
            DamageElement.Poison => "毒",
            _ => element.ToString(),
        };
    }

    public static class EnemyRankExtensions
    {
        /// <summary>默认暴击抵抗线（规则书 §7.3）。首领给 7，个别首领可在定义里覆盖到 8。</summary>
        public static int DefaultCritResist(this EnemyRank rank) => rank switch
        {
            EnemyRank.Normal => 4,
            EnemyRank.Elite => 6,
            EnemyRank.Boss => 7,
            _ => 4,
        };
    }
}
