# 真实数据层架构 · v1

> 配套：规则书 [v0_5.md](v0_5.md) · 引擎架构 [architecture_v0_5.md](architecture_v0_5.md) · 裁定表 [rulings_v0_5.md](rulings_v0_5.md)
> 目标：把《最终物语·命刻之战》v1.0.0（Godot 版）的真实卡牌 / 角色 / 首领数据接进本项目，
> 并且**为玩家自定义内容留好扩展口**。
> 约定：本系统所有文档统一放在 `Assets/docs/`。

---

## 0. 决策摘要

| # | 决策 | 取值 |
| --- | ----- | ----- |
| D-1 | 真实数据的存储格式 | **JSON 为唯一真源**，放 `StreamingAssets`，由纯 C# 的 `TCGinFU.Content` 解析 |
| D-2 | 老卡组违反 §3.2 构筑配比 | **按卡面功能重新归类**到 职业/武器/防具/秘宝 四类（§5.3 给出逐张归类表） |
| D-3 | 本轮迁移深度 | **只迁卡面数据，行为留桩**：49 张卡的数值与文本全进，特殊逻辑记成 `behaviourId` 占位 |

D-1 的三条约束（任一条都足以否掉 ScriptableObject 方案）：

1. **玩家自定义**要求数据是玩家能用记事本打开的真实文件，SO 必须开 Unity 编辑器才能产出；
2. `Assets/Scripts/Core` 设了 `noEngineReferences`，是为了 `dotnet test` 能脱离 Unity 秒级跑完规则测试
   （见 [architecture_v0_5.md](architecture_v0_5.md) §3.1）。数据若是 SO，这些测试就够不到真实卡池；
3. 现状诊断：`TCGinFU.Data` 这层**已经是死代码**——0 个 `.asset` 资产，
   且没有任何 asmdef 引用它（`Presentation` / `View` / `Sandbox` / `Tests` 引的都是 `TCGinFU.Content`）。
   把真实数据压在一个没人用的层上，只会再造一份对不上的真相。

> `TCGinFU.Data` 本轮**不删也不改**。它作为「将来要做可视化构筑编辑器」的壳保留；
> 真到那一步，它应当是 JSON 的**编辑器前端**，而不是第二份真源。

---

## 1. 数据从哪来：来源与可复现性

老项目源码已丢失，数据是从发行版可执行文件里反编译出来的。全过程可复现，工具提交在 `Tools/godot-legacy-extract/`：

```
runtime/game_bin.exe   (97 MB, Godot 4.4.1 自包含导出)
  └─ 尾部 12 字节 [int64 pck_size][magic "GDPC"]  → 内嵌 PCK，偏移 97447936，149044 字节
     └─ extract_pck.js   PCK v2，pack_flags=2(PACK_REL_FILEBASE) → 文件基址 = pck起点 + file_base
        └─ scripts/*.gdc  GDSC v100 二进制 token 流，zstd 压缩
           └─ gdc_decompile.js  → scripts/*.gd  (3992 行可读源码)
```

`.gdc` 不是字节码而是**词法 token 流**，常量表与标识符表完整保留，所以反编译结果在
语义上与原始源码等价（仅丢失注释与空行）。校验方式：`main.gd` 还原出
`extends Control` / `func _ready() -> void:`，与 Godot 的 token 枚举逐项对上。

**真实数据的权威来源**是反编译出的这 4 个文件：

| 文件 | 内容 |
| ----- | ----- |
| `card_database.gd` | 49 张卡的全部字段 + 2 份 30 张主卡组配方 + 4 份 10 张形态组配方 |
| `boss_database.gd` | 11 个首领意图 + 两阶段意图池 |
| `battle_state.gd` | 2 名角色与 2 个敌人单位的面板数值、命刻/过热/相位等场景机制 |
| `card_resolver.gd` | 每张卡的特殊结算逻辑（本轮只读不迁，见 §6） |

---

