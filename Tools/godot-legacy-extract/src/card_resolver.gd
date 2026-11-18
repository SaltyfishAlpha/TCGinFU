class_name CardResolver
extends RefCounted

func play(state, hero_id: String, hand_index: int, options: Dictionary = { }) -> Dictionary:
	var h = state.heroes[hero_id]
	var card_id = h.hand[hand_index]
	var card = CardDatabase.get_card(card_id)
	var card_name = card.get("name", "")
	if card.get("kind", "") == "ascension":
		return _cycle_ascension(state, hero_id, hand_index, card)
	var cost = state.get_card_cost(hero_id, card, options)
	var uncharged_options = options.duplicate()
	uncharged_options["use_mp_charge"] = false
	var uncharged_cost = state.get_card_cost(hero_id, card, uncharged_options)
	if options.get("use_mp_charge", false) and int(uncharged_cost.mp) > int(cost.mp):
		state.consume_charge(hero_id)
	h.ap -= int(cost.ap)
	h.mp -= int(cost.mp)
	h.hand.remove_at(hand_index)
	var target_id = options.get("target_id", "boss")
	if not state.enemies.has(target_id) or not state.enemies[target_id].alive:
		target_id = "boss" if state.enemies.boss.alive else "arm"
	var ally_id = options.get("ally_id", hero_id)
	if not state.heroes.has(ally_id): ally_id = hero_id
	var discount_applied = hero_id == "weiting" and h.scroll_active and not h.spell_discount_used and state._is_spell(card)
	if discount_applied: h.spell_discount_used = true
	var result: Dictionary = { "ok": true, "card": card }
	state._log("%s 打出【%s】（%dAP / %dMP）。" % [h.name, card_name, cost.ap, cost.mp], "card")
	if card.get("kind", "") == "ongoing":
		if h.ongoing != "": h.discard.append(h.ongoing)
		h.ongoing = card_id
		state._log("【%s】置入持续区；旧持续牌进入弃牌堆。" % card_name, "system")
	else:
		_execute_effect(state, hero_id, card, target_id, ally_id, options, result)
		if card_name == "顺风充能":
			state.attach_card(ally_id, card_id, hero_id)
			state._log("顺风充能不进入弃牌堆，直接成为 %s 的充能。" % state.heroes[ally_id].name, "charge")
		else:
			h.discard.append(card_id)
	if card_name in ["熵之轮盘·无常命运", "暗言术·活力汲取", "深渊虹吸·活力汲取贰式"]:
		h.entropy_discount = false
		h.next_entropy_crit = 0
	h.cards_played += 1
	state.changed.emit()
	return result

func _cycle_ascension(state, hero_id: String, hand_index: int, card: Dictionary) -> Dictionary:
	var h = state.heroes[hero_id]
	h.hand.remove_at(hand_index)
	h.deck.append(card.id)
	state._shuffle(h.deck)
	state._log("%s 展示【%s】并洗回主卡组，抽1张牌。" % [h.name, card.name], "crit")
	if card.name == "幻影夜行":
		h.crits += 1
		state._log("幻影夜行展示进度+1（当前%d/2）。" % mini(h.crits, 2), "system")
	elif card.name == "星穹坍缩":
		state.damage_hero(hero_id, 1, "dark", "pierce", { "true_damage": true, "self_damage": true })
	state.draw_cards(hero_id, 1)
	state.changed.emit()
	return { "ok": true, "cycled": true, "card": card }

