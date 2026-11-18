class_name CardDatabase
extends RefCounted












const _FORM_OWNERS = {
	"phantom": "miku",
	"hawkeye": "miku",
	"cosmic": "weiting",
	"alchemy": "weiting",
}

const _ALLOWED_KINDS = [
	"skill", "reaction", "ongoing", "treasure", "ascension",
	"form_attack", "form_skill", "form_reaction",
]
const _ALLOWED_COLORS = ["green", "blue", "purple", "neutral"]
const _ALLOWED_DAMAGE_TYPES = [
	"", "physical", "wind", "dark", "lightning", "fire", "ice",
	"earth", "light", "poison", "arcane", "adaptive", "random",
]
const _ALLOWED_TARGETS = ["enemy", "all_enemies", "self", "ally", "none"]
const _FIELD_TYPES = {
	"id": TYPE_STRING,
	"name": TYPE_STRING,
	"owner": TYPE_STRING,
	"form": TYPE_STRING,
	"kind": TYPE_STRING,
	"colors": TYPE_ARRAY,
	"ap": TYPE_INT,
	"mp": TYPE_INT,
	"damage": TYPE_INT,
	"damage_type": TYPE_STRING,
	"crit": TYPE_BOOL,
	"ranged": TYPE_BOOL,
	"target": TYPE_STRING,
	"effect": TYPE_STRING,
	"description": TYPE_STRING,
}