## 2. 目录布局

```
Assets/
  StreamingAssets/
    Content/
      pack.json                 内容包清单：id、显示名、版本、schemaVersion
      cards/
        miku.json               米阔阔主卡池（16 种）
        weiting.json            维汀主卡池（17 种）
        forms.json              4 组形态牌（16 种）
      characters/
        heroes.json             米阔阔、维汀
      enemies/
        chronos_iv.json         克洛诺斯-IV型 + 镇压重盾臂 + 11 个意图
      decks/
        miku_starter.json       30 张 + 2 组形态组
        weiting_starter.json    30 张 + 2 组形态组
  Scenes/
    Battle.unity                原有：样例卡组 + 哥布林
    ClockworkCollapse.unity     ★ 新增：真实数据的测试场景
  Scripts/
    Content/                    TCGinFU.Content.asmdef（noEngineReferences = true）
      Json/                     ★ Json.cs / JsonValue.cs —— 极简只读解析器
      Loading/                  ★ ContentLoader / ContentDatabase / FileContentSource
      SampleCards.cs 等          原有样例内容，保留不动
    Presentation/
      ContentScenario.cs        ★ 把内容包组装成一局战斗
```

**为什么是 `StreamingAssets` 而不是 `Resources` 或 `Assets/Content`**：
`StreamingAssets` 下的文件在构建产物里**保持为磁盘上的原始文件**，不被打包进 assetbundle。
玩家拿到的就是一个能直接编辑的 `Content/cards/miku.json`——这正是 D-1 要的扩展口。
`Resources` 会把 JSON 压进资源包，`Assets/Content` 则根本不进构建。

三个入口读同一批文件，没有第二条路径：

| 场景 | 根目录 |
| ----- | ----- |
| Unity 播放 / 构建产物 | `Application.streamingAssetsPath + "/Content"` |
| `dotnet test`（Unity 之外） | 仓库内相对路径 `Assets/StreamingAssets/Content` |
| 玩家自定义包 | `Application.persistentDataPath + "/Content/<packId>"` |

---

## 3. 加载管线

```
*.json ──(string)──▶ Json.Parse ──▶ JsonValue ──▶ CardJson.ToDefinition()  ──┐
                                                  CharacterJson.ToSheet()   ├─▶ ContentDatabase
                                                  EnemyJson.ToEnemy()       │     按 id 索引
                                                  DeckJson.ToDeckList()  ───┘
```

关键约定：

- **`ContentLoader` 不碰文件系统**。它只接受 `Func<string, string>`（相对路径 → 文本），
  由调用方决定内容是 `File.ReadAllText` 来的还是 `TextAsset.text` 来的，测试也不必 mock 文件系统。
  配套的 `FileContentSource` / `ContentPaths` 负责把一个目录包装成那个委托——
  它们用的是 BCL 的 `System.IO` 而不是 `UnityEngine`，所以 `noEngineReferences` 依然成立。
  `ContentPaths.Resolve()` 按「显式参数 → 环境变量 `TCGINFU_CONTENT_DIR` → 从工作目录逐级向上找
  `Assets/StreamingAssets/Content`」定位，三个入口因此共用同一份数据。
- **自带 JSON 解析器**。项目没有 Newtonsoft（`Packages/manifest.json` 里没有），
  而 `JsonUtility` 属于 `UnityEngine`，在 `noEngineReferences` 的程序集里用不了。
  所以在 `Content/Json/` 放一个约 200 行的只读递归下降解析器：
  无反射、无依赖、netstandard2.1 与 net8.0 都能编。
  它只需支持对象 / 数组 / 字符串 / 数字 / 布尔 / null —— 内容数据用不到更多。
- **缺字段用默认值，不抛异常；非法值要报错**。
  `ContentLoader` 返回 `LoadReport`（沿用 `DeckValidationResult` 的风格：收集**全部**问题而不是第一条），
  玩家改坏一个字段时应当看到「第 37 行 colors 里的 `greeen` 不是合法颜色」，而不是整个游戏起不来。

