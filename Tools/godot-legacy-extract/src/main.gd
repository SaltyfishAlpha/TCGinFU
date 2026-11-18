extends Control

var battle_ui

func _ready() -> void:
	battle_ui = load("res://scripts/battle_ui.gd").new()
	battle_ui.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(battle_ui)
