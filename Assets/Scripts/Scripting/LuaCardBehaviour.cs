using System.Collections.Generic;
using MoonSharp.Interpreter;
using TCGinFU.Core;

namespace TCGinFU.Scripting
{
    /// <summary>
    /// 一张 Lua 卡。把 Lua 表上的函数接到 <see cref="ICardBehaviour"/> 的各个钩子上。
    ///
    /// <para>Lua 表里**写了哪个函数就有哪个钩子**，没写的就是没挂。
    /// 这比要求每张卡实现一个完整接口友好得多，也让「这张卡插手了什么」一眼可见。</para>
    ///
    /// <code>
    /// behaviour("ambush_opening", {
    ///   on_resolved = function(ctx)
    ///     if ctx.target and not ctx.target.is_down then
    ///       apply_status(ctx.target, "exposed", 1)
    ///     end
    ///   end,
    /// })
    /// </code>
    /// </summary>
    internal sealed class LuaCardBehaviour : ICardBehaviour
    {
        private readonly LuaScriptHost _host;
        private readonly Table _table;

        internal LuaCardBehaviour(LuaScriptHost host, string id, Table table)
        {
            _host = host;
            Id = id;
            _table = table;
        }

        public string Id { get; }

        /// <summary>这张卡挂了哪些钩子，供加载期对账与调试显示。</summary>
        internal IEnumerable<string> HookNames
        {
            get
            {
                foreach (TablePair pair in _table.Pairs)
                {
                    if (pair.Value.Type == DataType.Function) yield return pair.Key.CastToString();
                }
            }
        }

        private DynValue Hook(string name)
        {
            DynValue fn = _table.Get(name);
            return fn.Type == DataType.Function ? fn : null;
        }

        public bool CanPlay(EffectContext ctx, out string reason)
        {
            reason = null;

            DynValue fn = Hook("can_play");
            if (fn == null) return true;

            _host.BeginEffect(ctx);
            DynValue result = _host.Call(fn, _host.Proxies.EffectCtx(ctx));

            // 允许 `return false, "理由"`，也允许只 `return false`
            if (result.Type == DataType.Tuple)
            {
                if (result.Tuple.Length > 1 && result.Tuple[1].Type == DataType.String)
                {
                    reason = result.Tuple[1].String;
                }

                result = result.Tuple.Length > 0 ? result.Tuple[0] : DynValue.True;
            }

            // 脚本忘了 return 时按「可以打」处理：卡莫名其妙打不出去比少个效果更难查
            return result.IsNilOrNan() || result.CastToBool();
        }

        public void OnPlay(EffectContext ctx) => Fire("on_play", ctx);

        public void OnResolved(EffectContext ctx) => Fire("on_resolved", ctx);

        private void Fire(string hook, EffectContext ctx)
        {
            DynValue fn = Hook(hook);
            if (fn == null) return;

            _host.BeginEffect(ctx);
            _host.Call(fn, _host.Proxies.EffectCtx(ctx));
        }

        public bool TransformConditionMet(BattleState state, PlayerCharacter owner)
        {
            DynValue fn = Hook("transform_condition");
            if (fn == null) return true;

            // 升变条件在轮始问，此刻没有「正在打出的牌」，所以只给角色句柄
            return _host.Call(fn, _host.Proxies.Combatant(owner)).CastToBool();
        }

        public void ModifyAttack(AttackContext attack, Combatant host, AttackSide side)
        {
            DynValue fn = Hook(side == AttackSide.Attacker ? "on_attack" : "on_defend");
            if (fn == null) return;

            _host.CurrentCrit = attack.Crit;
            _host.Call(fn, _host.Proxies.Attack(attack), _host.Proxies.Combatant(host));
        }

        public void ModifyCritRoll(CritRollContext roll, Combatant host)
        {
            DynValue fn = Hook("on_crit_roll");
            if (fn == null) return;

            _host.CurrentCrit = CritResult.None;
            _host.Call(fn, _host.Proxies.CritRoll(roll), _host.Proxies.Combatant(host));
        }

        public void ModifyCost(CostContext cost, PlayerCharacter host)
        {
            DynValue fn = Hook("on_cost");
            if (fn == null) return;

            _host.CurrentCrit = CritResult.None;
            _host.Call(fn, _host.Proxies.Cost(cost), _host.Proxies.Combatant(host));
        }

        public void ModifyChargeSpend(ChargeSpendContext spend, PlayerCharacter host)
        {
            DynValue fn = Hook("on_charge_spend");
            if (fn == null) return;

            _host.CurrentCrit = CritResult.None;
            _host.Call(fn, _host.Proxies.ChargeSpend(spend), _host.Proxies.Combatant(host));
        }

        public void OnTrigger(TriggerContext trigger)
        {
            DynValue fn = Hook(LuaProxies.TriggerHookName(trigger.Trigger));
            if (fn == null) return;

            _host.CurrentCrit = CritResult.None;
            _host.Call(fn, _host.Proxies.Trigger(trigger));
        }
    }
}