### 3.1 内容包与覆盖顺序

`ContentDatabase` 按包加载，后加载的包**按 id 覆盖**先前的条目：

```
base（StreamingAssets/Content）  →  玩家包 A  →  玩家包 B
```

这让玩家自定义有两种粒度：改一张既有卡（同 id 覆盖），或加一张新卡（新 id）。
`pack.json` 里的 `schemaVersion` 用来在格式演进后给出可读的报错，而不是静默读出错值。

---

## 4. JSON 模式

### 4.1 卡牌

```json
{
  "id": "ambush_opening",
  "name": "暗算起手",
  "owner": "miku",
  "category": "Class",
  "kind": "Skill",
  "classId": "浪客",
  "colors": ["Dexterity", "Insight"],
  "ap": 1,
  "mp": 5,
  "charge": 0,
  "hasCrit": true,
  "rulesText": "对单个目标造成5点物理伤害【暴击】。命中后令目标获得【破绽】标记：本轮内下一次受到的任意伤害+3。",
  "behaviourId": "ambush_opening",
  "effects": [
    { "kind": "Damage", "amount": { "source": "Flat", "add": 5 },
      "element": "Physical", "target": "SingleEnemy" }
  ],
  "unmapped": { "ranged": false }
}
```

- 枚举字段一律写**枚举名**（`"Dexterity"` 而不是 `2`）。数字会在枚举增删项时静默错位。
- `amount` 是 `ValueExpr` 的镜像：`source` / `add` / `times` / `dividedBy`，后两者缺省为 1。
  形态牌的 `B_atk + 4` 写成 `{ "source": "FormAttack", "add": 4 }`。
- **`unmapped` 是有意保留的**。老数据里 `ranged` / `requires_back_row` / `can_target_self` /
  `damage_options` 这些字段在当前 Core 里没有对应概念。丢掉它们等于让迁移不可逆；
  原样留在 `unmapped` 下，将来实现对应机制时直接就能对账。它不参与任何结算。

### 4.2 角色

```json
{
  "id": "miku",
  "name": "米阔阔",
  "subtitle": "浪客 · 神射手",
  "classes": [{ "classId": "浪客", "level": 3 }, { "classId": "神射手", "level": 2 }],
  "dice": { "Might": "D6", "Insight": "D10", "Dexterity": "D10", "Willpower": "D8" },
  "hpBonus": 5,
  "mpBonus": -10,
  "startingRow": "Back",
  "deckId": "miku_starter",
  "legacy": { "hp": 40, "mp": 35 }
}
```

`hpBonus` / `mpBonus` 的来历见 §5.4——老游戏的面板是手写死的，和规则书 §2.1 的公式对不上。

### 4.3 敌人

```json
{
  "id": "boss", "name": "克洛诺斯-IV型", "subtitle": "裂界发条巨像",
  "hp": 140, "rank": "Boss", "critResist": 7, "row": "Back",
  "affinities": { "Lightning": "Vulnerable", "Dark": "Vulnerable",
                  "Ice": "Resistant", "Earth": "Resistant", "Poison": "Immune" },
  "intentPools": {
    "phaseOne": ["gravity_sweep", "dimension_cannon", "aether_siphon",
                 "clockwork_volley", "phase_shift", "armor_reconstruction"],
    "phaseTwo": ["time_guillotine", "fate_turbulence", "causal_erasure"]
  },
  "intents": [
    { "id": "gravity_sweep", "name": "重力横扫", "damage": 12,
      "element": "Physical", "targeting": "FrontRowAoe",
      "text": "前排全体受到12点物理伤害并被击退至后排；前排无人时，后排受到4点震波伤害。" }
  ]
}
```

`Enemy.IntentPlanner` 在 Core 里本来就是个委托，所以「按阶段抽意图」可以完全在 Content 层
用数据拼出来，**不需要动 Core**。

