class_name BattleState
extends RefCounted

signal changed
signal log_added(text: String, tone: String)
signal unit_hit(unit_id: String, amount: int, element: String)
signal round_started(round_number: int)
signal ascension_offered(hero_id: String, offers: Array)
signal battle_finished(victory: bool)

const HERO_IDS = ["miku", "weiting"]
const ENEMY_IDS = ["arm", "boss"]
const FORM_NAMES = { "phantom": "幻影夜行", "hawkeye": "鹰眼绝境", "cosmic": "星穹坍缩", "alchemy": "真理炼金矩阵" }
const FORM_CARDS = { "phantom": "幻影夜行", "hawkeye": "鹰眼绝境", "cosmic": "星穹坍缩", "alchemy": "真理炼金矩阵" }
const HERO_FORMS = { "miku": ["phantom", "hawkeye"], "weiting": ["cosmic", "alchemy"] }
const ELEMENT_NAMES = { "physical": "物理", "wind": "风", "lightning": "雷", "dark": "暗", "earth": "地", "fire": "火", "ice": "冰", "light": "光", "poison": "毒", "arcane": "奥术" }

var rng = RandomNumberGenerator.new()
var resolver = CardResolver.new()
var heroes: Dictionary = { }
var enemies: Dictionary = { }
var round_number: int = 0
var phase: String = "idle"
var active_hero: String = "miku"
var battle_seed: int = 0
var doom: int = 0
var doom_max: int = 6
var doom_reversed: bool = false
var pending_collapse: bool = false
var collapse_this_round: bool = false
var overheat_this_round: bool = false
var overheat_next: bool = false
var boss_phase_two: bool = false
var crisis_pending: bool = false
var boss_permanent_bonus: int = 0
var enemy_damage_reduction: Dictionary = { "boss": 0, "arm": 0 }
var armor_check: Dictionary = { }
var intents: Array = []
var intent_deck: Array = []
var enemy_queue: Array = []
var enemy_index: int = -1
var pending_action: Dictionary = { }
var ascension_offers: Dictionary = { }
var combat_log: Array = []
var team_charges_used: int = 0
var weak_hits_this_round: int = 0
var last_events: Array = []
var winner: String = ""

func start_battle(seed_value: int = -1) -> void:
	if seed_value < 0:
		rng.randomize()
		battle_seed = int(rng.seed)
	else:
		battle_seed = seed_value
		rng.seed = seed_value
	round_number = 0
	phase = "setup"
	winner = ""
	doom = 0
	doom_reversed = false
	pending_collapse = false
	collapse_this_round = false
	overheat_this_round = false
	overheat_next = false
	boss_phase_two = false
	crisis_pending = false
	boss_permanent_bonus = 0
	enemy_damage_reduction = { "boss": 0, "arm": 0 }
	armor_check = { }
	intents.clear()
	intent_deck.clear()
	enemy_queue.clear()
	enemy_index = -1
	pending_action = { }
	ascension_offers.clear()
	combat_log.clear()
	team_charges_used = 0
	weak_hits_this_round = 0
	last_events.clear()
	heroes = {
		"miku": _make_hero("miku", "米阔阔", "浪客 · 神射手", 40, 35, 10, 10, 8, 6, "back"),
		"weiting": _make_hero("weiting", "维汀", "混沌法师 · 造物使", 35, 50, 8, 10, 8, 8, "front")
	}
	enemies = {
		"boss": _make_enemy("boss", "克洛诺斯-IV型", "裂界发条巨像", 140, 60, 8, 6, 7, "back"),
		"arm": _make_enemy("arm", "镇压重盾臂", "伴生部位 · 前排护航", 35, 0, 4, 0, 5, "front")
	}
	for hero_id in HERO_IDS:
		var hero = heroes[hero_id]
		hero.deck = CardDatabase.build_main_deck(hero_id)
		_shuffle(hero.deck)
	heroes.miku.charges.append({ "card_id": "feather_token", "name": "疾风羽饰", "owner": "miku", "token": true })
	heroes.weiting.scroll_active = true
	_log("标准有预备战斗：维汀以满MP与激活卷轴入场，米阔阔携带1个羽饰充能。", "system")
	for hero_id in HERO_IDS:
		draw_cards(hero_id, 5)
	_start_next_round()

func _make_hero(id: String, title: String, subtitle: String, hp: int, mp: int, dex: int, insight: int, will: int, might: int, row: String) -> Dictionary:
	return {
		"id": id, "name": title, "subtitle": subtitle, "max_hp": hp, "hp": hp,
		"max_mp": mp, "mp": mp, "dice": { "green": dex, "blue": insight, "purple": will, "red": might },
		"base_auto_p": int(dex / 2), "base_auto_m": int(insight / 2) + (1 if id == "weiting" else 0),
		"p_shield": 0, "m_shield": 0, "auto_p": 0, "auto_m": 0,
		"ap": 3, "row": row, "deck": [], "hand": [], "discard": [], "charges": [], "ongoing": "",
		"form": "", "form_rounds": 0, "form_used": { }, "stored": { }, "form_just_activated": false, "exited_this_round": false,
		"alive": true, "downed": false, "down_rounds": 0, "eliminated": false,
		"charged_this_round": 0, "cards_played": 0, "damage_this_round": 0, "last_round_damage": 0,
		"damaged_this_round": false, "absorb_count": 0, "absorb_total": 0, "absorb_cap_bonus": 0,
		"mp_healed": 0, "crits": 0, "next_attack_bonus": 0, "next_magic_bonus": 0,
		"next_entropy_crit": 0, "entropy_discount": false, "anchor_seven": false,
		"spell_discount_used": false, "convert_wind": false, "oil_element": "", "oil_bonus": 0,
		"hidden": false, "refund_used": false, "sage_stone_used": { }, "core_charge_used": false,
		"charge_cap_bonus": 0, "charge_cap_turns": 0, "scroll_active": false,
		"temporary_crit_bonus": 0, "next_crit_bonus": 0, "vulnerable_elements": [], "negative": false
	}

