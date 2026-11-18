class_name BossDatabase
extends RefCounted

const INTENTS = {
	"gravity_sweep": { "name": "重力横扫", "category": "范围", "target": "front_all", "damage": 12, "element": "physical", "damage_class": "physical", "mp": 0, "description": "前排全体受到12点物理伤害并被击退至后排；前排无人时，后排受到4点震波伤害。" },
	"dimension_cannon": { "name": "次元穿刺炮", "category": "死刑", "target": "locked_hero", "damage": 20, "element": "physical", "damage_class": "physical", "mp": 0, "pierce": true, "description": "对锁定的后排角色造成20点物理穿透伤害；若其已移动至前排，则仅受8点擦伤。" },
	"aether_siphon": { "name": "以太虹吸", "category": "干扰", "target": "all_heroes", "damage": 6, "element": "dark", "damage_class": "magic", "mp": 10, "description": "全体受到6点暗属性魔法伤害，各吞噬1个充能；每吞噬1个，首领回复8HP。消耗10MP。" },
	"clockwork_volley": { "name": "发条连射", "category": "多段", "target": "single", "damage": 8, "element": "physical", "damage_class": "physical", "mp": 0, "hits": 2, "description": "对单个目标连续射击2次，每次造成8点物理伤害；前排无人时攻击后排上轮最高输出者。" },
	"phase_shift": { "name": "相位折跃", "category": "控制", "target": "all_heroes", "damage": 7, "element": "lightning", "damage_class": "magic", "mp": 10, "description": "全体受到7点雷属性魔法伤害，随后前后排互换，命刻额外+1。消耗10MP。" },
	"armor_reconstruction": { "name": "装甲重构", "category": "蓄力", "target": "self", "damage": 0, "element": "none", "damage_class": "none", "mp": 0, "description": "获得14点物理与14点魔法护盾；轮末若两项都未被击破，首领所有伤害永久+3。" },
	"time_guillotine": { "name": "时空断头台", "category": "双排死刑", "target": "both_rows", "damage": 15, "element": "physical", "damage_class": "mixed", "mp": 0, "description": "前排受到15点物理伤害，后排受到15点魔法伤害；每人只承受本排一段。" },
	"fate_turbulence": { "name": "命运乱流", "category": "手牌惩罚", "target": "all_heroes", "damage": 8, "element": "dark", "damage_class": "magic", "mp": 0, "description": "全体受到8点暗属性魔法伤害；每剩余1张手牌，额外受到2点无视护盾刺痛。" },
	"causal_erasure": { "name": "因果抹杀令", "category": "分摊死刑", "target": "highest_hp", "damage": 24, "element": "physical", "damage_class": "physical", "mp": 0, "description": "当前HP最高者受到24点物理伤害；可在响应时由相邻队友分摊，两人各12点并分别抵扣护盾。" },
	"zero_collapse": { "name": "归零坍缩", "category": "特殊必杀", "target": "all_heroes", "damage": 28, "element": "dark", "damage_class": "pierce", "mp": 0, "pierce": true, "description": "全员受到28点全能穿透伤害。释放后下轮过热：暴击抵抗4，受到最终伤害×1.5。" },
	"shield_quake": { "name": "重锤震地", "category": "部位攻击", "target": "front_all", "damage": 7, "element": "physical", "damage_class": "physical", "mp": 0, "description": "镇压重盾臂对前排全体造成7点物理伤害。" }
}

const PHASE_ONE = ["gravity_sweep", "dimension_cannon", "aether_siphon", "clockwork_volley", "phase_shift", "armor_reconstruction"]
const PHASE_TWO = ["time_guillotine", "fate_turbulence", "causal_erasure"]

static func get_intent(intent_id: String) -> Dictionary:
	return INTENTS.get(intent_id, { }).duplicate(true)
