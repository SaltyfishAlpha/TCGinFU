using System;
using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>一种 <see cref="EffectKind"/> 的执行器。</summary>
    public interface IEffectHandler
    {
        EffectKind Kind { get; }
        void Apply(EffectContext ctx, EffectData data);
    }

    /// <summary>EffectKind → Handler 的注册表。内置全套默认实现，可按 Kind 覆盖。</summary>
    public sealed class EffectRegistry
    {
        private readonly Dictionary<EffectKind, IEffectHandler> _handlers = new();

        public static EffectRegistry CreateDefault()
        {
            var registry = new EffectRegistry();
            registry.Register(new DamageEffectHandler());
            registry.Register(new HealEffectHandler());
            registry.Register(new ShieldEffectHandler());
            registry.Register(new RestoreMpEffectHandler());
            registry.Register(new GainApEffectHandler());
            registry.Register(new DrawEffectHandler());
            registry.Register(new GainChargeEffectHandler());
            registry.Register(new ApplyStatusEffectHandler());
            registry.Register(new MoveRowEffectHandler());
            registry.Register(new GrantFreeMoveEffectHandler());
            registry.Register(new SwapRowsEffectHandler());
            registry.Register(new CoverEffectHandler());
            return registry;
        }

        public void Register(IEffectHandler handler)
            => _handlers[handler.Kind] = handler ?? throw new ArgumentNullException(nameof(handler));

        public bool TryGet(EffectKind kind, out IEffectHandler handler)
            => _handlers.TryGetValue(kind, out handler);

        /// <summary>按顺序结算一串效果，逐条检查附加条件。</summary>
        public void ApplyAll(EffectContext ctx, IReadOnlyList<EffectData> effects)
        {
            if (effects == null) return;

            foreach (EffectData data in effects)
            {
                if (!TryGet(data.Kind, out IEffectHandler handler))
                {
                    throw new InvalidOperationException($"没有注册 {data.Kind} 的执行器");
                }

                handler.Apply(ctx, data);
            }
        }
    }
}