### 4.4 卡组

```json
{
  "id": "miku_starter", "name": "米阔阔 · 起始构筑", "characterId": "miku",
  "main": [{ "card": "ambush_opening", "count": 2 }],
  "formDecks": [{ "transformCard": "phantom_nightwalk",
                  "cards": [{ "card": "shadow_dash_assault", "count": 3 }] }]
}
```

---

## 5. 老数据 → 新模型的映射

### 5.1 枚举映射

| 老字段 | 老值 | 新值 |
| ----- | ----- | ----- |
| `colors` | `green` / `blue` / `purple` / `neutral` | `Dexterity` / `Insight` / `Willpower` / **空数组** |
| `kind` | `skill` | `CardKind.Skill` |
| | `reaction` | `CardKind.Timing` |
| | `ongoing` | `CardKind.Continuous` |
| | `ascension` | `CardKind.Transform` |
| | `treasure` | `CardKind.Skill` + `CardCategory.Treasure` |
| | `form_attack` / `form_skill` / `form_reaction` | `CardKind.Form` |
| `target` | `enemy` / `all_enemies` / `self` / `ally` | `SingleEnemy` / `AllEnemies` / `Self` / `SingleAlly` |
| `damage_type` | `physical/wind/dark/lightning/fire/ice/earth/light/poison` | 同名 `DamageElement` |
| | `arcane` | **待裁定，见 §7 R-21** |
| | `adaptive` / `random` | 无数据对应，走 `behaviourId`；`element` 暂填 `Dark` 并记入 `unmapped` |

`neutral` 映射成**空颜色数组**而不是某个具体颜色：规则书 §2.2 的颜色决定投哪颗暴击骰，
老游戏里 neutral 牌全部 `crit: false`（不投骰），所以空数组是忠实的。

### 5.2 数值字段

| 老字段 | 处理 |
| ----- | ----- |
| `damage` + `damage_type` | 一条 `EffectData.Damage(Flat(damage), element)` |
| `damage_formula: "B_atk + 4"` | 改用 `{ "source": "FormAttack", "add": 4 }`，**不再用硬编码的 `damage`** |
| `damage_formula: "B_atk * 2"` | `{ "source": "FormAttack", "times": 2 }` |
| `shield_formula: "B_def + 2"` | `EffectKind.GainShield` + `{ "source": "FormDefense", "add": 2 }` |
| `hits: 2` | 展开成 **2 条**相同的 `Damage` 效果（见 §7 R-23） |
| `self_damage: 2` | 一条指向 `Self` 的 `Damage`，记 `behaviourId` 因为老逻辑是无视护盾的 |
| `crit: true` | `hasCrit: true` |

形态牌原本既有 `damage`（5 级构筑下的具体值）又有 `damage_formula`。
采用公式、丢弃具体值，是因为 §2.3 的整个 `ValueExpr` 机制就是为此存在的——
同一张资产在 5 级和 50 级应当各自算出 12 和 27 点，而不是永远 12 点。
老数据里那个具体值作为**迁移自检**：用 5 级面板求值，应当算回老值。

### 5.3 卡牌来源重新归类（D-2）

老游戏没有武器牌 / 防具牌的概念，30 张主卡组直接映射会变成「职业牌 27 / 秘宝牌 3」，
过不了 §3.2 的 `DeckValidator`（职业 12–16 / 武器 3–8 / 防具 3–8 / 秘宝 0–6）。
按卡面**实际功能**归类如下，两份卡组都恰好落进合法区间：

**米阔阔**（职业 12 / 武器 8 / 防具 7 / 秘宝 3 = 30）

