-- 米阔阔（浪客 / 神射手）的卡牌行为。
--
-- 写法约定：
--   behaviour("<卡的 behaviourId>", { <钩子名> = function(...) end, ... })
--   写了哪个钩子就有哪个钩子，没写的就是没挂。
--
-- 可用的钩子：
--   can_play(ctx) -> bool[, 理由]   额外前置条件（数据效果之前，费用未付）
--   on_play(ctx)                    数据效果之前
--   on_resolved(ctx)                数据效果之后
--   transform_condition(owner)      条件型升变牌的前置条件
--   on_attack(atk, me)              我方出手时过手这次攻击
--   on_defend(atk, me)              我方挨打时过手这次攻击
--   on_crit_roll(roll, me)          骰子已投、暴击未判
--   on_damage_taken / on_damage_dealt / on_crit_dealt / on_card_played /
--   on_charge_spent / on_round_start / on_round_end
--
-- 注意：这些行为以**卡面文字**为准。老版本 GDScript 与卡面不符之处在注释里标出。

-- ───────────────────────── 暗算起手 ─────────────────────────
-- 「对单个目标造成5点物理伤害【暴击】。命中后令目标获得【破绽】标记：
--   本轮内下一次受到的任意伤害+3。」
--
-- 5 点伤害是数据驱动的（卡面 effects 里那条 Damage），这里只管「命中后」那半句。
behaviour("ambush_opening", {
  on_resolved = function(ctx)
    local t = ctx.target
    if t and not t.is_down then
      apply_status(t, "exposed", 1)
    end
  end,
})

-- ───────────────────────── 卑劣打击·壹式 ─────────────────────────
-- 「造成6点物理伤害【暴击】；若目标处于负面状态、带有【破绽】标记或本轮已受到过伤害，
--   基础伤害提升至9点。」
--
-- 基础 6 点在数据里，这里只加那 3 点差额。
-- 注意【破绽】自己还会再给 +3（那是它作为状态的效果），两者叠加是卡面本来的意思：
-- 一个改的是「基础伤害」，一个改的是「受到的伤害」。
behaviour("cheap_shot_first_form", {
  on_attack = function(atk, me)
    local t = atk.target
    if not t then return end

    local boosted = t.has_negative_status
      or has_status(t, "exposed")
      or t.damage_taken_this_round > 0

    if boosted then
      atk.flat_bonus = atk.flat_bonus + 3
    end
  end,
})

-- ───────────────────────── 鹰眼锁定（持续牌） ─────────────────────────
-- 「置入持续区。只要你处于后排，你的远程攻击无视目标2点物理护盾，
--   且所有【暴击】掷骰结果+1。」
--
-- 老版本 GDScript 在无视护盾上多加了一个「伤害属性必须是物理」的条件，卡面没有这一条；
-- 这里按卡面来：只要是远程攻击就无视，不限属性。
behaviour("hawkeye_lock", {
  on_attack = function(atk, me)
    if me.row == "back" and atk.card_ranged then
      atk.ignore_shield = atk.ignore_shield + 2
    end
  end,

  -- 「所有【暴击】掷骰结果+1」——卡面没限定远程，所以不看 card_ranged
  on_crit_roll = function(roll, me)
    if me.row == "back" then
      roll.bonus = roll.bonus + 1
    end
  end,
})

-- ───────────────────────── 先声夺人 ─────────────────────────
-- 「在前后排之间移动1次并抽1张牌。若在战斗的第1轮打出此牌，
--   免除5 MP消耗，且本轮额外获得+1 AP。」
--
-- 「移动1次并抽1张牌」是**强制**的（不是「可」），纯数据就能表达，
-- 已经写在卡面 effects 里（MoveRow + Draw）。这里只管第1轮的那两条特例。
--
-- 老版本 GDScript 用的是 `move_hero(hero_id, true)`（定向到后排），
-- 卡面写的是「在前后排之间移动」，所以数据里用的是切换而非定向。
behaviour("seize_initiative", {
  -- 费用钩子必须无副作用：它在校验期会被反复调用，而且这张牌未必真打得出去
  on_cost = function(cost, me)
    if cost.round == 1 then
      cost.mp = 0
    end
  end,

  on_resolved = function(ctx)
    if ctx.round == 1 then
      gain_ap(ctx.caster, 1)
      log(ctx.caster.name .. " 先声夺人首轮：免除 5 MP 并额外获得 1 AP。")
    end
  end,
})

