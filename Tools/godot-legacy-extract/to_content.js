// 把反编译出的 card_database.gd / boss_database.gd / battle_state.gd 转成
// Assets/StreamingAssets/Content/ 下的内容包 JSON。
//
// 映射规则全部来自 Assets/docs/data_architecture.md §5，表格在本文件里原样落地，
// 改归类 / 改裁定时请同步两边。
//
//   node to_content.js ./src ../../Assets/StreamingAssets/Content

const fs = require('fs');
const path = require('path');

const srcDir = process.argv[2] || path.join(__dirname, 'src');
const outDir = process.argv[3] || path.join(__dirname, '..', '..', 'Assets', 'StreamingAssets', 'Content');

// ───────────────────────────── GDScript const 提取 ─────────────────────────────

function readConst(file, name) {
  const text = fs.readFileSync(path.join(srcDir, file), 'utf8');
  const head = new RegExp(`const\\s+${name}\\s*=\\s*`).exec(text);
  if (!head) throw new Error(`${file}: 找不到 const ${name}`);

  let i = head.index + head[0].length;
  const open = text[i];
  const close = open === '{' ? '}' : ']';
  let depth = 0, inStr = false;
  for (let j = i; j < text.length; j++) {
    const c = text[j];
    if (inStr) { if (c === '\\') j++; else if (c === '"') inStr = false; continue; }
    if (c === '"') inStr = true;
    else if (c === open) depth++;
    else if (c === close && --depth === 0) {
      // GDScript 的字典/数组字面量正好是合法的 JS 字面量（含尾逗号）
      return new Function('return ' + text.slice(i, j + 1))();
    }
  }
  throw new Error(`${file}: const ${name} 括号未闭合`);
}

// ───────────────────────────── 映射表（= 文档 §5） ─────────────────────────────

const COLOR = { green: 'Dexterity', blue: 'Insight', purple: 'Willpower' }; // neutral → 空数组
const KIND = {
  skill: 'Skill', reaction: 'Timing', ongoing: 'Continuous',
  ascension: 'Transform', treasure: 'Skill',
  form_attack: 'Form', form_skill: 'Form', form_reaction: 'Form',
};
const TARGET = {
  enemy: 'SingleEnemy', all_enemies: 'AllEnemies',
  self: 'Self', ally: 'SingleAlly', none: 'Self',
};
const ELEMENT = {
  physical: 'Physical', wind: 'Wind', dark: 'Dark', lightning: 'Lightning',
  fire: 'Fire', ice: 'Ice', earth: 'Earth', light: 'Light', poison: 'Poison',
  arcane: 'Light',   // 裁定 R-21：规则书只有九属性，暂借 Light（对本场 Boss 行为等价）
  adaptive: 'Dark',  // 走 behaviour，元素占位
  random: 'Dark',
};

// §5.3 来源重新归类（裁定 R-22）。未列出的主卡 = 职业牌，形态牌一律职业牌。
const CATEGORY = {
  // 米阔阔：武器 8
  skirmish_shot: 'Weapon', rapid_arrow_rain: 'Weapon', suppressing_volley: 'Weapon',
  // 米阔阔：防具 7
  rolling_cloak: 'Armor', read_dodge: 'Armor', tactical_concealment: 'Armor',
  // 米阔阔：秘宝 3
  tailwind_charge: 'Treasure', verdant_wind_arrow: 'Treasure',
  // 维汀：武器 5
  weakness_analysis: 'Weapon', arcane_bolt: 'Weapon',
  // 维汀：防具 8
  arcane_barrier: 'Armor', sages_robe: 'Armor', lucky_reversal: 'Armor', emergency_catalyst: 'Armor',
  // 维汀：秘宝 4
  pain_inscription: 'Treasure', mana_overload_ritual: 'Treasure',
};

// 装备数值：由老卡面的「本 5 级构筑为 N 点」反推（见文档 §5.2）
//   B_atk = 武器一阶伤害 + L_bonus(1)，B_def = 防具一阶护盾 + L_bonus(1)
//   米阔阔 B_atk=8 → 武器 7；维汀 B_atk=7 → 武器 6；两人 B_def=8 → 防具 7
const WEAPON_BASE = { miku: 7, weiting: 6 };
const ARMOR_BASE = 7;

