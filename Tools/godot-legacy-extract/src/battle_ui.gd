extends Control

signal reaction_choice_made(choice: Dictionary)

var state = BattleState.new()
var started: bool = false
var busy: bool = false
var battle_serial: int = 0
var refresh_queued: bool = false
var selected_hand: int = -1
var selected_enemy: String = "boss"
var use_mp_charge: bool = false
var use_lucky: bool = false
var use_special_charge: bool = false
var ally_choice: String = "miku"
var alchemy_mode: String = "both"
var modal_open: bool = false
var audio_player: AudioStreamPlayer

var top_round: Label
var top_phase: Label
var doom_label: Label
var doom_bar: ProgressBar
var enemy_units: HBoxContainer
var intent_list: HBoxContainer
var hero_list: HBoxContainer
var hand_list: HBoxContainer
var hand_title: Label
var detail_title: Label
var detail_meta: Label
var detail_description: RichTextLabel
var target_button: OptionButton
var ally_button: OptionButton
var charge_check: CheckButton
var lucky_check: CheckButton
var special_charge_check: CheckButton
var play_button: Button
var attach_button: Button
var move_button: Button
var ap_button: Button
var scroll_button: Button
var interfere_button: Button
var revive_button: Button
var catalyst_button: Button
var end_button: Button
var log_box: RichTextLabel
var modal_layer: CanvasLayer
var unit_nodes: Dictionary = { }

const COLOR_MAP = { "system": "9bacc2", "round": "d8b96f", "card": "dfe8f5", "damage": "ef8a8f", "danger": "ef7379", "warning": "e9bd70", "good": "70d6a0", "crit": "f2ce78", "heal": "70d6a0", "block": "73b9d8", "charge": "b08ae5", "arcane": "b08ae5", "move": "68a9ec", "intent": "e6a974", "victory": "f2ce78" }

func _ready() -> void:
	theme = UIFactory.theme()
	_build_background()
	_build_battle_screen()
	_build_modal_layer()
	state.changed.connect(_queue_refresh)
	state.log_added.connect(_on_log)
	state.unit_hit.connect(_on_unit_hit)
	state.ascension_offered.connect(_on_ascension_offered)
	state.battle_finished.connect(_on_battle_finished)
	_show_main_menu()

func _build_background() -> void:
	var background = ColorRect.new()
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var material = ShaderMaterial.new()
	material.shader = load("res://assets/background.gdshader")
	background.material = material
	background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(background)

func _build_battle_screen() -> void:
	var margin = MarginContainer.new()
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 20)
	margin.add_theme_constant_override("margin_right", 20)
	margin.add_theme_constant_override("margin_top", 14)
	margin.add_theme_constant_override("margin_bottom", 16)
	add_child(margin)
	var root = VBoxContainer.new()
	root.add_theme_constant_override("separation", 10)
	margin.add_child(root)
	_build_topbar(root)
	var content = HBoxContainer.new()
	content.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content.add_theme_constant_override("separation", 12)
	root.add_child(content)
	var left = VBoxContainer.new()
	left.custom_minimum_size.x = 1110
	left.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	left.add_theme_constant_override("separation", 10)
	content.add_child(left)
	_build_enemy_zone(left)
	_build_hero_zone(left)
	_build_hand_zone(left)
	var right = VBoxContainer.new()
	right.custom_minimum_size.x = 404
	right.size_flags_horizontal = Control.SIZE_FILL
	right.add_theme_constant_override("separation", 10)
	content.add_child(right)
	_build_action_panel(right)
	_build_log_panel(right)

func _build_topbar(parent: VBoxContainer) -> void:
	var panel = PanelContainer.new()
	panel.custom_minimum_size.y = 62
	panel.add_theme_stylebox_override("panel", UIFactory.style(Color("101a2c"), Color("35455d"), 9, 1, 10))
	parent.add_child(panel)
	var row = HBoxContainer.new()
	row.add_theme_constant_override("separation", 14)
	panel.add_child(row)
	var brand = UIFactory.label("命刻之战", 25, UIFactory.GOLD)
	row.add_child(brand)
	var version = UIFactory.label("FINAL STORY  /  RULESET 0.5", 11, UIFactory.MUTED)
	version.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(version)
	top_round = UIFactory.label("ROUND 01", 18)
	row.add_child(top_round)
	top_phase = UIFactory.label("  玩家阶段  ", 14, UIFactory.TEAL)
	top_phase.add_theme_stylebox_override("normal", UIFactory.style(Color("15313b"), Color("2b6b70"), 6, 1, 5))
	row.add_child(top_phase)
	row.add_child(UIFactory.label("时空坍缩", 13, UIFactory.MUTED))
	doom_bar = ProgressBar.new()
	doom_bar.custom_minimum_size = Vector2(130, 14)
	doom_bar.max_value = 6
	doom_bar.show_percentage = false
	doom_bar.add_theme_stylebox_override("background", UIFactory.style(Color("09111f"), Color("2d3d53"), 5, 1, 0))
	doom_bar.add_theme_stylebox_override("fill", UIFactory.style(Color("b45d70"), Color("e38a8f"), 5, 0, 0))
	row.add_child(doom_bar)
	doom_label = UIFactory.label("0 / 6", 15, UIFactory.RED)
	row.add_child(doom_label)
	var help = UIFactory.button("规则 · 操作", UIFactory.LINE)
	help.pressed.connect(_show_help)
	row.add_child(help)
	var restart = UIFactory.button("重新挑战", UIFactory.LINE)
	restart.pressed.connect(_restart_battle)
	row.add_child(restart)

func _build_enemy_zone(parent: VBoxContainer) -> void:
	var panel = PanelContainer.new()
	panel.custom_minimum_size.y = 290
	panel.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	parent.add_child(panel)
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation", 8)
	panel.add_child(v)
	var heading = HBoxContainer.new()
	v.add_child(heading)
	heading.add_child(UIFactory.label("敌方阵地  /  CLOCKWORK CORE", 13, UIFactory.MUTED))
	var spacer = Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	heading.add_child(spacer)
	heading.add_child(UIFactory.label("点击敌人切换攻击目标", 12, UIFactory.GOLD))
	var row = HBoxContainer.new()
	row.add_theme_constant_override("separation", 10)
	row.size_flags_vertical = Control.SIZE_EXPAND_FILL
	v.add_child(row)
	enemy_units = HBoxContainer.new()
	enemy_units.custom_minimum_size.x = 442
	enemy_units.add_theme_constant_override("separation", 8)
	row.add_child(enemy_units)
	intent_list = HBoxContainer.new()
	intent_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	intent_list.add_theme_constant_override("separation", 8)
	row.add_child(intent_list)

