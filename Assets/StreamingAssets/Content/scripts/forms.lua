-- 形态额外卡组（升变期间的牌）的行为。
-- 钩子清单与写法约定见 miku.lua 顶部。
--
-- 这一整个文件原本是不存在的：forms.json 里 13 张牌都写了 behaviourId，
-- 却没有任何一处 behaviour() 与之对应。引擎把「声明了但没注册」当成纯数据卡放过，
-- 于是这些卡的卡面文字只兑现了「造成 N 点伤害」那半句，剩下半句静默失效。
--
-- 形态牌的数值基准来自角色面板：ctx.caster.form_attack / form_defense（即卡面的 B_atk / B_def）。

-- ───────────────────────── 瞬影突袭 ─────────────────────────
-- 「造成B_atk点物理伤害【暴击】；若本轮已打出过其他牌，返还1 AP（每轮限返还1次）。」
--
-- 「已打出过**其他**牌」必须读「这张牌之前」的计数：角色身上的 cards_played_this_round
-- 在效果结算时已经把这张牌自己算进去了，用它会让这张牌第一个打出也返还 AP。
behaviour("shadow_dash_assault", {
  on_resolved = function(ctx)
    if ctx.cards_played_before <= 0 then return end
    if has_status(ctx.caster, "dash_refunded") then return end

    gain_ap(ctx.caster, 1)
    apply_status(ctx.caster, "dash_refunded", 1)
    log(ctx.caster.name .. " 瞬影突袭：本轮已出过牌，返还 1 AP。")
  end,
})

-- ───────────────────────── 暗夜剔骨 ─────────────────────────
-- 「造成B_atk+4点物理伤害【暴击】；若消耗1个充能指示物，本次攻击必定暴击且抽1张牌。」
--
-- 「必定暴击」只能在**判定之前**生效，所以走掷骰钩子而不是事后改结果——
-- 事后把布尔值掰成 true 会让双色牌的超暴击规则失效（§7.3）。
behaviour("night_eviscerate", {
  on_crit_roll = function(roll, me)
    if roll.charge_spent >= 1 then
      roll.force_crit = true
    end
  end,

  on_resolved = function(ctx)
    if ctx.charge_spent >= 1 then
      draw(ctx.caster, 1)
      log(ctx.caster.name .. " 暗夜剔骨：消耗充能，必定暴击并抽 1 张牌。")
    end
  end,
})

-- ───────────────────────── 烟幕替身（时机牌） ─────────────────────────
-- 「自身或相邻队友受击时打出，获得B_def+2点物理护盾，
--   并使攻击者本轮后续造成的伤害降低3点。」
--
-- 护盾走数据（卡面 effects）。减伤挂在**攻击者**身上，不是被保护的那个。
behaviour("smoke_decoy", {
  can_play = function(ctx)
    if not ctx.incoming_attack then
      return false, "烟幕替身只能在敌人宣告攻击时打出"
    end
    return true
  end,

  on_resolved = function(ctx)
    local atk = ctx.incoming_attack
    if not atk or not atk.source then return end

    apply_status(atk.source, "suppressed", 1)
    log(atk.source.name .. " 被烟幕压制：本轮后续伤害 -3。")
  end,
})

-- ───────────────────────── 追风连矢 ─────────────────────────
-- 「造成B_atk+2点物理伤害【暴击】；命中后若目标未死亡，
--   **可**免费将1张手牌贴入充能区，不占每轮充能限额。」
--
-- 卡面写的是「可」，所以给的是一次**额外的贴充能机会**，由玩家自己决定贴哪张、贴不贴；
-- 直接 charge_from 会替玩家把手牌末尾那张贴掉，那是强制而不是「可」。
behaviour("wind_chasing_arrows", {
  on_resolved = function(ctx)
    local t = ctx.target
    if not t or t.is_down then return end

    grant_charge_placement(ctx.caster, 1)
    log(ctx.caster.name .. " 追风连矢：本轮多一次免费的贴充能机会。")
  end,
})