| 来源 | 卡 × 张数 |
| ----- | ----- |
| 职业 12 | 幻影夜行 1、鹰眼绝境 1（升变）、鹰眼锁定 1（持续）、暗算起手 2、卑劣打击·壹式 2、先声夺人 2、交叉火力网 1、穿云狙击 2 |
| 武器 8 | 游击速射 3、连珠箭雨 3、压制连射 2 —— 均为 `ranged: true` 的纯射击牌 |
| 防具 7 | 翻滚披风 3、看破闪避 2（响应牌）、战术隐蔽 2（纯加盾） |
| 秘宝 3 | 顺风充能 2、翠风箭附 1 |

**维汀**（职业 13 / 武器 5 / 防具 8 / 秘宝 4 = 30）

| 来源 | 卡 × 张数 |
| ----- | ----- |
| 职业 13 | 星穹坍缩 1、真理炼金矩阵 1（升变）、灵智回流·常驻 2（持续）、苦痛转导 2、命运锚点·七 1、熵之轮盘 2、暗言术·活力汲取 1、深渊虹吸 2、调合·万能灵药 1 |
| 武器 5 | 弱点解析 3、秘法冲击 2 —— 维汀的基础法术攻击，对应她的法器 |
| 防具 8 | 奥术结界 3、睿智法袍 2、幸运改命 2、应急催化剂 1 |
| 秘宝 4 | 痛觉铭刻 2、魔力过载仪式 2 |

> 归类口径：**响应/加盾 → 防具；无职业绑定的基础攻击 → 武器；
> 带职业特色的技能、持续、升变 → 职业；自带仪式/道具叙事的 → 秘宝。**
> 这是一次设计判断而非机械转换，需要规则设计者复核。改动成本很低：
> 只是 JSON 里的一个 `category` 字段，不涉及任何代码。

### 5.4 角色面板的公式冲突

老游戏的 HP/MP 是写死的，和规则书 §2.1 的派生公式对不上：

| 角色 | 骰面 | 公式算出 | 老游戏值 | 差额 |
| ----- | ----- | ----- | ----- | ----- |
| 米阔阔 | 力 d6 / 意 d8，总 5 级 | HP 35 / MP 45 | HP 40 / MP 35 | `hpBonus +5` / `mpBonus −10` |
| 维汀 | 力 d8 / 意 d8，总 5 级 | HP 45 / MP 45 | HP 35 / MP 50 | `hpBonus −10` / `mpBonus +5` |

**取值：用 `hpBonus` / `mpBonus` 把老数值钉死。** 理由是这场 Boss 战的难度曲线是按
40/35 和 35/50 调出来的，换成公式值会让维汀瞬间多 10 点血、米阔阔少 10 点蓝，
已有的意图伤害数字（12 / 20 / 28）全部失准。

**代价要说清楚**：`CharacterSheet` 的字段注释写的是「职业与秘宝提供的加值」，
现在被当成「与老版本对齐的补正」用，而且会出现负值（`CharacterSheet` 不校验符号，能跑）。
这是个记账上的妥协，不是公式的胜利。真正的解法是规则设计者决定
**到底是公式改还是角色面板改**，定了之后把 bonus 清零。老值记在 JSON 的 `legacy` 里备查。

另外老游戏给维汀的自动魔法护盾特判了 `+1`（`insight / 2 + 1`）。
当前 Core 的 `AutoMagicShield` 是纯派生、无加值口子。本轮**不迁这条**，记入 §6 待办。

---

## 6. 本轮要动的代码（最小集）

只动与数据直接相关的部分，引擎规则与表现层一律不碰。