func _build_hero_zone(parent: VBoxContainer) -> void:
	hero_list = HBoxContainer.new()
	hero_list.custom_minimum_size.y = 154
	hero_list.add_theme_constant_override("separation", 10)
	parent.add_child(hero_list)

func _build_hand_zone(parent: VBoxContainer) -> void:
	var panel = PanelContainer.new()
	panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	panel.custom_minimum_size.y = 270
	parent.add_child(panel)
	var v = VBoxContainer.new()
	panel.add_child(v)
	var heading = HBoxContainer.new()
	v.add_child(heading)
	hand_title = UIFactory.label("手牌", 15, UIFactory.GOLD)
	hand_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	heading.add_child(hand_title)
	heading.add_child(UIFactory.label("选牌 → 选目标 → 打出  ｜  时机牌在敌方阶段使用", 12, UIFactory.MUTED))
	var scroll = ScrollContainer.new()
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	scroll.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	v.add_child(scroll)
	hand_list = HBoxContainer.new()
	hand_list.add_theme_constant_override("separation", 8)
	hand_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.add_child(hand_list)

func _build_action_panel(parent: VBoxContainer) -> void:
	var panel = PanelContainer.new()
	panel.custom_minimum_size.y = 478
	panel.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	parent.add_child(panel)
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation", 6)
	panel.add_child(v)
	var section = HBoxContainer.new()
	v.add_child(section)
	section.add_child(UIFactory.label("行动面板", 15, UIFactory.GOLD))
	var s = Control.new()
	s.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	section.add_child(s)
	section.add_child(UIFactory.label("ACTION", 10, UIFactory.MUTED))
	detail_title = UIFactory.label("选择一张手牌", 21)
	detail_title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	v.add_child(detail_title)
	detail_meta = UIFactory.label("两名角色可自由交替行动", 12, UIFactory.MUTED)
	v.add_child(detail_meta)
	detail_description = RichTextLabel.new()
	detail_description.bbcode_enabled = true
	detail_description.fit_content = false
	detail_description.scroll_active = true
	detail_description.custom_minimum_size.y = 108
	detail_description.size_flags_vertical = Control.SIZE_EXPAND_FILL
	detail_description.add_theme_font_size_override("normal_font_size", 13)
	v.add_child(detail_description)
	var selectors = HBoxContainer.new()
	selectors.add_theme_constant_override("separation", 6)
	v.add_child(selectors)
	target_button = OptionButton.new()
	target_button.add_item("目标：克洛诺斯", 0)
	target_button.add_item("目标：镇压重盾臂", 1)
	target_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	target_button.item_selected.connect(func (index):
		selected_enemy = "boss" if index == 0 else "arm"
		_queue_refresh()
	)
	selectors.add_child(target_button)
	ally_button = OptionButton.new()
	ally_button.add_item("队友：米阔阔", 0)
	ally_button.add_item("队友：维汀", 1)
	ally_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	ally_button.item_selected.connect(func (index): ally_choice = "miku" if index == 0 else "weiting")
	selectors.add_child(ally_button)
	charge_check = CheckButton.new()
	charge_check.text = "消耗1充能，抵扣5MP"
	charge_check.toggled.connect(func (value): use_mp_charge = value; _queue_refresh())
	v.add_child(charge_check)
	lucky_check = CheckButton.new()
	lucky_check.text = "维汀响应：幸运改命（5MP）"
	lucky_check.toggled.connect(func (value): use_lucky = value)
	v.add_child(lucky_check)
	special_charge_check = CheckButton.new()
	special_charge_check.text = "额外消耗1充能：必定暴击并抽1张"
	special_charge_check.toggled.connect(func (value): use_special_charge = value)
	v.add_child(special_charge_check)
	var card_actions = HBoxContainer.new()
	card_actions.add_theme_constant_override("separation", 6)
	v.add_child(card_actions)
	play_button = UIFactory.button("打出卡牌", UIFactory.TEAL)
	play_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	play_button.pressed.connect(_play_selected)
	card_actions.add_child(play_button)
	attach_button = UIFactory.button("贴为充能", UIFactory.PURPLE)
	attach_button.pressed.connect(_attach_selected)
	card_actions.add_child(attach_button)
	var line = HSeparator.new()
	v.add_child(line)
	var grid = GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 6)
	grid.add_theme_constant_override("v_separation", 6)
	v.add_child(grid)
	move_button = UIFactory.button("前后排移动 · 1AP")
	move_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	move_button.pressed.connect(func (): state.move_hero(state.active_hero))
	grid.add_child(move_button)
	ap_button = UIFactory.button("充能转AP · +1")
	ap_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	ap_button.pressed.connect(func (): state.charge_to_ap(state.active_hero))
	grid.add_child(ap_button)
	scroll_button = UIFactory.button("关闭卷轴 · 0AP")
	scroll_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll_button.pressed.connect(func (): state.toggle_scroll())
	grid.add_child(scroll_button)
	interfere_button = UIFactory.button("以太干扰 · 1AP+1充能")
	interfere_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	interfere_button.pressed.connect(func (): state.interfere())
	grid.add_child(interfere_button)
	revive_button = UIFactory.button("复苏队友 · 2AP+1牌")
	revive_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	revive_button.pressed.connect(_revive_ally)
	grid.add_child(revive_button)
	catalyst_button = UIFactory.button("维汀响应：应急催化剂", UIFactory.PURPLE)
	catalyst_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	catalyst_button.pressed.connect(_show_catalyst_choice)
	grid.add_child(catalyst_button)
	var discard_button = UIFactory.button("查看弃牌与充能")
	discard_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	discard_button.pressed.connect(_show_piles)
	grid.add_child(discard_button)
	end_button = UIFactory.button("结束全队行动  →  敌人阶段", UIFactory.RED)
	end_button.custom_minimum_size.y = 38
	end_button.pressed.connect(_end_player_phase)
	v.add_child(end_button)

