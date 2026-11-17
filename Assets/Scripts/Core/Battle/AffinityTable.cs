using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>某个角色对九大伤害属性的亲和度表（§7.4）。未登记的属性按 Normal 处理。</summary>
    public sealed class AffinityTable
    {
        private readonly Dictionary<DamageElement, Affinity> _map = new();

        public Affinity this[DamageElement element]
            => _map.TryGetValue(element, out Affinity affinity) ? affinity : Affinity.Normal;

        public AffinityTable Set(DamageElement element, Affinity affinity)
        {
            _map[element] = affinity;
            return this;
        }

        public AffinityTable Vulnerable(DamageElement element) => Set(element, Affinity.Vulnerable);
        public AffinityTable Resistant(DamageElement element) => Set(element, Affinity.Resistant);
        public AffinityTable Immune(DamageElement element) => Set(element, Affinity.Immune);
        public AffinityTable Absorb(DamageElement element) => Set(element, Affinity.Absorb);

        public static readonly AffinityTable Neutral = new();
    }
}
