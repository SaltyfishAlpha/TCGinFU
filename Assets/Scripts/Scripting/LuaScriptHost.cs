using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using TCGinFU.Core;

namespace TCGinFU.Scripting
{
    /// <summary>加载脚本时的报告。一个文件写坏不该让整包加载失败。</summary>
    public sealed class ScriptLoadReport
    {
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();

        /// <summary>成功注册的行为 Id → 它挂了哪些钩子。</summary>
        public Dictionary<string, List<string>> Registered { get; } = new();

        public bool IsValid => Errors.Count == 0;

        public override string ToString()
        {
            var lines = new List<string> { $"注册 {Registered.Count} 个行为" };
            lines.AddRange(Errors.ConvertAll(e => "错误：" + e));
            lines.AddRange(Warnings.ConvertAll(w => "警告：" + w));
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// Lua 行为层的宿主。
    ///
    /// <para><b>为什么是 Lua 而不是 C#：</b>发布后玩家要能自定义卡牌与角色。
    /// C# 的 behaviour 要重新编译、要随版本分发 dll，玩家做不到；
    /// 而纯数据的 DSL 又盖不住老卡那种「掷骰分三档结算」的逻辑。</para>
    ///
    /// <para><b>沙箱：</b>用 MoonSharp 的 HardSandbox 预设关掉 io / os / 加载外部文件，
    /// 再加上手写的 API 绑定（见 <see cref="LuaApi"/> 与 <see cref="LuaProxies"/>），
    /// 脚本能碰到的东西就只有我们明确给出的那些函数与字段。</para>
    ///
    /// <para><b>确定性：</b>脚本不许自带随机源，掷骰一律走引擎的 <see cref="IRng"/>，
    /// 否则同一个种子复盘不出同一局，联机与战报回放都会对不上。</para>
    /// </summary>
    public sealed class LuaScriptHost
    {
        private readonly Script _script;
        private readonly LuaApi _api;
        private readonly Dictionary<string, LuaCardBehaviour> _behaviours = new(StringComparer.Ordinal);

        private readonly ScriptWatchdog _watchdog;

        /// <param name="budgetMs">
        /// 单次钩子调用的时间上限。超时的脚本会被中断并记成一条故障事件，战斗继续。
        /// </param>
        public LuaScriptHost(int budgetMs = ScriptWatchdog.DefaultBudgetMs)
        {
            // HardSandbox：没有 io、os、debug，也没有 require/dofile/loadfile——
            // mod 脚本碰不到文件系统，也没法从磁盘再拉一段代码进来
            _script = new Script(CoreModules.Preset_HardSandbox);

            // 时限守卫：沙箱挡得住「乱碰东西」，挡不住「不还回来」——
            // 一个 while true do end 不抛任何异常，只是让主线程再也不回来
            _watchdog = new ScriptWatchdog(budgetMs);
            _script.AttachDebugger(_watchdog);

            Proxies = new LuaProxies(_script);
            _api = new LuaApi(Proxies);
            _api.Install(_script);

            // 注册入口：脚本靠它把自己挂到某个 behaviourId 上
            _script.Globals["behaviour"] = DynValue.NewCallback((_, args) =>
            {
                string id = args[0].CastToString();
                if (string.IsNullOrEmpty(id)) throw new ScriptRuntimeException("behaviour 的第 1 个参数要是行为 Id");
                if (args[1].Type != DataType.Table) throw new ScriptRuntimeException($"{id} 的第 2 个参数要是一张表");

                _behaviours[id] = new LuaCardBehaviour(this, id, args[1].Table);
                return DynValue.Nil;
            });
        }

        internal LuaProxies Proxies { get; }

        /// <summary>当前驱动的引擎。<see cref="BindEngine"/> 之后脚本的所有写操作都落到它上面。</summary>
        internal BattleEngine CurrentEngine => _api.Engine;

        /// <summary>本次调用所处的暴击结果，供脚本里的 <c>damage(..., true)</c> 沿用。</summary>
        internal CritResult CurrentCrit
        {
            get => _api.CurrentCrit;
            set => _api.CurrentCrit = value;
        }

        /// <summary>
        /// 进入一次出牌类钩子。把上下文交给 API 层，
        /// <c>charge_this</c> 这类「作用于正在打出的这张牌」的动词才知道说的是哪张。
        /// </summary>
        internal void BeginEffect(EffectContext ctx)
        {
            _api.CurrentCrit = ctx.Crit;
            _api.CurrentEffect = ctx;
        }

        /// <summary>
        /// 把宿主绑到一局战斗上。换局必须重绑——否则上一局的参战者句柄还缓存着，
        /// 脚本会拿到已经不在场上的对象。
        /// </summary>
        public void BindEngine(BattleEngine engine)
        {
            _api.Engine = engine;
            Proxies.Clear();
        }

        /// <summary>
        /// 加载一段脚本源码。<paramref name="name"/> 用于报错定位，传文件名。
        /// 单个文件语法错或运行期抛错只记进报告，不影响其它文件。
        /// </summary>
        public void Load(string source, string name, ScriptLoadReport report)
        {
            try
            {
                // 加载期也要管：死循环写在文件顶层一样能把游戏卡在加载界面。
                // 但预算要宽得多——加载要编译整个文件，真实内容包的 miku.lua 就超过 50ms，
                // 用钩子那档预算会把正常内容包直接误杀
                _watchdog.Begin($"加载 {name}", ScriptWatchdog.DefaultLoadBudgetMs);
                _script.DoString(source, codeFriendlyName: name);
            }
            catch (InterpreterException ex)
            {
                // DecoratedMessage 带文件名与行号，是 mod 作者唯一能据以改错的信息
                report.Errors.Add($"{name}：{ex.DecoratedMessage ?? ex.Message}");
            }
            catch (Exception ex)
            {
                report.Errors.Add($"{name}：{ex.Message}");
            }
            finally
            {
                _watchdog.End();
            }
        }

        /// <summary>
        /// 把加载到的行为装进引擎用的注册表，并对账：
        /// 卡面写了 behaviourId 却没有脚本、或脚本注册了没人用的 Id，都会记进报告。
        /// 这是内容包做完之后最容易出的错，所以要在加载期就喊出来。
        /// </summary>
        public BehaviourRegistry BuildRegistry(IEnumerable<string> declaredBehaviourIds, ScriptLoadReport report)
        {
            var registry = new BehaviourRegistry();

            foreach (KeyValuePair<string, LuaCardBehaviour> pair in _behaviours)
            {
                registry.Register(pair.Value);
                report.Registered[pair.Key] = new List<string>(pair.Value.HookNames);

                if (report.Registered[pair.Key].Count == 0)
                {
                    report.Warnings.Add($"行为 {pair.Key} 一个钩子都没挂，等同于没写");
                }
            }

            if (declaredBehaviourIds == null) return registry;

            var declared = new HashSet<string>(declaredBehaviourIds, StringComparer.Ordinal);

            foreach (string id in declared)
            {
                if (!_behaviours.ContainsKey(id)) report.Warnings.Add($"卡面声明了 behaviourId「{id}」，但没有脚本实现它");
            }

            foreach (string id in _behaviours.Keys)
            {
                if (!declared.Contains(id)) report.Warnings.Add($"脚本注册了「{id}」，但没有卡面引用它");
            }

            return registry;
        }

        /// <summary>本局累计有多少次脚本因超时被中断。</summary>
        public int TimeoutCount => _watchdog.TimeoutCount;

        /// <summary>
        /// 调一个 Lua 函数，带时限。异常原样抛出，由引擎的 Invoke 统一隔离并记进事件流。
        /// </summary>
        internal DynValue Call(DynValue fn, params DynValue[] args)
        {
            _watchdog.Begin("钩子调用");
            try
            {
                return _script.Call(fn, args);
            }
            finally
            {
                _watchdog.End();
            }
        }
    }
}