func _build_log_panel(parent: VBoxContainer) -> void:
	var panel = PanelContainer.new()
	panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	parent.add_child(panel)
	var v = VBoxContainer.new()
	panel.add_child(v)
	v.add_child(UIFactory.label("战斗记录  /  COMBAT LOG", 12, UIFactory.MUTED))
	log_box = RichTextLabel.new()
	log_box.bbcode_enabled = true
	log_box.scroll_active = true
	log_box.scroll_following = true
	log_box.size_flags_vertical = Control.SIZE_EXPAND_FILL
	log_box.add_theme_font_size_override("normal_font_size", 12)
	v.add_child(log_box)

func _build_modal_layer() -> void:
	modal_layer = CanvasLayer.new()
	modal_layer.layer = 10
	add_child(modal_layer)

func _start_battle(seed_value: int = -1) -> void:
	battle_serial += 1
	_close_modal()
	started = true
	busy = false
	selected_hand = -1
	selected_enemy = "boss"
	use_mp_charge = false
	use_lucky = false
	use_special_charge = false
	log_box.clear()
	state = BattleState.new()
	state.changed.connect(_queue_refresh)
	state.log_added.connect(_on_log)
	state.unit_hit.connect(_on_unit_hit)
	state.ascension_offered.connect(_on_ascension_offered)
	state.battle_finished.connect(_on_battle_finished)
	state.start_battle(seed_value)
	_refresh()

func _restart_battle() -> void:
	_start_battle()

func _show_main_menu() -> void:
	var content = VBoxContainer.new()
	content.alignment = BoxContainer.ALIGNMENT_CENTER
	content.add_theme_constant_override("separation", 14)
	var eyebrow = UIFactory.label("F A B U L A   U L T I M A   /   C A R D   E N C O U N T E R", 13, UIFactory.TEAL)
	eyebrow.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	content.add_child(eyebrow)
	var title = UIFactory.label("命刻之战", 58, UIFactory.GOLD)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	content.add_child(title)
	var sub = UIFactory.label("最终物语 · 打牌战斗规则 v0.5", 19, UIFactory.TEXT)
	sub.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	content.add_child(sub)
	content.add_child(UIFactory.spacer(8))
	var desc = UIFactory.label("米阔阔 × 维汀  对阵  裂界发条巨像 克洛诺斯-IV型", 17, Color("bac9d9"))
	desc.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	content.add_child(desc)
	var feature = UIFactory.label("双角色协同  ·  双职业升变  ·  部位破坏  ·  半可见意图  ·  命刻拆弹", 14, UIFactory.MUTED)
	feature.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	content.add_child(feature)
	content.add_child(UIFactory.spacer(14))
	var start = UIFactory.button("开 始 挑 战", UIFactory.GOLD)
	start.custom_minimum_size = Vector2(300, 50)
	start.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	start.add_theme_font_size_override("font_size", 21)
	start.pressed.connect(func (): _start_battle())
	content.add_child(start)
	var rules = UIFactory.button("查看规则与操作", UIFactory.LINE)
	rules.custom_minimum_size = Vector2(300, 42)
	rules.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	rules.pressed.connect(_show_help)
	content.add_child(rules)
	content.add_child(UIFactory.spacer(10))
	var foot = UIFactory.label("标准有预备战斗 · 双人基准 · 本地单机 · 随机骰点", 12, UIFactory.MUTED)
	foot.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	content.add_child(foot)
	_show_modal(content, Vector2(850, 610), true)

func _show_help() -> void:
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation", 10)
	v.add_child(UIFactory.label("规则速查与操作", 28, UIFactory.GOLD))
	var text = RichTextLabel.new()
	text.bbcode_enabled = true
	text.custom_minimum_size = Vector2(820, 490)
	text.size_flags_vertical = Control.SIZE_EXPAND_FILL
	text.add_theme_font_size_override("normal_font_size", 15)
	text.text = "[color=#d8b96f][font_size=20]目标[/font_size][/color]\n击败克洛诺斯本体即获胜。重盾臂存活时，每轮敌人阶段先攻击前排；击破后本体前移、双盾清空、暴击抵抗降至6。\n\n[color=#58d0c5][font_size=20]你的回合[/font_size][/color]\n• 点击角色卡切换当前角色；两人各有3AP，可任意交替行动。\n• 点击手牌，再选择敌人与辅助选项，最后点“打出卡牌”。点击“贴为充能”可将选中手牌贴给指定队友。\n• 常规移动消耗1AP；1充能可换1AP，或勾选后抵扣本次5MP。\n• “以太干扰”消耗当前角色1AP与1充能，使命刻-1；两轮内对本体2次弱点伤害或1次超暴击也会自动逆转，全队每轮限1次。\n• 时机牌不能主动打出；敌方每次意图结算前会打开响应窗口，可为自己或队友选择防御牌。\n\n[color=#68a9ec][font_size=20]骰点与伤害[/font_size][/color]\n单色暴击：属性骰≥目标抵抗，伤害×1.5。双色取高骰判普通暴击；双骰均达标，或原始双骰同点且≥4，触发超暴击×2。\n幻影夜行普通暴击×1.75；后排鹰眼绝境超暴击×2.5。幸运改命会在骰点后把一颗改为7。\n弱点×2、抗性÷2、免疫0。物理/魔法护盾分别抵扣；穿透无视自动盾，但时机牌新增的护盾仍有效。\n\n[color=#b08ae5][font_size=20]升变[/font_size][/color]\n轮始出现升变选择。进入后暂存原手牌与主卡组，改用10张形态牌，持续2轮；首轮抽5，次轮抽剩余牌，然后恢复原卡组。\n第3轮提供保底升变选择。打出早期“幻影夜行/星穹坍缩”可展示洗回并抽牌推进条件。\n\n[color=#ef7379][font_size=20]命刻与倒地[/font_size][/color]\n命刻每轮末+1。到6后，下轮第1意图替换为“归零坍缩”，随后首领过热1轮：抵抗4、受伤×1.5。\n角色HP为0会倒地。存活队友可花2AP与1张手牌使其以1/4HP复苏；全队倒地则失败。\n\n[color=#9bacc2]电子化补全：主卡组抽空时洗回弃牌堆；轮末超7张自动从手牌末端弃置。具体卡面MP优先于规则附录的泛称。[/color]"























	v.add_child(text)
	var close = UIFactory.button("返回", UIFactory.GOLD)
	close.custom_minimum_size.y = 40
	close.pressed.connect(func ():
		_close_modal()
		if not started: _show_main_menu()
	)
	v.add_child(close)
	_show_modal(v, Vector2(930, 690), true)