func _make_enemy(id: String, title: String, subtitle: String, hp: int, mp: int, p: int, m: int, crit: int, row: String) -> Dictionary:
	return {
		"id": id, "name": title, "subtitle": subtitle, "max_hp": hp, "hp": hp, "max_mp": mp, "mp": mp,
		"base_auto_p": p, "base_auto_m": m, "p_shield": 0, "m_shield": 0, "auto_p": 0, "auto_m": 0,
		"crit_line": crit, "row": row, "alive": true, "revealed": false, "exposed": false,
		"damaged_this_round": false, "damaged_by": { }, "negative": false, "temporary_crit_bonus": 0,
		"vulnerable_elements": [], "weaknesses": ["lightning", "dark"] if id == "boss" else ["wind"],
		"resistances": ["ice", "earth"] if id == "boss" else ["physical", "fire"], "immunities": ["poison"]
	}

func _start_next_round() -> void:
	if phase in ["victory", "defeat"]:
		return
	round_number += 1
	phase = "round_start"
	pending_action = { }
	doom_reversed = false
	weak_hits_this_round = 0
	enemy_damage_reduction = { "boss": 0, "arm": 0 }
	collapse_this_round = pending_collapse
	overheat_this_round = overheat_next
	overheat_next = false
	if pending_collapse:
		doom = 0
		pending_collapse = false
		_log("命刻抵达6格：本轮第1张意图被强制替换为【归零坍缩】！", "danger")
	if overheat_this_round:
		_log("克洛诺斯【过热瘫痪】：本轮暴击抵抗4，受到的最终伤害×1.5。", "good")
	var boss = enemies.boss
	if not boss_phase_two and (crisis_pending or boss.hp <= 70) and boss.hp > 0:
		boss_phase_two = true
		crisis_pending = false
		intent_deck.clear()
		_log("首领危机升变【终焉发条 · 过载形态】！双盾变为4/4，攻击附加暗属性伤害。", "danger")
	_reset_round_units()
	_down_recovery_rolls()
	_generate_intents()
	ascension_offers.clear()
	for hero_id in HERO_IDS:
		var hero = heroes[hero_id]
		hero.exited_this_round = false
	for hero_id in HERO_IDS:
		var hero = heroes[hero_id]
		if hero.form != "":
			if hero.form_rounds >= 2:
				_exit_form(hero_id)
			else:
				hero.form_rounds += 1
		if not hero.alive or hero.downed:
			continue
		if hero.form == "":
			var offers = get_available_ascensions(hero_id)
			if not offers.is_empty():
				ascension_offers[hero_id] = offers
	if ascension_offers.is_empty():
		finalize_round_start()
	else:
		for hero_id in ascension_offers:
			ascension_offered.emit(hero_id, ascension_offers[hero_id])
	changed.emit()

func _reset_round_units() -> void:
	for hero_id in HERO_IDS:
		var h = heroes[hero_id]
		h.p_shield = h.base_auto_p
		h.m_shield = h.base_auto_m
		h.auto_p = h.base_auto_p
		h.auto_m = h.base_auto_m
		h.ap = 3
		h.cards_played = 0
		h.damage_this_round = 0
		h.damaged_this_round = false
		h.absorb_count = 0
		h.absorb_cap_bonus = 0
		h.next_attack_bonus = 0
		h.next_magic_bonus = 0
		h.next_entropy_crit = 0
		h.entropy_discount = false
		h.spell_discount_used = false
		h.convert_wind = false
		h.oil_element = ""
		h.oil_bonus = 0
		h.hidden = false
		h.refund_used = false
		h.sage_stone_used = { }
		h.core_charge_used = false
		h.temporary_crit_bonus = 0
		h.next_crit_bonus = 0
		h.vulnerable_elements = []
		h.negative = false
		h.charged_this_round = 0
		if h.charge_cap_turns > 0:
			h.charge_cap_turns -= 1
			if h.charge_cap_turns <= 0:
				h.charge_cap_bonus = 0
	for enemy_id in ENEMY_IDS:
		var e = enemies[enemy_id]
		if not e.alive:
			e.p_shield = 0
			e.m_shield = 0
			continue
		var p = e.base_auto_p
		var m = e.base_auto_m
		if enemy_id == "boss":
			if boss_phase_two:
				p = 4
				m = 4
		e.p_shield = p
		e.m_shield = m
		e.auto_p = p
		e.auto_m = m
		e.exposed = false
		e.damaged_this_round = false
		e.damaged_by = { }
		e.negative = false
		e.temporary_crit_bonus = 0
		e.vulnerable_elements = []

func _down_recovery_rolls() -> void:
	for hero_id in HERO_IDS:
		var h = heroes[hero_id]
		if not h.downed or h.eliminated:
			continue
		h.down_rounds += 1
		if h.down_rounds >= 3:
			h.eliminated = true
			h.alive = false
			_log("%s 倒地满3轮，退出本场战斗。" % h.name, "danger")
			continue
		var roll = rng.randi_range(1, int(h.dice.red))
		if roll >= 10:
			_revive_hero(hero_id)
			_log("%s 的力量骰为%d，自行站起！" % [h.name, roll], "good")
		else:
			_log("%s 倒地复苏骰为%d，未能站起；可由队友消耗2AP与1张手牌复苏。" % [h.name, roll], "warning")

func get_available_ascensions(hero_id: String) -> Array:
	var h = heroes.get(hero_id, { })
	if h.is_empty() or h.downed or not h.alive or h.form != "" or h.get("exited_this_round", false):
		return []
	var result: Array = []
	for form_id in HERO_FORMS[hero_id]:
		if h.form_used.get(form_id, false):
			continue
		var card_name = FORM_CARDS[form_id]
		var in_hand = card_name in _hand_names(hero_id)
		var available = round_number >= 3
		if in_hand:
			match form_id:
				"phantom": available = available or h.crits >= 2 or team_charges_used >= 2
				"hawkeye": available = available or h.row == "back"
				"cosmic": available = available or h.absorb_total >= 2 or h.mp_healed >= 15
				"alchemy": available = available or team_charge_count() >= 3
		if available:
			result.append(form_id)
	return result