const _CARDS = {



	"phantom_nightwalk": {
		"id": "phantom_nightwalk",
		"name": "幻影夜行",
		"owner": "miku",
		"form": "",
		"kind": "ascension",
		"colors": ["green", "blue"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "phantom_nightwalk",
		"description": "前置条件：本局累计触发2次暴击，或全队累计消耗2个充能指示物。第3轮前抽到时，可0 AP展示此牌并洗回主卡组，抽1张牌且视为达成1次前置条件。第3轮开始时若条件满足，可从主卡组直接跳出发动升变，持续2轮。升变期间你的暴击伤害倍率提升至×1.75；每当你的攻击触发暴击时，回复5 MP。",
		"form_id": "phantom",
	},
	"hawkeye_overdrive": {
		"id": "hawkeye_overdrive",
		"name": "鹰眼绝境",
		"owner": "miku",
		"form": "",
		"kind": "ascension",
		"colors": ["green", "blue"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "hawkeye_overdrive",
		"description": "轮始阶段若自身处于后排且此牌在手牌中，可在第1或第2轮直接发动升变；若前3轮未摸到，第3轮起可从主卡组自动跳出发动，持续2轮。升变期间只要你处于后排，所有【暴击】掷骰结果+2，且双色牌触发【超暴击】的伤害倍率从×2提升至×2.5。",
		"form_id": "hawkeye",
	},
	"ambush_opening": {
		"id": "ambush_opening",
		"name": "暗算起手",
		"owner": "miku",
		"form": "",
		"kind": "skill",
		"colors": ["green", "blue"],
		"ap": 1,
		"mp": 5,
		"damage": 5,
		"damage_type": "physical",
		"crit": true,
		"ranged": false,
		"target": "enemy",
		"effect": "ambush_opening",
		"description": "对单个目标造成5点物理伤害【暴击】。命中后令目标获得【破绽】标记：本轮内下一次受到的任意伤害+3。",
	},
	"cheap_shot_first_form": {
		"id": "cheap_shot_first_form",
		"name": "卑劣打击·壹式",
		"owner": "miku",
		"form": "",
		"kind": "skill",
		"colors": ["green", "blue"],
		"ap": 1,
		"mp": 5,
		"damage": 6,
		"damage_type": "physical",
		"crit": true,
		"ranged": false,
		"target": "enemy",
		"effect": "cheap_shot_first_form",
		"description": "对单个目标造成6点物理伤害【暴击】；若目标处于负面状态、带有【破绽】标记或本轮已受到过伤害，基础伤害提升至9点。",
	},
	"read_dodge": {
		"id": "read_dodge",
		"name": "看破闪避",
		"owner": "miku",
		"form": "",
		"kind": "reaction",
		"colors": ["green"],
		"ap": 0,
		"mp": 5,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "ally",
		"effect": "read_dodge",
		"description": "在敌人阶段自身或相邻队友被宣告为单体攻击目标时打出。投掷1颗敏捷骰，若点数≥敌方暴击抵抗，完全闪避本次攻击并抽1张牌；若未达标，仍为目标抵消6点伤害。",
		"can_target_self": true,
	},
	"seize_initiative": {
		"id": "seize_initiative",
		"name": "先声夺人",
		"owner": "miku",
		"form": "",
		"kind": "skill",
		"colors": ["green"],
		"ap": 0,
		"mp": 5,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "seize_initiative",
		"description": "在前后排之间移动1次并抽1张牌。若在战斗的第1轮打出此牌，免除5 MP消耗，且本轮额外获得+1 AP。",
	},
	"suppressing_volley": {
		"id": "suppressing_volley",
		"name": "压制连射",
		"owner": "miku",
		"form": "",
		"kind": "skill",
		"colors": ["green"],
		"ap": 1,
		"mp": 10,
		"damage": 5,
		"damage_type": "physical",
		"crit": true,
		"ranged": true,
		"target": "enemy",
		"effect": "suppressing_volley",
		"description": "对单个敌人连续射击2次，每次造成5点物理伤害【暴击】；两次射击分别独立投掷敏捷骰判定暴击。",
		"hits": 2,
	},
	"crossfire_network": {
		"id": "crossfire_network",
		"name": "交叉火力网",
		"owner": "miku",
		"form": "",
		"kind": "skill",
		"colors": ["green", "blue"],
		"ap": 1,
		"mp": 10,
		"damage": 8,
		"damage_type": "physical",
		"crit": true,
		"ranged": true,
		"target": "enemy",
		"effect": "crossfire_network",
		"description": "对单个目标造成8点物理伤害【暴击】；若本轮已有其他队友对该目标造成过伤害，此牌消耗减少1 AP（变为0 AP）且伤害提升至12点。",
	},
	"hawkeye_lock": {
		"id": "hawkeye_lock",
		"name": "鹰眼锁定",
		"owner": "miku",
		"form": "",
		"kind": "ongoing",
		"colors": ["green", "blue"],
		"ap": 1,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "hawkeye_lock",
		"description": "置入持续区。只要你处于后排，你的远程攻击无视目标2点物理护盾，且所有【暴击】掷骰结果+1。",
	},
	"cloud_piercing_snipe": {
		"id": "cloud_piercing_snipe",
		"name": "穿云狙击",
		"owner": "miku",
		"form": "",
		"kind": "skill",
		"colors": ["green", "blue"],
		"ap": 2,
		"mp": 10,
		"damage": 12,
		"damage_type": "physical",
		"crit": true,
		"ranged": true,
		"target": "enemy",
		"effect": "cloud_piercing_snipe",
		"description": "仅限在后排打出。对单个目标造成12点物理伤害【双色暴击】，本次攻击无视敌方自动物理护盾；双骰同时达标时触发×2超暴击，造成24点穿透伤害。",
		"requires_back_row": true,
	},
	"skirmish_shot": {
		"id": "skirmish_shot",
		"name": "游击速射",
		"owner": "miku",
		"form": "",
		"kind": "skill",
		"colors": ["green"],
		"ap": 1,
		"mp": 0,
		"damage": 7,
		"damage_type": "physical",
		"crit": true,
		"ranged": true,
		"target": "enemy",
		"effect": "skirmish_shot",
		"description": "造成7点物理伤害【暴击】（纯绿，投敏捷骰）；打出前或结算后可免费在前后排之间移动1次。",
	},
	"rapid_arrow_rain": {
		"id": "rapid_arrow_rain",
		"name": "连珠箭雨",
		"owner": "miku",
		"form": "",
		"kind": "skill",
		"colors": ["green"],
		"ap": 1,
		"mp": 0,
		"damage": 7,
		"damage_type": "physical",
		"crit": true,
		"ranged": true,
		"target": "enemy",
		"effect": "rapid_arrow_rain",
		"description": "对单个目标造成7点物理伤害【暴击】；若触发【暴击】，对敌方另一排的1名随机目标追加4点物理伤害。若全场仅1名敌人，则主目标额外受到+2点伤害。",
	},
	"rolling_cloak": {
		"id": "rolling_cloak",
		"name": "翻滚披风",
		"owner": "miku",
		"form": "",
		"kind": "reaction",
		"colors": ["neutral"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "ally",
		"effect": "rolling_cloak",
		"description": "自身或相邻队友受击时打出，使目标获得4点物理护盾与4点魔法护盾，并可免费在前后排之间移动1次。",
		"can_target_self": true,
	},
	"tactical_concealment": {
		"id": "tactical_concealment",
		"name": "战术隐蔽",
		"owner": "miku",
		"form": "",
		"kind": "skill",
		"colors": ["neutral"],
		"ap": 1,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "tactical_concealment",
		"description": "获得7点物理护盾；若打出时自身处于后排，本轮敌方的常规单体攻击意图无法将你选为目标。",
	},
	"tailwind_charge": {
		"id": "tailwind_charge",
		"name": "顺风充能",
		"owner": "miku",
		"form": "",
		"kind": "treasure",
		"colors": ["neutral"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "ally",
		"effect": "tailwind_charge",
		"description": "抽1张牌，并将此牌直接置入自己或任意队友的充能区，不占用每轮1次的贴充能动作限额。",
		"can_target_self": true,
	},
	"verdant_wind_arrow": {
		"id": "verdant_wind_arrow",
		"name": "翠风箭附",
		"owner": "miku",
		"form": "",
		"kind": "treasure",
		"colors": ["green"],
		"ap": 0,
		"mp": 5,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "verdant_wind_arrow",
		"description": "本轮你造成的所有物理伤害转化为风属性伤害（可触发敌方风属性弱点×2增伤），且你本轮下一次攻击造成的伤害+3。",
	},




	"shadow_dash_assault": {
		"id": "shadow_dash_assault",
		"name": "瞬影突袭",
		"owner": "miku",
		"form": "phantom",
		"kind": "form_attack",
		"colors": ["green"],
		"ap": 1,
		"mp": 0,
		"damage": 8,
		"damage_type": "physical",
		"crit": true,
		"ranged": false,
		"target": "enemy",
		"effect": "shadow_dash_assault",
		"description": "造成B_atk点物理伤害【暴击】（本5级短弓构筑B_atk=8，即8点）；若本轮已打出过其他牌，返还1 AP（每轮限返还1次）。",
		"damage_formula": "B_atk",
	},
	"night_eviscerate": {
		"id": "night_eviscerate",
		"name": "暗夜剔骨",
		"owner": "miku",
		"form": "phantom",
		"kind": "form_skill",
		"colors": ["green", "blue"],
		"ap": 1,
		"mp": 5,
		"damage": 12,
		"damage_type": "physical",
		"crit": true,
		"ranged": false,
		"target": "enemy",
		"effect": "night_eviscerate",
		"description": "造成B_atk+4点物理伤害【暴击】（本5级构筑为12点）；若消耗1个充能指示物，本次攻击必定暴击且抽1张牌。",
		"damage_formula": "B_atk + 4",
	},
	"thieves_trick": {
		"id": "thieves_trick",
		"name": "盗贼戏法",
		"owner": "miku",
		"form": "phantom",
		"kind": "form_skill",
		"colors": ["blue"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "ally",
		"effect": "thieves_trick",
		"description": "从弃牌堆选择1张牌直接置入自己或队友的充能区，并使该角色本轮下一次攻击伤害+3。",
		"can_target_self": true,
	},
	"smoke_decoy": {
		"id": "smoke_decoy",
		"name": "烟幕替身",
		"owner": "miku",
		"form": "phantom",
		"kind": "form_reaction",
		"colors": ["green"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "ally",
		"effect": "smoke_decoy",
		"description": "自身或相邻队友受击时打出，获得B_def+2点物理护盾（本5级构筑为10点），并使攻击者本轮后续造成的伤害降低3点。",
		"can_target_self": true,
		"shield_formula": "B_def + 2",
	},




	"wind_chasing_arrows": {
		"id": "wind_chasing_arrows",
		"name": "追风连矢",
		"owner": "miku",
		"form": "hawkeye",
		"kind": "form_attack",
		"colors": ["green"],
		"ap": 1,
		"mp": 0,
		"damage": 10,
		"damage_type": "physical",
		"crit": true,
		"ranged": true,
		"target": "enemy",
		"effect": "wind_chasing_arrows",
		"description": "造成B_atk+2点物理伤害【暴击】（本5级构筑为10点）；命中后若目标未死亡，可免费将1张手牌贴入充能区，不占每轮充能限额。",
		"damage_formula": "B_atk + 2",
	},
	"death_sentence": {
		"id": "death_sentence",
		"name": "死神裁决",
		"owner": "miku",
		"form": "hawkeye",
		"kind": "form_skill",
		"colors": ["green", "blue"],
		"ap": 2,
		"mp": 10,
		"damage": 16,
		"damage_type": "physical",
		"crit": true,
		"ranged": true,
		"target": "enemy",
		"effect": "death_sentence",
		"description": "造成B_atk×2点物理伤害【双色暴击】（本5级构筑为16点）；后排触发×2.5超暴击时造成B_atk×5点毁灭性伤害（本5级构筑为40点）。",
		"damage_formula": "B_atk * 2",
	},
	"arrow_storm_coverage": {
		"id": "arrow_storm_coverage",
		"name": "箭雨覆盖",
		"owner": "miku",
		"form": "hawkeye",
		"kind": "form_skill",
		"colors": ["green", "blue"],
		"ap": 1,
		"mp": 5,
		"damage": 8,
		"damage_type": "physical",
		"crit": true,
		"ranged": true,
		"target": "all_enemies",
		"effect": "arrow_storm_coverage",
		"description": "对敌方全体造成B_atk点物理伤害【双色暴击】（本5级构筑为8点），并削减所有敌人3点物理护盾。",
		"damage_formula": "B_atk",
	},
	"wind_wall_counter": {
		"id": "wind_wall_counter",
		"name": "风墙反制",
		"owner": "miku",
		"form": "hawkeye",
		"kind": "form_reaction",
		"colors": ["blue"],
		"ap": 0,
		"mp": 0,
		"damage": 8,
		"damage_type": "physical",
		"crit": false,
		"ranged": false,
		"target": "ally",
		"effect": "wind_wall_counter",
		"description": "敌人阶段自身或相邻队友受击时打出，获得B_def点物理护盾与B_def点魔法护盾（本5级构筑各8点）；若完全抵消本次攻击伤害，立即对攻击者反击造成B_atk点物理伤害（本5级构筑为8点）。",
		"can_target_self": true,
		"shield_formula": "B_def physical + B_def magic",
		"damage_formula": "B_atk（仅完全抵消时反击）",
	},




	"cosmic_collapse": {
		"id": "cosmic_collapse",
		"name": "星穹坍缩",
		"owner": "weiting",
		"form": "",
		"kind": "ascension",
		"colors": ["blue", "purple"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "cosmic_collapse",
		"description": "前置条件：本局累计触发2次「灵智回流」效果，或累计回复过15 MP。第3轮前抽到时，可0 AP展示此牌并洗回主卡组，抽1张牌并对自己造成1点刺痛伤害（可直接触发在场的「灵智回流」）。第3轮开始时若条件满足，可从主卡组直接跳出发动升变，持续2轮。升变期间你的所有骰子投掷结果+2；每当你回复HP或MP时，对生命值最低的敌人自动造成3点暗属性穿透伤害。",
		"form_id": "cosmic",
	},
	"truth_alchemy_matrix": {
		"id": "truth_alchemy_matrix",
		"name": "真理炼金矩阵",
		"owner": "weiting",
		"form": "",
		"kind": "ascension",
		"colors": ["blue"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "truth_alchemy_matrix",
		"description": "轮始阶段若全队充能区合计有至少3个充能指示物且此牌在手牌中，可在第1或第2轮提前发动；否则在第3轮起可从主卡组自动跳出发动，持续2轮。升变期间你每轮可执行2次免费的「贴充能」动作；且全队角色每当消耗充能指示物时，投掷1颗任意骰子，若点数为偶数，则该充能指示物不进入弃牌堆，继续留在充能区中。",
		"form_id": "alchemy",
	},
	"intellect_reflux_persistent": {
		"id": "intellect_reflux_persistent",
		"name": "灵智回流·常驻",
		"owner": "weiting",
		"form": "",
		"kind": "ongoing",
		"colors": ["blue", "purple"],
		"ap": 1,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "intellect_reflux_persistent",
		"description": "置入持续区。每当你受到伤害时（包括自身秘宝或卡牌造成的刺痛伤害），立即回复5 MP（每轮最多触发2次）；若单次受到敌方实际生命值伤害≥5点，额外从弃牌堆将1张牌贴入你的充能区。",
	},
	"pain_transduction": {
		"id": "pain_transduction",
		"name": "苦痛转导",
		"owner": "weiting",
		"form": "",
		"kind": "skill",
		"colors": ["purple"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "pain_transduction",
		"description": "仅限本轮你已受到过伤害（含卷轴刺痛）时打出。立即回复10 MP，并使你本轮打出的下一张魔法攻击牌伤害+3。",
	},
	"fate_anchor_seven": {
		"id": "fate_anchor_seven",
		"name": "命运锚点·七",
		"owner": "weiting",
		"form": "",
		"kind": "skill",
		"colors": ["blue"],
		"ap": 0,
		"mp": 5,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "fate_anchor_seven",
		"description": "使你本轮下一次投掷任何骰子（暴击骰或【无常命运】判定骰）时，可将其中1颗骰子的结果直接视为7。",
	},
	"lucky_reversal": {
		"id": "lucky_reversal",
		"name": "幸运改命",
		"owner": "weiting",
		"form": "",
		"kind": "reaction",
		"colors": ["blue"],
		"ap": 0,
		"mp": 5,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "ally",
		"effect": "lucky_reversal",
		"description": "在你或任意队友投掷骰子后立即打出。将其中1颗骰子的结果直接改为7；若另一颗双色骰原本就是7，则直接达成双骰同面，触发×2【超暴击】。",
		"can_target_self": true,
	},
	"entropy_roulette": {
		"id": "entropy_roulette",
		"name": "熵之轮盘·无常命运",
		"owner": "weiting",
		"form": "",
		"kind": "skill",
		"colors": ["blue", "purple"],
		"ap": 1,
		"mp": 10,
		"damage": 0,
		"damage_type": "random",
		"crit": true,
		"ranged": true,
		"target": "all_enemies",
		"effect": "entropy_roulette",
		"description": "投掷1颗洞察骰与1颗意志骰并取较高值（可受【幸运七】修改）。点数1—3：混沌失控，对敌方前排全体造成6点暗属性伤害，并对自身造成2点刺痛伤害。点数4—6：对单个目标造成9点火或冰属性伤害【双色暴击】，并使其下回合暴击抵抗-1。点数7+：天命大吉！对敌方全体造成10点雷或风属性伤害【双色暴击】，并为全队每名角色各贴1个充能指示物（从各自牌库顶盲贴）。",
		"damage_options": [6, 9, 10],
		"target_group": "variable_by_roll",
	},
	"dark_word_vitality_drain": {
		"id": "dark_word_vitality_drain",
		"name": "暗言术·活力汲取",
		"owner": "weiting",
		"form": "",
		"kind": "skill",
		"colors": ["blue", "purple"],
		"ap": 1,
		"mp": 5,
		"damage": 6,
		"damage_type": "dark",
		"crit": true,
		"ranged": true,
		"target": "enemy",
		"effect": "dark_word_vitality_drain",
		"description": "对单个目标造成6点暗属性魔法伤害【双色暴击】，你自己或1名队友回复等同于本次实际伤害一半（向上取整）的HP。",
	},
	"abyssal_siphon": {
		"id": "abyssal_siphon",
		"name": "深渊虹吸·活力汲取贰式",
		"owner": "weiting",
		"form": "",
		"kind": "skill",
		"colors": ["blue", "purple"],
		"ap": 1,
		"mp": 10,
		"damage": 9,
		"damage_type": "dark",
		"crit": true,
		"ranged": true,
		"target": "enemy",
		"effect": "abyssal_siphon",
		"description": "对单个目标造成9点暗属性魔法伤害【双色暴击】。你立即回复6 HP；若你生命值已满，溢出的治疗量自动转化为你或相邻队友的魔法护盾。若触发【暴击】，额外回复5 MP。",
	},
	"universal_elixir": {
		"id": "universal_elixir",
		"name": "调合·万能灵药",
		"owner": "weiting",
		"form": "",
		"kind": "skill",
		"colors": ["blue"],
		"ap": 1,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "ally",
		"effect": "universal_elixir",
		"description": "打出时需消耗1个充能指示物，从以下两项炼金效果中二选一；若消耗2个充能指示物则两项同时生效。①生命灵药：令1名我方角色回复10 HP与10 MP。②元素涂油：令1名我方角色本轮所有攻击附加指定元素属性（火／冰／雷／风／毒任选），且本轮造成的伤害+3。",
		"can_target_self": true,
	},
	"emergency_catalyst": {
		"id": "emergency_catalyst",
		"name": "应急催化剂",
		"owner": "weiting",
		"form": "",
		"kind": "reaction",
		"colors": ["blue"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "ally",
		"effect": "emergency_catalyst",
		"description": "在任意队友行动时或敌人阶段打出。将你手牌中的1张牌直接贴入该队友的充能区（不占用每轮1次的贴充能限额），并令该队友获得4点全能护盾，或使其本轮下一次暴击掷骰结果+1。",
	},
	"weakness_analysis": {
		"id": "weakness_analysis",
		"name": "弱点解析",
		"owner": "weiting",
		"form": "",
		"kind": "skill",
		"colors": ["blue"],
		"ap": 1,
		"mp": 0,
		"damage": 4,
		"damage_type": "arcane",
		"crit": true,
		"ranged": true,
		"target": "enemy",
		"effect": "weakness_analysis",
		"description": "造成4点魔法伤害【暴击】（纯蓝，投洞察骰），揭示目标1项属性弱点并抽1张牌。",
	},
	"arcane_bolt": {
		"id": "arcane_bolt",
		"name": "秘法冲击",
		"owner": "weiting",
		"form": "",
		"kind": "skill",
		"colors": ["blue"],
		"ap": 1,
		"mp": 0,
		"damage": 6,
		"damage_type": "arcane",
		"crit": true,
		"ranged": true,
		"target": "enemy",
		"effect": "arcane_bolt",
		"description": "造成6点魔法伤害【暴击】；若目标已被揭示属性弱点或处于负面状态，额外造成+3点魔法伤害。",
	},
	"arcane_barrier": {
		"id": "arcane_barrier",
		"name": "奥术结界",
		"owner": "weiting",
		"form": "",
		"kind": "skill",
		"colors": ["neutral"],
		"ap": 1,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "arcane_barrier",
		"description": "获得7点魔法护盾，并使自身充能区上限在本轮及下一轮临时+1。",
	},
	"sages_robe": {
		"id": "sages_robe",
		"name": "睿智法袍",
		"owner": "weiting",
		"form": "",
		"kind": "reaction",
		"colors": ["neutral"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "ally",
		"effect": "sages_robe",
		"description": "自身或相邻队友受击时打出，获得4点物理护盾与4点魔法护盾；若你的持续区有卡牌在场，额外回复5 MP。",
		"can_target_self": true,
	},
	"pain_inscription": {
		"id": "pain_inscription",
		"name": "痛觉铭刻",
		"owner": "weiting",
		"form": "",
		"kind": "treasure",
		"colors": ["purple"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "pain_inscription",
		"description": "立即对自身造成2点无视护盾的刺痛伤害；若【魔力激发卷轴】处于关闭状态，则同时将其免费切换为【激活】状态。回复10 MP并抽1张牌。",
		"self_damage": 2,
	},
	"mana_overload_ritual": {
		"id": "mana_overload_ritual",
		"name": "魔力过载仪式",
		"owner": "weiting",
		"form": "",
		"kind": "treasure",
		"colors": ["blue", "purple"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "self",
		"effect": "mana_overload_ritual",
		"description": "仅限【魔力激发卷轴】处于激活状态时打出。本轮你的「灵智回流」触发次数上限+1，且你本轮打出的下一张【熵系魔法】牌消耗减少5 MP、暴击掷骰结果+1。",
	},




	"event_horizon_singularity": {
		"id": "event_horizon_singularity",
		"name": "视界奇点",
		"owner": "weiting",
		"form": "cosmic",
		"kind": "form_skill",
		"colors": ["blue", "purple"],
		"ap": 1,
		"mp": 5,
		"damage": 11,
		"damage_type": "dark",
		"crit": true,
		"ranged": true,
		"target": "enemy",
		"effect": "event_horizon_singularity",
		"description": "造成B_atk+4点暗属性魔法伤害【双色暴击】（本5级构筑为11点），无视目标魔法护盾。",
		"damage_formula": "B_atk + 4",
	},
	"grand_chaos_roulette": {
		"id": "grand_chaos_roulette",
		"name": "无常大轮盘",
		"owner": "weiting",
		"form": "cosmic",
		"kind": "form_skill",
		"colors": ["blue", "purple"],
		"ap": 1,
		"mp": 5,
		"damage": 9,
		"damage_type": "adaptive",
		"crit": true,
		"ranged": true,
		"target": "all_enemies",
		"effect": "grand_chaos_roulette",
		"description": "对敌方全体造成B_atk+2点魔法伤害【双色暴击】（本5级构筑为9点），该伤害自动适应并触发每名敌人的易损弱点属性。",
		"damage_formula": "B_atk + 2",
	},
	"soul_devouring_feast": {
		"id": "soul_devouring_feast",
		"name": "噬魂狂宴",
		"owner": "weiting",
		"form": "cosmic",
		"kind": "form_skill",
		"colors": ["purple"],
		"ap": 1,
		"mp": 0,
		"damage": 7,
		"damage_type": "dark",
		"crit": false,
		"ranged": true,
		"target": "enemy",
		"effect": "soul_devouring_feast",
		"description": "对单个目标造成B_atk点暗属性伤害（本5级构筑为7点），全队所有角色回复6 HP与10 MP。",
		"damage_formula": "B_atk",
	},
	"time_paradox": {
		"id": "time_paradox",
		"name": "时间悖论",
		"owner": "weiting",
		"form": "cosmic",
		"kind": "form_reaction",
		"colors": ["blue"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "ally",
		"effect": "time_paradox",
		"description": "自身或相邻队友受击时打出，获得B_def+2点物理护盾与B_def+2点魔法护盾（本5级构筑各10点），并令敌方本次攻击的暴击判定强制改为失败。",
		"can_target_self": true,
		"shield_formula": "(B_def + 2) physical + (B_def + 2) magic",
	},




	"elemental_proliferation_bomb": {
		"id": "elemental_proliferation_bomb",
		"name": "元素增殖炸弹",
		"owner": "weiting",
		"form": "alchemy",
		"kind": "form_attack",
		"colors": ["blue"],
		"ap": 1,
		"mp": 0,
		"damage": 10,
		"damage_type": "adaptive",
		"crit": true,
		"ranged": true,
		"target": "enemy",
		"effect": "elemental_proliferation_bomb",
		"description": "造成B_atk+3点指定元素伤害【暴击】（本5级构筑为10点），并令目标在本轮内获得该元素的【易损】状态：受到该属性伤害×2。",
		"damage_formula": "B_atk + 3",
	},
	"philosophers_stone_release": {
		"id": "philosophers_stone_release",
		"name": "贤者之石解放",
		"owner": "weiting",
		"form": "alchemy",
		"kind": "form_skill",
		"colors": ["blue", "purple"],
		"ap": 1,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "ally",
		"effect": "philosophers_stone_release",
		"description": "令1名我方角色立即获得+2 AP、回复10 MP并抽1张牌（每轮对同一角色限用1次）。",
		"can_target_self": true,
	},
	"aether_reassembly_crucible": {
		"id": "aether_reassembly_crucible",
		"name": "以太重组坩埚",
		"owner": "weiting",
		"form": "alchemy",
		"kind": "form_skill",
		"colors": ["blue"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "none",
		"effect": "aether_reassembly_crucible",
		"description": "从你的弃牌堆将最多2张牌直接贴入全队任意角色的充能区，随后抽1张牌。",
	},
	"alchemical_protection_field": {
		"id": "alchemical_protection_field",
		"name": "炼金防护力场",
		"owner": "weiting",
		"form": "alchemy",
		"kind": "form_reaction",
		"colors": ["blue"],
		"ap": 0,
		"mp": 0,
		"damage": 0,
		"damage_type": "",
		"crit": false,
		"ranged": false,
		"target": "ally",
		"effect": "alchemical_protection_field",
		"description": "敌人阶段打出，为前排全体或后排全体提供B_def点全能护盾（本5级构筑为8点），并移除每名受保护角色身上的1个负面状态。",
		"shield_formula": "B_def",
		"target_group": "front_row_or_back_row",
	},
}


const _MAIN_RECIPES = {
	"miku": {
		"phantom_nightwalk": 1,
		"hawkeye_overdrive": 1,
		"ambush_opening": 2,
		"cheap_shot_first_form": 2,
		"read_dodge": 2,
		"seize_initiative": 2,
		"suppressing_volley": 2,
		"crossfire_network": 1,
		"hawkeye_lock": 1,
		"cloud_piercing_snipe": 2,
		"skirmish_shot": 3,
		"rapid_arrow_rain": 3,
		"rolling_cloak": 3,
		"tactical_concealment": 2,
		"tailwind_charge": 2,
		"verdant_wind_arrow": 1,
	},
	"weiting": {
		"cosmic_collapse": 1,
		"truth_alchemy_matrix": 1,
		"intellect_reflux_persistent": 2,
		"pain_transduction": 2,
		"fate_anchor_seven": 1,
		"lucky_reversal": 2,
		"entropy_roulette": 2,
		"dark_word_vitality_drain": 1,
		"abyssal_siphon": 2,
		"universal_elixir": 1,
		"emergency_catalyst": 1,
		"weakness_analysis": 3,
		"arcane_bolt": 2,
		"arcane_barrier": 3,
		"sages_robe": 2,
		"pain_inscription": 2,
		"mana_overload_ritual": 2,
	},
}

const _FORM_RECIPES = {
	"phantom": {
		"shadow_dash_assault": 3,
		"night_eviscerate": 3,
		"thieves_trick": 2,
		"smoke_decoy": 2,
	},
	"hawkeye": {
		"wind_chasing_arrows": 3,
		"death_sentence": 3,
		"arrow_storm_coverage": 2,
		"wind_wall_counter": 2,
	},
	"cosmic": {
		"event_horizon_singularity": 3,
		"grand_chaos_roulette": 3,
		"soul_devouring_feast": 2,
		"time_paradox": 2,
	},
	"alchemy": {
		"elemental_proliferation_bomb": 3,
		"philosophers_stone_release": 3,
		"aether_reassembly_crucible": 2,
		"alchemical_protection_field": 2,
	},
}


static func get_card(card_id: String) -> Dictionary:
	if not _CARDS.has(card_id):
		return { }
	return (_CARDS[card_id] as Dictionary).duplicate(true)


static func build_main_deck(owner_id: String) -> Array[String]:
	var deck: Array[String] = []
	if not _MAIN_RECIPES.has(owner_id):
		return deck
	var recipe: Dictionary = _MAIN_RECIPES[owner_id]
	for card_id in recipe:
		for _copy in range(recipe[card_id] as int):
			deck.append(card_id)
	return deck


static func build_form_deck(form_id: String) -> Array[String]:
	var deck: Array[String] = []
	if not _FORM_RECIPES.has(form_id):
		return deck
	var recipe: Dictionary = _FORM_RECIPES[form_id]
	for card_id in recipe:
		for _copy in range(recipe[card_id] as int):
			deck.append(card_id)
	return deck


static func validate() -> Array[String]:
	var issues: Array[String] = []
	var snake_regex = RegEx.new()
	snake_regex.compile("^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$")
	var effect_owners: Dictionary = { }

	for dictionary_key in _CARDS:
		var card: Dictionary = _CARDS[dictionary_key]
		for field in _FIELD_TYPES:
			if not card.has(field):
				issues.append("%s: 缺少字段 %s" % [dictionary_key, field])
			elif typeof(card[field]) != _FIELD_TYPES[field]:
				issues.append("%s.%s: 字段类型错误" % [dictionary_key, field])

		var card_id = str(card.get("id", ""))
		var effect = str(card.get("effect", ""))
		if card_id != dictionary_key:
			issues.append("%s: id 与字典键不一致" % dictionary_key)
		if snake_regex.search(card_id) == null:
			issues.append("%s: id 不是英文 snake_case" % dictionary_key)
		if snake_regex.search(effect) == null:
			issues.append("%s: effect 不是英文 snake_case" % dictionary_key)
		if effect_owners.has(effect):
			issues.append("%s: effect 与 %s 重复" % [dictionary_key, effect_owners[effect]])
		else:
			effect_owners[effect] = dictionary_key

		if str(card.get("name", "")).is_empty():
			issues.append("%s: name 为空" % dictionary_key)
		if str(card.get("description", "")).is_empty():
			issues.append("%s: description 为空" % dictionary_key)
		if not ["miku", "weiting"].has(card.get("owner", "")):
			issues.append("%s: owner 非法" % dictionary_key)
		if not _ALLOWED_KINDS.has(card.get("kind", "")):
			issues.append("%s: kind 非法" % dictionary_key)
		if not _ALLOWED_DAMAGE_TYPES.has(card.get("damage_type", "")):
			issues.append("%s: damage_type 非法" % dictionary_key)
		if not _ALLOWED_TARGETS.has(card.get("target", "")):
			issues.append("%s: target 非法" % dictionary_key)
		for numeric_field in ["ap", "mp", "damage"]:
			if int(card.get(numeric_field, -1)) < 0:
				issues.append("%s: %s 不能为负数" % [dictionary_key, numeric_field])
		if int(card.get("damage", 0)) > 0 and str(card.get("damage_type", "")).is_empty():
			issues.append("%s: 有伤害但 damage_type 为空" % dictionary_key)

		var colors = card.get("colors", [])
		if colors is Array:
			if colors.is_empty() or colors.size() > 2:
				issues.append("%s: colors 必须包含1或2项" % dictionary_key)
			var unique_colors: Dictionary = { }
			for color in colors:
				if not _ALLOWED_COLORS.has(color):
					issues.append("%s: 非法颜色 %s" % [dictionary_key, str(color)])
				if unique_colors.has(color):
					issues.append("%s: colors 存在重复项" % dictionary_key)
				unique_colors[color] = true
			if colors.has("neutral") and colors.size() != 1:
				issues.append("%s: neutral 不能组成双色牌" % dictionary_key)

		var form = str(card.get("form", ""))
		var kind = str(card.get("kind", ""))
		if kind.begins_with("form_"):
			if not _FORM_OWNERS.has(form):
				issues.append("%s: 形态牌缺少合法 form" % dictionary_key)
			elif form != "" and card.get("owner", "") != _FORM_OWNERS[form]:
				issues.append("%s: form 与 owner 不匹配" % dictionary_key)
		elif not form.is_empty():
			issues.append("%s: 主卡的 form 必须为空" % dictionary_key)

	for owner_id in ["miku", "weiting"]:
		if not _MAIN_RECIPES.has(owner_id):
			issues.append("%s: 缺少主卡组配方" % owner_id)
			continue
		var recipe: Dictionary = _MAIN_RECIPES[owner_id]
		var total = 0
		for card_id in recipe:
			var copies = int(recipe[card_id])
			total += copies
			if copies <= 0:
				issues.append("%s/%s: 构筑数量必须为正数" % [owner_id, card_id])
			if not _CARDS.has(card_id):
				issues.append("%s/%s: 卡牌未定义" % [owner_id, card_id])
				continue
			var card: Dictionary = _CARDS[card_id]
			if card.get("owner", "") != owner_id:
				issues.append("%s/%s: owner 不匹配" % [owner_id, card_id])
			if str(card.get("form", "")) != "" or str(card.get("kind", "")).begins_with("form_"):
				issues.append("%s/%s: 形态牌不能进入主卡组" % [owner_id, card_id])
			var copy_limit = 1 if card.get("kind", "") == "ascension" else 3
			if copies > copy_limit:
				issues.append("%s/%s: 超过同名牌上限 %d" % [owner_id, card_id, copy_limit])
		if total != 30:
			issues.append("%s: 主卡组应为30张，实际为%d张" % [owner_id, total])
		_validate_built_deck(owner_id, build_main_deck(owner_id), recipe, issues)

	for form_id in _FORM_OWNERS:
		if not _FORM_RECIPES.has(form_id):
			issues.append("%s: 缺少形态卡组配方" % form_id)
			continue
		var recipe: Dictionary = _FORM_RECIPES[form_id]
		var total = 0
		for card_id in recipe:
			var copies = int(recipe[card_id])
			total += copies
			if copies <= 0:
				issues.append("%s/%s: 构筑数量必须为正数" % [form_id, card_id])
			if not _CARDS.has(card_id):
				issues.append("%s/%s: 卡牌未定义" % [form_id, card_id])
				continue
			var card: Dictionary = _CARDS[card_id]
			if card.get("form", "") != form_id:
				issues.append("%s/%s: form 不匹配" % [form_id, card_id])
			if card.get("owner", "") != _FORM_OWNERS[form_id]:
				issues.append("%s/%s: owner 不匹配" % [form_id, card_id])
			if not str(card.get("kind", "")).begins_with("form_"):
				issues.append("%s/%s: 主卡不能进入形态卡组" % [form_id, card_id])
		if total != 10:
			issues.append("%s: 形态卡组应为10张，实际为%d张" % [form_id, total])
		_validate_built_deck(form_id, build_form_deck(form_id), recipe, issues)

	return issues


static func _validate_built_deck(
	deck_id: String,
	deck: Array[String],
	recipe: Dictionary,
	issues: Array[String]
) -> void:
	var actual_counts: Dictionary = { }
	for card_id in deck:
		actual_counts[card_id] = int(actual_counts.get(card_id, 0)) + 1
	for card_id in recipe:
		var expected = int(recipe[card_id])
		var actual = int(actual_counts.get(card_id, 0))
		if actual != expected:
			issues.append("%s/%s: 展开数量应为%d，实际为%d" % [deck_id, card_id, expected, actual])
	for card_id in actual_counts:
		if not recipe.has(card_id):
			issues.append("%s/%s: 展开牌组出现配方外卡牌" % [deck_id, card_id])
