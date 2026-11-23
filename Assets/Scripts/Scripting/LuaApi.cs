using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using TCGinFU.Core;

namespace TCGinFU.Scripting
{
    /// <summary>
    /// 暴露给脚本的全局函数——行为层的「动词表」。
    ///
    /// <para>每一个都转发到 <see cref="BattleEngine"/> 的写入口，所以脚本造成的状态变更
    /// 和内置逻辑造成的一样会发 <see cref="BattleEvent"/>，战报与 UI 不会脱节。
    /// 这也意味着 mod 能做的事天然被限制在引擎已有的动词里：
    /// 想要新动词，就得往引擎加，而不是让脚本绕过去直接改字段。</para>
    /// </summary>
    internal sealed class LuaApi
    {
        private readonly LuaProxies _proxies;

        /// <summary>当前正在驱动的引擎。引擎是单线程的，所以一个字段就够。</summary>
        internal BattleEngine Engine { get; set; }

        internal LuaApi(LuaProxies proxies) => _proxies = proxies;

        private BattleState State => Engine?.State;

        private Combatant Who(DynValue v) => _proxies.Resolve(v, State);

        private PlayerCharacter Player(DynValue v) => Who(v) as PlayerCharacter;

        internal void Install(Script script)
        {
            ScriptRef = script;
            Table g = script.Globals;

            // ── 伤害与治疗 ──
            g["damage"] = DynValue.NewCallback((_, a) =>
            {
                Combatant src = Who(a[0]);
                Combatant dst = Who(a[1]);
                int amount = Int(a, 2);
                DamageElement element = a.Count > 3 ? ParseElement(a[3].CastToString()) : DamageElement.Physical;

                // 第 5 个参数：是否沿用本次出牌的暴击结果。多段攻击要各自投骰时传 false
                CritResult crit = a.Count > 4 && a[4].CastToBool() ? CurrentCrit : CritResult.None;

                DamageResult r = Engine?.DealDamage(src, dst, amount, element, crit);
                return DynValue.NewNumber(r?.HpLost ?? 0);
            });

            g["heal"] = DynValue.NewCallback((_, a) =>
                DynValue.NewNumber(Engine?.Heal(Who(a[0]), Int(a, 1)) ?? 0));

            // 穿透伤害：完全无视护盾。老数据里「刺痛」「穿透」都是这个意思
            g["pierce"] = DynValue.NewCallback((_, a) =>
            {
                Combatant src = Who(a[0]);
                Combatant dst = Who(a[1]);
                DamageElement element = a.Count > 3 ? ParseElement(a[3].CastToString()) : DamageElement.Physical;

                var attack = new AttackContext(src, dst, Int(a, 2), element, CritResult.None)
                {
                    // 给一个大到护盾不可能挡住的数。护盾本身不会因为被无视而消耗，
                    // 所以多出来的部分不产生任何副作用
                    IgnoreShield = int.MaxValue / 2,
                };

                DamageResult r = Engine?.ResolveAttack(attack);
                return DynValue.NewNumber(r?.HpLost ?? 0);
            });

            g["shield"] = DynValue.NewCallback((_, a) =>
            {
                Engine?.GrantShield(Who(a[0]), ParseShield(a.Count > 2 ? a[2].CastToString() : "physical"), Int(a, 1));
                return DynValue.Nil;
            });

            // ── 资源 ──
            g["restore_mp"] = DynValue.NewCallback((_, a) =>
            {
                Engine?.RestoreMp(Player(a[0]), Int(a, 1));
                return DynValue.Nil;
            });

            g["gain_ap"] = DynValue.NewCallback((_, a) =>
            {
                Engine?.GrantAp(Player(a[0]), Int(a, 1));
                return DynValue.Nil;
            });

            g["draw"] = DynValue.NewCallback((_, a) =>
            {
                var drawn = Engine?.DrawCards(Player(a[0]), Int(a, 1));
                return DynValue.NewNumber(drawn?.Count ?? 0);
            });

            // ── 状态 ──
            g["apply_status"] = DynValue.NewCallback((_, a) =>
            {
                Combatant target = Who(a[0]);
                string id = a[1].CastToString();
                if (target == null || string.IsNullOrEmpty(id)) return DynValue.Nil;

                int duration = a.Count > 2 ? Int(a, 2) : 1;
                int stacks = a.Count > 3 ? Math.Max(1, Int(a, 3)) : 1;
                Engine.ApplyStatus(target, Engine.Statuses.Create(id, duration, stacks));
                return DynValue.Nil;
            });

            g["has_status"] = DynValue.NewCallback((_, a) =>
                DynValue.NewBoolean(Who(a[0])?.Statuses.Has(a[1].CastToString()) ?? false));

            g["status_stacks"] = DynValue.NewCallback((_, a) =>
                DynValue.NewNumber(Who(a[0])?.Statuses.Get(a[1].CastToString())?.Stacks ?? 0));

            g["remove_status"] = DynValue.NewCallback((_, a) =>
            {
                Who(a[0])?.Statuses.Remove(a[1].CastToString());
                return DynValue.Nil;
            });

            // ── 走位 ──
            // move_row 是「在前后排之间切换」，move_to 是「移动到指定排」。
            // 两者不能混用：「击退至后排」对已经在后排的人应当无事发生，
            // 用切换会把他拉到前排，效果正好相反。
            g["move_row"] = DynValue.NewCallback((_, a) =>
                DynValue.NewBoolean(Engine?.SwitchRow(Who(a[0]), free: true) ?? false));

            g["move_to"] = DynValue.NewCallback((_, a) =>
            {
                Row row = ParseRow(a[1].CastToString());
                return DynValue.NewBoolean(Engine?.MoveRow(Who(a[0]), row, free: true) ?? false);
            });

            // 「**可**免费移动1次」——不立刻移动，存成额度等玩家自己兑现
            g["grant_free_move"] = DynValue.NewCallback((_, a) =>
            {
                Engine?.GrantFreeMove(Player(a[0]), a.Count > 1 ? Int(a, 1) : 1);
                return DynValue.Nil;
            });

            // 从某个区搬一张牌进充能区：charge_from(来源的人, 贴给谁, "discard"|"deck"|"hand", 数量)
            // 返回实际贴进去的数量——区里不够时会少于要求的数量
            g["charge_from"] = DynValue.NewCallback((_, a) =>
            {
                PlayerCharacter from = Player(a[0]);
                PlayerCharacter to = a.Count > 1 && a[1].IsNotNil() ? Player(a[1]) : from;
                ChargeSource source = ParseChargeSource(a.Count > 2 ? a[2].CastToString() : "discard");
                int count = a.Count > 3 ? Int(a, 3) : 1;

                return DynValue.NewNumber(Engine?.ChargeFromZone(from, to, source, count) ?? 0);
            });

            // 把**正在打出的这张牌本身**变成充能指示物（「顺风充能」）。
            // 它结算完就不进弃牌堆了，所以要顺手告诉引擎别再安置一次
            g["charge_this"] = DynValue.NewCallback((_, a) =>
            {
                EffectContext ctx = CurrentEffect;
                if (ctx == null || Engine == null) return DynValue.False;

                // 敌人的牌是个备选池、没有充能区，所以这个动词只对玩家有意义
                PlayerCharacter to = (a.Count > 0 ? Player(a[0]) : null) ?? ctx.CasterPlayer;
                if (to == null) return DynValue.False;

                Engine.PlaceCharge(to, ctx.Card);
                ctx.CardRehomed = true;
                return DynValue.True;
            });

            // 敌人吞噬玩家的充能（「以太虹吸」）。和「消耗」不是一回事：
            // 不记进 ChargesSpent 统计，否则敌人一发虹吸就替玩家满足了升变条件
            g["devour_charges"] = DynValue.NewCallback((_, a) =>
                DynValue.NewNumber(Engine?.DevourCharges(Player(a[0]), a.Count > 1 ? Int(a, 1) : 1) ?? 0));

            // 推进场景命刻（「相位折跃」额外 +1）
            g["advance_clock"] = DynValue.NewCallback((_, a) =>
            {
                Engine?.AdvanceSceneClock(a.Count > 0 ? Int(a, 0) : 1);
                return DynValue.Nil;
            });

            // 充能区上限临时增减（「奥术结界」本轮及下轮 +1）。收回也走这里，传负数
            g["adjust_charge_cap"] = DynValue.NewCallback((_, a) =>
            {
                Engine?.AdjustChargeCapacity(Player(a[0]), Int(a, 1));
                return DynValue.Nil;
            });

            // 额外的「贴充能」次数（常规每轮 1 次）。轮始重置，所以要挂在 on_round_start 上
            g["grant_charge_placement"] = DynValue.NewCallback((_, a) =>
            {
                Engine?.GrantChargePlacement(Player(a[0]), a.Count > 1 ? Int(a, 1) : 1);
                return DynValue.Nil;
            });

            // 前后排整体互换，保持各自的列（相位折跃）
            g["swap_rows"] = DynValue.NewCallback((_, a) =>
            {
                Engine?.SwapRows(playerSide: a.Count == 0 || a[0].CastToBool());
                return DynValue.Nil;
            });

            // ── 查询战场 ──
            // enemies()/allies() 是**玩家视角**的简写，敌人的脚本不要用它们——
            // 敌人用它会把自己人当成「敌方」。敌人脚本一律用 opponents(me)/same_side(me)
            g["enemies"] = DynValue.NewCallback((_, _) => List(State?.LivingEnemies.Cast<Combatant>()));
            g["allies"] = DynValue.NewCallback((_, _) => List(State?.LivingPlayers.Cast<Combatant>()));

            g["opponents"] = DynValue.NewCallback((_, a) => List(SideOf(Who(a[0]), opposite: true)));
            g["same_side"] = DynValue.NewCallback((_, a) => List(SideOf(Who(a[0]), opposite: false)));

            g["stat"] = DynValue.NewCallback((_, a) =>
            {
                string key = a[0].CastToString();
                // 传第二个参数就是某人各自的口径，不传就是全队合计
                return DynValue.NewNumber(a.Count > 1 && a[1].IsNotNil()
                    ? State?.Stats.For(Who(a[1])?.Id, key) ?? 0
                    : State?.Stats.Team(key) ?? 0);
            });

            // 脚本记自己的累计量（「本局累计触发 N 次……」）。
            // 状态层只能说「现在挂着什么」，说不了「一共发生过几次」
            g["add_stat"] = DynValue.NewCallback((_, a) =>
            {
                string key = a[0].CastToString();
                if (string.IsNullOrEmpty(key) || State == null) return DynValue.Nil;

                State.Stats.Record(key, Who(a.Count > 1 ? a[1] : null)?.Id, a.Count > 2 ? Int(a, 2) : 1);
                return DynValue.Nil;
            });

            g["round"] = DynValue.NewCallback((_, _) => DynValue.NewNumber(State?.Round ?? 0));

            // 全队充能区合计——「真理炼金矩阵」的前置条件要读它
            g["team_charges"] = DynValue.NewCallback((_, _) =>
            {
                int total = 0;
                if (State != null)
                {
                    foreach (PlayerCharacter p in State.LivingPlayers) total += p.Zones.Charges.Count;
                }

                return DynValue.NewNumber(total);
            });

            // ── 掷骰 ──
            // 走引擎自己的 IRng，脚本不能自带随机源，否则同一个种子复盘不出同一局
            g["roll"] = DynValue.NewCallback((_, a) =>
            {
                int sides = Int(a, 0, 6);
                return DynValue.NewNumber(State?.Rng.NextInt(1, sides + 1) ?? 0);
            });

            g["roll_attribute"] = DynValue.NewCallback((_, a) =>
            {
                PlayerCharacter p = Player(a[0]);
                if (p == null || State == null) return DynValue.NewNumber(0);
                return DynValue.NewNumber(State.Rng.RollDie(p.Sheet.Dice[ParseColor(a[1].CastToString())]));
            });

            // ── 战报 ──
            g["log"] = DynValue.NewCallback((_, a) =>
            {
                Engine?.EmitScriptLog(a[0].CastToString());
                return DynValue.Nil;
            });
        }

