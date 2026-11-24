-- 维汀（熵术士 / 造物使）的卡牌行为。
-- 钩子清单与写法约定见 miku.lua 顶部。
-- 这些行为以**卡面文字**为准；与老版本 GDScript 不符之处在注释里标出。

-- ───────────────────────── 灵智回流·常驻（持续牌） ─────────────────────────
-- 「置入持续区。每当你受到伤害时（包括自身秘宝或卡牌造成的刺痛伤害），
--   立即回复5 MP（每轮最多触发2次）；若单次受到敌方实际生命值伤害≥5点，
--   额外从弃牌堆将1张牌贴入你的充能区。」
--
-- 「每轮最多触发2次」用一个每轮清零的计数状态来记——
-- 状态层只能说「现在挂着什么」，所以次数就挂成层数。
behaviour("intellect_reflux_persistent", {
  on_round_start = function(ctx)
    remove_status(ctx.host, "reflux_used")
  end,

  on_damage_taken = function(ctx)
    if status_stacks(ctx.host, "reflux_used") >= 2 then return end

    apply_status(ctx.host, "reflux_used", -1, 1)
    restore_mp(ctx.host, 5)

    -- 「星穹坍缩」的前置条件要数这个，所以触发一次就记一次
    add_stat("reflux_triggers", ctx.host, 1)
    log(ctx.host.name .. " 的灵智回流：回复 5 MP。")

    -- 「若单次受到**敌方**实际生命值伤害≥5点」——自伤不算
    if ctx.amount >= 5 and ctx.other and ctx.other.is_enemy then
      if charge_from(ctx.host, ctx.host, "discard", 1) > 0 then
        log(ctx.host.name .. " 的灵智回流：重击追加 1 个充能。")
      end
    end
  end,
})

-- ───────────────────────── 星穹坍缩（条件型升变牌） ─────────────────────────
-- 「前置条件：本局累计触发2次「灵智回流」效果，或累计回复过15 MP。
--   ……升变期间你的所有骰子投掷结果+2；
--   每当你回复HP或MP时，对生命值最低的敌人自动造成3点暗属性穿透伤害。」
behaviour("cosmic_collapse", {
  transform_condition = function(owner)
    return stat("reflux_triggers", owner) >= 2 or stat("mp_restored", owner) >= 15
  end,

  -- 「所有骰子投掷结果+2」——不限后排、不限牌，和鹰眼绝境的条件不同
  on_crit_roll = function(roll, me)
    roll.bonus = roll.bonus + 2
  end,
})

-- ───────────────────────── 真理炼金矩阵（条件型升变牌） ─────────────────────────
-- 「轮始阶段若全队充能区合计有至少3个充能指示物且此牌在手牌中，可在第1或第2轮提前发动；
--   否则在第3轮起可从主卡组自动跳出发动，持续2轮。
--   升变期间你每轮可执行2次免费的「贴充能」动作；
--   且全队角色每当消耗充能指示物时，投掷1颗任意骰子，若点数为偶数，
--   则该充能指示物不进入弃牌堆，继续留在充能区中。」
behaviour("truth_alchemy_matrix", {
  transform_condition = function(owner)
    return team_charges() >= 3 and round() <= 2
  end,

  -- 「每轮可执行2次免费的贴充能」：常规是每轮 1 次（§5.2 / 裁定 R-08），所以额外给 1 次
  on_round_start = function(ctx)
    grant_charge_placement(ctx.host, 1)
  end,

  -- 「全队角色每当消耗充能指示物时，投掷1颗任意骰子，若点数为偶数，
  --   则该充能指示物不进入弃牌堆，继续留在充能区中。」
  --
  -- 注意这条对**全队**生效，所以引擎的 ModifyChargeSpend 是全队广播的，
  -- 只问消耗者的话这张卡对队友就完全失效了。
  on_charge_spend = function(spend, me)
    local rolled = roll(6)
    if rolled % 2 == 0 then
      spend.retained = true
      log("真理炼金矩阵：掷出 " .. rolled .. "（偶数），该充能留在充能区。")
    end
  end,
})

