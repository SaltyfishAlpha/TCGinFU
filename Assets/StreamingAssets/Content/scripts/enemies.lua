-- 克洛诺斯-IV型与镇压重盾臂的意图。
--
-- **敌人的意图就是它手里一套玩家看不到的牌**，所以这里用的钩子和玩家卡完全一样：
-- can_play / on_play / on_resolved / on_attack / on_round_end…
-- 没有任何 on_intent_* 之类的平行接口。
--
-- 结算顺序：
--   ① 意图宣告的那段伤害先结算（响应窗口里玩家能看到并改写它）
--   ② 然后才跑这里的 on_resolved——「吞噬充能」「前后排互换」这类后续效果
-- 所以 on_resolved 里看到的已经是打完之后的局面。
--
-- ctx.caster 是敌人本身；ctx.target 是这条意图的首个目标。

-- ───────────────────────── 重力横扫 ─────────────────────────
-- 「前排全体受到12点物理伤害并被击退至后排；前排无人时，后排受到4点震波伤害。」
behaviour("gravity_sweep", {
  on_resolved = function(ctx)
    local hit_anyone = false
    for _, p in ipairs(opponents(ctx.caster)) do
      if p.row == "front" then
        hit_anyone = true
        -- 「击退至后排」是**定向**移动：已经在后排的人不该被拉到前排
        move_to(p, "back")
      end
    end

    -- 「前排无人时，后排受到4点震波伤害」
    if not hit_anyone then
      for _, p in ipairs(opponents(ctx.caster)) do
        damage(ctx.caster, p, 4, "physical")
      end
      log("重力横扫：前排无人，后排承受 4 点震波。")
    end
  end,
})

-- ───────────────────────── 次元穿刺炮 ─────────────────────────
-- 「对锁定的后排角色造成20点物理穿透伤害；若其已移动至前排，则仅受8点擦伤。」
--
-- 「已移动至前排」是在响应窗口里躲的——所以必须在伤害结算前改，用 on_attack。
behaviour("dimension_cannon", {
  on_attack = function(atk, me)
    local t = atk.target
    if not t then return end

    if t.row == "front" then
      atk.base_amount = 8
    else
      -- 「穿透」：无视护盾。给一个足够大的数而不是精确值——
      -- 护盾本身不会因为被无视而消耗，多出来的部分不产生副作用
      atk.ignore_shield = atk.ignore_shield + 999
    end
  end,
})

-- ───────────────────────── 以太虹吸 ─────────────────────────
-- 「全体受到6点暗属性魔法伤害，各吞噬1个充能；每吞噬1个，首领回复8HP。消耗10MP。」
--
-- MP 费用由引擎按卡面的 mp 字段扣，这里不用管。
behaviour("aether_siphon", {
  on_resolved = function(ctx)
    local total = 0
    for _, p in ipairs(opponents(ctx.caster)) do
      total = total + devour_charges(p, 1)
    end

    if total > 0 then
      heal(ctx.caster, total * 8)
      log(string.format("以太虹吸：吞噬 %d 个充能，首领回复 %d HP。", total, total * 8))
    end
  end,
})

-- ───────────────────────── 发条连射 ─────────────────────────
-- 「对单个目标连续射击2次，每次造成8点物理伤害。」
--
-- 第 1 发是意图宣告的那段（响应窗口能改），第 2 发在这里补。
behaviour("clockwork_volley", {
  on_resolved = function(ctx)
    if ctx.target and not ctx.target.is_down then
      damage(ctx.caster, ctx.target, 8, "physical")
    end
  end,
})

-- ───────────────────────── 相位折跃 ─────────────────────────
-- 「全体受到7点雷属性魔法伤害，随后前后排互换，命刻额外+1。消耗10MP。」
behaviour("phase_shift", {
  on_resolved = function(ctx)
    -- 整体互换而不是逐个切换：逐个切会让先动的人占掉对排槽位，
    -- 后动的人被挤到别的列，§4.3 的「正前方」相邻关系就乱了
    swap_rows(true)
    advance_clock(1)
    log("相位折跃：玩家前后排互换，命刻 +1。")
  end,
})

