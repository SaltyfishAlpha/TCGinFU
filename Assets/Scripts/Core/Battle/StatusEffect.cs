using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>挂在角色身上的一个状态。Duration 以轮计，轮末递减；-1 表示本局持续。</summary>
    public sealed class StatusEffect
    {
        public StatusEffect(string id, int duration, int stacks = 1)
        {
            Id = id;
            Duration = duration;
            Stacks = stacks;
        }

        public string Id { get; }
        public int Duration { get; private set; }
        public int Stacks { get; private set; }

        /// <summary>每轮末造成的持续伤害（§8 ⑤）。</summary>
        public int DamagePerRound { get; set; }

        /// <summary>每轮末的持续恢复。</summary>
        public int HealPerRound { get; set; }

        public DamageElement DotElement { get; set; } = DamageElement.Poison;

        /// <summary>对伤害的 flat 加减修正（伤害管线步骤 ②）。</summary>
        public int OutgoingDamageBonus { get; set; }

        public int IncomingDamageReduction { get; set; }

        /// <summary>宿主**受到**的伤害 flat 加成。老卡的【破绽】标记就是它。</summary>
        public int IncomingDamageBonus { get; set; }

        /// <summary>
        /// 触发一次即消失（不等轮末递减）。【破绽】写的是「下一次受到的任意伤害+3」，
        /// 靠 Duration 做不到——持续 1 轮的状态在本轮内会生效任意多次。
        /// </summary>
        public bool ConsumedOnTrigger { get; set; }

        /// <summary>可选的脚本行为 Id。挂着这个状态期间，该脚本的常驻钩子对宿主生效。</summary>
        public string BehaviourId { get; set; }

        /// <summary>
        /// 对宿主暴击抵抗线的修正（负数 = 更容易被暴击）。
        /// 老 Boss 的「过热：暴击抵抗4」就是把 7 压到 4。
        /// </summary>
        public int CritResistDelta { get; set; }

        /// <summary>
        /// 宿主**受到**的最终伤害倍率，分数形式。(0,0) 表示不改。
        /// <para>「过热：受到最终伤害×1.5」写 (3, 2)。用分数不用 float，
        /// 是为了让取整在任何平台上都是同一个整数。</para>
        /// </summary>
        public int IncomingMultiplierNumerator { get; set; }

        public int IncomingMultiplierDenominator { get; set; }

        public bool IsPermanent => Duration < 0;
        public bool IsExpired => Duration == 0;

        public void AddStacks(int count) => Stacks += count;

        public void RefreshDuration(int duration)
        {
            if (duration > Duration) Duration = duration;
        }

        /// <summary>轮末递减。返回是否已过期。</summary>
        public bool Tick()
        {
            if (IsPermanent) return false;
            if (Duration > 0) Duration--;
            return Duration == 0;
        }

        public override string ToString() => $"{Id}×{Stacks}({(IsPermanent ? "∞" : Duration.ToString())})";
    }

    public sealed class StatusCollection
    {
        private readonly List<StatusEffect> _statuses = new();

        public IReadOnlyList<StatusEffect> All => _statuses;

        public StatusEffect Get(string id) => _statuses.FirstOrDefault(s => s.Id == id);

        public bool Has(string id) => Get(id) != null;

        /// <summary>同 Id 叠层并刷新持续时间，不重复挂。</summary>
        public void Apply(StatusEffect status)
        {
            StatusEffect existing = Get(status.Id);
            if (existing == null)
            {
                _statuses.Add(status);
                return;
            }

            existing.AddStacks(status.Stacks);
            existing.RefreshDuration(status.Duration);
        }

        public void Remove(string id) => _statuses.RemoveAll(s => s.Id == id);

        public int SumOutgoingDamageBonus => _statuses.Sum(s => s.OutgoingDamageBonus * s.Stacks);
        public int SumIncomingDamageReduction => _statuses.Sum(s => s.IncomingDamageReduction * s.Stacks);
        public int SumIncomingDamageBonus => _statuses.Sum(s => s.IncomingDamageBonus * s.Stacks);

        /// <summary>暴击抵抗线的总修正。</summary>
        public int SumCritResistDelta => _statuses.Sum(s => s.CritResistDelta);

        /// <summary>
        /// 受到伤害的总倍率（多个状态连乘）。没有任何倍率状态时返回 (1,1)。
        /// </summary>
        public (int Numerator, int Denominator) IncomingMultiplier
        {
            get
            {
                int num = 1;
                int den = 1;

                foreach (StatusEffect s in _statuses)
                {
                    if (s.IncomingMultiplierDenominator <= 0 || s.IncomingMultiplierNumerator <= 0) continue;
                    num *= s.IncomingMultiplierNumerator;
                    den *= s.IncomingMultiplierDenominator;
                }

                return (num, den);
            }
        }

        /// <summary>
        /// 一次伤害结算完后，移除所有「触发即消失」的状态，返回被移除的 Id。
        /// 只有真的参与了这次结算（有数值修正）的才算触发过。
        /// </summary>
        public List<string> ConsumeTriggered()
        {
            var consumed = new List<string>();
            for (int i = _statuses.Count - 1; i >= 0; i--)
            {
                if (!_statuses[i].ConsumedOnTrigger) continue;
                consumed.Add(_statuses[i].Id);
                _statuses.RemoveAt(i);
            }

            return consumed;
        }

        /// <summary>轮末递减并移除过期状态，返回被移除的状态 Id。</summary>
        public List<string> TickAndPrune()
        {
            var expired = new List<string>();
            for (int i = _statuses.Count - 1; i >= 0; i--)
            {
                if (_statuses[i].Tick())
                {
                    expired.Add(_statuses[i].Id);
                    _statuses.RemoveAt(i);
                }
            }

            return expired;
        }
    }
}