-- ───────────────────────── 苦痛转导 ─────────────────────────
-- 「仅限本轮你已受到过伤害（含卷轴刺痛）时打出。立即回复10 MP，
--   并使你本轮打出的下一张魔法攻击牌伤害+3。」
behaviour("pain_transduction", {
  can_play = function(ctx)
    if ctx.caster.damage_taken_this_round <= 0 then
      return false, "苦痛转导只能在本轮已受到过伤害时打出"
    end
    return true
  end,

  on_resolved = function(ctx)
    restore_mp(ctx.caster, 10)
    apply_status(ctx.caster, "next_magic_bonus", 1)
  end,
})

-- ───────────────────────── 命运锚点·七 ─────────────────────────
-- 「使你本轮下一次投掷任何骰子时，可将其中1颗骰子的结果直接视为7。」
behaviour("fate_anchor_seven", {
  on_resolved = function(ctx)
    apply_status(ctx.caster, "anchor_seven", 1)
  end,
})

-- ───────────────────────── 奥术结界 ─────────────────────────
-- 「获得7点魔法护盾，并使自身充能区上限在本轮及下一轮临时+1。」
--
-- 7 点魔法护盾走数据。上限加成不随轮次自动清零，
-- 所以这里自己记剩余轮数，到期再把它收回去。
behaviour("arcane_barrier", {
  on_resolved = function(ctx)
    adjust_charge_cap(ctx.caster, 1)
    -- 持续 2 轮：本轮 + 下一轮
    apply_status(ctx.caster, "arcane_barrier_cap", 2)
  end,
})

behaviour("status_arcane_barrier_cap", {
  -- 状态过期的那一轮轮末把上限收回去。挂在状态自己身上，
  -- 比让卡牌记「我什么时候该收回」可靠——卡早就进弃牌堆了
  on_round_end = function(ctx)
    if status_stacks(ctx.host, "arcane_barrier_cap") > 0 then return end
    adjust_charge_cap(ctx.host, -1)
  end,
})

-- ───────────────────────── 睿智法袍（时机牌） ─────────────────────────
-- 「自身或相邻队友受击时打出，获得4点物理护盾与4点魔法护盾；
--   若你的持续区有卡牌在场，额外回复5 MP。」
behaviour("sages_robe", {
  can_play = function(ctx)
    if not ctx.incoming_attack then
      return false, "睿智法袍只能在敌人宣告攻击时打出"
    end
    return true
  end,

  on_resolved = function(ctx)
    local who = ctx.current_target or ctx.caster
    shield(who, 4, "physical")
    shield(who, 4, "magical")

    -- 「**你的**持续区」——看的是打出者自己，不是被保护的那个
    if ctx.caster.continuous ~= "" then
      restore_mp(ctx.caster, 5)
    end
  end,
})

-- ───────────────────────── 贤者之石解放 ─────────────────────────
-- 「令1名我方角色立即获得+2 AP、回复10 MP并抽1张牌（每轮对同一角色限用1次）。」
behaviour("philosophers_stone_release", {
  can_play = function(ctx)
    local who = ctx.target or ctx.caster
    if has_status(who, "stone_used") then
      return false, who.name .. " 本轮已经用过贤者之石解放了"
    end
    return true
  end,

  on_resolved = function(ctx)
    local who = ctx.target or ctx.caster
    gain_ap(who, 2)
    restore_mp(who, 10)
    draw(who, 1)
    apply_status(who, "stone_used", 1)
  end,
})

-- ───────────────────────── 以太重组坩埚 ─────────────────────────
-- 「从你的弃牌堆将最多2张牌直接贴入全队任意角色的充能区，随后抽1张牌。」
--
-- 「全队任意角色」在没有逐张选目标的 UI 之前，指定了目标就全贴给他，没指定就贴给自己。
--
-- 抽牌**必须**在贴充能之后，所以写在这里而不是卡面 effects 里：
-- 数据效果跑在 on_resolved 之前，先抽牌会在牌库空时把弃牌堆洗回卡组，
-- 「从弃牌堆贴2张」就没得可贴了。卡面写的也是「随后」。
behaviour("aether_reassembly_crucible", {
  on_resolved = function(ctx)
    local who = ctx.target or ctx.caster
    local placed = charge_from(ctx.caster, who, "discard", 2)
    log("以太重组坩埚：从弃牌堆贴入 " .. placed .. " 个充能。")
    draw(ctx.caster, 1)
  end,
})

