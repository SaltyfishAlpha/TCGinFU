class_name UIFactory
extends RefCounted

const INK = Color("0b1222")
const PANEL = Color("111c30")
const PANEL_LIGHT = Color("18263d")
const LINE = Color("30425c")
const GOLD = Color("d8b96f")
const TEXT = Color("e8edf4")
const MUTED = Color("8fa2ba")
const TEAL = Color("58d0c5")
const RED = Color("ef7379")
const GREEN = Color("70d6a0")
const BLUE = Color("68a9ec")
const PURPLE = Color("b08ae5")

static func theme() -> Theme:
	var result = Theme.new()
	var font = SystemFont.new()
	font.font_names = PackedStringArray(["Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "SimHei"])
	font.allow_system_fallback = true
	result.default_font = font
	result.default_font_size = 16
	result.set_color("font_color", "Label", TEXT)
	result.set_color("font_color", "Button", TEXT)
	result.set_color("font_hover_color", "Button", Color.WHITE)
	result.set_color("font_pressed_color", "Button", Color.WHITE)
	result.set_color("font_disabled_color", "Button", Color("61718a"))
	result.set_stylebox("normal", "Button", style(PANEL_LIGHT, LINE, 7, 1, 10))
	result.set_stylebox("hover", "Button", style(Color("20334d"), Color("6285a5"), 7, 1, 10))
	result.set_stylebox("pressed", "Button", style(Color("153b47"), TEAL, 7, 1, 10))
	result.set_stylebox("disabled", "Button", style(Color("101827"), Color("202c3d"), 7, 1, 10))
	result.set_stylebox("focus", "Button", style(Color(0, 0, 0, 0), GOLD, 7, 2, 10))
	result.set_stylebox("panel", "PanelContainer", style(PANEL, LINE, 10, 1, 12))
	result.set_color("font_color", "CheckButton", TEXT)
	return result

static func style(background: Color, border: Color, radius: int = 8, width: int = 1, padding: int = 8) -> StyleBoxFlat:
	var box = StyleBoxFlat.new()
	box.bg_color = background
	box.border_color = border
	box.set_border_width_all(width)
	box.set_corner_radius_all(radius)
	box.content_margin_left = padding
	box.content_margin_right = padding
	box.content_margin_top = padding
	box.content_margin_bottom = padding
	return box

static func label(text: String, size: int = 16, color: Color = TEXT) -> Label:
	var node = Label.new()
	node.text = text
	node.add_theme_font_size_override("font_size", size)
	node.add_theme_color_override("font_color", color)
	return node

static func rich(text: String, size: int = 15, color: Color = TEXT) -> RichTextLabel:
	var node = RichTextLabel.new()
	node.bbcode_enabled = true
	node.fit_content = true
	node.scroll_active = false
	node.text = "[font_size=%d][color=#%s]%s[/color][/font_size]" % [size, color.to_html(false), text]
	node.add_theme_font_size_override("normal_font_size", size)
	return node

static func button(text: String, accent: Color = LINE) -> Button:
	var node = Button.new()
	node.text = text
	node.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	node.add_theme_stylebox_override("normal", style(PANEL_LIGHT, accent, 7, 1, 9))
	node.add_theme_stylebox_override("hover", style(PANEL_LIGHT.lightened(0.1), accent.lightened(0.15), 7, 1, 9))
	return node

static func spacer(height: int = 6) -> Control:
	var node = Control.new()
	node.custom_minimum_size.y = height
	return node

static func clear(node: Node) -> void:
	for child in node.get_children():
		node.remove_child(child)
		child.queue_free()
