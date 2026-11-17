namespace TCGinFU.Core
{
    /// <summary>
    /// 建卡与升级期的规则（规则书 §2.1）。不参与战斗结算，放这里是为了让面板相关规则集中在一处。
    /// </summary>
    public static class CharacterProgression
    {
        /// <summary>达到这些总等级时，各获得 1 次属性骰升级机会。</summary>
        public static readonly int[] DieUpgradeLevels = { 20, 40 };

        /// <summary>角色到当前总等级为止总共获得过几次属性骰升级机会。</summary>
        public static int EarnedDieUpgrades(int totalLevel)
        {
            int count = 0;
            foreach (int threshold in DieUpgradeLevels)
            {
                if (totalLevel >= threshold) count++;
            }

            return count;
        }

        /// <summary>
        /// 校验一份骰面配置相对起始配置是否合法：升级次数不能超过等级给予的次数，且不能越过 d12。
        /// </summary>
        public static bool ValidateDice(AttributeDice starting, AttributeDice current, int totalLevel, out string error)
        {
            int spent = 0;
            foreach (AttributeColor color in new[]
                     {
                         AttributeColor.Might, AttributeColor.Insight,
                         AttributeColor.Dexterity, AttributeColor.Willpower,
                     })
            {
                int steps = (current.Face(color) - starting.Face(color)) / 2;
                if (steps < 0)
                {
                    error = $"{color} 的骰面低于起始值";
                    return false;
                }

                spent += steps;
            }

            int earned = EarnedDieUpgrades(totalLevel);
            if (spent > earned)
            {
                error = $"属性骰升级次数 {spent} 超过 {totalLevel} 级可获得的 {earned} 次";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>多职业构筑：初始选择 2–3 个职业共享等级。</summary>
        public static bool ValidateStartingClasses(CharacterSheet sheet, out string error)
        {
            if (sheet.Classes.Count is < 2 or > 3)
            {
                error = $"初始应选择 2–3 个职业，当前 {sheet.Classes.Count} 个";
                return false;
            }

            error = null;
            return true;
        }
    }
}