func _execute_effect(state, hero_id: String, card: Dictionary, target_id: String, ally_id: String, options: Dictionary, result: Dictionary) -> void:
	var h = state.heroes[hero_id]
	var name = card.get("name", "")
	match name:
		"暗算起手":
			attack(state, hero_id, target_id, card, 5, options)
			if state.enemies[target_id].alive:
				state.enemies[target_id].exposed = true
				state._log("目标获得【破绽】：下一次受到任意伤害+3。", "warning")
		"卑劣打击·壹式":
			var target = state.enemies[target_id]
			var boosted = target.exposed or target.damaged_this_round or target.negative
			attack(state, hero_id, target_id, card, 9 if boosted else 6, options)
		"先声夺人":
			state.move_hero(hero_id, true)
			state.draw_cards(hero_id, 1)
			if state.round_number == 1:
				h.ap += 1
				state._log("先声夺人首轮效果：免除5MP并额外获得1AP。", "good")
		"压制连射":
			for hit in range(2):
				if state.enemies[target_id].alive:
					attack(state, hero_id, target_id, card, 5, options)
		"交叉火力网":
			var boosted = false
			var target = state.enemies[target_id]
			for other_id in state.HERO_IDS:
				if other_id != hero_id and target.damaged_by.get(other_id, false): boosted = true
			attack(state, hero_id, target_id, card, 12 if boosted else 8, options)
		"穿云狙击":
			var snipe_options = options.duplicate()
			snipe_options["ignore_auto"] = true
			attack(state, hero_id, target_id, card, 12, snipe_options)
		"游击速射":
			attack(state, hero_id, target_id, card, 7, options)
			if h.row == "front": state.move_hero(hero_id, true)
		"连珠箭雨":
			var alive_enemies = _alive_enemies(state)
			var hit = attack(state, hero_id, target_id, card, 9 if alive_enemies.size() == 1 else 7, options)
			if hit.get("crit", false) and alive_enemies.size() > 1:
				var candidates: Array = []
				for id in alive_enemies:
					if id != target_id and state.enemies[id].row != state.enemies[target_id].row: candidates.append(id)
				if candidates.is_empty():
					for id in alive_enemies:
						if id != target_id: candidates.append(id)
				if not candidates.is_empty():
					var extra = candidates[state.rng.randi_range(0, candidates.size() - 1)]
					state._log("连珠箭雨暴击：向另一排追加4点伤害。", "crit")
					var extra_element = _attack_element(h, card)
					state.damage_enemy(extra, 4, extra_element, { "damage_class": "physical" if extra_element == "physical" else "magic", "source_hero": hero_id })
		"战术隐蔽":
			h.p_shield += 7
			if h.row == "back":
				h.hidden = true
				state._log("战术隐蔽：本轮常规单体意图无法选择米阔阔。", "block")
		"顺风充能":
			state.draw_cards(hero_id, 1)
		"翠风箭附":
			h.convert_wind = true
			h.next_attack_bonus += 3
			state._log("本轮米阔阔的物理伤害转化为风属性，下一次攻击+3。", "good")
		"瞬影突袭":
			attack(state, hero_id, target_id, card, 8, options)
			if h.cards_played > 0 and not h.refund_used:
				h.refund_used = true
				h.ap += 1
				state._log("瞬影突袭：本轮已出过其他牌，返还1AP。", "good")
		"暗夜剔骨":
			var night_options = options.duplicate()
			if not h.charges.is_empty() and options.get("use_special_charge", false):
				state.consume_charge(hero_id)
				night_options["force_crit"] = true
				state.draw_cards(hero_id, 1)
				state._log("暗夜剔骨消耗1充能：必定暴击并抽1张牌。", "crit")
			attack(state, hero_id, target_id, card, 12, night_options)
		"盗贼戏法":
			if not h.discard.is_empty():
				var stolen = h.discard.pop_back()
				state.attach_card(ally_id, stolen, hero_id)
				state.heroes[ally_id].next_attack_bonus += 3
				state._log("盗贼戏法将【%s】贴给%s，其下一次攻击+3。" % [CardDatabase.get_card(stolen).get("name", stolen), state.heroes[ally_id].name], "charge")
		"追风连矢":
			attack(state, hero_id, target_id, card, 10, options)
			if state.enemies[target_id].alive and not h.hand.is_empty():
				var charge_index = h.hand.size() - 1
				var charged = h.hand[charge_index]
				h.hand.remove_at(charge_index)
				state.attach_card(hero_id, charged, hero_id)
				state._log("追风连矢命中：免费将【%s】贴入充能区。" % CardDatabase.get_card(charged).get("name", charged), "charge")
		"死神裁决":
			attack(state, hero_id, target_id, card, 16, options)
		"箭雨覆盖":
			for enemy_id in _alive_enemies(state):
				attack(state, hero_id, enemy_id, card, 8, options)
				state.enemies[enemy_id].p_shield = maxi(0, state.enemies[enemy_id].p_shield - 3)
		"苦痛转导":
			state.heal_mp(hero_id, 10)
			h.next_magic_bonus += 3
		"命运锚点·七":
			h.anchor_seven = true
			state._log("命运锚点已设置：下一次任何骰判可将1颗骰子视为7。", "arcane")
		"熵之轮盘·无常命运":
			_entropy_roulette(state, hero_id, target_id, card, options)
		"暗言术·活力汲取":
			var hit = attack(state, hero_id, target_id, card, 6, options)
			var healing = int(ceil(float(hit.get("hp_damage", 0)) / 2.0))
			state.heal_hp(ally_id, healing)
		"深渊虹吸·活力汲取贰式":
			var hit = attack(state, hero_id, target_id, card, 9, options)
			var healed = state.heal_hp(hero_id, 6)
			var overflow = 6 - healed
			if overflow > 0:
				state.heroes[ally_id].m_shield += overflow
				state._log("溢出治疗转化为%s的%d点魔法护盾。" % [state.heroes[ally_id].name, overflow], "block")
			if hit.get("crit", false): state.heal_mp(hero_id, 5)
		"调合·万能灵药":
			var charges_used = 0
			if not h.charges.is_empty():
				state.consume_charge(hero_id)
				charges_used = 1
			var mode = options.get("alchemy_mode", "both")
			if charges_used == 1 and mode == "both": mode = "life"
			if mode == "both" and not h.charges.is_empty():
				state.consume_charge(hero_id)
				charges_used = 2
			elif mode == "both": mode = "life"
			if mode in ["life", "both"]:
				state.heal_hp(ally_id, 10)
				state.heal_mp(ally_id, 10)
			if mode in ["element", "both"]:
				var target = state.heroes[ally_id]
				target.oil_element = options.get("element", "lightning")
				target.oil_bonus = 3
				state._log("元素涂油：%s 本轮攻击附加%s，伤害+3。" % [target.name, state.ELEMENT_NAMES.get(target.oil_element, target.oil_element)], "good")
		"弱点解析":
			attack(state, hero_id, target_id, card, 4, options)
			state.reveal_enemy(target_id)
			state.draw_cards(hero_id, 1)
		"秘法冲击":
			var target = state.enemies[target_id]
			attack(state, hero_id, target_id, card, 9 if target.revealed or target.negative else 6, options)
		"奥术结界":
			h.m_shield += 7
			h.charge_cap_bonus = 1
			h.charge_cap_turns = 2
			state._log("奥术结界：获得7魔法护盾，充能上限本轮及下轮+1。", "block")
		"痛觉铭刻":
			h.scroll_active = true
			state.damage_hero(hero_id, 2, "dark", "pierce", { "true_damage": true, "self_damage": true })
			state.heal_mp(hero_id, 10)
			state.draw_cards(hero_id, 1)
		"魔力过载仪式":
			h.absorb_cap_bonus += 1
			h.entropy_discount = true
			h.next_entropy_crit += 1
			state._log("过载仪式：本轮灵智回流上限+1，下张熵系魔法-5MP且暴击骰+1。", "arcane")
		"视界奇点":
			var singular_options = options.duplicate()
			singular_options["ignore_magic"] = true
			attack(state, hero_id, target_id, card, 11, singular_options)
		"无常大轮盘":
			for enemy_id in _alive_enemies(state):
				var adaptive_options = options.duplicate()
				adaptive_options["adaptive"] = true
				attack(state, hero_id, enemy_id, card, 9, adaptive_options)
		"噬魂狂宴":
			attack(state, hero_id, target_id, card, 7, options)
			for id in state.HERO_IDS:
				state.heal_hp(id, 6)
				state.heal_mp(id, 10)
		"元素增殖炸弹":
			var element = options.get("element", "wind" if target_id == "arm" else "lightning")
			var bomb_options = options.duplicate()
			bomb_options["element"] = element
			attack(state, hero_id, target_id, card, 10, bomb_options)
			if state.enemies[target_id].alive and not element in state.enemies[target_id].vulnerable_elements:
				state.enemies[target_id].vulnerable_elements.append(element)
				state._log("%s 本轮对%s产生【易损】。" % [state.enemies[target_id].name, state.ELEMENT_NAMES.get(element, element)], "warning")
		"贤者之石解放":
			h.sage_stone_used[ally_id] = true
			var target = state.heroes[ally_id]
			target.ap += 2
			state.heal_mp(ally_id, 10)
			state.draw_cards(ally_id, 1)
			state._log("贤者之石解放：%s 获得2AP、回复10MP并抽1张牌。" % target.name, "good")
		"以太重组坩埚":
			for n in range(2):
				if h.discard.is_empty(): break
				var restored = h.discard.pop_back()
				var receiver = ally_id if n == 0 else hero_id
				state.attach_card(receiver, restored, hero_id)
				state._log("以太重组：【%s】贴入%s充能区。" % [CardDatabase.get_card(restored).get("name", restored), state.heroes[receiver].name], "charge")
			state.draw_cards(hero_id, 1)
		_:
			if card.get("kind", "") != "ongoing":
				state._log("【%s】的通用效果已结算。" % name, "system")

