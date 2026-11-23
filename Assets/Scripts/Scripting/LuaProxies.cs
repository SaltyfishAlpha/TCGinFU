using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using TCGinFU.Core;

namespace TCGinFU.Scripting
{
    /// <summary>
    /// 把引擎对象包成 Lua 表。
    ///
    /// <para>刻意**不用** MoonSharp 的 <c>UserData.RegisterType</c> 反射绑定，原因有三：
    /// ① 反射绑定在 IL2CPP/AOT 下要靠 link.xml 保活，发布后玩家装 mod 时最容易炸的就是这里；
    /// ② 反射会把 C# 对象的**全部**公开成员暴露给脚本，等于没有沙箱——
    ///    mod 脚本能顺着 <c>engine.State.Players[0]</c> 摸到任何东西，还能绕过引擎写入口直接改字段；
    /// ③ 手写绑定逼着我们明确「脚本能看什么、能改什么」，这张表就是行为层真正的 API 契约。</para>
    ///
    /// <para>代价是每加一个字段要手写一行。这是故意的——API 面应该长得慢。</para>
    /// </summary>
    internal sealed class LuaProxies
    {
        private readonly Script _script;

        /// <summary>同一个参战者在一次脚本调用里拿到的是同一张表，<c>a == b</c> 在 Lua 侧才成立。</summary>
        private readonly Dictionary<Combatant, Table> _combatants = new();

        /// <summary>反向表：脚本把句柄传回来时要认得出它是谁。</summary>
        private readonly Dictionary<Table, Combatant> _byTable = new();

        internal LuaProxies(Script script) => _script = script;

        /// <summary>战斗结束或换局时清掉缓存，别让上一局的对象被下一局的脚本捞到。</summary>
        internal void Clear()
        {
            _combatants.Clear();
            _byTable.Clear();
        }

        /// <summary>把脚本传回来的句柄还原成参战者。也接受裸的 id 字符串。</summary>
        internal Combatant Resolve(DynValue value, BattleState state)
        {
            if (value == null) return null;

            if (value.Type == DataType.Table && _byTable.TryGetValue(value.Table, out Combatant c)) return c;
            if (value.Type == DataType.String) return state?.Find(value.String);

            return null;
        }

        // ──────────────────────────────── 参战者 ────────────────────────────────

        /// <summary>
        /// 参战者句柄。只读——所有状态变更都必须走 <see cref="LuaApi"/> 的全局函数，
        /// 那边才会发 <see cref="BattleEvent"/>。直接改字段会让战报和 UI 失去同步。
        /// </summary>
        internal DynValue Combatant(Combatant c)
        {
            if (c == null) return DynValue.Nil;

            if (_combatants.TryGetValue(c, out Table cached)) return DynValue.NewTable(cached);

            var table = new Table(_script);
            table["id"] = c.Id;

            var meta = new Table(_script);
            meta["__index"] = DynValue.NewCallback((_, args) => ReadCombatant(c, args[1].CastToString()));
            meta["__newindex"] = DynValue.NewCallback((_, args) =>
                throw new ScriptRuntimeException(
                    $"不能直接改 {c.Name} 的 {args[1].CastToString()}；状态变更请用 damage/heal/shield 这些函数"));

            table.MetaTable = meta;
            _combatants[c] = table;
            _byTable[table] = c;
            return DynValue.NewTable(table);
        }

        private DynValue ReadCombatant(Combatant c, string key) => key switch
        {
            "id" => DynValue.NewString(c.Id),
            "name" => DynValue.NewString(c.Name),
            "hp" => DynValue.NewNumber(c.Hp),
            "max_hp" => DynValue.NewNumber(c.MaxHp),
            "row" => DynValue.NewString(c.Row == Row.Front ? "front" : "back"),
            "slot" => DynValue.NewNumber(c.Slot),
            "is_down" => DynValue.NewBoolean(c.IsDown),
            "crit_resist" => DynValue.NewNumber(c.CritResist),
            "shield_physical" => DynValue.NewNumber(c.Shields.Physical),
            "shield_magical" => DynValue.NewNumber(c.Shields.Magical),
            // 自动双盾与卡牌给的护盾要分得开：有卡面专门写「无视**自动**物理护盾」
            "auto_shield_physical" => DynValue.NewNumber(c.Shields.AutoPhysical),
            "auto_shield_magical" => DynValue.NewNumber(c.Shields.AutoMagical),
            "damage_taken_this_round" => DynValue.NewNumber(c.DamageTakenThisRound),
            "damage_dealt_this_round" => DynValue.NewNumber(c.DamageDealtThisRound),
            "is_enemy" => DynValue.NewBoolean(c is Enemy),
            "is_player" => DynValue.NewBoolean(c is PlayerCharacter),

            // 「处于负面状态」——老卡多张用它做前置条件
            "has_negative_status" => DynValue.NewBoolean(HasNegativeStatus(c)),

            _ => ReadPlayerOnly(c, key),
        };

