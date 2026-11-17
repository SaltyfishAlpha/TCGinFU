using System;
using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 一张卡的静态定义（不可变、全局共享）。对局中流转的是 <see cref="CardInstance"/>。
    /// </summary>
    public sealed class CardDefinition
    {
        public CardDefinition(string id, string name, CardCategory category, CardKind kind)
        {
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("卡牌 Id 不能为空", nameof(id)) : id;
            Name = name ?? id;
            Category = category;
            Kind = kind;
        }

        public string Id { get; }
        public string Name { get; }
        public CardCategory Category { get; }
        public CardKind Kind { get; }

        /// <summary>卡牌颜色。单色 1 项，还原双属性检定的卡为 2 项（§2.2）。</summary>
        public IReadOnlyList<AttributeColor> Colors { get; init; } = Array.Empty<AttributeColor>();

        public int ApCost { get; init; }
        public int MpCost { get; init; }

        /// <summary>卡面「消耗 X 个充能」词条的 X。</summary>
        public int ChargeCost { get; init; }

        /// <summary>卡面是否带【暴击】标识。只有带标识的牌才投暴击骰（§7.3）。</summary>
        public bool HasCrit { get; init; }

        /// <summary>打出时按顺序结算的数据驱动效果。</summary>
        public IReadOnlyList<EffectData> Effects { get; init; } = Array.Empty<EffectData>();

        /// <summary>可选的脚本化行为钩子 Id，由 <see cref="BehaviourRegistry"/> 解析。</summary>
        public string BehaviourId { get; init; }

        /// <summary>所属职业（职业牌、升变牌、形态牌、英雄牌用，决定形态卡组的配对）。</summary>
        public string ClassId { get; init; }

        /// <summary>武器牌的一阶基础伤害。</summary>
        public int WeaponBaseDamage { get; init; }

        /// <summary>防具牌的一阶基础护盾。</summary>
        public int ArmorBaseShield { get; init; }

        /// <summary>升变牌的触发方式（§6.1）。</summary>
        public TransformTrigger TransformTrigger { get; init; } = TransformTrigger.Conditional;

        /// <summary>条件型升变牌的前置条件说明，供 UI 展示；实际判定走 Behaviour。</summary>
        public string TransformConditionText { get; init; }

        /// <summary>同名牌编入上限：升变牌 1 张，其余 3 张（§3.2）。</summary>
        public int CopyLimit => Kind == CardKind.Transform ? 1 : 3;

        /// <summary>时机牌与防具牌可以在敌人阶段的响应窗口打出（§4.3 / §8 ④）。</summary>
        public bool IsReactive => Kind == CardKind.Timing || Category == CardCategory.Armor;

        public bool IsDualColor => Colors.Count >= 2;

        public bool DealsDamage => Effects.Any(e => e.Kind == EffectKind.Damage);

        public override string ToString() => $"{Name}({Id})";
    }
}
