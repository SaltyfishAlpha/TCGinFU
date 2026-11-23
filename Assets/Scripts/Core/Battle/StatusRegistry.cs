using System;
using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>
    /// 一类状态的模板。卡面上只写「挂 bleed，持续 2 轮，1 层」，
    /// 「每层每轮扣几点、算什么属性」这些数值属于状态本身，放在这里统一维护，
    /// 免得每张挂流血的卡都重复抄一遍。
    /// </summary>
    public sealed class StatusDefinition
    {
        public StatusDefinition(string id) => Id = id ?? throw new ArgumentNullException(nameof(id));

        public string Id { get; }

        /// <summary>每层每轮末造成的持续伤害（§8 ⑤）。</summary>
        public int DamagePerRound { get; init; }

        public int HealPerRound { get; init; }

        public DamageElement DotElement { get; init; } = DamageElement.Poison;

        /// <summary>每层给宿主的伤害 flat 加成（伤害管线步骤 ②）。</summary>
        public int OutgoingDamageBonus { get; init; }

        public int IncomingDamageReduction { get; init; }

        /// <summary>每层给宿主**受到**的伤害 flat 加成（【破绽】）。</summary>
        public int IncomingDamageBonus { get; init; }

        /// <summary>触发一次即消失，而不是按轮递减。</summary>
        public bool ConsumedOnTrigger { get; init; }

        /// <summary>挂着期间生效的脚本行为 Id。</summary>
        public string BehaviourId { get; init; }

        /// <summary>对宿主暴击抵抗线的修正（负数 = 更容易被暴击）。</summary>
        public int CritResistDelta { get; init; }

        /// <summary>宿主受到的最终伤害倍率，分数形式。(0,0) 表示不改。</summary>
        public int IncomingMultiplierNumerator { get; init; }

        public int IncomingMultiplierDenominator { get; init; }

        /// <summary>挂着期间宿主对该属性易损（最终伤害 ×2）。null 表示不改克制。</summary>
        public DamageElement? VulnerableElement { get; init; }

        public StatusEffect Create(int duration, int stacks) => new(Id, duration, stacks)
        {
            DamagePerRound = DamagePerRound,
            HealPerRound = HealPerRound,
            DotElement = DotElement,
            OutgoingDamageBonus = OutgoingDamageBonus,
            IncomingDamageReduction = IncomingDamageReduction,
            IncomingDamageBonus = IncomingDamageBonus,
            ConsumedOnTrigger = ConsumedOnTrigger,
            BehaviourId = BehaviourId,
            CritResistDelta = CritResistDelta,
            IncomingMultiplierNumerator = IncomingMultiplierNumerator,
            IncomingMultiplierDenominator = IncomingMultiplierDenominator,
            VulnerableElement = VulnerableElement,
        };
    }

    public sealed class StatusRegistry
    {
        private readonly Dictionary<string, StatusDefinition> _definitions = new(StringComparer.Ordinal);

        public void Register(StatusDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            _definitions[definition.Id] = definition;
        }

        public bool IsRegistered(string id) => id != null && _definitions.ContainsKey(id);

        /// <summary>
        /// 按模板造一个状态实例。未注册的 Id 产出一个无数值的纯标记状态——
        /// 这是刻意的：内容还没做完时挂个占位标记不该让整局崩掉。
        /// </summary>
        public StatusEffect Create(string id, int duration, int stacks)
            => _definitions.TryGetValue(id, out StatusDefinition definition)
                ? definition.Create(duration, stacks)
                : new StatusEffect(id, duration, stacks);
    }
}
