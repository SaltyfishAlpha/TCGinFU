using TCGinFU.Core;

namespace TCGinFU.Content
{
    /// <summary>
    /// 样例卡池：一个「浪客 / 神射手」双职业角色的全部可用牌。
    /// 覆盖 4 种卡牌来源（职业/武器/防具/秘宝）× 6 种结算形态
    /// （技能/时机/持续/升变/形态/英雄），用来端到端验证引擎。
    /// 除显式标注挂钩子的 3 张外，其余全部纯数据驱动。
    /// </summary>
    public static class SampleCards
    {
        public const string RogueClass = "浪客";

        // ───────────────────────── 职业牌 · 技能牌 ─────────────────────────

        /// <summary>快刀：绿色单色【暴击】，伤害跟当前武器走，不吃固定值。</summary>
        public static readonly CardDefinition QuickBlade =
            new("cls_quick_blade", "快刀", CardCategory.Class, CardKind.Skill)
            {
                ClassId = RogueClass,
                Colors = new[] { AttributeColor.Dexterity },
                ApCost = 1,
                HasCrit = true,
                Effects = new[]
                {
                    EffectData.Damage(new ValueExpr(ValueSource.WeaponDamage, add: 2)),
                },
            };

        /// <summary>奥术飞弹：蓝色，耗 MP 的火属性法术，用来验证充能抵扣 MP 与属性克制。</summary>
        public static readonly CardDefinition ArcaneBolt =
            new("cls_arcane_bolt", "奥术飞弹", CardCategory.Class, CardKind.Skill)
            {
                ClassId = RogueClass,
                Colors = new[] { AttributeColor.Insight },
                ApCost = 1,
                MpCost = 5,
                HasCrit = true,
                Effects = new[]
                {
                    EffectData.Damage(ValueExpr.Flat(9), DamageElement.Fire),
                },
            };

        /// <summary>穿心射：绿+蓝双色【暴击】，可触发超暴击（§7.3）。暴击时追加流血。</summary>
        public static readonly CardDefinition PiercingShot =
            new("cls_piercing_shot", "穿心射", CardCategory.Class, CardKind.Skill)
            {
                ClassId = RogueClass,
                Colors = new[] { AttributeColor.Dexterity, AttributeColor.Insight },
                ApCost = 2,
                MpCost = 5,
                HasCrit = true,
                Effects = new[]
                {
                    EffectData.Damage(ValueExpr.Flat(12)),
                    new EffectData
                    {
                        Kind = EffectKind.ApplyStatus,
                        StatusId = SampleStatuses.Bleed,
                        Duration = 2,
                        Amount = ValueExpr.Flat(1),
                        Target = TargetSelector.SingleEnemy,
                        Condition = EffectCondition.OnCrit,
                    },
                },
            };

        /// <summary>背水一击：危机状态下追加伤害，演示 EffectCondition 的条件分支。</summary>
        public static readonly CardDefinition DesperateSlash =
            new("cls_desperate_slash", "背水一击", CardCategory.Class, CardKind.Skill)
            {
                ClassId = RogueClass,
                Colors = new[] { AttributeColor.Might },
                ApCost = 1,
                Effects = new[]
                {
                    EffectData.Damage(ValueExpr.Flat(6)),
                    new EffectData
                    {
                        Kind = EffectKind.Damage,
                        Amount = ValueExpr.Flat(7),
                        Target = TargetSelector.SingleEnemy,
                        Condition = EffectCondition.IfCasterInCrisis,
                    },
                },
            };

        /// <summary>超载：卡面「消耗 1 个充能」词条，伤害随消耗的充能数放大（§5.2）。</summary>
        public static readonly CardDefinition Overcharge =
            new("cls_overcharge", "超载", CardCategory.Class, CardKind.Skill)
            {
                ClassId = RogueClass,
                Colors = new[] { AttributeColor.Willpower },
                ApCost = 1,
                ChargeCost = 1,
                Effects = new[]
                {
                    EffectData.Damage(
                        new ValueExpr(ValueSource.ChargeSpent, add: 4, times: 5),
                        DamageElement.Lightning),
                },
            };

        // ───────────────────────── 职业牌 · 时机牌 ─────────────────────────