-- ───────────────────────── 应急催化剂（时机牌） ─────────────────────────
-- 「在任意队友行动时或敌人阶段打出。将你手牌中的1张牌直接贴入该队友的充能区
--   （不占用每轮1次的贴充能限额），并令该队友获得4点全能护盾，
--   或使其本轮下一次暴击掷骰结果+1。」
--
-- 二选一由界面收集，键名 "mode"：0=全能护盾（默认） 1=暴击掷骰+1。
-- 界面还没做时取默认值——护盾是两者里更无条件有用的那个。
behaviour("emergency_catalyst", {
  on_resolved = function(ctx)
    local who = ctx.target or ctx.caster
    charge_from(ctx.caster, who, "hand", 1)

    if ctx.choice("mode", 0) == 1 then
      apply_status(who, "crit_roll_bonus", 1)
    else
      shield(who, 4, "physical")
      shield(who, 4, "magical")
    end
  end,
})

-- ───────────────────────── 暗言术·活力汲取 ─────────────────────────
-- 「对单个目标造成6点暗属性魔法伤害【双色暴击】，
--   你自己或1名队友回复等同于**本次实际伤害**一半（向上取整）的HP。」
--
-- 「实际伤害」是战果不是卡面值：护盾、克制、暴击都会让两者差很远，
-- 照卡面的 6 去算一半是错的。ctx.damage_dealt 是数据层那条 Damage 真打掉的 HP。
--
-- 「你自己或1名队友」——卡面的单体目标已经被敌人占掉了（这是张攻击牌），
-- 没有第二个指定目标，所以回在施放者自己身上。
behaviour("dark_word_vitality_drain", {
  on_resolved = function(ctx)
    local dealt = ctx.damage_dealt
    if dealt <= 0 then return end

    local amount = math.ceil(dealt / 2)
    heal(ctx.caster, amount)
    log(string.format("活力汲取：实际造成 %d 点，回复 %d HP。", dealt, amount))
  end,
})

-- ───────────────────────── 深渊虹吸·活力汲取贰式 ─────────────────────────
-- 「对单个目标造成9点暗属性魔法伤害【双色暴击】。你立即回复6 HP；
--   若你生命值已满，**溢出的治疗量**自动转化为你或相邻队友的魔法护盾。
--   若触发【暴击】，额外回复5 MP。」
--
-- heal 返回的是**实际**回复量，满血时回 0——6 减去它就是溢出量。
behaviour("abyssal_siphon", {
  on_resolved = function(ctx)
    local healed = heal(ctx.caster, 6)
    local overflow = 6 - healed

    if overflow > 0 then
      shield(ctx.caster, overflow, "magical")
      log(string.format("深渊虹吸：生命已满，%d 点治疗转为魔法护盾。", overflow))
    end

    if ctx.crit then
      restore_mp(ctx.caster, 5)
      log("深渊虹吸暴击：额外回复 5 MP。")
    end
  end,
})

-- ───────────────────────── 弱点解析 ─────────────────────────
-- 「造成4点魔法伤害【暴击】（纯蓝，投洞察骰），揭示目标1项属性弱点并抽1张牌。」
--
-- 「揭示」是本局持续的信息，不按轮过期，所以持续时间写 -1。
-- 「秘法冲击」要读这个标记来判断加不加伤。
behaviour("weakness_analysis", {
  on_resolved = function(ctx)
    local t = ctx.target
    if t then
      apply_status(t, "weakness_revealed", -1)

      local weakness = weakness_of(t)
      log(t.name .. (weakness and ("的属性弱点是：" .. weakness) or " 没有属性弱点。"))
    end

    draw(ctx.caster, 1)
  end,
})

-- ───────────────────────── 秘法冲击 ─────────────────────────
-- 「造成6点魔法伤害【暴击】；若目标已被揭示属性弱点或处于负面状态，额外造成+3点魔法伤害。」
behaviour("arcane_bolt", {
  on_attack = function(atk, me)
    local t = atk.target
    if not t then return end

    if has_status(t, "weakness_revealed") or t.has_negative_status then
      atk.flat_bonus = atk.flat_bonus + 3
    end
  end,
})