func choose_ascension(hero_id: String, form_id: String = "") -> bool:
	if phase != "round_start" or not ascension_offers.has(hero_id):
		return false
	if form_id != "":
		if not form_id in ascension_offers[hero_id]:
			return false
		_activate_form(hero_id, form_id)
	else:
		if round_number >= 3:
			return false
		_log("%s 暂缓升变。" % heroes[hero_id].name, "system")
	ascension_offers.erase(hero_id)
	if ascension_offers.is_empty():
		finalize_round_start()
	else:
		changed.emit()
	return true

func _activate_form(hero_id: String, form_id: String) -> void:
	var h = heroes[hero_id]
	var card_name = FORM_CARDS[form_id]
	var found = false
	for i in range(h.hand.size() - 1, -1, -1):
		if CardDatabase.get_card(h.hand[i]).get("name", "") == card_name:
			h.hand.remove_at(i)
			found = true
			break
	if not found:
		for i in range(h.deck.size() - 1, -1, -1):
			if CardDatabase.get_card(h.deck[i]).get("name", "") == card_name:
				h.deck.remove_at(i)
				found = true
				break
	h.stored = { "hand": h.hand.duplicate(), "deck": h.deck.duplicate(), "discard": h.discard.duplicate(), "ascension": card_name }
	h.hand = []
	h.deck = CardDatabase.build_form_deck(form_id)
	h.discard = []
	_shuffle(h.deck)
	h.form = form_id
	h.form_rounds = 1
	h.form_just_activated = true
	h.form_used[form_id] = true
	_log("【升变】%s 开启【%s】！主卡组与手牌暂存，形态卡组入场2轮。" % [h.name, FORM_NAMES[form_id]], "crit")

func _exit_form(hero_id: String) -> void:
	var h = heroes[hero_id]
	var old_form = h.form
	for other_id in HERO_IDS:
		var kept_charges: Array = []
		for charge in heroes[other_id].charges:
			var charge_card = CardDatabase.get_card(charge.get("card_id", ""))
			if charge_card.get("form", "") == old_form:
				_log("形态充能【%s】随【%s】一并移出本局。" % [charge.get("name", "形态牌"), FORM_NAMES.get(old_form, old_form)], "system")
			else:
				kept_charges.append(charge)
		heroes[other_id].charges = kept_charges
	h.hand = h.stored.get("hand", [])
	h.deck = h.stored.get("deck", [])
	h.discard = h.stored.get("discard", [])
	h.stored = { }
	h.form = ""
	h.form_rounds = 0
	h.form_just_activated = false
	h.exited_this_round = true
	_log("【%s】的两轮爆发结束，形态牌移出本局，%s 恢复原卡组。" % [FORM_NAMES.get(old_form, old_form), h.name], "system")

func finalize_round_start() -> void:
	if phase != "round_start":
		return
	for hero_id in HERO_IDS:
		var h = heroes[hero_id]
		if not h.alive or h.downed:
			continue
		if h.form != "":
			draw_cards(hero_id, 5)
		elif round_number > 1:
			draw_cards(hero_id, 3)
		h.form_just_activated = false
	phase = "player"
	var w = heroes.weiting
	if w.scroll_active and not w.downed and w.alive:
		_log("魔力激发卷轴造成2点无视护盾刺痛。", "arcane")
		damage_hero("weiting", 2, "dark", "pierce", { "true_damage": true, "self_damage": true })
	_log("—— 第%d轮 · 玩家阶段：每人3AP，自由交替行动 ——" % round_number, "round")
	round_started.emit(round_number)
	changed.emit()
	check_battle_end()

func draw_cards(hero_id: String, count: int) -> void:
	var h = heroes[hero_id]
	for n in range(count):
		if h.deck.is_empty():
			if h.form != "":
				break
			if h.discard.is_empty():
				_log("%s 的牌库与弃牌堆均为空，无法抽牌。" % h.name, "warning")
				break
			h.deck = h.discard.duplicate()
			h.discard.clear()
			_shuffle(h.deck)
			_log("牌库耗尽：%s 将弃牌堆洗回主卡组（电子化补全规则）。" % h.name, "system")
		var card_id = h.deck.pop_back()
		h.hand.append(card_id)

func _hand_names(hero_id: String) -> Array:
	var names: Array = []
	for card_id in heroes[hero_id].hand:
		names.append(CardDatabase.get_card(card_id).get("name", ""))
	return names

func get_card_cost(hero_id: String, card: Dictionary, options: Dictionary = { }) -> Dictionary:
	var h = heroes[hero_id]
	var ap = int(card.get("ap", 0))
	var mp = int(card.get("mp", 0))
	if card.get("name", "") == "先声夺人" and round_number == 1:
		mp = 0
	if card.get("name", "") == "交叉火力网":
		var target = enemies.get(options.get("target_id", "boss"), { })
		for other_id in HERO_IDS:
			if other_id != hero_id and target.get("damaged_by", { }).get(other_id, false):
				ap = 0
	if hero_id == "weiting" and h.scroll_active and not h.spell_discount_used and _is_spell(card):
		mp = maxi(0, mp - 5)
	if hero_id == "weiting" and h.entropy_discount and card.get("name", "") in ["熵之轮盘·无常命运", "暗言术·活力汲取", "深渊虹吸·活力汲取贰式"]:
		mp = maxi(0, mp - 5)
	if options.get("use_mp_charge", false) and mp > 0 and not h.charges.is_empty():
		mp = maxi(0, mp - 5)
	return { "ap": ap, "mp": mp }

func _is_spell(card: Dictionary) -> bool:
	return card.get("kind", "") in ["skill", "form_skill"] and card.get("damage_type", "none") not in ["physical", "none"] and card.get("target", "") != "none"

