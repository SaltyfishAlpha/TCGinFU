using System.Collections.Generic;
using System.Diagnostics;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;

namespace TCGinFU.Scripting
{
    /// <summary>
    /// 脚本执行时限。
    ///
    /// <para><b>为什么必须有：</b>发布后玩家能装 mod，而 Lua 里一个
    /// <c>while true do end</c> 就能把主线程挂死——游戏不是崩溃而是整个卡住，
    /// 玩家连「这是哪张卡的问题」都看不到。异常隔离拦不住这种，因为它根本不抛异常。</para>
    ///
    /// <para><b>实现：</b>MoonSharp 没有现成的指令预算，但它的
    /// <see cref="IDebugger"/> 会被虚拟机按指令轮询 <see cref="IsPauseRequested"/>。
    /// 这里借这个钩子在超时后抛出 <see cref="ScriptRuntimeException"/>，
    /// 让它顺着正常的异常路径被引擎的 Invoke 隔离掉——那边已经会把它记成
    /// 一条 <c>BehaviourFaulted</c> 事件，所以超时和脚本报错在战报上长得一样。</para>
    ///
    /// <para>代价是挂了调试器会让脚本整体变慢。对卡牌行为这种「几十条指令跑完」的
    /// 用量完全不要紧，换来的是 mod 写崩了也只卡一帧。</para>
    /// </summary>
    internal sealed class ScriptWatchdog : IDebugger
    {
        /// <summary>
        /// 单次钩子调用的时间上限。一张卡的行为跑到毫秒级就该结束了，
        /// 100ms 是留给慢机器和首次 JIT 的余量——**宁可多等 100ms 也不要误杀正常卡**：
        /// 误杀会让一张写对的卡静默失效，比多卡一帧难查得多。
        /// </summary>
        public const int DefaultBudgetMs = 100;

        /// <summary>
        /// 加载一个脚本文件的时间上限。加载要编译整个文件，比跑单个钩子重一个量级——
        /// 真实内容包的 miku.lua 就超过 50ms。这里给到秒级，
        /// 它要拦的是死循环（永不返回），不是「有点慢」。
        /// </summary>
        public const int DefaultLoadBudgetMs = 5000;

        private readonly Stopwatch _clock = new();
        private readonly int _budgetMs;
        private int _currentBudgetMs;
        private bool _armed;
        private string _where;

        internal ScriptWatchdog(int budgetMs = DefaultBudgetMs) => _budgetMs = budgetMs;

        /// <summary>累计超时次数，供诊断「是哪张 mod 卡在拖慢游戏」。</summary>
        internal int TimeoutCount { get; private set; }

        /// <summary>
        /// 开始计时。<paramref name="where"/> 用于报错时指出是哪个钩子；
        /// <paramref name="budgetMs"/> 传 0 用默认预算。
        /// </summary>
        internal void Begin(string where, int budgetMs = 0)
        {
            _where = where;
            _currentBudgetMs = budgetMs > 0 ? budgetMs : _budgetMs;
            _armed = true;
            _clock.Restart();
        }

        internal void End()
        {
            _armed = false;
            _clock.Stop();
        }

        private bool Expired => _armed && _clock.ElapsedMilliseconds > _currentBudgetMs;

        // ──────────────────────────── IDebugger ────────────────────────────

        public DebuggerCaps GetDebuggerCaps() => DebuggerCaps.CanDebugByteCode;

        /// <summary>虚拟机按指令轮询这里。返回 true 之后它才会调 <see cref="GetAction"/>。</summary>
        public bool IsPauseRequested() => Expired;

        public DebuggerAction GetAction(int ilOffset, SourceRef sourceref)
        {
            if (!Expired) return new DebuggerAction { Action = DebuggerAction.ActionType.Run };

            TimeoutCount++;
            End();

            // 抛出去，交给引擎的异常隔离——超时和脚本报错在战报上就是同一类故障
            throw new ScriptRuntimeException(
                $"脚本执行超过 {_currentBudgetMs}ms 已被中断（{_where}）。" +
                "多半是死循环或者递归没有出口。");
        }

        public bool SignalRuntimeException(ScriptRuntimeException ex) => false;

        public void SetDebugService(DebugService debugService) { }

        public void SetSourceCode(SourceCode sourceCode) { }

        public void SetByteCode(string[] byteCode) { }

        public void SignalExecutionEnded() => End();

        public void Update(WatchType watchType, IEnumerable<WatchItem> items, int threadId) { }

        public List<DynamicExpression> GetWatchItems() => new();

        public void RefreshBreakpoints(IEnumerable<SourceRef> refs) { }
    }
}