func attack(state, hero_id: String, target_id: String, card: Dictionary, base_damage: int, options: Dictionary = { }) -> Dictionary:
	var h = state.heroes[hero_id]
	var target = state.enemies[target_id]
	if not target.alive: return { }
	var rolled = options.get("rolled", { })
	if rolled.is_empty(): rolled = _roll_dice(state, hero_id, target_id, card, options)
	var crit = false
	var super_crit = false
	var line = _crit_line(state, target_id)
	if card.get("crit", false):
		for value in rolled.get("values", []):
			if value >= line: crit = true
		if rolled.get("values", []).size() >= 2:
			var both = true
			for value in rolled.values:
				if value < line: both = false
			var raw = rolled.get("raw", [])
			super_crit = both or (raw.size() >= 2 and raw[0] == raw[1] and raw[0] >= 4)
		if options.get("force_crit", false): crit = true
	var damage = base_damage
	if target.exposed:
		damage += 3
		target.exposed = false
		state._log("触发【破绽】：本次伤害+3。", "warning")
	if h.next_attack_bonus > 0:
		damage += h.next_attack_bonus
		state._log("下一次攻击增伤+%d已消耗。" % h.next_attack_bonus, "good")
		h.next_attack_bonus = 0
	if h.next_magic_bonus > 0 and card.get("damage_type", "") not in ["physical", ""]:
		damage += h.next_magic_bonus
		h.next_magic_bonus = 0
		state._log("苦痛转导：本次魔法攻击+3。", "arcane")
	if h.oil_bonus > 0: damage += h.oil_bonus
	var amplified_damage = damage
	if crit:
		var multiplier = 1.75 if h.form == "phantom" else 1.5
		damage = int(amplified_damage * multiplier)
	if super_crit:
		var multiplier = 2.5 if h.form == "hawkeye" and h.row == "back" else 2.0
		damage = int(amplified_damage * multiplier)
		state._log("【双色超暴击】骰点%s，伤害倍率×%s！" % [str(rolled.get("raw", [])), str(multiplier)], "crit")
	elif crit:
		state._log("【暴击】骰点%s ≥ 抵抗%d。" % [str(rolled.get("values", [])), line], "crit")
	var element = _attack_element(h, card)
	if options.has("element"): element = options.element
	if options.get("adaptive", false) or element == "adaptive":
		if not target.weaknesses.is_empty(): element = target.weaknesses[0]
		elif not target.vulnerable_elements.is_empty(): element = target.vulnerable_elements[0]
		else: element = "dark"
	var physical = element == "physical"
	var ignore_shield = 2 if physical and card.get("ranged", false) and h.ongoing == "hawkeye_lock" and h.row == "back" else 0
	var hit = state.damage_enemy(target_id, damage, element, {
		"damage_class": "physical" if physical else "magic",
		"source_hero": hero_id,
		"ignore_shield": ignore_shield,
		"ignore_auto": options.get("ignore_auto", false),
		"ignore_magic": options.get("ignore_magic", false),
		"ignore_resistance": options.get("ignore_resistance", false),
		"super": super_crit
	})
	if (crit or super_crit) and hero_id == "miku":
		h.crits += 1
		if h.form == "phantom": state.heal_mp(hero_id, 5, false)
	hit["crit"] = crit
	hit["super"] = super_crit
	hit["dice"] = rolled.get("raw", [])
	hit["line"] = line
	hit["element"] = element
	return hit