| # | 改动 | 位置 | 理由 |
| --- | ----- | ----- | ----- |
| C-1 | 新增 `CardDefinition.RulesText` | `Core/Cards/CardDefinition.cs` | 老数据 49 条卡面描述没有容身之处。纯新增只读字段，不参与任何结算 |
| C-2 | 新增 JSON 解析器 | `Content/Json/` | 见 §3，项目没有可用的 JSON 库 |
| C-3 | 新增 `ContentLoader` / `ContentDatabase` / `FileContentSource` | `Content/Loading/` | 本文的主体 |
| C-4 | 新增真实数据 JSON | `StreamingAssets/Content/` | 迁移产物 |
| C-5 | 新增 `ContentScenario` | `Presentation/ContentScenario.cs` | 把内容包组装成一局战斗；样例内容那套 `SampleDeck`/`SampleEnemies` 原样保留 |
| C-6 | `BattleScenario` 加一个枚举值 `命刻之战` + `Restart()` 分一个支 | `Presentation/BattleSession.cs` | 让真实数据能被场景和沙盒选中。内容包加载失败时退回样例卡组并报错，不黑屏 |
| C-7 | 新增菜单项「生成命刻之战场景」 | `View/Editor/BattleSceneBuilder.cs` | 原 `GenerateAndOpen` 抽出 `Generate(scenePath, scenario)`，行为不变 |
| C-8 | 新增测试 | `Tests/EditMode/LegacyContentTests.cs` | 见 §8 |

**不动**：`TCGinFU.Data`（整层）、`BattleEngine` 及其 partial、`DamageResolver`、
`CritResolver`、`DeckValidator`、`EffectRegistry`、`View/` 下除建场景工具外的一切。

留给后续（本轮只记账，不实现）：

- 49 张卡里约 40 张的 `behaviourId` 实际逻辑（D-3 决定留桩）
- 命刻（doom）时钟、首领两阶段切换、过热、归零坍缩、相位折跃的排位互换
- 部位破坏（镇压重盾臂被击破后首领移至前排、双盾清零、暴击抵抗 +1）
- 倒地 / 复苏 / 淘汰（3 轮）
- 魔力激发卷轴的开关与轮始自伤
- 维汀自动魔法护盾 +1 的特判口子
- 老数据的 `ranged` / `requires_back_row` / `can_target_self` 等字段对应的机制

---

## 7. 新增裁定

这几条是迁移过程中**规则书没有覆盖、但必须做选择**的点，口径沿用 [rulings_v0_5.md](rulings_v0_5.md)。

### R-21 ⚠ 老数据的 `arcane`（奥术）伤害属性无处安放

- **依据**：规则书 §7.4 明确只有九大属性：物理、风、雷、暗、地、火、冰、光、毒。老游戏有第 10 种 `arcane`。
- **老数据实情**：`arcane` 只用在「弱点解析」和「秘法冲击」两张卡上，
  卡面文字写的是「造成 N 点**魔法伤害**」——它表达的是**无属性魔法伤害**，不是某种元素。
- **备选**：
  - (a) 新增 `DamageElement.Arcane`，映射到魔法盾、对所有敌人恒为 `Normal` 亲和。
    改动面：枚举 1 项 + `ToShieldKind` 一个 switch 分支 + 显示名。语义最诚实。
  - (b) 映射到 `Light`。零代码改动，且对本场 Boss 战**行为完全等价**（克洛诺斯对光既不弱也不抗），
    但语义是错的，将来加一个「光属性弱点」的敌人就会错。
- **实现取值**：**暂取 (b) `Light`**，并在卡的 `unmapped` 里记 `"damageType": "arcane"`。
  因为 D-3 限定本轮不改引擎语义，而 (a) 动的是 Core 枚举。
- **不确定度**：高。这条**需要规则设计者拍板**——若认可「无属性魔法伤害」是个合法概念，应当改走 (a)。

### R-22 老卡牌的来源归类

- **依据**：§3.2 要求 30 张里武器 3–8、防具 3–8；老游戏无此概念。
- **实现**：按卡面功能人工归类，见 §5.3。
- **不确定度**：中。纯数据改动，复核后改 JSON 即可。

### R-23 `hits: N` 展开为 N 条独立效果

- **依据**：老卡「压制连射」写「连续射击 2 次，每次造成 5 点……两次射击**分别独立**投掷敏捷骰判定暴击」。
- **实现**：展开成 2 条 `Damage`。
- **风险**：当前 `BattleEngine` 的暴击是**每次打牌投一次**，不是每条效果投一次，
  所以展开后两条共享同一个暴击结果，与老卡面「分别独立判定」**不一致**。