-- ───────────────────────── 装甲重构 ─────────────────────────
-- 「获得14点物理与14点魔法护盾；轮末若两项都未被击破，首领所有伤害永久+3。」
behaviour("armor_reconstruction", {
  on_resolved = function(ctx)
    shield(ctx.caster, 14, "physical")
    shield(ctx.caster, 14, "magical")
    -- 轮末那次检查挂在状态上，由 status_armor_watch 负责——
    -- 卡本身这时候已经结算完了，没有「等到轮末」的立场
    apply_status(ctx.caster, "armor_watch", 1)
  end,
})

-- ───────────────────────── 时空断头台 ─────────────────────────
-- 「前排受到15点物理伤害，后排受到15点魔法伤害；每人只承受本排一段。」
behaviour("time_guillotine", {
  on_attack = function(atk, me)
    local t = atk.target
    if t and t.row == "back" then
      -- 后排那一段是魔法伤害，吃的是魔法护盾
      atk.element = "dark"
    end
  end,
})

-- ───────────────────────── 命运乱流 ─────────────────────────
-- 「全体受到8点暗属性魔法伤害；每剩余1张手牌，额外受到2点无视护盾刺痛。」
behaviour("fate_turbulence", {
  on_resolved = function(ctx)
    for _, p in ipairs(opponents(ctx.caster)) do
      local extra = p.hand_count * 2
      if extra > 0 then
        pierce(ctx.caster, p, extra, "dark")
        log(string.format("命运乱流：%s 手上 %d 张牌，额外承受 %d 点刺痛。",
          p.name, p.hand_count, extra))
      end
    end
  end,
})

-- ───────────────────────── 因果抹杀令 ─────────────────────────
-- 「当前HP最高者受到24点物理伤害；可在响应时由相邻队友分摊，两人各12点并分别抵扣护盾。」
--
-- 整条都是纯数据，这里**不需要**行为：
--   前半句（索敌「当前HP最高者」+ 24 点）＝ targeting = HighestHpSingle；
--   后半句「由相邻队友分摊」＝ 意图上的开关 shareable = true。
--
-- 为什么分摊不写成行为钩子：它要玩家在**响应窗口里**做一次选择，而响应窗口开在
-- 所有行为钩子之前——等脚本被问到时窗口已经关了。所以引擎把它做成了一个动作
-- （`ShareDamageAction`；Lua 侧是 `share_damage(站出来的队友, 原目标)`），由界面提交。
-- 与援护（`RegisterCover`）的区别：援护把伤害**整份**转移，分摊是对半拆成两份各自抵扣护盾。

-- ───────────────────────── 归零坍缩 ─────────────────────────
-- 「全员受到28点全能穿透伤害。释放后下轮过热：暴击抵抗4，受到最终伤害×1.5。」
behaviour("zero_collapse", {
  on_attack = function(atk, me)
    -- 「穿透」：无视护盾
    atk.ignore_shield = atk.ignore_shield + 999
  end,

  on_resolved = function(ctx)
    -- 过热挂在首领自己身上，持续到下一轮轮末
    apply_status(ctx.caster, "overheat", 2)
    log("归零坍缩：首领进入过热，下轮更易被暴击且受伤加重。")
  end,
})

-- ───────────────────────── 状态：装甲重构的轮末检查 ─────────────────────────
behaviour("status_armor_watch", {
  on_round_end = function(ctx)
    local me = ctx.host
    -- 「两项都未被击破」= 轮末时两种护盾都还有剩
    if me.shield_physical > 0 and me.shield_magical > 0 then
      apply_status(me, "armor_overcharge", -1)   -- -1 = 本局永久
      log(me.name .. " 的装甲重构未被击破：所有伤害永久 +3。")
    end
    remove_status(me, "armor_watch")
  end,
})