func validate_play(hero_id: String, hand_index: int, options: Dictionary = { }) -> String:
	if phase != "player": return "当前不是玩家阶段"
	if not heroes.has(hero_id): return "角色不存在"
	var h = heroes[hero_id]
	if h.downed or not h.alive: return "倒地角色无法出牌"
	if hand_index < 0 or hand_index >= h.hand.size(): return "请选择有效手牌"
	var card = CardDatabase.get_card(h.hand[hand_index])
	if card.is_empty(): return "卡牌数据缺失"
	if card.get("kind", "") in ["reaction", "form_reaction"]: return "时机牌需在合适的响应窗口打出"
	if card.get("kind", "") == "ascension":
		if round_number < 3 and card.get("name", "") in ["幻影夜行", "星穹坍缩"]:
			return ""
		return "升变牌请在轮始升变窗口发动"
	var cost = get_card_cost(hero_id, card, options)
	if h.ap < cost.ap: return "AP不足"
	if h.mp < cost.mp: return "MP不足"
	if options.get("use_mp_charge", false):
		var uncharged_options = options.duplicate()
		uncharged_options["use_mp_charge"] = false
		var uncharged_mp = int(get_card_cost(hero_id, card, uncharged_options).mp)
		if uncharged_mp > int(cost.mp) and h.charges.is_empty(): return "没有可用充能"
	var card_name = card.get("name", "")
	if card_name == "穿云狙击" and h.row != "back": return "穿云狙击仅限后排打出"
	if card_name == "苦痛转导" and not h.damaged_this_round: return "本轮尚未受到伤害"
	if card_name == "调合·万能灵药" and h.charges.is_empty(): return "至少需要1个充能"
	if card_name == "魔力过载仪式" and not h.scroll_active: return "卷轴未激活"
	if card_name == "贤者之石解放" and h.sage_stone_used.get(options.get("ally_id", hero_id), false): return "本轮已对该角色使用过贤者之石解放"
	return ""

func play_card(hero_id: String, hand_index: int, options: Dictionary = { }) -> Dictionary:
	var error = validate_play(hero_id, hand_index, options)
	if error != "":
		return { "ok": false, "error": error }
	var result = resolver.play(self, hero_id, hand_index, options)
	changed.emit()
	check_battle_end()
	return result

func move_hero(hero_id: String, free: bool = false) -> bool:
	if phase != "player": return false
	var h = heroes[hero_id]
	if h.downed or not h.alive or (not free and h.ap < 1): return false
	if not free: h.ap -= 1
	h.row = "back" if h.row == "front" else "front"
	_log("%s 移动至%s。" % [h.name, "后排" if h.row == "back" else "前排"], "move")
	changed.emit()
	return true

func attach_from_hand(hero_id: String, hand_index: int, target_id: String) -> bool:
	if phase != "player": return false
	var h = heroes[hero_id]
	if h.downed or hand_index < 0 or hand_index >= h.hand.size(): return false
	var limit = 2 if h.form == "alchemy" else 1
	if h.charged_this_round >= limit: return false
	if not heroes.has(target_id) or heroes[target_id].downed: return false
	var card_id = h.hand[hand_index]
	h.hand.remove_at(hand_index)
	h.charged_this_round += 1
	attach_card(target_id, card_id, hero_id)
	_log("%s 将【%s】贴入 %s 的充能区。" % [h.name, CardDatabase.get_card(card_id).get("name", card_id), heroes[target_id].name], "charge")
	changed.emit()
	return true

func attach_card(target_id: String, card_id: String, owner_id: String = "") -> bool:
	if not heroes.has(target_id): return false
	var h = heroes[target_id]
	var cap = 2 + int(h.charge_cap_bonus)
	if card_id == "": return false
	h.charges.append({ "card_id": card_id, "name": CardDatabase.get_card(card_id).get("name", card_id), "owner": owner_id if owner_id != "" else target_id, "token": false })
	while h.charges.size() > cap:
		var old = h.charges.pop_front()
		_discard_charge(old)
		_log("充能区已满：最早的【%s】进入原持有者弃牌堆。" % old.name, "system")
	return true

func consume_charge(hero_id: String, allow_matrix: bool = true) -> Dictionary:
	var h = heroes[hero_id]
	if h.charges.is_empty(): return { }
	var charge = h.charges.pop_front()
	team_charges_used += 1
	var retained = false
	if allow_matrix and heroes.weiting.form == "alchemy":
		var die = rng.randi_range(1, 8)
		if die % 2 == 0:
			h.charges.append(charge)
			retained = true
		_log("真理炼金矩阵掷出%d：充能%s。" % [die, "保留并回到充能区" if retained else "正常消耗"], "charge")
	if not retained:
		_discard_charge(charge)
	return charge

func _discard_charge(charge: Dictionary) -> void:
	if charge.get("token", false): return
	var owner_id = charge.get("owner", "")
	if heroes.has(owner_id):
		heroes[owner_id].discard.append(charge.card_id)

func charge_to_ap(hero_id: String) -> bool:
	if phase != "player" or heroes[hero_id].charges.is_empty() or heroes[hero_id].downed: return false
	consume_charge(hero_id)
	heroes[hero_id].ap += 1
	_log("%s 消耗1充能，获得1AP。" % heroes[hero_id].name, "charge")
	changed.emit()
	return true

func team_charge_count() -> int:
	return heroes.miku.charges.size() + heroes.weiting.charges.size()

func interfere() -> bool:
	if phase != "player" or doom_reversed or doom <= 0: return false
	var h = heroes[active_hero]
	if h.ap < 1 or h.charges.is_empty() or h.downed: return false
	h.ap -= 1
	consume_charge(active_hero)
	reverse_doom("以太干扰")
	changed.emit()
	return true

func reverse_doom(source: String) -> void:
	if doom_reversed or doom <= 0: return
	doom -= 1
	doom_reversed = true
	_log("【%s】使命刻逆转1格（本轮已使用逆转）。当前命刻%d/6。" % [source, doom], "good")

func toggle_scroll() -> bool:
	var h = heroes.weiting
	if phase != "player" or h.downed: return false
	if h.scroll_active:
		h.scroll_active = false
		_log("维汀关闭魔力激发卷轴。", "system")
	else:
		if h.ap < 1 or h.mp < 5: return false
		h.ap -= 1
		h.mp -= 5
		h.scroll_active = true
		_log("维汀消耗1AP与5MP，重新激活魔力激发卷轴。", "arcane")
	changed.emit()
	return true