        private DynValue ReadPlayerOnly(Combatant c, string key)
        {
            if (c is not PlayerCharacter p) return DynValue.Nil;

            return key switch
            {
                "mp" => DynValue.NewNumber(p.Mp),
                "max_mp" => DynValue.NewNumber(p.MaxMp),
                "ap" => DynValue.NewNumber(p.Ap),
                "in_crisis" => DynValue.NewBoolean(p.IsInCrisis),
                "charges" => DynValue.NewNumber(p.Zones.Charges.Count),
                "free_moves" => DynValue.NewNumber(p.FreeMoves),
                "has_moved" => DynValue.NewBoolean(p.HasMovedThisRound),
                "hand_count" => DynValue.NewNumber(p.Zones.Hand.Count),
                "deck_count" => DynValue.NewNumber(p.Zones.Deck.Count),
                "discard_count" => DynValue.NewNumber(p.Zones.Discard.Count),
                "form_attack" => DynValue.NewNumber(p.FormAttackBase),
                "form_defense" => DynValue.NewNumber(p.FormDefenseBase),
                "continuous" => DynValue.NewString(p.Zones.Continuous?.Definition.Id ?? ""),
                "transformed" => DynValue.NewBoolean(p.Transform.Active),
                _ => DynValue.Nil,
            };
        }

        /// <summary>
        /// 「负面状态」的判定口径：带持续伤害、或削弱了宿主数值的状态。
        /// 规则书没有单列负面状态标签，这里按效果反推——比让每张卡自己列一串 id 稳。
        /// </summary>
        private static bool HasNegativeStatus(Combatant c)
        {
            foreach (StatusEffect s in c.Statuses.All)
            {
                if (s.DamagePerRound > 0 || s.IncomingDamageBonus > 0 || s.OutgoingDamageBonus < 0) return true;
            }

            return false;
        }

        // ──────────────────────────────── 攻击结算单 ────────────────────────────────

        /// <summary>
        /// 可写的攻击结算单。这是脚本唯一能改伤害的地方，字段与 <see cref="AttackContext"/> 一一对应。
        /// </summary>
        internal DynValue Attack(AttackContext a)
        {
            if (a == null) return DynValue.Nil;

            var table = new Table(_script);
            var meta = new Table(_script);

            meta["__index"] = DynValue.NewCallback((_, args) => args[1].CastToString() switch
            {
                "source" => Combatant(a.Source),
                "target" => Combatant(a.Target),
                "base_amount" => DynValue.NewNumber(a.BaseAmount),
                "flat_bonus" => DynValue.NewNumber(a.FlatBonus),
                "reduction" => DynValue.NewNumber(a.Reduction),
                "final_amount" => DynValue.NewNumber(a.FinalAmount),
                "element" => DynValue.NewString(a.Element.ToString().ToLowerInvariant()),
                "ignore_shield" => DynValue.NewNumber(a.IgnoreShield),
                "avoided" => DynValue.NewBoolean(a.Avoided),
                "crit" => DynValue.NewBoolean(a.Crit.IsCrit),
                "super_crit" => DynValue.NewBoolean(a.Crit.IsSuperCrit),
                "card" => DynValue.NewString(a.Card?.Definition.Id ?? ""),
                "card_ranged" => DynValue.NewBoolean(a.Card?.Definition.IsRanged ?? false),
                "intent" => DynValue.NewString(a.Intent?.Label ?? ""),
                "from_card" => DynValue.NewBoolean(a.IsFromCard),
                "from_intent" => DynValue.NewBoolean(a.IsFromIntent),
                _ => DynValue.Nil,
            });

            meta["__newindex"] = DynValue.NewCallback((_, args) =>
            {
                string key = args[1].CastToString();
                DynValue v = args[2];

                switch (key)
                {
                    case "base_amount": a.BaseAmount = (int)v.CastToNumber().GetValueOrDefault(); break;
                    case "flat_bonus": a.FlatBonus = (int)v.CastToNumber().GetValueOrDefault(); break;
                    case "reduction": a.Reduction = (int)v.CastToNumber().GetValueOrDefault(); break;
                    case "ignore_shield": a.IgnoreShield = (int)v.CastToNumber().GetValueOrDefault(); break;
                    case "avoided": a.Avoided = v.CastToBool(); break;
                    case "element": a.Element = LuaApi.ParseElement(v.CastToString()); break;

                    // 「本次攻击不会暴击」——老卡「时间悖论」。只能抹掉，不能凭空造暴击：
                    // 暴击是投骰结果，脚本直接写 true 会让骰子失去意义
                    case "crit" when !v.CastToBool(): a.Crit = CritResult.None; break;

                    // 暴击倍率写成「几分之几」：×1.75 → crit_multiplier = {7, 4}
                    case "crit_multiplier":
                        a.CritMultiplier = v.Type == DataType.Table
                            ? ((int)v.Table.Get(1).CastToNumber().GetValueOrDefault(1),
                               (int)v.Table.Get(2).CastToNumber().GetValueOrDefault(1))
                            : null;
                        break;

                    default:
                        throw new ScriptRuntimeException($"攻击结算单上没有可写字段 {key}");
                }

                return DynValue.Nil;
            });

            table.MetaTable = meta;
            return DynValue.NewTable(table);
        }