func _show_modal(content: Control, modal_size: Vector2, dim: bool = true) -> void:
	_close_modal()
	modal_open = true
	var root = Control.new()
	root.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	modal_layer.add_child(root)
	if dim:
		var shade = ColorRect.new()
		shade.color = Color(0.015, 0.025, 0.045, 0.82)
		root.add_child(shade)
	var center = CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	root.add_child(center)
	var frame = PanelContainer.new()
	frame.custom_minimum_size = modal_size
	frame.add_theme_stylebox_override("panel", UIFactory.style(Color("0f1a2c"), UIFactory.GOLD, 14, 1, 24))
	center.add_child(frame)
	frame.add_child(content)

func _close_modal() -> void:
	modal_open = false
	if modal_layer == null: return
	for child in modal_layer.get_children():
		modal_layer.remove_child(child)
		child.queue_free()

func _queue_refresh() -> void:
	if refresh_queued or not is_inside_tree(): return
	refresh_queued = true
	call_deferred("_refresh")

func _refresh() -> void:
	refresh_queued = false
	if not started or not is_instance_valid(hand_list): return
	var h = state.heroes[state.active_hero]
	top_round.text = "ROUND %02d" % state.round_number
	top_phase.text = { "player": "  玩家阶段  ", "enemy": "  敌人阶段  ", "round_start": "  升变窗口  ", "victory": "  战斗胜利  ", "defeat": "  战斗失败  " }.get(state.phase, "  准备中  ")
	doom_bar.value = state.doom
	doom_label.text = "%d / 6" % state.doom
	hand_title.text = "%s  /  手牌 %d  ·  牌库 %d  ·  弃牌 %d" % [h.name, h.hand.size(), h.deck.size(), h.discard.size()]
	_refresh_enemies()
	_refresh_heroes()
	_refresh_hand()
	_refresh_actions()
	_update_target_buttons()

func _refresh_enemies() -> void:
	UIFactory.clear(enemy_units)
	UIFactory.clear(intent_list)
	unit_nodes.clear()
	for enemy_id in ["boss", "arm"]:
		var e = state.enemies[enemy_id]
		var panel = PanelContainer.new()
		panel.custom_minimum_size.x = 214
		panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
		var border = UIFactory.GOLD if selected_enemy == enemy_id and e.alive else (UIFactory.RED if enemy_id == "boss" else UIFactory.LINE)
		panel.add_theme_stylebox_override("panel", UIFactory.style(Color("142035"), border, 8, 2 if selected_enemy == enemy_id else 1, 9))
		enemy_units.add_child(panel)
		unit_nodes[enemy_id] = panel
		var button = Button.new()
		button.flat = true
		button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		button.pressed.connect(func (): selected_enemy = enemy_id; _queue_refresh())
		panel.add_child(button)
		var v = VBoxContainer.new()
		v.add_theme_constant_override("separation", 3)
		button.add_child(v)
		var avatar = UnitAvatar.new()
		avatar.unit_kind = enemy_id
		avatar.tint = UIFactory.RED if enemy_id == "boss" else UIFactory.GOLD
		avatar.custom_minimum_size = Vector2(70, 72)
		avatar.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		v.add_child(avatar)
		var title = UIFactory.label(e.name, 16 if enemy_id == "boss" else 14)
		title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		v.add_child(title)
		var hp = UIFactory.label("HP  %d / %d" % [e.hp, e.max_hp], 14, UIFactory.TEXT)
		hp.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		v.add_child(hp)
		var hp_bar = _bar(e.hp, e.max_hp, UIFactory.RED)
		v.add_child(hp_bar)
		var status = UIFactory.label("物盾 %d   魔盾 %d   抵抗 %d" % [e.p_shield, e.m_shield, 4 if enemy_id == "boss" and state.overheat_this_round else e.crit_line], 11, UIFactory.BLUE)
		status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		v.add_child(status)
		var row_text = "前排" if e.row == "front" else "后排"
		var extra = "%s · 弱%s" % [row_text, "雷/暗" if enemy_id == "boss" else "风"]
		if not e.alive: extra = "已摧毁"
		elif enemy_id == "boss" and state.boss_phase_two: extra += " · 过载"
		var sub = UIFactory.label(extra, 11, UIFactory.MUTED)
		sub.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		v.add_child(sub)
	for i in range(state.intents.size()):
		var data = state.get_intent_visible_data(i)
		var panel = PanelContainer.new()
		panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		panel.add_theme_stylebox_override("panel", UIFactory.style(Color("111d30") if not data.hidden else Color("18182b"), Color("6a536f") if data.hidden else Color("584b43"), 7, 1, 9))
		intent_list.add_child(panel)
		var v = VBoxContainer.new()
		v.add_theme_constant_override("separation", 6)
		panel.add_child(v)
		var order = UIFactory.label("0%d  /  %s" % [i + 1, "暗置" if data.hidden else "明置"], 10, UIFactory.PURPLE if data.hidden else UIFactory.MUTED)
		v.add_child(order)
		var title = UIFactory.label(data.name, 16, UIFactory.PURPLE if data.hidden else UIFactory.GOLD)
		title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		v.add_child(title)
		var desc = UIFactory.label(data.description, 11, Color("b2bfd0"))
		desc.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		desc.size_flags_vertical = Control.SIZE_EXPAND_FILL
		v.add_child(desc)
		if data.get("locked", "") != "" and not data.hidden:
			var locked = data.locked
			if state.heroes.has(locked): v.add_child(UIFactory.label("锁定：" + state.heroes[locked].name, 11, UIFactory.RED))