const PRIMARY_CLASS = { miku: '浪客', weiting: '混沌法师' };
const TRANSFORM_CLASS = {
  phantom_nightwalk: '浪客', hawkeye_overdrive: '神射手',
  cosmic_collapse: '混沌法师', truth_alchemy_matrix: '造物使',
};
const FORM_TO_TRANSFORM = {
  phantom: 'phantom_nightwalk', hawkeye: 'hawkeye_overdrive',
  cosmic: 'cosmic_collapse', alchemy: 'truth_alchemy_matrix',
};

// 带 shield_formula 的 4 张卡：公式是自由文本，逐张手工对映（文档 §5.2）
const SHIELDS = {
  smoke_decoy: [{ kind: 'Physical', add: 2 }],
  wind_wall_counter: [{ kind: 'Physical', add: 0 }, { kind: 'Magical', add: 0 }],
  time_paradox: [{ kind: 'Physical', add: 2 }, { kind: 'Magical', add: 2 }],
  alchemical_protection_field: [{ kind: 'Physical', add: 0 }, { kind: 'Magical', add: 0 }],
};
// 伤害是条件触发的，不能当成打出即结算 → 留给 behaviour
const DAMAGE_IS_CONDITIONAL = new Set(['wind_wall_counter']);

// 老卡里没有新模型对应概念的字段，原样留档（文档 §4.1）
const UNMAPPED_FIELDS = [
  'ranged', 'requires_back_row', 'can_target_self', 'damage_options',
  'target_group', 'self_damage', 'hits', 'damage_formula', 'shield_formula',
];

// ───────────────────────────── 卡牌转换 ─────────────────────────────

/** "B_atk + 4" / "B_atk * 2" / "B_atk" → ValueExpr 镜像 */
function parseFormula(formula) {
  const m = /^B_(atk|def)\s*(?:([+*])\s*(\d+))?$/.exec(formula.trim());
  if (!m) return null;
  const source = m[1] === 'atk' ? 'FormAttack' : 'FormDefense';
  if (!m[2]) return { source, add: 0 };
  return m[2] === '+' ? { source, add: Number(m[3]) } : { source, add: 0, times: Number(m[3]) };
}

function buildEffects(card) {
  const effects = [];
  const target = TARGET[card.target] ?? 'Self';

  for (const s of SHIELDS[card.id] ?? []) {
    effects.push({
      kind: 'GainShield',
      amount: { source: 'FormDefense', add: s.add },
      shieldKind: s.kind,
      target: card.target === 'ally' ? 'SingleAlly' : 'Self',
    });
  }

  if (!DAMAGE_IS_CONDITIONAL.has(card.id)) {
    let amount = null;
    if (card.damage_formula) amount = parseFormula(card.damage_formula);
    if (!amount && card.damage > 0) amount = { source: 'Flat', add: card.damage };

    if (amount) {
      // hits: N → N 条独立 Damage（裁定 R-23）
      for (let i = 0; i < (card.hits ?? 1); i++) {
        effects.push({
          kind: 'Damage', amount,
          element: ELEMENT[card.damage_type] ?? 'Physical',
          target: target === 'Self' ? 'SingleEnemy' : target,
        });
      }
    }
  }

  if (card.self_damage > 0) {
    effects.push({
      kind: 'Damage',
      amount: { source: 'Flat', add: card.self_damage },
      element: 'Dark', target: 'Self',
    });
  }

  return effects;
}