        /// <summary>本次出牌投出的暴击结果，供 <c>damage(..., true)</c> 沿用。</summary>
        internal CritResult CurrentCrit { get; set; } = CritResult.None;

        /// <summary>当前正在结算的出牌上下文。只有出牌类钩子里有值。</summary>
        internal EffectContext CurrentEffect { get; set; }

        /// <summary>造表用的 Script 实例，<see cref="Install"/> 时填好。</summary>
        private Script ScriptRef { get; set; }

        /// <summary>包成 Lua 的 1-based 数组表，这样脚本里可以直接 <c>ipairs(enemies())</c>。</summary>
        private DynValue List(System.Collections.Generic.IEnumerable<Combatant> source)
        {
            var table = new Table(ScriptRef);
            int i = 1;
            if (source != null)
            {
                foreach (Combatant c in source) table[i++] = _proxies.Combatant(c);
            }

            return DynValue.NewTable(table);
        }

        /// <summary>
        /// 取 <paramref name="who"/> 的同阵营或对立面。
        /// 敌人脚本必须用这个而不是 <c>enemies()</c>——后者是玩家视角写死的，
        /// 敌人用它会把自己人当成「敌方」。
        /// </summary>
        private IEnumerable<Combatant> SideOf(Combatant who, bool opposite)
        {
            if (State == null || who == null) return System.Array.Empty<Combatant>();

            bool wantPlayers = who is Enemy ? opposite : !opposite;
            return wantPlayers
                ? State.LivingPlayers.Cast<Combatant>()
                : State.LivingEnemies.Cast<Combatant>();
        }