func _refresh_heroes() -> void:
	UIFactory.clear(hero_list)
	for hero_id in state.HERO_IDS:
		var h = state.heroes[hero_id]
		var panel = PanelContainer.new()
		panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		var border = UIFactory.TEAL if state.active_hero == hero_id else UIFactory.LINE
		if h.downed: border = UIFactory.RED
		panel.add_theme_stylebox_override("panel", UIFactory.style(Color("122237") if state.active_hero == hero_id else Color("111b2d"), border, 9, 2 if state.active_hero == hero_id else 1, 9))
		hero_list.add_child(panel)
		unit_nodes[hero_id] = panel
		var button = Button.new()
		button.flat = true
		button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		button.pressed.connect(func ():
			state.active_hero = hero_id
			selected_hand = -1
			ally_choice = hero_id
			_queue_refresh()
		)
		panel.add_child(button)
		var row = HBoxContainer.new()
		row.add_theme_constant_override("separation", 10)
		button.add_child(row)
		var avatar = UnitAvatar.new()
		avatar.unit_kind = hero_id
		avatar.tint = UIFactory.GREEN if hero_id == "miku" else UIFactory.PURPLE
		avatar.custom_minimum_size = Vector2(90, 106)
		row.add_child(avatar)
		var v = VBoxContainer.new()
		v.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		v.add_theme_constant_override("separation", 3)
		row.add_child(v)
		var name_row = HBoxContainer.new()
		v.add_child(name_row)
		name_row.add_child(UIFactory.label(h.name, 21, UIFactory.TEXT))
		var ap = UIFactory.label("  %d AP  " % h.ap, 16, UIFactory.GOLD)
		name_row.add_child(ap)
		var sp = Control.new()
		sp.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_row.add_child(sp)
		name_row.add_child(UIFactory.label("前排" if h.row == "front" else "后排", 13, UIFactory.TEAL))
		v.add_child(UIFactory.label(h.subtitle, 11, UIFactory.MUTED))
		var stats = HBoxContainer.new()
		stats.add_theme_constant_override("separation", 16)
		v.add_child(stats)
		stats.add_child(UIFactory.label("HP %d/%d" % [h.hp, h.max_hp], 14, UIFactory.RED if h.hp <= int(h.max_hp / 2) else UIFactory.TEXT))
		stats.add_child(UIFactory.label("MP %d/%d" % [h.mp, h.max_mp], 14, UIFactory.BLUE))
		stats.add_child(UIFactory.label("盾 %d / %d" % [h.p_shield, h.m_shield], 12, UIFactory.TEAL))
		var hp_bar = _bar(h.hp, h.max_hp, UIFactory.RED)
		v.add_child(hp_bar)
		var form_name = BattleState.FORM_NAMES.get(h.form, "常态")
		var info = "%s  ·  充能 %d/%d  ·  持续 %s" % [form_name, h.charges.size(), 2 + h.charge_cap_bonus, h.ongoing if h.ongoing == "" else CardDatabase.get_card(h.ongoing).name]
		if h.downed: info = "倒地 · 等待队友复苏"
		v.add_child(UIFactory.label(info, 11, UIFactory.PURPLE if h.form != "" else UIFactory.MUTED))

func _refresh_hand() -> void:
	UIFactory.clear(hand_list)
	var h = state.heroes[state.active_hero]
	if h.hand.is_empty():
		var empty = UIFactory.label("当前没有手牌。可结束行动，或使用角色的其他能力。", 15, UIFactory.MUTED)
		hand_list.add_child(empty)
		return
	for i in range(h.hand.size()):
		var card = CardDatabase.get_card(h.hand[i])
		var options = _current_options()
		var cost = state.get_card_cost(state.active_hero, card, options)
		var playable = state.validate_play(state.active_hero, i, options) == "" and not busy
		var view = CardView.new()
		view.setup(card, i, cost, selected_hand == i, playable)
		view.clicked.connect(func (index):
			selected_hand = index
			use_mp_charge = false
			use_lucky = false
			use_special_charge = false
			_queue_refresh()
		)
		hand_list.add_child(view)

func _refresh_actions() -> void:
	var player_phase = state.phase == "player" and not busy
	var h = state.heroes[state.active_hero]
	move_button.disabled = not player_phase or h.ap < 1 or h.downed
	ap_button.disabled = not player_phase or h.charges.is_empty() or h.downed
	interfere_button.disabled = not player_phase or h.ap < 1 or h.charges.is_empty() or state.doom_reversed or state.doom <= 0
	end_button.disabled = not player_phase
	var scroll_active = state.heroes.weiting.scroll_active
	scroll_button.disabled = not player_phase or state.active_hero != "weiting" or h.downed or (not scroll_active and (h.ap < 1 or h.mp < 5))
	scroll_button.text = "关闭卷轴 · 0AP" if scroll_active else "激活卷轴 · 1AP+5MP"
	var other = "weiting" if state.active_hero == "miku" else "miku"
	revive_button.disabled = not player_phase or not state.heroes[other].downed or h.ap < 2 or h.hand.is_empty()
	catalyst_button.visible = state.active_hero == "miku"
	catalyst_button.disabled = not player_phase or state.heroes.weiting.downed or state.find_hand_card("weiting", "应急催化剂") < 0 or state.heroes.weiting.hand.size() < 2 or h.downed
	play_button.disabled = true
	attach_button.disabled = not player_phase or selected_hand < 0 or selected_hand >= h.hand.size() or h.charged_this_round >= (2 if h.form == "alchemy" else 1)
	charge_check.disabled = true
	lucky_check.disabled = true
	charge_check.set_pressed_no_signal(use_mp_charge)
	lucky_check.set_pressed_no_signal(use_lucky)
	special_charge_check.set_pressed_no_signal(use_special_charge)
	special_charge_check.visible = false
	if selected_hand < 0 or selected_hand >= h.hand.size():
		detail_title.text = "选择一张手牌"
		detail_meta.text = "%s · %d AP · %s" % [h.name, h.ap, "前排" if h.row == "front" else "后排"]
		detail_description.text = "[color=#8fa2ba]两名角色可自由交替行动。点击手牌查看完整效果；点击敌方单位切换攻击目标。[/color]"
		return
	var card = CardDatabase.get_card(h.hand[selected_hand])
	var options = _current_options()
	var cost = state.get_card_cost(state.active_hero, card, options)
	detail_title.text = card.name
	detail_meta.text = "%d AP  /  %d MP    ·    %s" % [cost.ap, cost.mp, " / ".join(card.get("colors", []))]
	detail_description.text = "[color=#e8edf4]%s[/color]" % card.description
	var error = state.validate_play(state.active_hero, selected_hand, options)
	play_button.disabled = not player_phase or error != ""
	play_button.tooltip_text = error
	play_button.text = "打出卡牌" if error == "" else error
	var uncharged_options = options.duplicate()
	uncharged_options["use_mp_charge"] = false
	var uncharged_mp = int(state.get_card_cost(state.active_hero, card, uncharged_options).mp)
	charge_check.disabled = not player_phase or h.charges.is_empty() or uncharged_mp <= int(cost.mp) or card.get("kind", "") in ["reaction", "ascension"]
	lucky_check.disabled = not player_phase or not card.get("crit", false) or state.find_hand_card("weiting", "幸运改命") < 0 or state.heroes.weiting.mp < 5
	special_charge_check.visible = card.get("name", "") == "暗夜剔骨"
	special_charge_check.disabled = not player_phase or h.charges.is_empty()