-- ───────────────────────── 游击速射 ─────────────────────────
-- 「造成7点物理伤害【暴击】（纯绿，投敏捷骰）；打出前或结算后**可**免费在前后排之间移动1次。」
--
-- 老版本 GDScript 写的是 `if h.row == "front": move_hero(hero_id, true)`——
-- 三处与卡面不符：强制而非「可」、只许前→后而非「前后排之间」、没有「打出前」这个时点。
-- 这里按卡面给一次**可自由兑现**的免费移动，动不动、什么时候动由玩家定。
-- （「打出前」那半句在当前动作模型里等价于：这张牌打出之前玩家本来就能走位。）
behaviour("skirmish_shot", {
  on_resolved = function(ctx)
    grant_free_move(ctx.caster, 1)
  end,
})

-- ───────────────────────── 翻滚披风（时机牌） ─────────────────────────
-- 「自身或相邻队友受击时打出，使目标获得4点物理护盾与4点魔法护盾，
--   并**可**免费在前后排之间移动1次。」
--
-- 注意护盾与移动权都给**目标**（被保护的那个），不是打出者。
behaviour("rolling_cloak", {
  can_play = function(ctx)
    if not ctx.incoming_attack then
      return false, "翻滚披风只能在敌人宣告攻击时打出"
    end
    return true
  end,

  on_resolved = function(ctx)
    local who = ctx.current_target or ctx.caster
    shield(who, 4, "physical")
    shield(who, 4, "magical")
    grant_free_move(who, 1)
  end,
})

-- ───────────────────────── 穿云狙击 ─────────────────────────
-- 「**仅限在后排打出**。……本次攻击无视敌方自动物理护盾」
--
-- 「仅限后排」是卡面的硬前置条件，现在由 CardDefinition.RequiresBackRow 在校验期挡掉，
-- 不需要脚本再判一次。这里只处理「无视自动物理护盾」。
behaviour("cloud_piercing_snipe", {
  on_attack = function(atk, me)
    local t = atk.target
    if t then
      atk.ignore_shield = atk.ignore_shield + t.auto_shield_physical
    end
  end,
})

-- ───────────────────────── 战术隐蔽 ─────────────────────────
-- 「获得7点物理护盾；若打出时自身处于后排，本轮敌方的常规单体攻击意图无法将你选为目标。」
--
-- 7 点护盾走数据（卡面 effects）。隐蔽只在**打出时**就在后排才给，
-- 之后再挪回后排不补发——卡面写的是「打出时」。
behaviour("tactical_concealment", {
  on_resolved = function(ctx)
    if ctx.caster.row == "back" then
      apply_status(ctx.caster, "hidden", 1)
      log(ctx.caster.name .. " 进入隐蔽：本轮常规单体意图无法选中他。")
    end
  end,
})

-- ───────────────────────── 顺风充能 ─────────────────────────
-- 「抽1张牌，并将此牌直接置入自己或任意队友的充能区，
--   不占用每轮1次的贴充能动作限额。」
--
-- 「此牌」指的是**这张顺风充能自己**，不是刚抽的那张——
-- 老引擎也是这么做的（attach_card 传的是 card_id），而且这解释了为什么它不进弃牌堆。
-- 抽牌走数据，这里只管把自己贴进充能区。
behaviour("tailwind_charge", {
  on_resolved = function(ctx)
    -- 目标没指定就贴给自己。charge_this 会顺手告诉引擎别再把这张牌送进弃牌堆
    charge_this(ctx.target or ctx.caster)
  end,
})