function convertCard(card) {
  const isForm = card.kind.startsWith('form_');
  const classId = isForm
    ? TRANSFORM_CLASS[FORM_TO_TRANSFORM[card.form]]
    : TRANSFORM_CLASS[card.id] ?? PRIMARY_CLASS[card.owner];

  const category = isForm ? 'Class' : CATEGORY[card.id] ?? 'Class';

  const out = {
    id: card.id,
    name: card.name,
    owner: card.owner,
    category,
    kind: KIND[card.kind],
    classId,
    colors: card.colors.filter(c => c !== 'neutral').map(c => COLOR[c]),
    ap: card.ap,
    mp: card.mp,
    charge: 0,
    hasCrit: card.crit,
    rulesText: card.description,
    behaviourId: card.effect,
    effects: buildEffects(card),
  };

  if (isForm) out.formId = card.form;
  if (category === 'Weapon') { out.weaponBaseDamage = WEAPON_BASE[card.owner]; out.twoHanded = true; }
  if (category === 'Armor') { out.armorBaseShield = ARMOR_BASE; out.shieldKind = 'Physical'; }

  const unmapped = {};
  for (const f of UNMAPPED_FIELDS) if (card[f] !== undefined) unmapped[f] = card[f];
  if (card.damage_type === 'arcane') unmapped.damageType = 'arcane'; // R-21 留痕
  if (card.damage > 0 && card.damage_formula) {
    // 不变式：有 legacyDamageAtLevel5 ⇒ 卡上有一条能算回该值的 Damage 效果（测试 T-5 据此断言）。
    // 条件触发的伤害没有落成效果，换个键名记录，免得破坏这条不变式。
    const key = DAMAGE_IS_CONDITIONAL.has(card.id) ? 'conditionalDamageAtLevel5' : 'legacyDamageAtLevel5';
    unmapped[key] = card.damage;
  }
  if (Object.keys(unmapped).length) out.unmapped = unmapped;

  return out;
}

// ───────────────────────────── 主流程 ─────────────────────────────

const CARDS = readConst('card_database.gd', '_CARDS');
const MAIN_RECIPES = readConst('card_database.gd', '_MAIN_RECIPES');
const FORM_RECIPES = readConst('card_database.gd', '_FORM_RECIPES');
const INTENTS = readConst('boss_database.gd', 'INTENTS');
const PHASE_ONE = readConst('boss_database.gd', 'PHASE_ONE');
const PHASE_TWO = readConst('boss_database.gd', 'PHASE_TWO');

const converted = Object.values(CARDS).map(convertCard);
const byId = Object.fromEntries(converted.map(c => [c.id, c]));

function write(rel, obj) {
  const dest = path.join(outDir, rel);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.writeFileSync(dest, JSON.stringify(obj, null, 2) + '\n', 'utf8');
  console.log('写出', rel);
}

// 卡牌：按归属拆三个文件
const mainOf = owner => converted.filter(c => c.owner === owner && !c.formId);
write('cards/miku.json', { cards: mainOf('miku') });
write('cards/weiting.json', { cards: mainOf('weiting') });
write('cards/forms.json', { cards: converted.filter(c => c.formId) });

// 角色
write('characters/heroes.json', {
  characters: [
    {
      id: 'miku', name: '米阔阔', subtitle: '浪客 · 神射手',
      classes: [{ classId: '浪客', level: 3 }, { classId: '神射手', level: 2 }],
      dice: { Might: 'D6', Insight: 'D10', Dexterity: 'D10', Willpower: 'D8' },
      // 老面板 40/35 与 §2.1 公式算出的 35/45 对不上，用加值钉死（文档 §5.4）
      hpBonus: 5, mpBonus: -10,
      startingRow: 'Back', chargeCapacity: 2,
      weaponCardId: 'skirmish_shot', armorCardId: 'rolling_cloak',
      deckId: 'miku_starter',
      legacy: { hp: 40, mp: 35 },
    },
    {
      id: 'weiting', name: '维汀', subtitle: '混沌法师 · 造物使',
      classes: [{ classId: '混沌法师', level: 4 }, { classId: '造物使', level: 1 }],
      dice: { Might: 'D8', Insight: 'D10', Dexterity: 'D8', Willpower: 'D8' },
      hpBonus: -10, mpBonus: 5,
      startingRow: 'Front', chargeCapacity: 2,
      weaponCardId: 'weakness_analysis', armorCardId: 'arcane_barrier',
      deckId: 'weiting_starter',
      legacy: { hp: 35, mp: 50, autoMagicShieldBonus: 1 },
    },
  ],
});

// 敌人
const INTENT_TARGETING = {
  front_all: ['FrontRowAoe', 'Front'],
  both_rows: ['AllPlayers', 'Front'],
  all_heroes: ['AllPlayers', 'Front'],
  single: ['DefaultSingle', 'Front'],
  locked_hero: ['DesignatedRowSingle', 'Back'],
  highest_hp: ['DefaultSingle', 'Front'],
  self: ['SelfOrNone', 'Front'],
};