        private static int Int(CallbackArguments a, int index, int fallback = 0)
            => a.Count > index ? (int)a[index].CastToNumber().GetValueOrDefault(fallback) : fallback;

        // ──────────────────────────────── 字符串 ↔ 枚举 ────────────────────────────────

        internal static DamageElement ParseElement(string s)
            => Enum.TryParse(s, ignoreCase: true, out DamageElement e) ? e : DamageElement.Physical;

        internal static ShieldKind ParseShield(string s)
            => s != null && s.StartsWith("mag", StringComparison.OrdinalIgnoreCase)
                ? ShieldKind.Magical
                : ShieldKind.Physical;

        internal static ChargeSource ParseChargeSource(string s) => s?.ToLowerInvariant() switch
        {
            "deck" or "decktop" or "牌库" => ChargeSource.DeckTop,
            "hand" or "手牌" => ChargeSource.HandBottom,
            _ => ChargeSource.Discard,
        };

        internal static Row ParseRow(string s)
            => s != null && s.StartsWith("b", System.StringComparison.OrdinalIgnoreCase)
                ? Row.Back
                : Row.Front;

        internal static AttributeColor ParseColor(string s) => s?.ToLowerInvariant() switch
        {
            "might" or "red" or "力量" => AttributeColor.Might,
            "insight" or "blue" or "洞察" => AttributeColor.Insight,
            "dexterity" or "green" or "敏捷" => AttributeColor.Dexterity,
            "willpower" or "purple" or "意志" => AttributeColor.Willpower,
            _ => AttributeColor.Insight,
        };
    }
}