-- ───────────────────────── 死神裁决 ─────────────────────────
-- 「造成B_atk×2点物理伤害【双色暴击】（5级构筑16点）；
--   后排触发×2.5超暴击时造成B_atk×5点毁灭性伤害（5级构筑40点）。」
--
-- 16 × 2.5 = 40 = B_atk×5，所以这条词条实质是**改写超暴击倍率**，不是另算一段伤害。
-- 写成 16→40 的特判会在换构筑（B_atk 不是 8）时对不上卡面。
behaviour("death_sentence", {
  on_attack = function(atk, me)
    if me.row == "back" and atk.super_crit then
      atk.crit_multiplier = { 5, 2 }
    end
  end,
})

-- ───────────────────────── 箭雨覆盖 ─────────────────────────
-- 「对敌方全体造成B_atk点物理伤害【双色暴击】，并削减所有敌人3点物理护盾。」
--
-- 「削减护盾」不是伤害：它不走伤害管线、不触发受击，也不该被克制与暴击放大。
behaviour("arrow_storm_coverage", {
  on_resolved = function(ctx)
    for _, e in ipairs(enemies()) do
      strip_shield(e, 3, "physical")
    end
  end,
})

-- ───────────────────────── 风墙反制（时机牌） ─────────────────────────
-- 「敌人阶段自身或相邻队友受击时打出，获得B_def点物理护盾与B_def点魔法护盾；
--   **若完全抵消本次攻击伤害**，立即对攻击者反击造成B_atk点物理伤害。」
--
-- 护盾走数据。反击的判定要等这次攻击真的结算完才知道「有没有完全抵消」，
-- 而那时这张牌早就结算完进弃牌堆了——所以挂一个一次性状态去等那个时点。
behaviour("wind_wall_counter", {
  can_play = function(ctx)
    if not ctx.incoming_attack then
      return false, "风墙反制只能在敌人宣告攻击时打出"
    end
    return true
  end,

  on_resolved = function(ctx)
    apply_status(ctx.current_target or ctx.caster, "wind_wall", 1)
  end,
})

-- 风墙的那一刻：攻击结算完毕、掉 0 血且确实被盾吃下过。
-- 挂在 on_attack_resolved 而不是 on_damage_taken——后者掉 0 血根本不广播，
-- 而「完全抵消」恰恰只在掉 0 血时成立。
behaviour("status_wind_wall", {
  on_attack_resolved = function(ctx)
    remove_status(ctx.host, "wind_wall")

    if not ctx.fully_absorbed then return end
    if not ctx.other or ctx.other.is_down then return end

    damage(ctx.host, ctx.other, ctx.host.form_attack, "physical")
    log(ctx.host.name .. " 的风墙完全挡下攻击，反击 " .. ctx.other.name .. "。")
  end,
})

-- ───────────────────────── 视界奇点 ─────────────────────────
-- 「造成B_atk+4点暗属性魔法伤害【双色暴击】，无视目标魔法护盾。」
behaviour("event_horizon_singularity", {
  on_attack = function(atk, me)
    local t = atk.target
    if t then
      atk.ignore_shield = atk.ignore_shield + t.shield_magical
    end
  end,
})

-- ───────────────────────── 无常大轮盘 ─────────────────────────
-- 「对敌方全体造成B_atk+2点魔法伤害【双色暴击】，
--   该伤害**自动适应并触发每名敌人的易损弱点属性**。」
--
-- 伤害与公式留在数据层（卡面那条 AllEnemies 的 B_atk+2），这里只改**属性**：
-- AoE 的每个目标各走一次攻击管线，所以 on_attack 会被逐个问到，
-- 每次都能按当前这名敌人挑属性。没有弱点的退回卡面默认的暗属性。
--
-- 不把整段伤害搬进脚本：形态牌的数值公式是有测试钉住的（T05 要算回老数据的 9 点），
-- 搬走等于把公式从数据层偷走，换构筑时就对不上卡面了。
behaviour("grand_chaos_roulette", {
  on_attack = function(atk, me)
    local t = atk.target
    if not t then return end

    atk.element = weakness_of(t) or atk.element
  end,
})

