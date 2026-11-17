using System;

namespace TCGinFU.Core
{
    /// <summary>数值的取值来源。</summary>
    public enum ValueSource
    {
        /// <summary>固定值</summary>
        Flat,
        /// <summary>形态攻击基准 B_atk = 当前武器基础伤害 + L_bonus（§6.3）</summary>
        FormAttack,
        /// <summary>形态防御基准 B_def = 当前防具基础护盾 + L_bonus（§6.3）</summary>
        FormDefense,
        /// <summary>当前武器基础伤害（含阶梯补正）</summary>
        WeaponDamage,
        /// <summary>当前防具基础护盾（含阶梯补正）</summary>
        ArmorShield,
        /// <summary>L_bonus = ⌊角色总等级 ÷ 5⌋</summary>
        LevelBonus,
        /// <summary>施放者总等级</summary>
        CasterTotalLevel,
        /// <summary>本次打出该牌时消耗的充能数</summary>
        ChargeSpent,
    }

    /// <summary>
    /// 卡面数值表达式（规则书 §6.3）。形态牌写的是「造成 B_atk + 4 点伤害」，
    /// 所以引擎里任何数值字段都不能是裸 int，否则 5 级和 50 级就得各做一套卡。
    /// 求值结果 = Source 取值 × Times ÷ DividedBy + Add，整数除法向下取整。
    /// </summary>
    public readonly struct ValueExpr
    {
        public ValueSource Source { get; }
        public int Add { get; }
        public int Times { get; }
        public int DividedBy { get; }

        public ValueExpr(ValueSource source, int add = 0, int times = 1, int dividedBy = 1)
        {
            if (times == 0) throw new ArgumentOutOfRangeException(nameof(times), "倍率分子不能为 0");
            if (dividedBy <= 0) throw new ArgumentOutOfRangeException(nameof(dividedBy), "倍率分母必须为正");

            Source = source;
            Add = add;
            Times = times;
            DividedBy = dividedBy;
        }

        /// <summary>固定值。</summary>
        public static ValueExpr Flat(int value) => new(ValueSource.Flat, value);

        /// <summary>B_atk + add，即形态攻击牌的标准写法。</summary>
        public static ValueExpr FormAttack(int add) => new(ValueSource.FormAttack, add);

        /// <summary>B_def + add。</summary>
        public static ValueExpr FormDefense(int add) => new(ValueSource.FormDefense, add);

        public int Resolve(in ValueContext ctx)
        {
            int baseValue = Source switch
            {
                ValueSource.Flat => 0,
                ValueSource.FormAttack => ctx.WeaponDamage + ctx.LevelBonus,
                ValueSource.FormDefense => ctx.ArmorShield + ctx.LevelBonus,
                ValueSource.WeaponDamage => ctx.WeaponDamage,
                ValueSource.ArmorShield => ctx.ArmorShield,
                ValueSource.LevelBonus => ctx.LevelBonus,
                ValueSource.CasterTotalLevel => ctx.TotalLevel,
                ValueSource.ChargeSpent => ctx.ChargeSpent,
                _ => 0,
            };

            return baseValue * Times / DividedBy + Add;
        }

        public override string ToString()
        {
            if (Source == ValueSource.Flat && Times == 1 && DividedBy == 1) return Add.ToString();
            string core = Times == 1 && DividedBy == 1 ? $"{Source}" : $"{Source}×{Times}÷{DividedBy}";
            return Add == 0 ? core : $"{core}{(Add > 0 ? "+" : "")}{Add}";
        }
    }

    /// <summary>求值 <see cref="ValueExpr"/> 所需的施放者快照。</summary>
    public readonly struct ValueContext
    {
        public int WeaponDamage { get; }
        public int ArmorShield { get; }
        public int LevelBonus { get; }
        public int TotalLevel { get; }
        public int ChargeSpent { get; }

        public ValueContext(int weaponDamage, int armorShield, int levelBonus, int totalLevel, int chargeSpent = 0)
        {
            WeaponDamage = weaponDamage;
            ArmorShield = armorShield;
            LevelBonus = levelBonus;
            TotalLevel = totalLevel;
            ChargeSpent = chargeSpent;
        }
    }
}