        // ──────────────────────────────── 暴击掷骰 ────────────────────────────────

        /// <summary>骰值已出、暴击未判时的可写结算单。</summary>
        internal DynValue CritRoll(CritRollContext r)
        {
            var table = new Table(_script);
            var meta = new Table(_script);

            meta["__index"] = DynValue.NewCallback((_, args) =>
            {
                // 下标访问 roll[1] / roll[2] 取第几颗骰，与卡面「第 1 颗骰」对齐
                DynValue key = args[1];
                if (key.Type == DataType.Number)
                {
                    int i = (int)key.Number - 1;
                    return i >= 0 && i < r.Rolls.Count ? DynValue.NewNumber(r.Rolls[i]) : DynValue.Nil;
                }

                return key.CastToString() switch
                {
                    "count" => DynValue.NewNumber(r.Rolls.Count),
                    "bonus" => DynValue.NewNumber(r.RollBonus),
                    "resist_delta" => DynValue.NewNumber(r.ResistDelta),
                    "crit_resist" => DynValue.NewNumber(r.CritResist),
                    "dual_color" => DynValue.NewBoolean(r.DualColor),
                    "card" => DynValue.NewString(r.Card?.Id ?? ""),
                    "card_ranged" => DynValue.NewBoolean(r.Card?.IsRanged ?? false),
                    "target" => Combatant(r.Target),
                    _ => DynValue.Nil,
                };
            });

            meta["__newindex"] = DynValue.NewCallback((_, args) =>
            {
                DynValue key = args[1];
                int value = (int)args[2].CastToNumber().GetValueOrDefault();

                if (key.Type == DataType.Number)
                {
                    int i = (int)key.Number - 1;
                    if (i >= 0 && i < r.Rolls.Count) r.Rolls[i] = value;
                    return DynValue.Nil;
                }

                switch (key.CastToString())
                {
                    case "bonus": r.RollBonus = value; break;
                    case "resist_delta": r.ResistDelta = value; break;
                    default: throw new ScriptRuntimeException($"掷骰结算单上没有可写字段 {key.CastToString()}");
                }

                return DynValue.Nil;
            });

            table.MetaTable = meta;
            return DynValue.NewTable(table);
        }

        // ──────────────────────────────── 费用 ────────────────────────────────

        /// <summary>可写的费用结算单。改它的脚本必须无副作用——它会在校验期被反复调用。</summary>
        internal DynValue Cost(CostContext c)
        {
            var table = new Table(_script);
            var meta = new Table(_script);

            meta["__index"] = DynValue.NewCallback((_, args) => args[1].CastToString() switch
            {
                "ap" => DynValue.NewNumber(c.Ap),
                "mp" => DynValue.NewNumber(c.Mp),
                "charge" => DynValue.NewNumber(c.Charge),
                "round" => DynValue.NewNumber(c.Round),
                "card" => DynValue.NewString(c.Card?.Id ?? ""),
                "card_kind" => DynValue.NewString(c.Card?.Kind.ToString().ToLowerInvariant() ?? ""),
                "card_category" => DynValue.NewString(c.Card?.Category.ToString().ToLowerInvariant() ?? ""),
                "base_ap" => DynValue.NewNumber(c.Card?.ApCost ?? 0),
                "base_mp" => DynValue.NewNumber(c.Card?.MpCost ?? 0),
                _ => DynValue.Nil,
            });

            meta["__newindex"] = DynValue.NewCallback((_, args) =>
            {
                int value = (int)args[2].CastToNumber().GetValueOrDefault();
                switch (args[1].CastToString())
                {
                    case "ap": c.Ap = value; break;
                    case "mp": c.Mp = value; break;
                    case "charge": c.Charge = value; break;
                    default: throw new ScriptRuntimeException($"费用结算单上没有可写字段 {args[1].CastToString()}");
                }

                return DynValue.Nil;
            });

            table.MetaTable = meta;
            return DynValue.NewTable(table);
        }

        // ──────────────────────────────── 消耗充能 ────────────────────────────────