func _update_target_buttons() -> void:
	target_button.set_item_disabled(0, not state.enemies.boss.alive)
	target_button.set_item_disabled(1, not state.enemies.arm.alive)
	target_button.select(0 if selected_enemy == "boss" else 1)
	ally_button.select(0 if ally_choice == "miku" else 1)

func _current_options() -> Dictionary:
	return { "target_id": selected_enemy, "ally_id": ally_choice, "use_mp_charge": use_mp_charge, "use_lucky": use_lucky, "use_special_charge": use_special_charge, "alchemy_mode": alchemy_mode }

func _play_selected() -> void:
	var hero_id = state.active_hero
	if selected_hand < 0: return
	var index = selected_hand
	var options = _current_options()
	var card = CardDatabase.get_card(state.heroes[hero_id].hand[index])
	if card.get("name", "") == "调合·万能灵药":
		_show_alchemy_choice(hero_id, index, options)
		return
	selected_hand = -1
	var result = state.play_card(hero_id, index, options)
	if not result.get("ok", false):
		_toast(result.get("error", "无法出牌"))
	use_mp_charge = false
	use_lucky = false
	use_special_charge = false
	_queue_refresh()

func _show_catalyst_choice() -> void:
	var catalyst_index = state.find_hand_card("weiting", "应急催化剂")
	if catalyst_index < 0: return
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation", 12)
	v.add_child(UIFactory.label("维汀响应 · 应急催化剂", 27, UIFactory.PURPLE))
	v.add_child(UIFactory.label("在米阔阔行动时响应：选择一张维汀的手牌贴入米阔阔充能区，再选择一项增益。", 15, UIFactory.TEXT))
	var picker = OptionButton.new()
	for i in range(state.heroes.weiting.hand.size()):
		if i == catalyst_index: continue
		var card = CardDatabase.get_card(state.heroes.weiting.hand[i])
		var adjusted = i - (1 if i > catalyst_index else 0)
		var item = picker.item_count
		picker.add_item(card.name, item)
		picker.set_item_metadata(item, adjusted)
	picker.custom_minimum_size.y = 40
	v.add_child(picker)
	var shield = UIFactory.button("贴充能 + 4点物理/魔法护盾", UIFactory.TEAL)
	shield.custom_minimum_size.y = 46
	shield.pressed.connect(_confirm_catalyst.bind("shield", picker))
	v.add_child(shield)
	var crit = UIFactory.button("贴充能 + 米阔阔下一次暴击掷骰+1", UIFactory.GOLD)
	crit.custom_minimum_size.y = 46
	crit.pressed.connect(_confirm_catalyst.bind("crit", picker))
	v.add_child(crit)
	var cancel = UIFactory.button("取消", UIFactory.LINE)
	cancel.pressed.connect(_close_modal)
	v.add_child(cancel)
	_show_modal(v, Vector2(650, 440), true)

func _confirm_catalyst(mode: String, picker: OptionButton) -> void:
	var catalyst_index = state.find_hand_card("weiting", "应急催化剂")
	if catalyst_index < 0 or picker.item_count == 0: return
	var attach_index = int(picker.get_item_metadata(picker.selected))
	var context = { "id": "player_catalyst", "name": "应急催化剂", "source": "boss", "reaction": { } }
	state.resolver.play_reaction(state, { "hero_id": "weiting", "hand_index": catalyst_index, "target_id": "miku", "attach_index": attach_index, "mode": mode }, context)
	_close_modal()
	_queue_refresh()

func _show_alchemy_choice(hero_id: String, index: int, options: Dictionary) -> void:
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation", 12)
	v.add_child(UIFactory.label("调合 · 万能灵药", 28, UIFactory.GOLD))
	var target_name = state.heroes[options.get("ally_id", hero_id)].name
	var desc = UIFactory.label("目标：%s。选择本次炼金效果；双调合需要消耗2个充能。" % target_name, 15, UIFactory.TEXT)
	desc.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	v.add_child(desc)
	var life = UIFactory.button("生命灵药 · 1充能\n回复10HP与10MP", UIFactory.GREEN)
	life.custom_minimum_size.y = 72
	life.pressed.connect(_confirm_alchemy.bind(hero_id, index, options, "life"))
	v.add_child(life)
	var element = UIFactory.button("元素涂油 · 1充能\n本轮伤害+3，自动选择克制当前敌人的元素", UIFactory.BLUE)
	element.custom_minimum_size.y = 72
	element.pressed.connect(_confirm_alchemy.bind(hero_id, index, options, "element"))
	v.add_child(element)
	var both = UIFactory.button("双调合 · 2充能\n生命灵药与元素涂油同时生效", UIFactory.PURPLE)
	both.custom_minimum_size.y = 72
	both.disabled = state.heroes[hero_id].charges.size() < 2
	both.pressed.connect(_confirm_alchemy.bind(hero_id, index, options, "both"))
	v.add_child(both)
	var cancel = UIFactory.button("取消", UIFactory.LINE)
	cancel.pressed.connect(_close_modal)
	v.add_child(cancel)
	_show_modal(v, Vector2(650, 520), true)