func revive_ally(actor_id: String, target_id: String, hand_index: int = -1) -> bool:
	if phase != "player" or not heroes.has(target_id): return false
	var actor = heroes[actor_id]
	var target = heroes[target_id]
	if actor.downed or not target.downed or target.eliminated or actor.ap < 2 or actor.hand.is_empty(): return false
	if hand_index < 0 or hand_index >= actor.hand.size(): hand_index = actor.hand.size() - 1
	var card_id = actor.hand[hand_index]
	actor.hand.remove_at(hand_index)
	actor.discard.append(card_id)
	actor.ap -= 2
	_revive_hero(target_id)
	_log("%s 消耗2AP与【%s】复苏 %s。" % [actor.name, CardDatabase.get_card(card_id).get("name", card_id), target.name], "good")
	changed.emit()
	return true

func _revive_hero(hero_id: String) -> void:
	var h = heroes[hero_id]
	h.downed = false
	h.alive = true
	h.hp = maxi(1, int(h.max_hp / 4))
	h.down_rounds = 0

func end_player_turn() -> void:
	if phase != "player": return
	phase = "enemy"
	enemy_queue.clear()
	if enemies.arm.alive:
		enemy_queue.append("shield_quake")
	for intent in intents:
		enemy_queue.append(intent.id)
	enemy_index = -1
	pending_action = { }
	_log("—— 敌人阶段：依次结算意图，可使用防御时机牌 ——", "danger")
	changed.emit()

func prepare_next_enemy_action() -> Dictionary:
	if phase != "enemy": return { "done": true }
	if winner != "": return { "done": true }
	enemy_index += 1
	if enemy_index >= enemy_queue.size():
		_end_round()
		return { "done": true }
	var intent_id = enemy_queue[enemy_index]
	var data = BossDatabase.get_intent(intent_id)
	pending_action = data.duplicate(true)
	pending_action["id"] = intent_id
	pending_action["source"] = "arm" if intent_id == "shield_quake" else "boss"
	pending_action["targets"] = _targets_for_intent(data)
	for intent in intents:
		if intent.id == intent_id and intent.get("target_hero", "") != "":
			pending_action.targets = [intent.target_hero]
	pending_action["reaction"] = { }
	if intent_id == "dimension_cannon":
		var locked = ""
		for intent in intents:
			if intent.id == intent_id:
				locked = intent.get("locked", _highest_backline_hero())
		pending_action["locked"] = locked
		pending_action.targets = [locked] if locked != "" else []
	_log("意图结算【%s】。" % data.get("name", intent_id), "intent")
	changed.emit()
	return pending_action.duplicate(true)

func _targets_for_intent(data: Dictionary) -> Array:
	var alive = _alive_heroes()
	match data.get("target", ""):
		"front_all": return _heroes_in_row("front")
		"all_heroes": return alive
		"both_rows": return alive
		"single", "locked_hero":
			var front = _heroes_in_row("front")
			if not front.is_empty(): return [front[0]]
			var back = _highest_backline_hero()
			return [back] if back != "" else []
		"highest_hp":
			var best = ""
			var hp = -1
			for id in alive:
				if heroes[id].hp > hp:
					hp = heroes[id].hp
					best = id
			return [best] if best != "" else []
		"self": return ["boss"]
	return []

func legal_reactions(context: Dictionary) -> Array:
	var result: Array = []
	if context.is_empty() or context.get("target", "") in ["self", "none"]: return result
	for hero_id in HERO_IDS:
		var h = heroes[hero_id]
		if h.downed or not h.alive: continue
		for i in range(h.hand.size()):
			var card = CardDatabase.get_card(h.hand[i])
			if card.get("kind", "") not in ["reaction", "form_reaction"]: continue
			if card.get("name", "") == "幸运改命": continue
			if h.mp < int(card.get("mp", 0)): continue
			for target_id in context.get("targets", []):
				if not heroes.has(target_id) or heroes[target_id].downed: continue
				if card.get("name", "") == "看破闪避" and context.get("target", "") not in ["single", "locked_hero", "highest_hp"]: continue
				if card.get("name", "") == "应急催化剂":
					result.append({ "hero_id": hero_id, "hand_index": i, "target_id": target_id, "card": card, "mode": "shield" })
					result.append({ "hero_id": hero_id, "hand_index": i, "target_id": target_id, "card": card, "mode": "crit" })
				else:
					result.append({ "hero_id": hero_id, "hand_index": i, "target_id": target_id, "card": card })
	return result

func resolve_enemy_action(reaction: Dictionary = { }, split: bool = false) -> void:
	if phase != "enemy" or pending_action.is_empty(): return
	if not reaction.is_empty():
		resolver.play_reaction(self, reaction, pending_action)
	var id = pending_action.get("id", "")
	var context = pending_action
	if id == "causal_erasure" and split and context.targets.size() == 1 and _alive_heroes().size() > 1:
		var original = context.targets[0]
		for other in _alive_heroes():
			if other != original:
				context.targets.append(other)
				break
		context["split"] = true
		_log("分摊援护：两人各承受12点伤害，并分别结算护盾。", "good")
	_execute_intent(context)
	pending_action = { }
	changed.emit()
	check_battle_end()