- **实现取值**：先展开（伤害总量正确），把「多段独立暴击」记入 §6 待办。
- **不确定度**：中。数值对、手感不对。

---

## 8. 验收

判据写在 `Tests/EditMode/LegacyContentTests.cs`，读的是真实内容包。
**现状：全部通过（Unity EditMode 与 `dotnet test` 各 175 项，含原有 160 项）。**

| # | 断言 | 结果 |
| --- | ----- | ----- |
| T-1 | 内容包加载零错误；卡牌 49 张、角色 2 名、敌人 2 个、卡组 2 份 | ✅ |
| T-2 | 两份主卡组各 30 张，4 组形态组各 10 张 | ✅ |
| T-3 | 两份卡组都通过 `DeckValidator`，且来源配比与 §5.3 逐项相符 | ✅ |
| T-4 | `behaviourId` 唯一（老 `validate()` 也查这条）；49 张卡都有卡面文字 | ✅ |
| T-5 | 形态牌用 5 级面板对公式求值 = 老数据里的具体 `damage` 值 | ✅ |
| T-6 | 角色面板 = 老游戏值（米阔阔 40/35，维汀 35/50）；B_atk / B_def 回到 8 / 7 / 8 | ✅ |
| T-7 | 用真实数据开局，且 `SkipToEnd()` 能推进到分出胜负而不是卡死 | ✅ |

> T-5 的老数值取自卡的 `unmapped.legacyDamageAtLevel5`，不是写死在测试里的——
> 改了装备数值或公式映射，这条会立刻红。
> 不变式：**有 `legacyDamageAtLevel5` ⇒ 卡上有一条能算回该值的 `Damage` 效果**。
> 条件触发的伤害（目前只有「风墙反制」的反击）记在 `conditionalDamageAtLevel5` 下，不参与这条。

在 Unity 外跑时有两个坑（[architecture_v0_5.md](architecture_v0_5.md) §3.1 那套临时 csproj）：

1. 测试程序集**必须叫 `TCGinFU.Core.Tests`**——Core 的 `InternalsVisibleTo` 指名道姓是它，
   改名会让原有测试拿不到 `Ap` / `DamageDealtLastRound` 这些 internal setter；
2. 两个 `.csproj` **不能放同一个目录**，否则共用一份 `obj/`，
   互相覆盖 `project.assets.json`，表现为 NUnit 突然「找不到命名空间」。

内容包目录靠 `ContentPaths.Resolve()` 自动向上找；放在仓库外时设 `TCGINFU_CONTENT_DIR`。

**测试场景**：菜单 `TCGinFU → 生成命刻之战场景` → `Assets/Scenes/ClockworkCollapse.unity`，
按 Play 即为米阔阔 + 维汀 对 克洛诺斯-IV型 + 镇压重盾臂。
原有的 `TCGinFU → 生成战斗场景`（样例哥布林）行为不变。
沙盒窗口的预设列表里也会自动多出「命刻之战」一项。

---

## 9. 玩家自定义的扩展路径

本文的设计让以下三件事**不需要再改架构**：

1. **改一张卡的数值** —— 在玩家包里写同 id 的卡，覆盖生效；
2. **加一张全新的卡** —— 新 id，只要 `effects` 用的是现有 `EffectKind` 就立即可用，无需写代码；
3. **做一套完整的新角色 + 卡组** —— 新增 `characters/` 与 `decks/` 条目即可。

**做不到的**（诚实地说清楚边界）：玩家无法新增 `behaviourId` 背后的逻辑——
那是编译进程序集的 C# 代码。数据驱动的覆盖面就是 `EffectKind` 枚举的覆盖面。
想让玩家做出带特殊判定的卡，需要的是另一个量级的工程（嵌脚本解释器），不在本文范围。