func _confirm_alchemy(hero_id: String, index: int, options: Dictionary, mode: String) -> void:
	options["alchemy_mode"] = mode
	options["element"] = "wind" if selected_enemy == "arm" else "lightning"
	selected_hand = -1
	_close_modal()
	var result = state.play_card(hero_id, index, options)
	if not result.get("ok", false): _toast(result.get("error", "无法调合"))
	use_mp_charge = false
	use_lucky = false
	use_special_charge = false
	_queue_refresh()

func _attach_selected() -> void:
	if selected_hand < 0: return
	var index = selected_hand
	selected_hand = -1
	state.attach_from_hand(state.active_hero, index, ally_choice)

func _revive_ally() -> void:
	var actor = state.active_hero
	var target = "weiting" if actor == "miku" else "miku"
	state.revive_ally(actor, target, selected_hand)

func _end_player_phase() -> void:
	if busy or state.phase != "player": return
	busy = true
	selected_hand = -1
	state.end_player_turn()
	_run_enemy_phase()

func _run_enemy_phase() -> void:
	var serial = battle_serial
	await get_tree().process_frame
	while serial == battle_serial and state.phase == "enemy":
		var context = state.prepare_next_enemy_action()
		if context.get("done", false): break
		await get_tree().create_timer(0.28).timeout
		if serial != battle_serial: return
		if state.phase in ["victory", "defeat"]: break
		var choice = await _request_reaction(context)
		if serial != battle_serial: return
		if state.phase != "enemy": break
		state.resolve_enemy_action(choice.get("reaction", { }), choice.get("split", false))
		await get_tree().create_timer(0.42).timeout
		if serial != battle_serial: return
	if serial != battle_serial: return
	busy = false
	_queue_refresh()

func _request_reaction(context: Dictionary) -> Dictionary:
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation", 10)
	var category = UIFactory.label("敌人阶段  /  意图 %d" % (state.enemy_index + 1), 12, UIFactory.RED)
	v.add_child(category)
	v.add_child(UIFactory.label(context.get("name", "敌方行动"), 30, UIFactory.GOLD))
	var desc = UIFactory.label(context.get("description", ""), 15, UIFactory.TEXT)
	desc.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc.custom_minimum_size.y = 58
	v.add_child(desc)
	var target_names: Array = []
	for id in context.get("targets", []):
		if state.heroes.has(id): target_names.append(state.heroes[id].name + "（" + ("前排" if state.heroes[id].row == "front" else "后排") + "）")
	v.add_child(UIFactory.label("目标：" + ("、".join(target_names) if not target_names.is_empty() else "首领自身 / 无合法目标"), 14, UIFactory.RED))
	v.add_child(HSeparator.new())
	v.add_child(UIFactory.label("选择一张时机牌响应，或直接承受结算", 15, UIFactory.TEAL))
	var reactions = state.legal_reactions(context)
	var scroll = ScrollContainer.new()
	scroll.custom_minimum_size = Vector2(740, 220)
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	v.add_child(scroll)
	var list = VBoxContainer.new()
	list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	list.add_theme_constant_override("separation", 6)
	scroll.add_child(list)
	if reactions.is_empty():
		list.add_child(UIFactory.label("当前没有可用的防御时机牌。", 15, UIFactory.MUTED))
	for reaction in reactions:
		var card = reaction.card
		var mode_text = ""
		if card.name == "应急催化剂": mode_text = "（双盾）" if reaction.get("mode", "shield") == "shield" else "（下次暴击+1）"
		var button = UIFactory.button("%s%s  ·  %s → %s  ·  %dMP\n%s" % [card.name, mode_text, state.heroes[reaction.hero_id].name, state.heroes[reaction.target_id].name, card.mp, card.description], UIFactory.TEAL)
		button.custom_minimum_size.y = 64
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.pressed.connect(func ():
			_close_modal()
			reaction_choice_made.emit({ "reaction": reaction })
		)
		list.add_child(button)
	var buttons = HBoxContainer.new()
	buttons.add_theme_constant_override("separation", 8)
	v.add_child(buttons)
	var pass_button = UIFactory.button("不响应，继续结算", UIFactory.RED)
	pass_button.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	pass_button.pressed.connect(func ():
		_close_modal()
		reaction_choice_made.emit({ })
	)
	buttons.add_child(pass_button)
	if context.get("id", "") == "causal_erasure" and context.get("targets", []).size() == 1 and state._alive_heroes().size() > 1:
		var split = UIFactory.button("分摊援护：两人各12", UIFactory.GOLD)
		split.pressed.connect(func ():
			_close_modal()
			reaction_choice_made.emit({ "split": true })
		)
		buttons.add_child(split)
	_show_modal(v, Vector2(830, 570), true)
	var choice = await reaction_choice_made
	return choice

func _on_ascension_offered(_hero_id: String, _offers: Array) -> void:
	if not started: return
	call_deferred("_show_pending_ascension")

func _show_pending_ascension() -> void:
	if not started or modal_open or state.phase != "round_start" or state.ascension_offers.is_empty(): return
	var hero_id = state.ascension_offers.keys()[0]
	_show_ascension(hero_id, state.ascension_offers[hero_id])

