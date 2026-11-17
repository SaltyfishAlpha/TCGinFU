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