func _execute_intent(context: Dictionary) -> void:
	var id = context.get("id", "")
	var boss = enemies.boss
	if boss.mp < int(context.get("mp", 0)):
		_log("首领MP不足，【%s】无法正常发动。" % context.name, "good")
		return
	boss.mp -= int(context.get("mp", 0))
	match id:
		"gravity_sweep", "shield_quake":
			var targets = _heroes_in_row("front")
			var amount = int(context.damage)
			if targets.is_empty() and id == "gravity_sweep":
				targets = _heroes_in_row("back")
				amount = 4
				_log("前排无人，重力横扫挥空，仅造成4点震波。", "system")
			for hero_id in targets:
				_enemy_hit(hero_id, amount, context.element, context.damage_class, context)
				if id == "gravity_sweep" and heroes[hero_id].row == "front":
					heroes[hero_id].row = "back"
		"dimension_cannon":
			var target_id = context.get("locked", "")
			if target_id != "" and not heroes[target_id].downed:
				var amount = 20 if heroes[target_id].row == "back" else 8
				_enemy_hit(target_id, amount, "physical", "physical", context, true)
		"aether_siphon":
			for hero_id in _alive_heroes():
				_enemy_hit(hero_id, 6, "dark", "magic", context)
				if not heroes[hero_id].charges.is_empty():
					var eaten = heroes[hero_id].charges.pop_front()
					_discard_charge(eaten)
					heal_enemy("boss", 8)
					_log("以太虹吸吞噬 %s 的1个充能，首领回复8HP。" % heroes[hero_id].name, "danger")
		"clockwork_volley":
			var targets = context.get("targets", [])
			if not targets.is_empty():
				var target_id = targets[0]
				if heroes[target_id].hidden:
					var alternatives = _alive_heroes()
					alternatives.erase(target_id)
					if not alternatives.is_empty(): target_id = alternatives[0]
				for hit in range(2):
					if not heroes[target_id].downed:
						_enemy_hit(target_id, 8, "physical", "physical", context)
		"phase_shift":
			for hero_id in _alive_heroes():
				_enemy_hit(hero_id, 7, "lightning", "magic", context)
				heroes[hero_id].row = "back" if heroes[hero_id].row == "front" else "front"
			doom += 1
			_log("相位折跃：全员换位，场景命刻额外+1。", "danger")
		"armor_reconstruction":
			boss.p_shield += 14
			boss.m_shield += 14
			armor_check = { "active": true }
			_log("装甲重构获得14物理护盾与14魔法护盾；轮末至少击破一项！", "warning")
		"time_guillotine":
			for hero_id in _alive_heroes():
				if heroes[hero_id].row == "front":
					_enemy_hit(hero_id, 15, "physical", "physical", context)
				else:
					_enemy_hit(hero_id, 15, "arcane", "magic", context)
		"fate_turbulence":
			for hero_id in _alive_heroes():
				_enemy_hit(hero_id, 8, "dark", "magic", context)
				var extra = heroes[hero_id].hand.size() * 2
				if extra > 0:
					_log("%s 剩余%d张手牌，追加%d点刺痛。" % [heroes[hero_id].name, heroes[hero_id].hand.size(), extra], "danger")
					damage_hero(hero_id, extra, "dark", "pierce", { "true_damage": true })
		"causal_erasure":
			var amount = 12 if context.get("split", false) else 24
			for hero_id in context.targets:
				if not heroes[hero_id].downed:
					_enemy_hit(hero_id, amount, "physical", "physical", context)
		"zero_collapse":
			for hero_id in _alive_heroes():
				_enemy_hit(hero_id, 28, "dark", "pierce", context, true)
			overheat_next = true
			_log("归零坍缩释放完毕：首领将在下一轮过热瘫痪。", "good")

func _enemy_hit(hero_id: String, amount: int, element: String, damage_class: String, context: Dictionary, pierce: bool = false) -> void:
	if heroes[hero_id].downed or phase in ["victory", "defeat"]: return
	var reactions = context.get("reaction", { })
	var reaction = reactions.get(hero_id, { })
	if reaction.get("avoid", false):
		_log("%s 完全闪避【%s】。" % [heroes[hero_id].name, context.name], "good")
		return
	var source = context.get("source", "boss")
	var reduction = 0 if context.get("skip_reduction_for", "") == source else int(enemy_damage_reduction.get(source, 0))
	var permanent = boss_permanent_bonus if source == "boss" else 0
	var final_amount = maxi(0, amount + permanent - reduction - int(reaction.get("reduce", 0)))
	var result = damage_hero(hero_id, final_amount, element, damage_class, { "pierce": pierce, "reaction": reaction })
	var total_hp_damage = int(result.get("hp_damage", 0))
	if source == "boss" and boss_phase_two and not heroes[hero_id].downed and phase not in ["victory", "defeat"]:
		var rider = damage_hero(hero_id, 3, "dark", "magic", { "rider": true })
		total_hp_damage += int(rider.get("hp_damage", 0))
	if total_hp_damage == 0 and reaction.get("counter", 0) > 0 and enemies[source].alive:
		_log("风墙反制完全抵消攻击，反击 %s！" % enemies[source].name, "good")
		damage_enemy(source, reaction.counter, "physical", { "damage_class": "physical", "source_hero": reaction.get("actor", "miku") })
	if reaction.get("free_move", false) and not heroes[hero_id].downed and heroes[hero_id].row == "front":
		heroes[hero_id].row = "back"
		_log("翻滚披风：%s 在受击后免费移动至后排。" % heroes[hero_id].name, "move")

func damage_hero(hero_id: String, amount: int, element: String = "physical", damage_class: String = "physical", options: Dictionary = { }) -> Dictionary:
	var h = heroes[hero_id]
	if h.downed and not options.get("aoe", false): return { "hp_damage": 0, "fully_blocked": true }
	var incoming = maxi(0, amount)
	var remaining = incoming
	var absorbed = 0
	if not options.get("true_damage", false):
		var use_p = damage_class in ["physical", "pierce"]
		var shield_key = "p_shield" if use_p else "m_shield"
		var auto_key = "auto_p" if use_p else "auto_m"
		var ignored_auto = int(h[auto_key]) if options.get("pierce", false) else 0
		var bonus_shield = maxi(0, int(h[shield_key]) - int(h[auto_key]))
		var usable = maxi(0, int(h[shield_key]) - ignored_auto)
		absorbed = mini(remaining, usable)
		remaining -= absorbed
		if options.get("pierce", false):
			var bonus_absorbed = mini(absorbed, bonus_shield)
			h[shield_key] = int(h[shield_key]) - bonus_absorbed
		else:
			h[shield_key] = int(h[shield_key]) - absorbed
			h[auto_key] = mini(int(h[auto_key]), int(h[shield_key]))
	h.hp = maxi(0, h.hp - remaining)
	if remaining > 0:
		h.damaged_this_round = true
	unit_hit.emit(hero_id, remaining, element)
	_log("%s 受到%d点%s伤害（护盾抵消%d），HP %d/%d。" % [h.name, remaining, ELEMENT_NAMES.get(element, element), absorbed, h.hp, h.max_hp], "damage" if remaining > 0 else "block")
	if remaining > 0:
		_trigger_absorb(hero_id, remaining, options)
	if h.hp <= 0 and not h.downed:
		h.downed = true
		h.ap = 0
		h.down_rounds = 0
		_log("【%s 倒地】无法行动；队友可用2AP与1张手牌复苏。" % h.name, "danger")
	changed.emit()
	return { "hp_damage": remaining, "absorbed": absorbed, "fully_blocked": remaining == 0 }