        /// <summary>见切：0 AP 的援护牌，挂钩子限定只能应对物理攻击。</summary>
        public static readonly CardDefinition PerfectParry =
            new("cls_perfect_parry", "见切", CardCategory.Class, CardKind.Timing)
            {
                ClassId = RogueClass,
                Colors = new[] { AttributeColor.Dexterity },
                ApCost = 0,
                BehaviourId = SampleBehaviours.PerfectParry,
                Effects = new[]
                {
                    EffectData.Cover(),
                    EffectData.Shield(ValueExpr.FormDefense(0), ShieldKind.Physical),
                },
            };

        // ───────────────────────── 职业牌 · 持续牌 ─────────────────────────

        /// <summary>猎人印记：置入持续区提供常驻伤害加成（§7.1 被动技能重构）。</summary>
        public static readonly CardDefinition HuntersMark =
            new("cls_hunters_mark", "猎人印记", CardCategory.Class, CardKind.Continuous)
            {
                ClassId = RogueClass,
                Colors = new[] { AttributeColor.Insight },
                ApCost = 1,
                Effects = new[]
                {
                    new EffectData
                    {
                        Kind = EffectKind.ApplyStatus,
                        StatusId = SampleStatuses.MarkedPrey,
                        Duration = -1,
                        Amount = ValueExpr.Flat(1),
                        Target = TargetSelector.Self,
                    },
                },
            };

        // ───────────────────────── 职业牌 · 升变牌 ─────────────────────────

        /// <summary>影舞觉醒：条件型升变牌，条件判定挂钩子（见 <see cref="AwakenConditionBehaviour"/>）。</summary>
        public static readonly CardDefinition RogueAwaken =
            new("cls_rogue_awaken", "影舞觉醒", CardCategory.Class, CardKind.Transform)
            {
                ClassId = RogueClass,
                Colors = new[] { AttributeColor.Dexterity },
                TransformTrigger = TransformTrigger.Conditional,
                TransformConditionText = "本局你造成过暴击，或全队累计消耗过 3 个充能",
                BehaviourId = SampleBehaviours.Awaken,
            };

        // ───────────────────────── 武器牌 ─────────────────────────

        /// <summary>短弓速射：单手武器，低费高频（§7.1）。</summary>
        public static readonly CardDefinition ShortbowShot =
            new("wpn_shortbow_shot", "短弓速射", CardCategory.Weapon, CardKind.Skill)
            {
                Colors = new[] { AttributeColor.Dexterity },
                ApCost = 1,
                WeaponBaseDamage = 7,
                Effects = new[]
                {
                    EffectData.Damage(new ValueExpr(ValueSource.WeaponDamage)),
                },
            };

        /// <summary>重劈：双手武器，高费高单发（§7.1）。</summary>
        public static readonly CardDefinition HeavySwing =
            new("wpn_heavy_swing", "重劈", CardCategory.Weapon, CardKind.Skill)
            {
                Colors = new[] { AttributeColor.Might },
                ApCost = 2,
                WeaponBaseDamage = 7,
                HasCrit = true,
                Effects = new[]
                {
                    EffectData.Damage(new ValueExpr(ValueSource.WeaponDamage, add: 4)),
                },
            };

        // ───────────────────────── 防具牌 ─────────────────────────

        /// <summary>格挡：0 AP 自保，护盾跟当前防具走。</summary>
        public static readonly CardDefinition Block =
            new("arm_block", "格挡", CardCategory.Armor, CardKind.Timing)
            {
                ApCost = 0,
                ArmorBaseShield = 8,
                Effects = new[]
                {
                    EffectData.Shield(new ValueExpr(ValueSource.ArmorShield), ShieldKind.Physical),
                },
            };

        /// <summary>挺身援护：替相邻队友承伤并给自己加盾（§4.3）。纯数据驱动的援护牌。</summary>
        public static readonly CardDefinition GuardianStep =
            new("arm_guardian_step", "挺身援护", CardCategory.Armor, CardKind.Timing)
            {
                ApCost = 1,
                ArmorBaseShield = 8,
                Effects = new[]
                {
                    EffectData.Cover(),
                    EffectData.Shield(new ValueExpr(ValueSource.ArmorShield, add: 2), ShieldKind.Physical),
                },
            };

        // ───────────────────────── 秘宝牌 ─────────────────────────