func _crit_line(state, target_id: String) -> int:
	var target = state.enemies[target_id]
	var line = int(target.crit_line) + int(target.temporary_crit_bonus)
	if target_id == "boss" and state.overheat_this_round:
		line = 4 + int(target.temporary_crit_bonus)
	return maxi(3, line)

func _roll_dice(state, hero_id: String, target_id: String, card: Dictionary, options: Dictionary = { }) -> Dictionary:
	var h = state.heroes[hero_id]
	var raw: Array = []
	var values: Array = []
	var colors = card.get("colors", ["blue"])
	for color in colors:
		if color == "neutral": continue
		var sides = int(h.dice.get(color, 6))
		raw.append(state.rng.randi_range(1, sides))
	if raw.is_empty(): raw.append(state.rng.randi_range(1, 6))
	if h.anchor_seven:
		raw[0] = 7
		h.anchor_seven = false
		state._log("命运锚点·七：第1颗骰子直接视为7。", "arcane")
	if options.get("use_lucky", false):
		var lucky_index = state.find_hand_card("weiting", "幸运改命")
		if lucky_index >= 0 and state.heroes.weiting.mp >= 5 and not state.heroes.weiting.downed:
			var chosen = 0
			for i in range(raw.size()):
				if raw[i] != 7:
					chosen = i
					break
			raw[chosen] = 7
			var lucky_id = state.heroes.weiting.hand[lucky_index]
			state.heroes.weiting.hand.remove_at(lucky_index)
			state.heroes.weiting.discard.append(lucky_id)
			state.heroes.weiting.mp -= 5
			state._log("维汀响应【幸运改命】：消耗5MP，将一颗骰子改为7！", "crit")
	var line = _crit_line(state, target_id)
	var next_crit_bonus = int(h.next_crit_bonus) if card.get("crit", false) else 0
	for value in raw:
		var modified = int(value)
		if h.form == "cosmic": modified += 2
		if h.form == "hawkeye" and h.row == "back": modified += 2
		if h.ongoing == "hawkeye_lock" and h.row == "back": modified += 1
		if card.get("name", "") in ["熵之轮盘·无常命运", "暗言术·活力汲取", "深渊虹吸·活力汲取贰式"]:
			modified += int(h.next_entropy_crit)
		modified += int(h.temporary_crit_bonus)
		modified += next_crit_bonus
		values.append(modified)
	if next_crit_bonus > 0:
		h.next_crit_bonus = 0
		state._log("应急催化剂：本次暴击掷骰+%d已消耗。" % next_crit_bonus, "charge")
	return { "raw": raw, "values": values, "line": line }