func _trigger_absorb(hero_id: String, hp_damage: int, options: Dictionary) -> void:
	var h = heroes[hero_id]
	if h.ongoing != "intellect_reflux_persistent": return
	var cap = 2 + int(h.absorb_cap_bonus)
	if h.absorb_count < cap:
		h.absorb_count += 1
		h.absorb_total += 1
		_log("灵智回流触发（%d/%d）：回复5MP。" % [h.absorb_count, cap], "arcane")
		heal_mp(hero_id, 5, true)
		if not options.get("self_damage", false) and hp_damage >= 5 and not h.discard.is_empty():
			var card_id = h.discard.pop_back()
			attach_card(hero_id, card_id, hero_id)
			_log("受到至少5点敌方实际伤害：【%s】从弃牌堆贴入充能区。" % CardDatabase.get_card(card_id).get("name", card_id), "charge")

func damage_enemy(enemy_id: String, amount: int, element: String, options: Dictionary = { }) -> Dictionary:
	var e = enemies[enemy_id]
	if not e.alive: return { "hp_damage": 0, "absorbed": 0 }
	var incoming = maxi(0, amount)
	if element in e.immunities:
		_log("%s 免疫%s伤害。" % [e.name, ELEMENT_NAMES.get(element, element)], "block")
		return { "hp_damage": 0, "absorbed": 0 }
	if element in e.weaknesses or element in e.vulnerable_elements:
		incoming *= 2
	elif element in e.resistances and not options.get("ignore_resistance", false):
		incoming = int(incoming / 2)
	if enemy_id == "boss" and overheat_this_round:
		incoming = int(incoming * 1.5)
	var remaining = incoming
	var absorbed = 0
	if not options.get("true_damage", false):
		var physical = options.get("damage_class", "magic") == "physical"
		var shield_key = "p_shield" if physical else "m_shield"
		var auto_key = "auto_p" if physical else "auto_m"
		var shield = int(e[shield_key])
		var ignored = int(options.get("ignore_shield", 0))
		if options.get("ignore_auto", false): ignored += int(e[auto_key])
		if options.get("ignore_magic", false) and not physical: ignored = shield
		var usable = maxi(0, shield - ignored)
		absorbed = mini(remaining, usable)
		remaining -= absorbed
		if options.get("ignore_auto", false):
			var bonus = maxi(0, shield - int(e[auto_key]))
			e[shield_key] = shield - mini(absorbed, bonus)
		else:
			e[shield_key] = shield - absorbed
			e[auto_key] = mini(int(e[auto_key]), int(e[shield_key]))
	e.hp = maxi(0, e.hp - remaining)
	if enemy_id == "boss" and e.hp > 0 and e.hp <= 70 and not boss_phase_two:
		crisis_pending = true
	e.damaged_this_round = true
	var source = options.get("source_hero", "")
	if source != "" and heroes.has(source):
		e.damaged_by[source] = true
		heroes[source].damage_this_round += remaining
	unit_hit.emit(enemy_id, remaining, element)
	_log("%s 受到%d点%s伤害（护盾抵消%d），HP %d/%d。" % [e.name, remaining, ELEMENT_NAMES.get(element, element), absorbed, e.hp, e.max_hp], "damage")
	if enemy_id == "boss" and source != "":
		if element in ["lightning", "dark", "wind"] and boss_phase_two and not heroes[source].core_charge_used:
			heroes[source].core_charge_used = true
			if not heroes[source].discard.is_empty():
				var card_id = heroes[source].discard.pop_back()
				attach_card(source, card_id, source)
				_log("核心暴露：%s 免费将【%s】贴入充能区。" % [heroes[source].name, CardDatabase.get_card(card_id).get("name", card_id)], "charge")
		if element in e.weaknesses or element in e.vulnerable_elements:
			weak_hits_this_round += 1
			if weak_hits_this_round >= 2:
				reverse_doom("弱点重创")
	if options.get("super", false):
		reverse_doom("双色超暴击")
	if e.hp <= 0:
		e.alive = false
		e.p_shield = 0
		e.m_shield = 0
		if enemy_id == "arm":
			enemies.boss.row = "front"
			enemies.boss.p_shield = 0
			enemies.boss.m_shield = 0
			enemies.boss.auto_p = 0
			enemies.boss.auto_m = 0
			enemies.boss.crit_line = 6
			_log("【部位击破】重盾臂摧毁！本体被拉至前排、双盾清空、暴击抵抗永久降至6。", "crit")
		else:
			_log("克洛诺斯核心停止运转。战斗胜利！", "victory")
			_finish("victory")
	return { "hp_damage": remaining, "absorbed": absorbed, "raw_damage": incoming }

func heal_hp(hero_id: String, amount: int, trigger_cosmic: bool = true) -> int:
	var h = heroes[hero_id]
	if h.downed or not h.alive: return 0
	var actual = mini(amount, h.max_hp - h.hp)
	h.hp += actual
	if actual > 0:
		_log("%s 回复%dHP。" % [h.name, actual], "heal")
		if trigger_cosmic and h.form == "cosmic": _cosmic_pulse(hero_id)
	changed.emit()
	return actual

func heal_mp(hero_id: String, amount: int, count_for_ascension: bool = true) -> int:
	var h = heroes[hero_id]
	var actual = mini(amount, h.max_mp - h.mp)
	h.mp += actual
	if actual > 0:
		if count_for_ascension: h.mp_healed += actual
		_log("%s 回复%dMP。" % [h.name, actual], "heal")
		if h.form == "cosmic": _cosmic_pulse(hero_id)
	changed.emit()
	return actual