-- ───────────────────────── 翠风箭附 ─────────────────────────
-- 「本轮你造成的所有物理伤害转化为风属性伤害（可触发敌方风属性弱点×2增伤），
--   且你本轮下一次攻击造成的伤害+3。」
behaviour("verdant_wind_arrow", {
  on_resolved = function(ctx)
    apply_status(ctx.caster, "wind_conversion", 1)
    apply_status(ctx.caster, "next_attack_bonus", 1)
  end,
})

-- ───────────────────────── 盗贼戏法 ─────────────────────────
-- 「从弃牌堆选择1张牌直接置入自己或队友的充能区，
--   并使该角色本轮下一次攻击伤害+3。」
--
-- 「选择1张」目前取弃牌堆顶——让玩家挑那一步 UI 还没有，
-- 取顶部至少是确定且可复盘的。
behaviour("thieves_trick", {
  on_resolved = function(ctx)
    local who = ctx.target or ctx.caster
    if charge_from(ctx.caster, who, "discard", 1) > 0 then
      apply_status(who, "next_attack_bonus", 1)
    else
      log("盗贼戏法：弃牌堆是空的，没有可贴的牌。")
    end
  end,
})

-- ───────────────────────── 看破闪避（时机牌） ─────────────────────────
-- 「在敌人阶段自身或相邻队友被宣告为单体攻击目标时打出。投掷1颗敏捷骰，
--   若点数≥敌方暴击抵抗，完全闪避本次攻击并抽1张牌；若未达标，仍为目标抵消6点伤害。」
--
-- 老版本 GDScript 这里把攻击者写死成「shield_quake 就是 arm，否则是 boss」，
-- 换一场战斗就错。卡面说的是「敌方」，所以按本次攻击的实际来源取抵抗线。
behaviour("read_dodge", {
  can_play = function(ctx)
    local atk = ctx.incoming_attack
    if not atk then
      return false, "看破闪避只能在敌人宣告攻击时打出"
    end
    if atk.base_amount <= 0 then
      return false, "这条意图不造成伤害，没有可闪避的攻击"
    end
    return true
  end,

  on_resolved = function(ctx)
    local atk = ctx.incoming_attack
    if not atk then return end

    local line = atk.source.crit_resist
    local rolled = roll_attribute(ctx.caster, "dexterity")

    if rolled >= line then
      atk.avoided = true
      draw(ctx.caster, 1)
      log(string.format("看破闪避掷出 %d ≥ %d：完全闪避并抽 1 张牌。", rolled, line))
    else
      atk.reduction = atk.reduction + 6
      log(string.format("看破闪避掷出 %d，未达 %d：仍抵消 6 点伤害。", rolled, line))
    end
  end,
})

-- ───────────────────────── 交叉火力网 ─────────────────────────
-- 「对单个目标造成8点物理伤害【暴击】；若本轮已有其他队友对该目标造成过伤害，
--   此牌消耗减少1 AP（变为0 AP）且伤害提升至12点。」
--
-- 两半句的条件**口径不同**，这是被钩子的时点逼出来的：
-- 费用要在校验期算出来给 UI 显示，而那时玩家还没选目标（ResolveCost 只拿得到施放者与卡），
-- 所以减免只能问「本轮有没有队友打过人」；伤害那半句在攻击结算时才跑，
-- 那时目标已经定了，才能精确到「**这个**目标」。
-- 结果是：有队友出过手但打的是别人时，这张牌会便宜 1 AP 却不加伤。已记在 known_issues。
behaviour("crossfire_network", {
  on_cost = function(cost, me)
    for _, p in ipairs(allies()) do
      if p.id ~= me.id and p.damage_dealt_this_round > 0 then
        cost.ap = cost.ap - 1
        return
      end
    end
  end,

  on_attack = function(atk, me)
    local t = atk.target
    if not t then return end

    for _, p in ipairs(allies()) do
      if p.id ~= me.id and damaged_this_round_by(t, p) then
        atk.flat_bonus = atk.flat_bonus + 4   -- 8 → 12
        return
      end
    end
  end,
})