func _show_ascension(hero_id: String, offers: Array) -> void:
	if state.phase != "round_start" or not state.ascension_offers.has(hero_id): return
	var h = state.heroes[hero_id]
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation", 12)
	v.add_child(UIFactory.label("轮始 · 职业升变", 13, UIFactory.PURPLE))
	v.add_child(UIFactory.label(h.name + "，选择爆发形态", 29, UIFactory.GOLD))
	var desc = UIFactory.label("形态持续2轮。暂存当前手牌与主卡组，换入10张形态牌；持续区与充能区保留。", 15, UIFactory.TEXT)
	desc.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	v.add_child(desc)
	for form_id in offers:
		var form_card = ""
		for card_id in CardDatabase.build_main_deck(hero_id):
			var c = CardDatabase.get_card(card_id)
			if c.get("form_id", "") == form_id:
				form_card = c.description
				break
		var button = UIFactory.button("发动【%s】\n%s" % [BattleState.FORM_NAMES[form_id], form_card], UIFactory.PURPLE)
		button.custom_minimum_size.y = 125
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.pressed.connect(func ():
			_close_modal()
			state.choose_ascension(hero_id, form_id)
			call_deferred("_show_pending_ascension")
		)
		v.add_child(button)
	if state.round_number < 3:
		var skip = UIFactory.button("暂不升变，保留主卡组", UIFactory.LINE)
		skip.pressed.connect(func ():
			_close_modal()
			state.choose_ascension(hero_id, "")
			call_deferred("_show_pending_ascension")
		)
		v.add_child(skip)
	else:
		v.add_child(UIFactory.label("第3轮保底：请选择一张尚未完成的升变牌。", 13, UIFactory.MUTED))
	_show_modal(v, Vector2(850, 560 if offers.size() == 1 else 700), true)

func _show_piles() -> void:
	var h = state.heroes[state.active_hero]
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation", 12)
	v.add_child(UIFactory.label(h.name + " · 战场区域", 27, UIFactory.GOLD))
	var text = RichTextLabel.new()
	text.bbcode_enabled = true
	text.custom_minimum_size = Vector2(660, 420)
	var discard_names: Array = []
	for id in h.discard: discard_names.append(CardDatabase.get_card(id).name)
	var charge_names: Array = []
	for charge in h.charges: charge_names.append(charge.name)
	text.text = "[color=#b08ae5][font_size=20]充能区（%d/%d）[/font_size][/color]\n%s\n\n[color=#68a9ec][font_size=20]弃牌堆（%d）[/font_size][/color]\n%s\n\n[color=#8fa2ba]持续区：%s\n牌库：%d张　当前形态：%s\n\n消耗充能后，卡牌回到原持有者弃牌堆；充能区满时，最早贴入的牌被顶出。[/color]" % [h.charges.size(), 2 + h.charge_cap_bonus, "、".join(charge_names) if not charge_names.is_empty() else "空", h.discard.size(), "、".join(discard_names) if not discard_names.is_empty() else "空", h.ongoing if h.ongoing == "" else CardDatabase.get_card(h.ongoing).name, h.deck.size(), BattleState.FORM_NAMES.get(h.form, "常态")]
	v.add_child(text)
	var close = UIFactory.button("返回战斗", UIFactory.GOLD)
	close.pressed.connect(_close_modal)
	v.add_child(close)
	_show_modal(v, Vector2(760, 570), true)

func _on_battle_finished(victory: bool) -> void:
	call_deferred("_show_result", victory)

func _show_result(victory: bool) -> void:
	var v = VBoxContainer.new()
	v.alignment = BoxContainer.ALIGNMENT_CENTER
	v.add_theme_constant_override("separation", 16)
	var title = UIFactory.label("核心停转 · 战斗胜利" if victory else "小队倒地 · 挑战失败", 39, UIFactory.GOLD if victory else UIFactory.RED)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	v.add_child(title)
	var boss = state.enemies.boss
	var summary = UIFactory.label("坚持%d轮  ·  首领剩余HP %d  ·  米阔阔 %dHP  ·  维汀 %dHP" % [state.round_number, boss.hp, state.heroes.miku.hp, state.heroes.weiting.hp], 17, UIFactory.TEXT)
	summary.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	v.add_child(summary)
	var tip = UIFactory.label("命刻已经改写。再次挑战会重新洗切卡组与骰点。" if victory else "提示：优先用翠风箭附击破重盾臂；看清死刑意图后及时移动，并保留防御时机牌。", 14, UIFactory.MUTED)
	tip.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	v.add_child(tip)
	var again = UIFactory.button("再次挑战", UIFactory.GOLD)
	again.custom_minimum_size = Vector2(280, 46)
	again.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	again.pressed.connect(_restart_battle)
	v.add_child(again)
	var menu = UIFactory.button("返回标题", UIFactory.LINE)
	menu.custom_minimum_size = Vector2(280, 42)
	menu.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	menu.pressed.connect(func (): started = false; _show_main_menu())
	v.add_child(menu)
	_show_modal(v, Vector2(720, 460), true)

func _on_log(text: String, tone: String) -> void:
	if log_box == null: return
	var color = COLOR_MAP.get(tone, "b7c4d5")
	log_box.append_text("[color=#%s]%s[/color]\n" % [color, text])

func _on_unit_hit(unit_id: String, amount: int, element: String) -> void:
	if not unit_nodes.has(unit_id) or not is_instance_valid(unit_nodes[unit_id]): return
	var node = unit_nodes[unit_id]
	var original = node.modulate
	var tween = node.create_tween()
	tween.tween_property(node, "modulate", Color(1.0, 0.55, 0.58), 0.07)
	tween.tween_property(node, "modulate", original, 0.22)
	var floating = UIFactory.label("-%d %s" % [amount, BattleState.ELEMENT_NAMES.get(element, element)], 23, UIFactory.RED if amount > 0 else UIFactory.BLUE)
	floating.position = Vector2(30, 15)
	floating.z_index = 20
	node.add_child(floating)
	var float_tween = floating.create_tween().set_parallel(true)
	float_tween.tween_property(floating, "position:y", -24.0, 0.65)
	float_tween.tween_property(floating, "modulate:a", 0.0, 0.65)
	float_tween.chain().tween_callback(floating.queue_free)

func _toast(text: String) -> void:
	var v = VBoxContainer.new()
	v.add_child(UIFactory.label(text, 22, UIFactory.RED))
	var close = UIFactory.button("知道了")
	close.pressed.connect(_close_modal)
	v.add_child(close)
	_show_modal(v, Vector2(460, 180), true)

func _bar(value: int, maximum: int, color: Color) -> ProgressBar:
	var bar = ProgressBar.new()
	bar.custom_minimum_size.y = 8
	bar.max_value = maximum
	bar.value = value
	bar.show_percentage = false
	bar.add_theme_stylebox_override("background", UIFactory.style(Color("09111e"), Color("09111e"), 3, 0, 0))
	bar.add_theme_stylebox_override("fill", UIFactory.style(color, color, 3, 0, 0))
	return bar
