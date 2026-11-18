class_name UnitAvatar
extends Control

var unit_kind: String = "boss"
var tint: Color = Color("61d0c6")
var time: float = 0.0

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE

func _process(delta: float) -> void:
	time += delta
	queue_redraw()

func _draw() -> void:
	var c = size * 0.5
	var radius = minf(size.x, size.y) * 0.38
	draw_circle(c, radius + 7, Color(tint, 0.055))
	draw_arc(c, radius + 5, - PI * 0.5 + time * 0.12, PI * 1.15 + time * 0.12, 64, Color(tint, 0.32), 1.2, true)
	if unit_kind in ["boss", "arm"]:
		_draw_gear(c, radius)
	else:
		_draw_hero(c, radius)

func _draw_gear(c: Vector2, r: float) -> void:
	var points = PackedVector2Array()
	var teeth = 12 if unit_kind == "boss" else 8
	for i in range(teeth * 4):
		var angle = TAU * float(i) / float(teeth * 4) + time * (0.04 if unit_kind == "boss" else -0.025)
		var rr = r * (1.0 if i % 4 in [1, 2] else 0.83)
		points.append(c + Vector2(cos(angle), sin(angle)) * rr)
	draw_colored_polygon(points, Color("182b3d"))
	draw_polyline(PackedVector2Array(Array(points) + [points[0]]), Color(tint, 0.8), 2.0, true)
	draw_arc(c, r * 0.68, 0, TAU, 64, Color(tint, 0.55), 2.0, true)
	draw_arc(c, r * 0.42, 0, TAU, 48, Color(UIFactory.GOLD, 0.75), 2.0, true)
	for i in range(12):
		var a = TAU * i / 12.0
		draw_line(c + Vector2(cos(a), sin(a)) * r * 0.52, c + Vector2(cos(a), sin(a)) * r * 0.62, Color(tint, 0.55), 1.2, true)
	if unit_kind == "boss":
		draw_line(c, c + Vector2(cos(- PI * 0.32 + time * 0.03), sin(- PI * 0.32 + time * 0.03)) * r * 0.48, UIFactory.GOLD, 3.0, true)
		draw_line(c, c + Vector2(cos(PI * 0.8), sin(PI * 0.8)) * r * 0.33, UIFactory.TEXT, 2.0, true)
		draw_circle(c, 4.5, UIFactory.RED)
	else:
		var shield = PackedVector2Array([c + Vector2(-0.36, -0.45) * r, c + Vector2(0.36, -0.45) * r, c + Vector2(0.45, 0.12) * r, c + Vector2(0, 0.55) * r, c + Vector2(-0.45, 0.12) * r])
		draw_colored_polygon(shield, Color(tint, 0.18))
		draw_polyline(PackedVector2Array(Array(shield) + [shield[0]]), tint, 2.0, true)

func _draw_hero(c: Vector2, r: float) -> void:
	draw_circle(c, r * 0.8, Color("162a3b"))
	draw_arc(c, r * 0.8, 0, TAU, 64, Color(tint, 0.7), 2, true)
	if unit_kind == "miku":
		draw_arc(c + Vector2(-4, 0), r * 0.52, - PI * 0.58, PI * 0.58, 40, tint, 3.0, true)
		draw_line(c + Vector2(6, - r * 0.5), c + Vector2(6, r * 0.5), Color(tint, 0.7), 1.5, true)
		draw_line(c + Vector2(- r * 0.48, 0), c + Vector2(r * 0.58, 0), UIFactory.GOLD, 2.5, true)
		draw_colored_polygon(PackedVector2Array([c + Vector2(r * 0.62, 0), c + Vector2(r * 0.42, -5), c + Vector2(r * 0.42, 5)]), UIFactory.GOLD)
	else:
		var book = PackedVector2Array([c + Vector2(-0.5, -0.35) * r, c + Vector2(-0.05, -0.23) * r, c + Vector2(-0.05, 0.43) * r, c + Vector2(-0.5, 0.3) * r])
		var book2 = PackedVector2Array([c + Vector2(0.5, -0.35) * r, c + Vector2(0.05, -0.23) * r, c + Vector2(0.05, 0.43) * r, c + Vector2(0.5, 0.3) * r])
		draw_colored_polygon(book, Color(tint, 0.22))
		draw_colored_polygon(book2, Color(tint, 0.22))
		draw_polyline(PackedVector2Array(Array(book) + [book[0]]), tint, 2, true)
		draw_polyline(PackedVector2Array(Array(book2) + [book2[0]]), tint, 2, true)
		draw_arc(c + Vector2(0, - r * 0.05), r * 0.19, 0, TAU, 6, UIFactory.GOLD, 1.5, true)
		draw_circle(c + Vector2(0, - r * 0.05), 3, UIFactory.PURPLE)
