class_name CardView
extends PanelContainer

signal clicked(hand_index: int)

var hand_index: int = -1
var base_style: StyleBoxFlat
var hover_style: StyleBoxFlat

func setup(card: Dictionary, index: int, cost: Dictionary, selected: bool, playable: bool) -> void:
	hand_index = index
	custom_minimum_size = Vector2(150, 202)
	size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
	mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	var color = _card_color(card)
	if selected: color = UIFactory.GOLD
	base_style = UIFactory.style(Color("142137"), color, 9, 3 if selected else 1, 9)
	hover_style = UIFactory.style(Color("1d3049"), color.lightened(0.15), 9, 2, 9)
	add_theme_stylebox_override("panel", base_style)
	modulate = Color.WHITE if playable else Color(0.72, 0.76, 0.84, 0.82)
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation", 5)
	add_child(v)
	var top = HBoxContainer.new()
	v.add_child(top)
	var kind_names = { "skill": "技能", "reaction": "时机", "ongoing": "持续", "treasure": "秘宝", "ascension": "升变", "form_attack": "形态攻击", "form_skill": "形态技能", "form_reaction": "形态时机" }
	var kind = UIFactory.label(kind_names.get(card.get("kind", ""), "卡牌"), 11, UIFactory.MUTED)
	kind.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	top.add_child(kind)
	top.add_child(UIFactory.label("%d AP" % int(cost.get("ap", card.get("ap", 0))), 13, UIFactory.GOLD))
	var title = UIFactory.label(card.get("name", "未知卡牌"), 17, UIFactory.TEXT)
	title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	title.max_lines_visible = 2
	title.custom_minimum_size.y = 38
	v.add_child(title)
	var colors: Array = card.get("colors", [])
	var color_names: Array = []
	for c in colors:
		color_names.append({ "green": "◆ 绿", "blue": "◆ 蓝", "purple": "◆ 紫", "neutral": "◇ 通用" }.get(c, c))
	var element = card.get("damage_type", "")
	var subtitle = " · ".join(color_names)
	if element != "": subtitle += "  /  " + BattleState.ELEMENT_NAMES.get(element, element)
	var sub = UIFactory.label(subtitle, 11, _card_color(card))
	v.add_child(sub)
	var line = HSeparator.new()
	v.add_child(line)
	var description = UIFactory.label(card.get("description", ""), 11, Color("b9c7d8"))
	description.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	description.max_lines_visible = 6
	description.clip_text = true
	description.size_flags_vertical = Control.SIZE_EXPAND_FILL
	v.add_child(description)
	var bottom = HBoxContainer.new()
	v.add_child(bottom)
	var crit = UIFactory.label("【暴击】" if card.get("crit", false) else "◇ 战术", 11, UIFactory.GOLD if card.get("crit", false) else UIFactory.MUTED)
	crit.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bottom.add_child(crit)
	bottom.add_child(UIFactory.label("%d MP" % int(cost.get("mp", card.get("mp", 0))), 12, UIFactory.BLUE))
	tooltip_text = card.get("description", "")
	mouse_entered.connect(func (): add_theme_stylebox_override("panel", hover_style))
	mouse_exited.connect(func (): add_theme_stylebox_override("panel", base_style))

func _card_color(card: Dictionary) -> Color:
	var colors: Array = card.get("colors", [])
	if colors.size() > 1: return Color("b69add")
	if colors.is_empty(): return UIFactory.MUTED
	match colors[0]:
		"green": return UIFactory.GREEN
		"blue": return UIFactory.BLUE
		"purple": return UIFactory.PURPLE
	return UIFactory.MUTED

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed:
		clicked.emit(hand_index)
		accept_event()
