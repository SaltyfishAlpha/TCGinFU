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
        /// <param name="play">
        /// 打法。升变牌与敌人意图不按打法结算，传 null（见 <see cref="CardRole"/>）。
        /// </param>
        public CardDefinition(string id, string name, CardCategory category, CardPlay? play)
        {
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("卡牌 Id 不能为空", nameof(id)) : id;
            Name = name ?? id;
            Category = category;
            Play = play;
        }

        public string Id { get; }
        public string Name { get; }

        /// <summary>类别：这张牌从哪来、谁能编入（§7.1）。</summary>
        public CardCategory Category { get; }

        /// <summary>
        /// 打法：什么时候打、打出后去哪（§7.1）。
        /// <para>null 表示这张牌不按打法结算——只有升变牌与敌人意图是这样。</para>
        /// </summary>
        public CardPlay? Play { get; }

        /// <summary>打法之外的标记（升变牌 / 形态牌 / 敌人意图）。默认是普通牌。</summary>
        public CardRole Role { get; init; } = CardRole.Normal;

        /// <summary>
        /// 一次性牌：卡面写着「消耗」或「使用后置入除外区」。打出结算完进除外区，不进弃牌堆，
        /// 也就不会被洗回主卡组（§4.1）。
        ///
        /// <para>这是**声明式字段**不是脚本钩子：对绝大多数卡，「打出后去哪」是无条件的静态归宿，
        /// 引擎读一个 bool 就够了，而且构筑校验与卡面 UI 也能读到它。
        /// 等真出现「有条件地除外」或「跨回合置入」的卡，要加的是一个 Lua 可调的除外原语，
        /// 而不是让每张卡自己判断归宿。</para>
        ///
        /// <para>升变牌与形态牌**不靠这个字段**：它们整套在弹出升变时离场（§6.2 ⑤），
        /// 时机与「打出即除外」不同。</para>
        /// </summary>
        public bool ExileOnResolve { get; init; }

        /// <summary>
        /// 英雄技能牌（职业 10 级精通所得，§3.3）。
        /// <para>这是**标记**不是类别也不是打法：英雄技能牌属于职业牌，可以是任意打法。</para>
        /// </summary>
        public bool IsHeroSkill { get; init; }

        /// <summary>
        /// 升变牌自带的形态卡组（<see cref="Role"/> 为 <see cref="CardRole.Transform"/> 时有值）。
        /// 合计 <see cref="DeckList.FormDeckSize"/> 张，玩家不挑（§3.1）。
        ///
        /// <para>为什么记在升变牌上而不是卡组文件里：「这张升变牌配哪 10 张」是**内容定义**，
        /// 不是构筑选择。写在卡组文件里就得每个角色抄一份，而且一张形态牌只能归属一张升变牌；
        /// 记在这里，同一张形态牌可以同时出现在多张升变牌的形态卡组中。</para>
        /// </summary>
        public IReadOnlyList<FormDeckEntry> FormDeck { get; init; } = Array.Empty<FormDeckEntry>();

        /// <summary>卡牌颜色。单色 1 项，还原双属性检定的卡为 2 项（§2.2）。</summary>
        public IReadOnlyList<AttributeColor> Colors { get; init; } = Array.Empty<AttributeColor>();

        public int ApCost { get; init; }
        public int MpCost { get; init; }

        /// <summary>卡面「消耗 X 个充能」词条的 X。这是**硬费用**，不打出就是不打出。</summary>
        public int ChargeCost { get; init; }

        /// <summary>
        /// 卡面允许玩家**额外选择**消耗几个充能（「若消耗1个充能指示物则…」）。
        /// 空表示这张牌没有可选消耗，<see cref="PlayCardAction.ChargeForEffect"/> 只能是 0。
        ///
        /// <para>为什么声明在卡上而不是写在界面里：界面据此自动生成「消耗几个充能？」的提问，
        /// 不用每补一张这种牌就去改一次表现层；玩家自定义的卡也能用上同一个入口。
        /// 含 0 表示「可以不消耗」（暗夜剔骨），不含 0 表示至少要选一个（万能灵药）。</para>
        /// </summary>
        public IReadOnlyList<int> ChargeOptions { get; init; } = Array.Empty<int>();

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

        /// <summary>所属职业（职业牌、升变牌、形态牌、英雄技能牌用，决定形态卡组的配对）。</summary>
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
        public int CopyLimit => Role == CardRole.Transform ? 1 : 3;

        /// <summary>
        /// 卡面要玩家指定什么目标。<see cref="CardTargeting.Auto"/> 时按
        /// <see cref="Effects"/> 推导。
        ///
        /// <para>不能只靠推导：「从弃牌堆将最多2张牌贴入**全队任意角色**的充能区」这类卡
        /// 的目标是给行为层用的，数据效果里一条 SingleAlly 都没有，
        /// 推导出来就是「不需要目标」——玩家指定的队友会被整个丢掉，永远贴给自己。</para>
        /// </summary>
        public CardTargeting Targeting { get; init; } = CardTargeting.Auto;

        /// <summary>算上推导之后，这张牌到底指向谁。</summary>
        public CardTargeting ResolvedTargeting
        {
            get
            {
                if (Targeting != CardTargeting.Auto) return Targeting;
                if (Effects.Any(e => e.Target.Scope == TargetScope.SingleEnemy)) return CardTargeting.Enemy;
                if (Effects.Any(e => e.Target.Scope == TargetScope.SingleAlly)) return CardTargeting.Ally;
                return CardTargeting.None;
            }
        }

        /// <summary>
        /// 不指定目标就打不出去。只有**数据效果**真的需要目标时才成立——
        /// 行为层驱动的「自己或任意队友」自带默认值（不选就是自己），强制指定反而别扭。
        /// </summary>
        public bool RequiresExplicitTarget
            => Effects.Any(e => e.Target.Scope is TargetScope.SingleEnemy or TargetScope.SingleAlly);

        public bool IsDualColor => Colors.Count >= 2;

        public bool DealsDamage => Effects.Any(e => e.Kind == EffectKind.Damage);

        public override string ToString() => $"{Name}({Id})";
    }

    /// <summary>升变牌形态卡组里的一项：某张形态牌带几张。</summary>
    public readonly struct FormDeckEntry
    {
        public FormDeckEntry(string cardId, int count)
        {
            CardId = cardId;
            Count = count;
        }

        public string CardId { get; }
        public int Count { get; }
    }
}
