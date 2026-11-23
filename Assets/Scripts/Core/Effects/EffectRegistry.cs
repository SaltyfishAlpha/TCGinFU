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
            registry.Register(new DiscardEffectHandler());
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
        /// <summary>
        /// 从 <paramref name="from"/> 开始逐条结算。
        ///
        /// <para>返回**挂起在第几条**，没有挂起则返回 -1。效果要求挂起时
        /// （<see cref="EffectContext.Suspended"/>）这条效果已经整条做完了，
        /// 所以恢复时从返回值的下一条接着走。</para>
        /// </summary>
        public int ApplyAll(EffectContext ctx, IReadOnlyList<EffectData> effects, int from = 0)
        {
            if (effects == null) return -1;

            for (int i = from < 0 ? 0 : from; i < effects.Count; i++)
            {
                EffectData data = effects[i];
                if (!TryGet(data.Kind, out IEffectHandler handler))
                {
                    throw new InvalidOperationException($"没有注册 {data.Kind} 的执行器");
                }

                handler.Apply(ctx, data);

                if (!ctx.Suspended) continue;

                ctx.Suspended = false;
                return i;
            }

            return -1;
        }
    }
}