write('enemies/chronos_iv.json', {
  enemies: [
    {
      id: 'boss', name: '克洛诺斯-IV型', subtitle: '裂界发条巨像',
      hp: 140, rank: 'Boss', critResist: 7, row: 'Back',
      affinities: {
        Lightning: 'Vulnerable', Dark: 'Vulnerable',
        Ice: 'Resistant', Earth: 'Resistant', Poison: 'Immune',
      },
      intentPools: { phaseOne: PHASE_ONE, phaseTwo: PHASE_TWO },
      intents: Object.entries(INTENTS)
        .filter(([id]) => id !== 'shield_quake')
        .map(([id, it]) => {
          const [targeting, row] = INTENT_TARGETING[it.target] ?? ['DefaultSingle', 'Front'];
          return {
            id, name: it.name, category: it.category,
            damage: it.damage, element: ELEMENT[it.element] ?? 'Physical',
            targeting, designatedRow: row, hits: it.hits ?? 1,
            text: it.description,
            unmapped: { target: it.target, damageClass: it.damage_class, mp: it.mp, pierce: it.pierce ?? false },
          };
        }),
    },
    {
      id: 'arm', name: '镇压重盾臂', subtitle: '伴生部位 · 前排护航',
      hp: 35, rank: 'Normal', critResist: 5, row: 'Front',
      affinities: { Wind: 'Vulnerable', Physical: 'Resistant', Fire: 'Resistant', Poison: 'Immune' },
      intentPools: { phaseOne: ['shield_quake'], phaseTwo: ['shield_quake'] },
      intents: [{
        id: 'shield_quake', name: INTENTS.shield_quake.name, category: INTENTS.shield_quake.category,
        damage: INTENTS.shield_quake.damage, element: 'Physical',
        targeting: 'FrontRowAoe', designatedRow: 'Front', hits: 1,
        text: INTENTS.shield_quake.description,
      }],
    },
  ],
});

// 卡组
function deckJson(id, name, owner) {
  const forms = Object.entries(FORM_RECIPES)
    .filter(([formId]) => byId[FORM_TO_TRANSFORM[formId]].owner === owner)
    .map(([formId, recipe]) => ({
      transformCard: FORM_TO_TRANSFORM[formId],
      cards: Object.entries(recipe).map(([card, count]) => ({ card, count })),
    }));

  return {
    id, name, characterId: owner,
    main: Object.entries(MAIN_RECIPES[owner]).map(([card, count]) => ({ card, count })),
    formDecks: forms,
  };
}

write('decks/miku_starter.json', deckJson('miku_starter', '米阔阔 · 起始构筑', 'miku'));
write('decks/weiting_starter.json', deckJson('weiting_starter', '维汀 · 起始构筑', 'weiting'));

write('pack.json', {
  id: 'clockwork_collapse', name: '命刻之战', version: '1.0.0', schemaVersion: 1,
  source: '《最终物语·命刻之战》Windows v1.0.0（Godot 4.4.1）反编译迁移',
  cards: ['cards/miku.json', 'cards/weiting.json', 'cards/forms.json'],
  characters: ['characters/heroes.json'],
  enemies: ['enemies/chronos_iv.json'],
  decks: ['decks/miku_starter.json', 'decks/weiting_starter.json'],
});

// ───────────────────────────── 自检 ─────────────────────────────

console.log('\n── 自检 ──');
console.log('卡牌总数', converted.length, '(应为 49)');
for (const owner of ['miku', 'weiting']) {
  const counts = {};
  let total = 0;
  for (const [cardId, n] of Object.entries(MAIN_RECIPES[owner])) {
    counts[byId[cardId].category] = (counts[byId[cardId].category] ?? 0) + n;
    total += n;
  }
  console.log(`${owner}: 共 ${total} 张 →`, counts);
}
for (const [formId, recipe] of Object.entries(FORM_RECIPES)) {
  const n = Object.values(recipe).reduce((a, b) => a + b, 0);
  console.log(`形态组 ${formId}: ${n} 张 (应为 10)`);
}
const noEffect = converted.filter(c => c.effects.length === 0);
console.log(`无数据效果（纯 behaviour 留桩）的卡：${noEffect.length} / ${converted.length}`);