func _attack_element(h: Dictionary, card: Dictionary) -> String:
	var element = card.get("damage_type", "arcane")
	if h.convert_wind and element == "physical": return "wind"
	if h.oil_element != "" and element == "physical": return h.oil_element
	return element

func _entropy_roulette(state, hero_id: String, target_id: String, card: Dictionary, options: Dictionary) -> void:
	var rolled = _roll_dice(state, hero_id, target_id, card, options)
	var highest = 0
	for value in rolled.values: highest = maxi(highest, value)
	var attack_options = options.duplicate()
	attack_options["rolled"] = rolled
	state._log("熵之轮盘判定骰%s，最高点数%d。" % [str(rolled.raw), highest], "arcane")
	if highest <= 3:
		for enemy_id in _alive_enemies(state):
			if state.enemies[enemy_id].row == "front": attack(state, hero_id, enemy_id, card, 6, attack_options)
		state.damage_hero(hero_id, 2, "dark", "pierce", { "true_damage": true, "self_damage": true })
	elif highest <= 6:
		attack_options["element"] = "fire"
		attack(state, hero_id, target_id, card, 9, attack_options)
		state.enemies[target_id].temporary_crit_bonus -= 1
	else:
		for enemy_id in _alive_enemies(state):
			attack_options["element"] = "wind" if enemy_id == "arm" else "lightning"
			attack(state, hero_id, enemy_id, card, 10, attack_options)
		for id in state.HERO_IDS:
			var h = state.heroes[id]
			if not h.deck.is_empty():
				var charged = h.deck.pop_back()
				state.attach_card(id, charged, id)
		state._log("天命大吉：全队从各自牌库顶盲贴1个充能。", "charge")

