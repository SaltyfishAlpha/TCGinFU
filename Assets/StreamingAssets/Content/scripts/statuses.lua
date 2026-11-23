-- 状态自带的行为。
--
-- 状态可以挂 behaviourId（statuses.json 里写），挂着期间它的常驻钩子对宿主生效。
-- 这比「让每张施加状态的卡自己再写一遍效果」靠谱：
-- 状态是谁挂的不重要，重要的是挂着期间发生了什么。

-- 「本轮打出的下一张**魔法**攻击牌伤害+3」（苦痛转导）。
-- 不能用纯数据的 outgoingDamageBonus：那个对物理攻击也会加，而卡面限定了魔法。
behaviour("status_next_magic_bonus", {
  on_attack = function(atk, me)
    if atk.element ~= "physical" then
      atk.flat_bonus = atk.flat_bonus + 3
      remove_status(me, "next_magic_bonus")
    end
  end,
})

-- 「本轮你造成的所有**物理**伤害转化为风属性」（翠风箭附）。
-- 转化而不是追加：卡面说的是「转化为」，所以可以吃敌人的风属性弱点 ×2。
behaviour("status_wind_conversion", {
  on_attack = function(atk, me)
    if atk.element == "physical" then
      atk.element = "wind"
    end
  end,
})

-- 「下一次掷骰可将其中1颗骰子的结果直接视为7」（命运锚点·七）。
-- 改的是**骰值**不是暴击结果——双色牌的超暴击要靠两颗骰的关系判定，
-- 直接写暴击结果会把那套规则绕过去。
behaviour("status_anchor_seven", {
  on_crit_roll = function(roll, me)
    roll[1] = 7
    remove_status(me, "anchor_seven")
    log(me.name .. " 的命运锚点·七：第 1 颗骰直接视为 7。")
  end,
})

-- 「本轮下一次暴击掷骰结果+1」（应急催化剂）
behaviour("status_crit_roll_bonus", {
  on_crit_roll = function(roll, me)
    roll.bonus = roll.bonus + status_stacks(me, "crit_roll_bonus")
    remove_status(me, "crit_roll_bonus")
  end,
})

-- 「元素涂油：本轮所有攻击附加指定元素」（调合·万能灵药 ②）。
-- 伤害 +3 由状态的 outgoingDamageBonus 负责，这里只管改属性。
-- 具体是哪个元素存在状态层数里（见 universal_elixir 的注释）。
behaviour("status_oil", {
  on_attack = function(atk, me)
    local element = ({ "fire", "ice", "lightning", "wind", "poison" })[status_stacks(me, "oil")]
    if element then atk.element = element end
  end,
})