        /// <summary>充能已离开充能区、尚未进弃牌堆时的可写结算单。</summary>
        internal DynValue ChargeSpend(ChargeSpendContext s)
        {
            var table = new Table(_script);
            var meta = new Table(_script);

            meta["__index"] = DynValue.NewCallback((_, args) => args[1].CastToString() switch
            {
                "player" => Combatant(s.Player),
                "retained" => DynValue.NewBoolean(s.Retained),
                "use" => DynValue.NewString(s.Use.ToString().ToLowerInvariant()),
                "card" => DynValue.NewString(s.Token?.Card.Definition.Id ?? ""),
                "owner" => DynValue.NewString(s.Token?.OwnerId ?? ""),
                _ => DynValue.Nil,
            });

            meta["__newindex"] = DynValue.NewCallback((_, args) =>
            {
                if (args[1].CastToString() != "retained")
                {
                    throw new ScriptRuntimeException($"消耗充能结算单上没有可写字段 {args[1].CastToString()}");
                }

                s.Retained = args[2].CastToBool();
                return DynValue.Nil;
            });

            table.MetaTable = meta;
            return DynValue.NewTable(table);
        }

        // ──────────────────────────────── 出牌上下文 ────────────────────────────────

        internal DynValue EffectCtx(EffectContext ctx)
        {
            var table = new Table(_script);
            var meta = new Table(_script);

            meta["__index"] = DynValue.NewCallback((_, args) => args[1].CastToString() switch
            {
                "caster" => Combatant(ctx.Caster),
                "target" => Combatant(ctx.ChosenTarget),
                "current_target" => Combatant(ctx.CurrentTarget),
                "card" => DynValue.NewString(ctx.Definition.Id),
                "card_name" => DynValue.NewString(ctx.Definition.Name),
                "crit" => DynValue.NewBoolean(ctx.Crit.IsCrit),
                "super_crit" => DynValue.NewBoolean(ctx.Crit.IsSuperCrit),
                "charge_spent" => DynValue.NewNumber(ctx.ChargeSpent),
                "round" => DynValue.NewNumber(ctx.State.Round),
                "incoming_attack" => Attack(ctx.CurrentAttack),

                // 玩家在界面上做的额外选择。界面还没做出来时是空的，
                // 所以脚本一律要写成 `ctx.choice("x", 默认值)`
                "choice" => DynValue.NewCallback((_, args) =>
                    DynValue.NewNumber(ctx.Choice(args[1].CastToString(), (int)args[2].CastToNumber().GetValueOrDefault()))),

                _ => DynValue.Nil,
            });

            // 唯一可写的是「这张牌已被我安置到别处」——见 EffectContext.CardRehomed
            meta["__newindex"] = DynValue.NewCallback((_, args) =>
            {
                if (args[1].CastToString() != "card_rehomed")
                {
                    throw new ScriptRuntimeException($"出牌上下文上没有可写字段 {args[1].CastToString()}");
                }

                ctx.CardRehomed = args[2].CastToBool();
                return DynValue.Nil;
            });

            table.MetaTable = meta;
            return DynValue.NewTable(table);
        }

        // ──────────────────────────────── 触发上下文 ────────────────────────────────

        internal DynValue Trigger(TriggerContext t)
        {
            var table = new Table(_script);
            var meta = new Table(_script);

            meta["__index"] = DynValue.NewCallback((_, args) => args[1].CastToString() switch
            {
                "kind" => DynValue.NewString(TriggerName(t.Trigger)),
                "host" => Combatant(t.Host),
                "other" => Combatant(t.Other),
                "amount" => DynValue.NewNumber(t.Amount),
                "round" => DynValue.NewNumber(t.State.Round),
                "source" => DynValue.NewString(t.Source.ToString().ToLowerInvariant()),
                "card" => DynValue.NewString(t.Card?.Definition.Id ?? ""),
                "was_crit" => DynValue.NewBoolean(t.Damage?.IsCrit ?? false),
                _ => DynValue.Nil,
            });

            table.MetaTable = meta;
            return DynValue.NewTable(table);
        }

        internal static string TriggerName(BattleTrigger trigger) => trigger switch
        {
            BattleTrigger.DamageTaken => "damage_taken",
            BattleTrigger.DamageDealt => "damage_dealt",
            BattleTrigger.CritDealt => "crit_dealt",
            BattleTrigger.CardPlayed => "card_played",
            BattleTrigger.ChargeSpent => "charge_spent",
            BattleTrigger.RoundStart => "round_start",
            BattleTrigger.RoundEnd => "round_end",
            _ => trigger.ToString().ToLowerInvariant(),
        };

        /// <summary>Lua 侧的 <c>on_&lt;trigger&gt;</c> 函数名。</summary>
        internal static string TriggerHookName(BattleTrigger trigger) => "on_" + TriggerName(trigger);
    }
}