func play_reaction(state, reaction: Dictionary, context: Dictionary) -> void:
	var actor_id = reaction.get("hero_id", "")
	var target_id = reaction.get("target_id", "")
	var index = int(reaction.get("hand_index", -1))
	if actor_id == "" or target_id == "" or index < 0 or index >= state.heroes[actor_id].hand.size(): return
	var actor = state.heroes[actor_id]
	var target = state.heroes[target_id]
	var card_id = actor.hand[index]
	var card = CardDatabase.get_card(card_id)
	if actor.mp < int(card.get("mp", 0)): return
	actor.mp -= int(card.get("mp", 0))
	actor.hand.remove_at(index)
	actor.discard.append(card_id)
	state._log("%s 响应【%s】，保护 %s。" % [actor.name, card.name, target.name], "block")
	if not context.has("reaction"): context["reaction"] = { }
	if not context.reaction.has(target_id): context.reaction[target_id] = { }
	var applied = context.reaction[target_id]
	match card.get("name", ""):
		"看破闪避":
			var roll = state.rng.randi_range(1, int(actor.dice.green))
			var attacker = state.enemies.arm if context.get("id", "") == "shield_quake" else state.enemies.boss
			var line = int(attacker.crit_line)
			if context.get("id", "") != "shield_quake" and state.overheat_this_round: line = 4
			if roll >= line:
				applied["avoid"] = true
				state.draw_cards(actor_id, 1)
				state._log("看破闪避掷出%d ≥ %d：完全闪避并抽1张牌。" % [roll, line], "good")
			else:
				applied["reduce"] = 6
				state._log("看破闪避掷出%d，未达%d：仍抵消6点伤害。" % [roll, line], "block")
		"翻滚披风":
			target.p_shield += 4
			target.m_shield += 4
			applied["free_move"] = true
		"烟幕替身":
			target.p_shield += 10
			var source = context.get("source", "boss")
			state.enemy_damage_reduction[source] = 3
			context["skip_reduction_for"] = source
			state._log("烟幕替身：攻击者本轮后续伤害-3。", "block")
		"风墙反制":
			target.p_shield += 8
			target.m_shield += 8
			applied["counter"] = 8
			applied["actor"] = actor_id
		"睿智法袍":
			target.p_shield += 4
			target.m_shield += 4
			if actor.ongoing != "": state.heal_mp(actor_id, 5)
		"应急催化剂":
			if not actor.hand.is_empty():
				var attach_index = int(reaction.get("attach_index", actor.hand.size() - 1))
				attach_index = clampi(attach_index, 0, actor.hand.size() - 1)
				var charged = actor.hand[attach_index]
				actor.hand.remove_at(attach_index)
				state.attach_card(target_id, charged, actor_id)
				state._log("应急催化剂将【%s】贴入 %s 的充能区。" % [CardDatabase.get_card(charged).get("name", charged), target.name], "charge")
			if reaction.get("mode", "shield") == "crit":
				target.next_crit_bonus += 1
				state._log("%s 本轮下一次暴击掷骰+1。" % target.name, "crit")
			else:
				target.p_shield += 4
				target.m_shield += 4
		"时间悖论":
			target.p_shield += 10
			target.m_shield += 10
			applied["no_crit"] = true
		"炼金防护力场":
			for id in state.HERO_IDS:
				var h = state.heroes[id]
				if h.row == target.row and not h.downed:
					h.p_shield += 8
					h.m_shield += 8
					h.negative = false
		_:
			target.p_shield += 4
			target.m_shield += 4
	state.changed.emit()

func _alive_enemies(state) -> Array:
	var result: Array = []
	for id in state.ENEMY_IDS:
		if state.enemies[id].alive: result.append(id)
	return result