-- ───────────────────────── 连珠箭雨 ─────────────────────────
-- 「对单个目标造成7点物理伤害【暴击】；若触发【暴击】，对敌方**另一排**的1名随机目标
--   追加4点物理伤害。若全场仅1名敌人，则主目标额外受到+2点伤害。」
--
-- 随机走引擎的 roll()，不能自带随机源，否则同一个种子复盘不出同一局。
behaviour("rapid_arrow_rain", {
  on_attack = function(atk, me)
    if #enemies() == 1 then
      atk.flat_bonus = atk.flat_bonus + 2
    end
  end,

  on_resolved = function(ctx)
    if not ctx.crit then return end

    local t = ctx.target
    if not t then return end

    -- 「另一排」：和主目标不同排的敌人
    local others = {}
    for _, e in ipairs(enemies()) do
      if e.row ~= t.row then others[#others + 1] = e end
    end

    if #others == 0 then return end

    local pick = others[roll(#others)]
    damage(ctx.caster, pick, 4, "physical")
    log("连珠箭雨暴击：追加打击另一排的 " .. pick.name .. "，4 点物理伤害。")
  end,
})

-- ───────────────────────── 幻影夜行（条件型升变牌） ─────────────────────────
-- 「前置条件：本局累计触发2次暴击，或全队累计消耗2个充能指示物。
--   ……升变期间你的暴击伤害倍率提升至×1.75；每当你的攻击触发暴击时，回复5 MP。」
behaviour("phantom_nightwalk", {
  transform_condition = function(owner)
    return stat("crits_dealt", owner) >= 2 or stat("charges_spent") >= 2
  end,

  -- 卡面写的是「**暴击**伤害倍率提升至×1.75」，说的不是超暴击。
  -- 超暴击默认就是 ×2，无条件改成 ×1.75 等于把它削弱了——所以必须排除掉。
  -- ×1.75 用分数写，避免浮点在不同平台上取整不一致。
  on_attack = function(atk, me)
    if atk.crit and not atk.super_crit then
      atk.crit_multiplier = { 7, 4 }
    end
  end,

  on_crit_dealt = function(ctx)
    restore_mp(ctx.host, 5)
    log(ctx.host.name .. " 的幻影夜行：暴击回复 5 MP。")
  end,
})

-- ───────────────────────── 鹰眼绝境（条件型升变牌） ─────────────────────────
-- 「轮始阶段若自身处于后排且此牌在手牌中，可在第1或第2轮直接发动升变；
--   若前3轮未摸到，第3轮起可从主卡组自动跳出发动，持续2轮。
--   升变期间只要你处于后排，所有【暴击】掷骰结果+2，
--   且双色牌触发【超暴击】的伤害倍率从×2提升至×2.5。」
--
-- 「第3轮保底从卡组跳出」由引擎统一处理（§6.1 / 裁定 R-13），脚本只管前两轮的条件。
behaviour("hawkeye_overdrive", {
  transform_condition = function(owner)
    return owner.row == "back" and round() <= 2
  end,

  -- 「只要你处于后排」——前排时这两条都不生效
  on_crit_roll = function(roll, me)
    if me.row == "back" then
      roll.bonus = roll.bonus + 2
    end
  end,

  -- 只改**超暴击**的倍率（×2 → ×2.5），普通暴击仍是 ×1.5
  on_attack = function(atk, me)
    if me.row == "back" and atk.super_crit then
      atk.crit_multiplier = { 5, 2 }
    end
  end,
})