-- ───────────────────────── 痛觉铭刻 ─────────────────────────
-- 「立即对自身造成2点**无视护盾**的刺痛伤害；若【魔力激发卷轴】处于关闭状态，
--   则同时将其免费切换为【激活】状态。回复10 MP并抽1张牌。」
--
-- 自伤走 pierce 而不是数据层的 Damage：卡面写明无视护盾，
-- 而数据层那条 Damage 会被自动双盾整个吃掉，等于这张牌对自己毫发无伤——
-- 连带「灵智回流」（每当你受到伤害时回 5 MP）也跟着哑火，而这正是这张牌的用法。
--
-- 来源传 nil：刺痛是自伤，算进自己的输出统计会污染「本轮造成的伤害」。
--
-- TODO: 【魔力激发卷轴】这张秘宝还不在卡池里，所以中间那半句暂时无处落脚（见 known_issues）。
behaviour("pain_inscription", {
  on_resolved = function(ctx)
    pierce(nil, ctx.caster, 2, "dark")
    restore_mp(ctx.caster, 10)
    draw(ctx.caster, 1)
  end,
})

-- ───────────────────────── 熵之轮盘·无常命运 ─────────────────────────
-- 「投掷1颗洞察骰与1颗意志骰并取较高值。
--   1—3：对敌方前排全体造成6点暗属性伤害，并对自身造成2点刺痛伤害。
--   4—6：对单个目标造成9点火或冰属性伤害【双色暴击】，并使其下回合暴击抵抗-1。
--   7+：对敌方全体造成10点雷或风属性伤害【双色暴击】，
--        并为全队每名角色各贴1个充能指示物（从各自牌库顶盲贴）。」
--
-- 三段分支是数据层表达不了的典型：同一张牌按骰点走完全不同的目标与属性。
behaviour("entropy_roulette", {
  on_resolved = function(ctx)
    local a = roll_attribute(ctx.caster, "insight")
    local b = roll_attribute(ctx.caster, "willpower")
    local high = math.max(a, b)
    log(string.format("熵之轮盘：洞察 %d / 意志 %d，取 %d。", a, b, high))

    if high <= 3 then
      for _, e in ipairs(enemies()) do
        if e.row == "front" then damage(ctx.caster, e, 6, "dark") end
      end
      -- 「刺痛伤害」是自伤，来源传 nil 免得算进自己的输出统计
      damage(nil, ctx.caster, 2, "dark")

    elseif high <= 6 then
      local t = ctx.target
      if t then
        damage(ctx.caster, t, 9, "fire", true)
        apply_status(t, "exposed", 1)
      end

    else
      for _, e in ipairs(enemies()) do
        damage(ctx.caster, e, 10, "lightning", true)
      end
      for _, p in ipairs(allies()) do
        charge_from(p, p, "deck", 1)
      end
      log("天命大吉：全队从各自牌库顶盲贴 1 个充能。")
    end
  end,
})

-- ───────────────────────── 调合·万能灵药 ─────────────────────────
-- 「打出时需消耗1个充能指示物，从以下两项炼金效果中二选一；
--   若消耗2个充能指示物则两项同时生效。
--   ①生命灵药：令1名我方角色回复10 HP与10 MP。
--   ②元素涂油：令1名我方角色本轮所有攻击附加指定元素属性，且本轮造成的伤害+3。」
--
-- 界面收集两个选择：
--   "mode"    0=生命灵药（默认） 1=元素涂油        —— 只消耗 1 个充能时二选一
--   "element" 1=火 2=冰 3=雷(默认) 4=风 5=毒       —— 涂油的属性
-- 消耗 2 个充能时两项同时生效，不再看 mode。
-- 元素存在状态层数里（见 statuses.lua 的 status_oil）。
behaviour("universal_elixir", {
  -- 看的是「这次**打算**消耗几个」而不是「充能区里有几个」：
  -- 充能区满着但玩家选了 0 个，这张牌照样不该打得出去。
  can_play = function(ctx)
    if ctx.charge_spent < 1 then
      return false, "调合·万能灵药至少要消耗 1 个充能"
    end
    return true
  end,

  on_resolved = function(ctx)
    local who = ctx.target or ctx.caster
    local both = ctx.charge_spent >= 2
    local mode = ctx.choice("mode", 0)

    if both or mode == 0 then
      heal(who, 10)
      restore_mp(who, 10)
    end

    if both or mode == 1 then
      apply_status(who, "oil", 1, ctx.choice("element", 3))
    end
  end,
})