func _cosmic_pulse(source_hero: String) -> void:
	var target_id = ""
	var lowest = 100000
	for enemy_id in ENEMY_IDS:
		var e = enemies[enemy_id]
		if e.alive and e.hp < lowest:
			lowest = e.hp
			target_id = enemy_id
	if target_id != "":
		_log("星穹坍缩被动：恢复触发3点暗属性穿透脉冲。", "arcane")
		damage_enemy(target_id, 3, "dark", { "true_damage": true, "source_hero": source_hero })

func heal_enemy(enemy_id: String, amount: int) -> void:
	var e = enemies[enemy_id]
	if not e.alive: return
	var actual = mini(amount, e.max_hp - e.hp)
	e.hp += actual
	if actual > 0: _log("%s 回复%dHP。" % [e.name, actual], "danger")

func reveal_enemy(enemy_id: String) -> void:
	var e = enemies[enemy_id]
	e.revealed = true
	e.temporary_crit_bonus -= 1
	for intent in intents:
		if intent.get("hidden", false):
			intent.hidden = false
			intent["revealed"] = true
			_log("侦查破暗：暗置意图【%s】已翻开！" % BossDatabase.get_intent(intent.id).get("name", intent.id), "good")
	_log("揭示%s弱点：%s；本轮暴击抵抗-1。" % [e.name, _element_list(e.weaknesses)], "good")

func _element_list(elements: Array) -> String:
	var names: Array = []
	for element in elements: names.append(ELEMENT_NAMES.get(element, element))
	return " / ".join(names)

func _generate_intents() -> void:
	intents.clear()
	if intent_deck.is_empty():
		intent_deck = (BossDatabase.PHASE_TWO if boss_phase_two else BossDatabase.PHASE_ONE).duplicate()
		_shuffle(intent_deck)
	while intents.size() < 3:
		if intent_deck.is_empty():
			intent_deck = (BossDatabase.PHASE_TWO if boss_phase_two else BossDatabase.PHASE_ONE).duplicate()
			_shuffle(intent_deck)
		var id = intent_deck.pop_back()
		intents.append({ "id": id, "hidden": intents.size() == 2, "revealed": false })
	if collapse_this_round:
		intents[0] = { "id": "zero_collapse", "hidden": false, "revealed": true }
	for intent in intents:
		if intent.id == "dimension_cannon":
			intent["locked"] = _highest_backline_hero()
		elif intent.id == "clockwork_volley":
			var targets = _targets_for_intent(BossDatabase.get_intent(intent.id))
			intent["target_hero"] = targets[0] if not targets.is_empty() else ""
		elif intent.id == "causal_erasure":
			var targets = _targets_for_intent(BossDatabase.get_intent(intent.id))
			intent["target_hero"] = targets[0] if not targets.is_empty() else ""
	_log("首领意图已生成：前2张明置，第3张暗置。", "intent")

func get_intent_visible_data(index: int) -> Dictionary:
	if index < 0 or index >= intents.size(): return { }
	var intent = intents[index]
	var data = BossDatabase.get_intent(intent.id)
	data["hidden"] = intent.get("hidden", false)
	data["index"] = index
	data["locked"] = intent.get("locked", intent.get("target_hero", ""))
	if intent.get("hidden", false):
		data["name"] = "暗置 · " + data.get("category", "未知")
		data["description"] = "具体效果与数值隐藏。使用【弱点解析】可翻开本轮暗置意图。"
	return data

func _end_round() -> void:
	if phase in ["victory", "defeat"]: return
	if armor_check.get("active", false):
		if enemies.boss.p_shield > 0 and enemies.boss.m_shield > 0:
			boss_permanent_bonus += 3
			_log("装甲重构完成：两种护盾均未击破，首领所有伤害永久+3。", "danger")
		else:
			_log("至少一种重构护盾被击破，首领蓄力被打断。", "good")
	armor_check = { }
	doom += 1
	_log("轮末：场景命刻+1，当前%d/6。" % doom, "warning" if doom >= 4 else "system")
	if doom >= 6:
		pending_collapse = true
	for hero_id in HERO_IDS:
		var h = heroes[hero_id]
		h.last_round_damage = h.damage_this_round
		while h.hand.size() > 7:
			var card_id = h.hand.pop_back()
			h.discard.append(card_id)
			_log("手牌上限7：%s 弃置【%s】。" % [h.name, CardDatabase.get_card(card_id).get("name", card_id)], "system")
	_start_next_round()

func _alive_heroes() -> Array:
	var result: Array = []
	for id in HERO_IDS:
		if heroes[id].alive and not heroes[id].downed: result.append(id)
	return result

func _heroes_in_row(row: String) -> Array:
	var result: Array = []
	for id in HERO_IDS:
		if heroes[id].alive and not heroes[id].downed and heroes[id].row == row: result.append(id)
	return result

func _highest_backline_hero() -> String:
	var best = ""
	var damage = -1
	for id in _heroes_in_row("back"):
		if heroes[id].last_round_damage >= damage:
			damage = heroes[id].last_round_damage
			best = id
	return best

func check_battle_end() -> void:
	if winner != "": return
	if not enemies.boss.alive:
		_finish("victory")
		return
	if _alive_heroes().is_empty():
		_log("全队倒地，挑战失败。", "danger")
		_finish("defeat")

func _finish(result: String) -> void:
	if winner != "": return
	winner = result
	phase = result
	battle_finished.emit(result == "victory")
	changed.emit()

func find_hand_card(hero_id: String, card_name: String) -> int:
	var hand = heroes[hero_id].hand
	for i in range(hand.size()):
		if CardDatabase.get_card(hand[i]).get("name", "") == card_name: return i
	return -1

func _shuffle(array: Array) -> void:
	for i in range(array.size() - 1, 0, -1):
		var j = rng.randi_range(0, i)
		var temp = array[i]
		array[i] = array[j]
		array[j] = temp

func _log(text: String, tone: String = "system") -> void:
	combat_log.append({ "round": round_number, "text": text, "tone": tone })
	if combat_log.size() > 250: combat_log.pop_front()
	log_added.emit(text, tone)