        /// <summary>治愈秘药：跑团遗物衍生的不受职业限制的功能牌（§7.1）。</summary>
        public static readonly CardDefinition Elixir =
            new("tre_elixir", "治愈秘药", CardCategory.Treasure, CardKind.Skill)
            {
                ApCost = 0,
                Effects = new[]
                {
                    EffectData.Heal(ValueExpr.Flat(12)),
                },
            };

        /// <summary>魔力结晶：回 MP 并补牌，缓解「MP 不会每战回满」的压力（§5.2）。</summary>
        public static readonly CardDefinition ManaCrystal =
            new("tre_mana_crystal", "魔力结晶", CardCategory.Treasure, CardKind.Skill)
            {
                ApCost = 0,
                Effects = new[]
                {
                    new EffectData
                    {
                        Kind = EffectKind.RestoreMp,
                        Amount = ValueExpr.Flat(10),
                        Target = TargetSelector.Self,
                    },
                    EffectData.Draw(1),
                },
            };

        // ───────────────────────── 形态额外卡组 ─────────────────────────

        /// <summary>影袭：形态组主力，伤害写成 B_atk + 4，随等级与装备自动成长（§6.3）。</summary>
        public static readonly CardDefinition FormShadowStrike =
            new("form_shadow_strike", "影袭", CardCategory.Class, CardKind.Form)
            {
                ClassId = RogueClass,
                Colors = new[] { AttributeColor.Dexterity },
                ApCost = 1,
                Effects = new[]
                {
                    EffectData.Damage(ValueExpr.FormAttack(4), DamageElement.Dark),
                },
            };

        /// <summary>双牙：双色【暴击】形态牌，超暴击时追加一刀。</summary>
        public static readonly CardDefinition FormTwinFang =
            new("form_twin_fang", "双牙", CardCategory.Class, CardKind.Form)
            {
                ClassId = RogueClass,
                Colors = new[] { AttributeColor.Dexterity, AttributeColor.Might },
                ApCost = 1,
                HasCrit = true,
                Effects = new[]
                {
                    EffectData.Damage(ValueExpr.FormAttack(2)),
                    new EffectData
                    {
                        Kind = EffectKind.Damage,
                        Amount = ValueExpr.FormAttack(2),
                        Target = TargetSelector.SingleEnemy,
                        Condition = EffectCondition.OnSuperCrit,
                    },
                },
            };

        /// <summary>幻影帷幕：形态防御牌，护盾写成 B_def + 2。</summary>
        public static readonly CardDefinition FormVeil =
            new("form_veil", "幻影帷幕", CardCategory.Class, CardKind.Form)
            {
                ClassId = RogueClass,
                Colors = new[] { AttributeColor.Dexterity },
                ApCost = 1,
                Effects = new[]
                {
                    EffectData.Shield(ValueExpr.FormDefense(2), ShieldKind.Physical),
                    EffectData.Shield(ValueExpr.FormDefense(2), ShieldKind.Magical),
                },
            };

        /// <summary>疾影：形态组里的节奏牌，补 AP 与手牌。</summary>
        public static readonly CardDefinition FormHaste =
            new("form_haste", "疾影", CardCategory.Class, CardKind.Form)
            {
                ClassId = RogueClass,
                Colors = new[] { AttributeColor.Dexterity },
                ApCost = 0,
                Effects = new[]
                {
                    new EffectData
                    {
                        Kind = EffectKind.GainAp,
                        Amount = ValueExpr.Flat(1),
                        Target = TargetSelector.Self,
                    },
                    EffectData.Draw(2),
                },
            };

        /// <summary>
        /// 千刃乱舞：浪客 10 级精通的主动型英雄技能牌，编在形态组里（§3.3）。
        /// 连斩次数与目标随机，逻辑在 <see cref="ThousandCutsBehaviour"/>。
        /// </summary>
        public static readonly CardDefinition HeroThousandCuts =
            new("hero_thousand_cuts", "千刃乱舞", CardCategory.Class, CardKind.Hero)
            {
                ClassId = RogueClass,
                Colors = new[] { AttributeColor.Dexterity, AttributeColor.Might },
                ApCost = 2,
                BehaviourId = SampleBehaviours.ThousandCuts,
            };
    }

    /// <summary>样例卡用到的状态 Id。</summary>
    public static class SampleStatuses
    {
        /// <summary>流血：轮末持续伤害。</summary>
        public const string Bleed = "bleed";

        /// <summary>猎人印记：常驻伤害加成。</summary>
        public const string MarkedPrey = "marked_prey";
    }
}
