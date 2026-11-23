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

        /// <summary>
        /// 远程攻击牌。老卡「鹰眼锁定」写的是「你的**远程攻击**无视目标2点物理护盾」，
        /// 没有这个字段就只能退而求其次按伤害属性近似，和卡面不是一回事。
        /// </summary>
        public bool IsRanged { get; init; }

        /// <summary>
        /// 卡面「仅限在后排打出」（老卡「穿云狙击」）。
        /// 这是个**硬前置条件**，不是减益——站错位就该打不出来，而不是打出来少点伤害。
        /// </summary>
        public bool RequiresBackRow { get; init; }

        /// <summary>打出时按顺序结算的数据驱动效果。</summary>
        public IReadOnlyList<EffectData> Effects { get; init; } = Array.Empty<EffectData>();

        /// <summary>可选的脚本化行为钩子 Id，由 <see cref="BehaviourRegistry"/> 解析。</summary>
        public string BehaviourId { get; init; }

        /// <summary>所属职业（职业牌、升变牌、形态牌、英雄牌用，决定形态卡组的配对）。</summary>
        public string ClassId { get; init; }

        /// <summary>卡面规则文字，供界面展示。不参与任何结算——结算只看 <see cref="Effects"/> 与 Behaviour。</summary>
        public string RulesText { get; init; }

        /// <summary>
        /// 卡图标识。Core 只存一个字符串，**不碰任何 Unity 类型**——
        /// 由表现层把它解析成实际贴图（见 <c>CardArtLibrary</c>），
        /// 解析不到就退回占位色块，所以美术没到位也能照常开发与测试。
        /// </summary>
        public string ArtId { get; init; }

        /// <summary>
        /// 卡背标识。为空时用内容包的默认卡背（<c>ContentDatabase.DefaultCardBackArtId</c>）。
        /// 留在单卡上是为了让秘宝、形态牌这类能有自己的背面。
        /// </summary>
        public string BackArtId { get; init; }

        /// <summary>武器牌的一阶基础伤害（3–10 点）。装备这张牌时由它派生 <see cref="WeaponProfile"/>。</summary>
        public int WeaponBaseDamage { get; init; }

        /// <summary>武器牌造成的伤害属性。</summary>
        public DamageElement Element { get; init; } = DamageElement.Physical;

        /// <summary>双手武器偏向高费高单发，单手偏向低费高频率（§7.1）。</summary>
        public bool TwoHanded { get; init; }

        /// <summary>防具牌的一阶基础护盾（6–10 点）。装备这张牌时由它派生 <see cref="ArmorProfile"/>。</summary>
        public int ArmorBaseShield { get; init; }

        /// <summary>防具牌提供的护盾种类。</summary>
        public ShieldKind ShieldKind { get; init; } = ShieldKind.Physical;

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