-- ───────────────────────── 时间悖论（时机牌） ─────────────────────────
-- 「自身或相邻队友受击时打出，获得B_def+2点物理护盾与B_def+2点魔法护盾，
--   并令敌方本次攻击的暴击判定**强制改为失败**。」
--
-- 护盾走数据。暴击只能被抹掉、不能被凭空造出来（见 LuaProxies 的 crit 写入分支）。
behaviour("time_paradox", {
  can_play = function(ctx)
    if not ctx.incoming_attack then
      return false, "时间悖论只能在敌人宣告攻击时打出"
    end
    return true
  end,

  on_resolved = function(ctx)
    local atk = ctx.incoming_attack
    if not atk then return end

    atk.crit = false
    log("时间悖论：本次攻击的暴击判定强制失败。")
  end,
})

-- ───────────────────────── 元素增殖炸弹 ─────────────────────────
-- 「造成B_atk+3点指定元素伤害【暴击】，并令目标在本轮内获得该元素的【易损】状态：
--   受到该属性伤害×2。」
--
-- 界面收集选择，键名 "element"：1=火 2=冰 3=雷(默认) 4=风 5=毒。
--
-- 两个时点必须分开：
--   · 属性改写要在伤害**之前**（on_play 存标记 → on_attack 改属性）；
--   · 【易损】要在伤害**之后**（on_resolved），否则本次这一下自己就先吃了 ×2，
--     而卡面写的是「造成伤害，**并令**目标获得易损」。
local VULNERABLE = { "vulnerable_fire", "vulnerable_ice", "vulnerable_lightning",
                     "vulnerable_wind", "vulnerable_poison" }
local ELEMENTS = { "fire", "ice", "lightning", "wind", "poison" }

local function bomb_element(ctx)
  local i = ctx.choice("element", 3)
  if i < 1 or i > #ELEMENTS then i = 3 end
  return i
end

behaviour("elemental_proliferation_bomb", {
  -- 选中的属性存在标记状态的层数里（与「元素涂油」同一套做法），
  -- 因为 on_attack 拿不到出牌上下文，读不到 ctx.choice
  on_play = function(ctx)
    apply_status(ctx.caster, "bomb_element", 1, bomb_element(ctx))
  end,

  on_attack = function(atk, me)
    local element = ELEMENTS[status_stacks(me, "bomb_element")]
    if element then atk.element = element end
  end,

  on_resolved = function(ctx)
    remove_status(ctx.caster, "bomb_element")

    local t = ctx.target
    if not t or t.is_down then return end

    local i = bomb_element(ctx)
    apply_status(t, VULNERABLE[i], 1)
    log(t.name .. " 获得【易损·" .. ELEMENTS[i] .. "】：本轮受到该属性伤害 ×2。")
  end,
})

-- ───────────────────────── 炼金防护力场（时机牌） ─────────────────────────
-- 「敌人阶段打出，为**前排全体或后排全体**提供B_def点全能护盾，
--   并移除每名受保护角色身上的1个负面状态。」
--
-- 界面收集选择，键名 "row"：0=前排（默认） 1=后排。
-- 这张牌的受益者是**一整排**，不是单个队友——卡面 effects 原本写的是
-- 两条 SingleAlly 护盾，等于把一张群体保护牌做成了单体牌，所以整段改走脚本。
behaviour("alchemical_protection_field", {
  on_resolved = function(ctx)
    local row = ctx.choice("row", 0) == 1 and "back" or "front"
    local amount = ctx.caster.form_defense
    local covered = 0

    for _, p in ipairs(allies()) do
      if p.row == row then
        shield(p, amount, "physical")
        shield(p, amount, "magical")
        remove_negative_status(p, 1)
        covered = covered + 1
      end
    end

    log(string.format("炼金防护力场：%s排 %d 人各获得 %d 点全能护盾并净化 1 个负面状态。",
      row == "back" and "后" or "前", covered, amount))
  end,
})
