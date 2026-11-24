# 真实数据层架构 · v2

> 最后核对：2026-11-22（按工作区代码）
> 配套：规则书 [v0_5.md](v0_5.md) · 引擎架构 [architecture_v0_5.md](architecture_v0_5.md) · 裁定表 [rulings_v0_5.md](rulings_v0_5.md) · 行为层（Lua）[behaviour_layer_handoff.md](behaviour_layer_handoff.md)
> 范围：《最终物语·命刻之战》v1.0.0（Godot 版）的卡牌 / 角色 / 首领数据如何以 JSON 内容包接进本项目，
> 以及内容包的加载、模式、对账和尚未实现的扩展口。行为逻辑（Lua）只在这里讲接口，细节见行为层文档。

---

## 0. 决策摘要

| # | 决策 | 现状 |
| --- | ----- | ----- |
| D-1 | 数据的存储格式 | **JSON 为唯一真源**，放 `Assets/StreamingAssets/Content/`，由纯 C# 的 `TCGinFU.Content` 解析 |
| D-2 | 老卡牌的来源归类 | 按卡面功能归到 职业/武器/防具/秘宝（§5.3）。最初是为过 §3.2 配比；规则书 2026-11-22 修订已**取消配比**，归类现在只决定装备派生（R-19）和卡牌类别本身，见裁定 R-25 |
| D-3 | 特殊逻辑 | 初版只留 `behaviourId` 桩；**现在由内容包里的 Lua 脚本实现**（MoonSharp），见 §3.4 |

D-1 的理由仍然成立（任一条都足以否掉 ScriptableObject 方案）：

1. 玩家自定义要求数据是能用记事本打开的文件，SO 必须开 Unity 编辑器才能产出；
2. `TCGinFU.Core` / `TCGinFU.Content` 设了 `noEngineReferences`，`dotnet test` 可脱离 Unity 读真实卡池；
3. 数据只有一份，不会出现两份对不上的真相。

曾经并存的 `TCGinFU.Data`（ScriptableObject 层）已于 2026-11-22 **整体删除**——它没有任何 asmdef 引用，
也没有对应的 `.asset`，留着只会让人以为数据有两份真相。
见 [archive/legacy_cleanup_handoff.md](archive/legacy_cleanup_handoff.md)。
以后若要做可视化编辑器，应当做成 JSON 的**前端**，而不是另起一套序列化格式。

---

## 1. 数据从哪来：来源与可复现性

老项目源码已丢失，数据是从发行版可执行文件里反编译出来的，工具在 `Tools/godot-legacy-extract/`（详见其 README）：

```
runtime/game_bin.exe   (97 MB, Godot 4.4.1 自包含导出)
  └─ 尾部 12 字节 [int64 pck_size][magic "GDPC"]  → 内嵌 PCK
     └─ extract_pck.js    PCK v2，pack_flags=2(PACK_REL_FILEBASE) → 文件基址 = pck起点 + file_base
        └─ scripts/*.gdc  GDSC v100 词法 token 流，zstd 压缩
           └─ gdc_decompile.js → src/*.gd（9 个文件，3992 行）
              └─ to_content.js → StreamingAssets/Content/*.json（初版迁移产物）
```

`.gdc` 是 token 流而非字节码，标识符表和常量表完整保留，反编译结果与原始源码语义等价（只丢注释和空行）。

权威来源是 `src/` 下这 4 个文件：

| 文件 | 内容 |
| ----- | ----- |
| `card_database.gd` | 49 张卡的全部字段 + 2 份 30 张主卡组配方 + 4 份 10 张形态组配方 |
| `boss_database.gd` | 11 个首领意图 + 两阶段意图池 |
| `battle_state.gd` | 角色与敌人的面板数值、命刻 / 过热 / 相位等场景机制 |
| `card_resolver.gd` | 每张卡的特殊结算逻辑（已按卡面文字改写成 Lua，见裁定 R-22） |

**可复现性的边界**：`to_content.js` 只能复现**初版**产物（卡、角色、敌人、卡组、无 `statuses`/`scripts` 的 `pack.json`）。
之后的增补是手工改在 JSON 上的：`statuses.json`、`scripts/*.lua`、`pack.json` 的 `statuses`/`scripts`/`defaultCardBack`/`artFallback`、
卡的 `art`、角色的 `portrait`、意图的 `behaviourId`/`mp`、首领的 `mp`、部分卡新增的纯数据效果（如先声夺人的 `MoveRow` + `Draw`）。
**重跑 `to_content.js` 会覆盖这些改动**；现在的真源是工作区里的 JSON，脚本只作迁移对账用。

---

## 2. 目录布局

```
Assets/
  StreamingAssets/Content/        ← 唯一的内容包（id = clockwork_collapse）
    pack.json                     清单
    statuses.json                 14 个状态模板
    cards/
      miku.json                   米阔阔主卡池（16 种）
      weiting.json                维汀主卡池（17 种）
      forms.json                  4 组形态牌（16 种）
    characters/heroes.json        米阔阔、维汀
    enemies/chronos_iv.json       克洛诺斯-IV型（boss，10 个意图）+ 镇压重盾臂（arm，1 个意图）
    decks/
      miku_starter.json           30 张 + 2 组形态组
      weiting_starter.json        30 张 + 2 组形态组
    scripts/
      statuses.lua miku.lua weiting.lua enemies.lua   行为脚本（MoonSharp）
  Resources/Art/                  卡图 / 立绘 / 卡背（按 art id 用 Resources.Load 读，不在内容包里）
  Scripts/
    Content/                      TCGinFU.Content（noEngineReferences）
      Json/                       Json.cs / JsonValue.cs —— 只读递归下降解析器
      Loading/                    ContentLoader.cs / ContentDatabase.cs / FileContentSource.cs（含 ContentPaths）
    Scripting/                    TCGinFU.Scripting：LuaScriptHost 等，依赖 MoonSharp
    Presentation/ContentScenario.cs   把内容包 + 脚本组装成一局战斗
  Scenes/
    ClockworkCollapse.unity       读内容包（唯一的场景）
```

---

## 3. 加载管线

```
pack.json ──▶ ContentLoader.LoadPack(db, readFile, report)
                ├─ schemaVersion 校验（≠ 1 直接报错返回）
                ├─ defaultCardBack / artFallback → db
                ├─ scripts → db.ScriptPaths（只记路径，不执行）
                └─ 按固定顺序：statuses → cards → characters → enemies → decks
                     LoadStatuses / LoadCards→ReadCard→ReadEffect
                     LoadCharacters→ReadCharacter（EquipFrom / ArmorFrom 查已加载的卡）
                     LoadEnemies→ReadEnemy（意图经 ReadEnemyCard 变成一张 CardRole.EnemyIntent 的牌）
                     LoadDecks
              ▶ ContentDatabase（按 id 索引）
ContentScenario.CreateEngine
   ├─ BuildLineup：db.Character / db.BuildDeck / EnemyContent.CreateEnemy
   ├─ LoadScripts：逐个 LuaScriptHost.Load(db.ScriptPaths)
   ├─ host.BuildRegistry(db.DeclaredBehaviourIds) —— 对账，见 §3.4
   └─ new BattleEngine(state, EffectRegistry.CreateDefault(), behaviours, db.BuildStatusRegistry())
```

顺序是有意义的：角色的 `weaponCardId`/`armorCardId` 要查已加载的卡，卡组展开要按 id 找卡，所以卡必须先于角色和卡组。

关键约定：

- **`ContentLoader` 不碰文件系统**，只接受 `Func<string, string>`（相对路径 → 文本，读不到返回 null）。
  `FileContentSource` 把一个目录包装成这个委托，用的是 BCL `System.IO`，`noEngineReferences` 保得住。
- **自带 JSON 解析器**（`Content/Json/`，约 300 行）：项目没有 Newtonsoft，`JsonUtility` 属于 `UnityEngine`。
  只支持对象 / 数组 / 字符串 / 数字 / 布尔 / null；语法错误带行号（「第 N 行：…」）。
- **报错收集全部，不抛异常**：`LoadReport.Errors` 一次报全。非法**枚举名**会报「`文件 / id` 的 `字段`」错误；
  但**类型不对**（如 `"ap": "1"`）不报错，静默取默认值。枚举一律写名字，大小写不敏感。

### 3.1 内容包目录的定位

`ContentPaths` 有两个出口，用途不同：

- **`Resolve(explicitRoot)`** —— 定位**一个完整内容包**（JSON 加载用）。顺序：显式参数 → 环境变量
  `TCGINFU_CONTENT_DIR` → 平台目录 → 从工作目录和 `AppContext.BaseDirectory` 逐级向上找
  `Assets/StreamingAssets/Content`。显式参数与环境变量是**独占**的：指到哪就是哪，
  不带 `pack.json` 也原样返回，免得 CI 把变量指错时被悄悄换成仓库里的包。
  其余候选必须带 `pack.json` 才算数。
- **`ResolveAll(explicitRoot)`** —— 列出**所有存在的**候选目录，高优先级在前。
  不要求是完整包：玩家包可以只放几张图。美术素材的查找链用它（见 §3.3）。

`ContentPaths` 在 `noEngineReferences` 的程序集里，拿不到 `Application.*`，
所以两个平台目录由表现层的 `UnityContentRoots` 用 `[RuntimeInitializeOnLoadMethod]`
装进 `ContentPaths.PlatformRoots`。

| 入口 | 路径 | 现状 |
| ----- | ----- | ----- |
| Unity 编辑器播放 / EditMode 测试 | 向上找仓库内目录 | ✅ |
| `dotnet test`（Unity 外） | 同上，或设 `TCGINFU_CONTENT_DIR` | ✅ |
| 构建产物 | `Application.streamingAssetsPath + "/Content"` | ✅ |
| 玩家自定义包 | `Application.persistentDataPath + "/Content"` | ⚠️ **只对美术生效**：`ResolveAll` 看得到它，卡面图能被覆盖；JSON 仍只加载一个完整包（§3.2） |

> 两条平台路径都按「磁盘上的普通目录」读。桌面平台成立；Android 的
> `streamingAssetsPath` 在 apk 里，到那天要改成 `UnityWebRequest` 异步读。

### 3.3 美术素材的定位

卡面和卡牌效果、卡面文字一样是**内容**，所以正式卡图跟着内容包走，放在包的 `art/` 子目录下，
和描述它的 JSON 平级：

```
Content/
├── pack.json
├── cards/miku.json          ← "art": "cards/phantom_nightwalk"
└── art/
    ├── cards/phantom_nightwalk.png
    └── portraits/miku.png
```

`CardArtLibrary.Resolve(artId)` 的顺序：

1. 按 `ResolveAll()` 的优先级遍历内容包目录，试 `<root>/art/<artId>.png|.jpg|.jpeg`
   （同名时 png 赢，它带透明通道）；
2. 都没有，退回包体内的 `Resources/Art/<artId>`。

于是玩家包不改官方文件也能换掉官方卡的图——它排在官方包前面。

**`Resources/Art/` 只留兜底素材**（牌背 + 按类型的几张占位图）。它们必须在一个内容包都没装时也存在，
所以不能跟着包走。单卡没配 `art`、或配了但文件不在时，按 `pack.json` 的 `artFallback` 退到这几张。

包里的图是裸文件，不走 Unity 导入管线：没有 mipmap、没有平台压缩，用 `Texture2D.LoadImage` 读。
卡图这个量级不值得为此建资源管线；真要优化是以后换 Addressables 的事。
`artId` 里的 `..` 和绝对路径会被挡掉——内容包是玩家之间互相分发的东西，
不该让一个包靠写路径读到内容目录外面去。

### 3.2 只加载一个包

`ContentPaths.LoadDefault` 只加载**一个**包（命刻之战）。原设计的「base → 玩家包 A → 玩家包 B 按 id 覆盖」没有实现。
`ContentDatabase` 内部的实际行为：

| 条目 | 同 id 再次出现 |
| ----- | ----- |
| 卡牌 / 角色 / 敌人 / 卡组 | 后者**静默覆盖**前者，不报错 |
| 状态 | **报错**「状态 X 重复定义」，保留先加载的那份 |

`pack.json` 的 `id` / `name` / `version` / `source` 加载器都不读，只是说明性字段。

### 3.3 失败时的退路

`BattleSession.Restart` 调 `ContentScenario.CreateClockworkCollapse`：内容包有任何 `LoadReport` 错误时，
引擎不建，`Session.IsLoaded` 为 false，表现层**整屏显示加载报告**（哪个文件、哪一行出错）并给出「重试 / 退出」，
**不开局**。

这里原先会静默退回一套 C# 样例卡组。那样做最糟的不是少了提示，而是「内容包坏了」看起来像「游戏能玩」——
玩家改坏自己的 JSON 之后会以为是自己的卡没生效。样例内容已随 2026-11-22 的清理一并移除。

`ContentScenario.LastLoaded` 保存最近一次加载的库，
表现层从它取默认卡背、按类型兜底图和立绘 id。

### 3.4 行为脚本与加载期对账

`pack.json` 的 `scripts` 列出 Lua 文件；`ContentLoader` 只记路径，由 `TCGinFU.Scripting.LuaScriptHost` 执行。
脚本用 `behaviour("<id>", { 钩子 = function ... })` 把自己挂到卡牌 / 意图 / 状态的 `behaviourId` 上。
沙箱、钩子表、动词、超时看门狗见 [behaviour_layer_handoff.md](behaviour_layer_handoff.md)，这里不重复。

`LuaScriptHost.BuildRegistry` 拿 `db.DeclaredBehaviourIds`（卡 + 状态 + 意图声明的全部 `behaviourId`）对账，写进 `ScriptLoadReport.Warnings`：
声明了却没有脚本、脚本注册了却没人引用、行为一个钩子都没挂。当前内容包**常驻 2 条警告**：

| 类 | 条数 | 明细 |
| ----- | ----- | ----- |
| 哑火卡 | 2 | `lucky_reversal`（幸运改命，缺「掷骰后」响应窗口）、`mana_overload_ritual`（魔力过载仪式，缺魔力激发卷轴） |

核对口径：声明的不重复 id 64 个，注册 62 个，差 2；无「注册了没人引用」、无「空行为」。

> **这张表曾经有 24 条，其中 21 条被归类成「纯数据卡也声明了 `behaviourId`，是迁移时原样带过来的」。
> 那个判断是错的。** 那 21 张里绝大多数**并不是**纯数据卡，而是只兑现了卡面前半句：
> 伤害照常结算，回血、抽牌、削盾、净化、必定暴击这些后半句静默失效
> （未注册的 `behaviourId` 在 `BehaviourRegistry.Resolve` 里返回 null，那张卡**退化**为纯数据卡——
> 退化是设计上有意的，但它意味着漏写不会报错）。
>
> 教训：对账警告一旦被当成噪音整类放过，它就不再是对账了。
> 加新内容时请**逐条看完**，不要按类消音。
注意 `ContentScenario.CreateEngine` 只把脚本的 **Errors** 并进 `LoadReport`，**Warnings 不会出现在游戏里**，只有直接看 `ScriptLoadReport` 才能看到。
未注册的 `behaviourId` 在 `BehaviourRegistry.Resolve` 里返回 null，那张卡退化为纯数据卡。

---

## 4. JSON 模式

通则：字段缺省一律取下表的默认值；「必填」指缺了会报错并跳过该条目。枚举的合法值见各表末尾。

### 4.1 `pack.json`

```json
{
  "id": "clockwork_collapse", "name": "命刻之战", "version": "1.0.0", "schemaVersion": 1,
  "source": "《最终物语·命刻之战》Windows v1.0.0（Godot 4.4.1）反编译迁移",
  "statuses": ["statuses.json"],
  "scripts": ["scripts/statuses.lua", "scripts/miku.lua", "scripts/weiting.lua", "scripts/enemies.lua"],
  "cards": ["cards/miku.json", "cards/weiting.json", "cards/forms.json"],
  "characters": ["characters/heroes.json"],
  "enemies": ["enemies/chronos_iv.json"],
  "decks": ["decks/miku_starter.json", "decks/weiting_starter.json"],
  "defaultCardBack": "card bg beta",
  "artFallback": { "back": "card bg beta", "transform": "card levelup beta", "guard": "card guard beta",
                   "attack": "card attack beta", "spell": "card spell beta", "character": "card character beta" }
}
```

| 字段 | 说明 |
| ----- | ----- |
| `schemaVersion` | 缺省视为 1；不等于 `ContentLoader.SupportedSchemaVersion`（1）则整包报错不加载 |
| `statuses` / `cards` / `characters` / `enemies` / `decks` | 相对路径数组；非字符串项报错；文件缺失报「文件不存在」 |
| `scripts` | Lua 文件相对路径数组，由脚本宿主执行 |
| `defaultCardBack` | 单卡没写 `backArt` 时用的卡背 art id |
| `artFallback` | 按类型兜底的 art id。表现层的取键：升变牌 `transform`、防具 `guard`、带伤害的 `attack`、其余 `spell`；立绘 `character`；最后兜底 `back` |
| `id` / `name` / `version` / `source` | 说明性，加载器不读 |

art id 是 `Assets/Resources/Art/` 下的相对路径（不带扩展名），由 `CardArtLibrary` 用 `Resources.Load` 读取。
解析顺序：卡自己的 `art` → `artFallback[类型]` → `artFallback.back`。

### 4.2 卡牌（`cards/*.json` → `{ "cards": [...] }`）

```json
{
  "id": "suppressing_volley", "name": "压制连射", "owner": "miku",
  "category": "Class", "play": "Skill", "classId": "神射手", "colors": ["Dexterity"],
  "ap": 1, "mp": 10, "charge": 0, "hasCrit": true,
  "rulesText": "对单个敌人连续射击2次，每次造成5点物理伤害【暴击】；两次射击分别独立投掷敏捷骰判定暴击。",
  "behaviourId": "suppressing_volley",
  "effects": [
    { "kind": "Damage", "amount": { "source": "Flat", "add": 5 }, "element": "Physical", "target": "SingleEnemy" },
    { "kind": "Damage", "amount": { "source": "Flat", "add": 5 }, "element": "Physical", "target": "SingleEnemy" }
  ],
  "unmapped": { "ranged": true, "hits": 2 },
  "art": "cards/suppressing_volley"
}
```

| 字段 | 类型 / 默认 | 说明 |
| ----- | ----- | ----- |
| `id` | 字符串，**必填** | |
| `name` | 默认 = id | |
| `category` | `CardCategory`，默认 `Class` | **类别**：这张牌从哪来（§7.1）。和 `play` 是两个互不相干的维度 |
| `play` | `CardPlay`，**必填** | **打法**：什么时候打、打出后去哪。只有 `Skill` / `Timing` / `Continuous` 三种。唯一能省的是升变牌（`role: Transform`），它不按打法结算；其余的牌漏写就报错——默认成 `Skill` 会让一张写着「受击时打出」的牌在响应窗口被拒收 |
| `role` | `CardRole`，默认 `Normal` | 打法之外的**标记**：`Transform`（升变牌）/ `Form`（形态牌）。不是第四、第五种打法——形态牌仍然有自己的 `play`。`EnemyIntent` 不能手写，敌人意图只能定义在 `enemies/*.json` 里 |
| `formDeck` | `{ card, count }` 数组 | **升变牌自带的形态卡组**（§3.1），合计 10 张，玩家不挑。`role: Transform` 必填，其余的牌写了报错。记在升变牌上而不是卡组文件里：这是内容定义，不是构筑选择；而且同一张形态牌可以同时出现在多张升变牌的形态卡组中 |
| `hero` | 布尔，默认 false | 英雄技能牌（职业 10 级精通所得，§3.3）。这是**标记**不是类别也不是打法：它属于职业牌，可以是任意打法 |
| `classId` | 字符串 | 职业名，如「浪客」。**职业牌（含升变牌、形态牌）必填，其余类别写了报错**——§7.1：每张职业牌只归属 1 个职业，武器 / 防具 / 秘宝牌不带职业。「写的是不是持有者学过的职业」加载器查不了，归 `DeckValidator`（测试 T-3） |
| `colors` | `AttributeColor[]`，最多 2 项 | 超过 2 项报错；空数组 = 无色（不投暴击骰） |
| `ap` / `mp` / `charge` | 整数，默认 0 | `charge` 是卡面**硬费用**（「消耗 X 个充能」里的 X），不够就打不出 |
| `chargeOptions` | 整数数组，默认空 | 玩家可以**额外选择**消耗几个充能（见下） |
| `hasCrit` | 布尔，默认 false | |
| `ranged` | 布尔 | 缺省时读 `unmapped.ranged`（现有数据都写在 `unmapped` 里） |
| `requiresBackRow` | 布尔 | 缺省时读 `unmapped.requires_back_row` |
| `targeting` | `CardTargeting`，默认 `Auto` | 出牌时界面该让玩家点谁。`Auto` 按 `effects` 推导（有 `SingleEnemy` 就指敌人，有 `SingleAlly` 就指队友）。目标**只给行为层用**的卡（「贴入全队任意角色的充能区」）推导不出来，必须显式写 `Ally`，否则玩家点的队友会被整个丢掉、永远落在自己身上 |
| `rulesText` | 字符串 | 卡面文字，不参与结算 |
| `art` / `backArt` | art id | `backArt` 缺省用包的 `defaultCardBack` |
| `behaviourId` | 字符串 | Lua 行为的挂载点 |
| `weaponBaseDamage` | 整数，默认 0 | 武器牌的一阶基础伤害；角色装备时派生 `WeaponProfile`（R-19） |
| `armorBaseShield` | 整数，默认 0 | 防具牌的基础护盾；派生 `ArmorProfile` |
| `twoHanded` | 布尔，默认 false | |
| `element` | `DamageElement`，默认 `Physical` | 武器牌造成的伤害属性 |
| `shieldKind` | `ShieldKind`，默认 `Physical` | 防具牌提供的护盾种类 |
| `transformTrigger` | `TransformTrigger`，默认 `Conditional` | 升变牌 |
| `transformConditionText` | 字符串 | 升变条件的显示文本 |
| `effects` | 效果数组，见 §4.3 | |

加载器**不读**：`owner`、`unmapped` 下除 `ranged` / `requires_back_row` 以外的一切
（`hits`、`damage_formula`、`legacyDamageAtLevel5`、`conditionalDamageAtLevel5`、`damageType` 等——迁移留痕与测试对账用）。

#### `chargeOptions`：卡面允许玩家额外消耗几个充能

卡面写「**若**消耗 1 个充能指示物，本次攻击必定暴击」这类词条时，消耗多少是玩家当场决定的，
所以要在卡上声明可选的档位：

```json
"chargeOptions": [0, 1]     // 暗夜剔骨：可以不消耗，也可以消耗 1 个
"chargeOptions": [1, 2]     // 调合·万能灵药：至少 1 个，消耗 2 个则两项都生效
```

- 非负整数数组，加载时升序去重；**含 0** 表示「可以不消耗」，**不含 0** 表示至少要选一个。
- 缺省（空数组）= 这张牌没有可选消耗，`PlayCardAction.ChargeForEffect` 只能是 0，否则引擎拒绝。
- 和 `charge` 是两回事：`charge` 是扣定了的硬费用，两者**相加**后一起从充能区出，
  全部记为 `ChargeUse.PayCardCost`，脚本里读 `ctx.charge_spent` 拿到的是总数。
- **用充能换 MP 不走这里**：那是一个独立动作（`SpendChargeForMpAction`，§5.2 的「三选一」），
  转掉的充能不计入 `ctx.charge_spent`。
- 声明在卡上而不是写在界面里：界面据此**自动生成**「额外消耗几个充能？」的提问
  （`CardChoiceCatalog`），不用每补一张这种牌就改一次表现层，玩家自定义的卡也能用上同一个入口。

### 4.3 效果（卡牌与意图共用）

| 字段 | 类型 / 默认 | 说明 |
| ----- | ----- | ----- |
| `kind` | `EffectKind`，默认 `Damage` | |
| `amount.source` | `ValueSource`，默认 `Flat` | |
| `amount.add` | 整数，默认 0 | |
| `amount.times` | 整数，默认 1 | 写 0 报错并按 1 处理 |
| `amount.dividedBy` | 整数，默认 1 | ≤ 0 报错并按 1 处理 |
| `target` | `TargetScope`，默认 `SingleEnemy` | `kind: Cover` 时强制为 `CurrentTarget` |
| `element` | `DamageElement`，默认 `Physical` | |
| `shieldKind` | `ShieldKind`，默认 `Physical` | `GainShield` 用 |
| `statusId` | 字符串 | `ApplyStatus` 用，指向 `statuses.json` 的 id |
| `duration` | 整数，默认 1 | 状态持续轮数 |
| `condition` | `EffectCondition`，默认 `Always` | |
| `rollsOwnCrit` | 布尔，默认 false | 这条伤害单独投暴击骰（R-23） |
| `rowTarget` | `Row`，可省 | `MoveRow` 用：省略 = 前后排切换；写了 = 定向移动 |

`amount` 即 `ValueExpr(source, add, times, dividedBy)`，例：形态牌 `B_atk + 4` → `{ "source": "FormAttack", "add": 4 }`。

### 4.4 状态（`statuses.json` → `{ "statuses": [...] }`）

```json
{ "id": "overheat", "name": "过热", "rulesText": "…", "critResistDelta": -3, "incomingMultiplier": [3, 2], "negative": true }
```

| 字段 | 类型 / 默认 | 说明 |
| ----- | ----- | ----- |
| `id` | **必填**；重复 → 报错 | |
| `damagePerRound` / `healPerRound` | 整数，默认 0 | 每层每轮 |
| `dotElement` | `DamageElement`，默认 `Poison` | 持续伤害的属性 |
| `outgoingDamageBonus` | 整数，默认 0 | 造成伤害 + |
| `incomingDamageBonus` / `incomingDamageReduction` | 整数，默认 0 | 受到伤害 + / − |
| `consumedOnTrigger` | 布尔，默认 false | 触发一次即消耗 |
| `critResistDelta` | 整数，默认 0 | 暴击抵抗增减 |
| `incomingMultiplier` | `[分子, 分母]`，默认 `[0, 0]` = 不改 | 受伤倍率，如过热 ×1.5 = `[3, 2]` |
| `behaviourId` | 字符串 | 需要逻辑的状态挂 Lua（`scripts/statuses.lua`） |

加载器**不读**：`name`、`rulesText`、`negative`。状态的数值属于状态本身，卡牌只引用 `statusId`。

### 4.5 角色（`characters/*.json` → `{ "characters": [...] }`）

```json
{
  "id": "weiting", "name": "维汀", "subtitle": "熵术士 · 造物使",
  "classes": [{ "classId": "熵术士", "level": 4 }, { "classId": "造物使", "level": 1 }],
  "dice": { "Might": "D8", "Insight": "D10", "Dexterity": "D8", "Willpower": "D8" },
  "hpBonus": -10, "mpBonus": 5, "startingRow": "Front", "chargeCapacity": 2,
  "weaponCardId": "weakness_analysis", "armorCardId": "arcane_barrier", "deckId": "weiting_starter",
  "legacy": { "hp": 35, "mp": 50, "autoMagicShieldBonus": 1 },
  "portrait": "portraits/weiting"
}
```

| 字段 | 类型 / 默认 | 说明 |
| ----- | ----- | ----- |
| `id` | **必填** | |
| `name` / `subtitle` | 默认 id / 空 | |
| `classes[]` | `{ classId 必填, level 默认 1 }`，**至少 1 项** | 0 项报错并跳过该角色 |
| `dice.Might/Insight/Dexterity/Willpower` | `DieRank`，各默认 `D6` | |
| `hpBonus` / `mpBonus` | 整数，默认 0，可为负 | 见 §5.4 |
| `startingRow` | `Row`，默认 `Front` | |
| `chargeCapacity` | 整数，默认 2 | |
| `weaponCardId` | 卡 id，可省（= 徒手） | 必须是已加载的 `Weapon` 类卡，否则报错 |
| `armorCardId` | 卡 id，可省（= 无防具） | 必须是 `Armor` 类卡 |
| `deckId` | 卡组 id | |
| `portrait` | art id | |
| `legacy.hp` / `legacy.mp` | 整数 | 老游戏面板，仅对账 |

加载器**不读** `legacy.autoMagicShieldBonus`：维汀的「魔法盾 +1」只记了账，**没有生效**（见 §6）。

### 4.6 敌人（`enemies/*.json` → `{ "enemies": [...] }`）

```json
{
  "id": "boss", "name": "克洛诺斯-IV型", "subtitle": "裂界发条巨像",
  "hp": 140, "rank": "Boss", "critResist": 7, "row": "Back", "mp": 30,
  "affinities": { "Lightning": "Vulnerable", "Dark": "Vulnerable", "Ice": "Resistant",
                  "Earth": "Resistant", "Poison": "Immune" },
  "intentPools": { "phaseOne": ["gravity_sweep", "…"], "phaseTwo": ["time_guillotine", "fate_turbulence", "causal_erasure"] },
  "intents": [
    { "id": "aether_siphon", "name": "以太虹吸", "category": "干扰", "damage": 6, "element": "Dark",
      "targeting": "AllPlayers", "designatedRow": "Front", "hits": 1, "mp": 10,
      "text": "全体受到6点暗属性魔法伤害，各吞噬1个充能；每吞噬1个，首领回复8HP。消耗10MP。",
      "unmapped": { "…": "…" }, "behaviourId": "aether_siphon" }
  ]
}
```

| 字段 | 类型 / 默认 | 说明 |
| ----- | ----- | ----- |
| `id` | **必填** | |
| `name` | 默认 id | |
| `hp` | 整数，默认 1 | |
| `rank` | `EnemyRank`，默认 `Normal` | |
| `critResist` | 整数；缺省或负数 = 按 `rank` 取默认 | |
| `mp` | 整数，默认 0 | 敌人 MP 上限；意图的 `mp` 费用从这里扣 |
| `row` | `Row`，默认 `Front` | |
| `affinities` | `{ DamageElement 名: Affinity }` | 键不是合法属性名时报错 |
| `intents[]` | 见下表 | |
| `intentPools.phaseOne` / `phaseTwo` | 意图 id 数组 | 引用不存在的意图报错并剔除 |
| `phaseTwoHpThreshold` | 整数，默认 `hp / 2`（整除） | `phaseTwo` 非空且 HP ≤ 此值时从二阶段池抽 |
| `intentsPerRound` | 整数，默认 3 | 每轮从当前池**不放回**随机抽几条（用引擎的 `IRng`，同种子可复现）；池不够就抽完为止 |

意图字段（每条意图被读成一张 `CardRole.EnemyIntent` 的牌，挂在 `EnemyIntent.Card` 上，和玩家卡共用效果与行为体系）：

| 字段 | 类型 / 默认 | 说明 |
| ----- | ----- | ----- |
| `id` | **必填** | |
| `name` | 默认 id | |
| `damage` | 整数，默认 0 | 宣告的那段攻击（响应窗口可预览、可改写） |
| `element` | `DamageElement`，默认 `Physical` | |
| `targeting` | `IntentTargeting`，默认 `DefaultSingle` | |
| `designatedRow` | `Row`，默认 `Front` | `DesignatedRowSingle` 等用 |
| `locksTarget` | 布尔，可省 | 省略时按 `targeting` 推导（R-24） |
| `shareable` | 布尔，默认 false | 允许相邻队友在响应窗口里**分摊**：伤害对半拆，两人各自抵扣护盾（`ShareDamageAction`）。目前只有因果抹杀令开了这个口子，由 `CardEffectFixTests.F21` 钉住 |
| `mp` | 整数 | 费用；缺省时读 `unmapped.mp` |
| `text` | 字符串 | 读进牌的 `RulesText` |
| `behaviourId` | 字符串 | 后续效果（吞噬充能、换排、命刻 +1 等）由 Lua 实现 |
| `effects` | 效果数组（§4.3） | 现有数据未使用 |

加载器**不读**：敌人的 `subtitle`；意图的 `category`、`hits`、`unmapped.target / damageClass / pierce`。
现状：首领 JSON 未写 `phaseTwoHpThreshold`，取默认 70，与老版本「HP ≤ 70 进二阶段」一致；`intentsPerRound` 也取默认 3。

### 4.7 卡组（`decks/*.json`，每文件一份）

```json
{
  "id": "miku_starter", "name": "米阔阔 · 起始构筑", "characterId": "miku",
  "main": [{ "card": "ambush_opening", "count": 2 }]
}
```

`id` 必填；`count` 默认 1、最小 1；`card` 缺失的条目被丢弃。卡 id 不存在要到 `ContentDatabase.BuildDeck` 展开时才报错。
加载时**不做构筑校验**；校验由 `DeckValidator` 另行调用（测试 T-3）。

**卡组文件里没有形态卡组**（§3.1）：编入哪张升变牌，`BuildDeck` 就按那张牌的 `formDeck` 自动展开它那 10 张。
玩家唯一能动形态卡组的地方是用**主动型英雄技能牌**替换其中 1 张；内容包里还没有英雄技能牌，
所以这条替换记录（形如 `"heroSwaps": [{ "transform": "…", "replace": "…", "with": "…" }]`）**尚未实现**，
格式先占住位，等补上英雄技能牌再做。

### 4.8 枚举合法值

| 枚举 | 值 |
| ----- | ----- |
| `CardCategory` | Class Weapon Armor Treasure |
| `CardPlay` | Skill Timing Continuous |
| `CardRole` | Normal Transform Form EnemyIntent（后者不能手写） |
| `AttributeColor` | Might Insight Dexterity Willpower |
| `DamageElement` | Physical Wind Lightning Dark Earth Fire Ice Light Poison |
| `ShieldKind` | Physical Magical |
| `TransformTrigger` | Conditional Immediate |
| `EffectKind` | Damage Heal GainShield RestoreMp GainAp Draw Discard GainCharge ApplyStatus MoveRow GrantFreeMove SwapRows Cover |
| `ValueSource` | Flat FormAttack FormDefense WeaponDamage ArmorShield LevelBonus CasterTotalLevel ChargeSpent |
| `TargetScope` | Self SingleEnemy AllEnemies FrontRowEnemies BackRowEnemies SingleAlly AllAllies AdjacentAllies CurrentTarget |
| `EffectCondition` | Always OnCrit OnSuperCrit IfCasterInCrisis IfChargeSpent IfTargetInCrisis |
| `IntentTargeting` | DefaultSingle FrontRowAoe BackRowAoe AllPlayers DesignatedRowSingle SelfOrNone HighestHpSingle |
| `Affinity` | Normal Vulnerable Resistant Immune Absorb |
| `EnemyRank` | Normal Elite Boss |
| `DieRank` | D6 D8 D10 D12 |
| `Row` | Front Back |

---

## 5. 老数据 → 新模型的映射

映射表在 `Tools/godot-legacy-extract/to_content.js` 里原样落地，改一边要同步另一边。

### 5.1 枚举映射

| 老字段 | 老值 | 新值 |
| ----- | ----- | ----- |
| `colors` | `green` / `blue` / `purple` / `neutral` | `Dexterity` / `Insight` / `Willpower` / **空数组** |
| `kind` | `skill` | `play: Skill` |
| | `reaction` | `play: Timing` |
| | `ongoing` | `play: Continuous` |
| | `ascension` | `role: Transform`（不按打法结算，没有 `play`） |
| | `treasure` | `play: Skill` + `category: Treasure` |
| | `form_attack` / `form_skill` | `role: Form` + `play: Skill` |
| | `form_reaction` | `role: Form` + `play: Timing` |
| `target` | `enemy` / `all_enemies` / `self` / `ally` / `none` | `SingleEnemy` / `AllEnemies` / `Self` / `SingleAlly` / `Self` |
| `damage_type` | `physical/wind/dark/lightning/fire/ice/earth/light/poison` | 同名 `DamageElement` |
| | `arcane` | `Light`，并在 `unmapped.damageType` 留痕（裁定 R-21） |
| | `adaptive` / `random` | `Dark` 占位，实际属性由行为决定 |

`neutral` 映射成空颜色数组：规则书 §2.2 的颜色决定投哪颗暴击骰，老游戏 neutral 牌全部 `crit: false`。

### 5.2 数值字段

| 老字段 | 处理 |
| ----- | ----- |
| `damage` + `damage_type` | 一条 `Damage`，`{ "source": "Flat", "add": damage }` |
| `damage_formula: "B_atk + 4"` | `{ "source": "FormAttack", "add": 4 }`，不再用硬编码的 `damage` |
| `damage_formula: "B_atk * 2"` | `{ "source": "FormAttack", "times": 2 }` |
| `shield_formula: "B_def + 2"` | `GainShield` + `{ "source": "FormDefense", "add": 2 }` |
| `hits: N` | 展开成 N 条相同的 `Damage`，`unmapped.hits` 留痕（裁定 R-23） |
| `self_damage` | 记在 `unmapped`，由行为实现（老逻辑无视护盾） |
| `crit: true` | `hasCrit: true` |

形态牌采用公式、丢弃具体值：同一张牌在 5 级和 50 级应各自求值。老的具体值存在 `unmapped.legacyDamageAtLevel5`，
作为迁移自检（T-5）：用 5 级面板求值应算回老值。条件触发的伤害（风墙反制的反击）记在 `conditionalDamageAtLevel5`，不参与这条。

武器 / 防具基础值由老卡面「本 5 级构筑为 N 点」反推：米阔阔武器 7、维汀武器 6、防具统一 7（武器牌一律标 `twoHanded`），
使 B_atk / B_def 回到老值 8 / 7 / 8。

### 5.3 卡牌来源归类

老游戏没有武器 / 防具牌概念。按卡面功能归类（裁定 R-25），当前 JSON 与此表一致：

**米阔阔**（职业 12 / 武器 8 / 防具 7 / 秘宝 3 = 30）

| 来源 | 卡 × 张数 |
| ----- | ----- |
| 职业 12 | 幻影夜行 1、鹰眼绝境 1（升变）、鹰眼锁定 1（持续）、暗算起手 2、卑劣打击·壹式 2、先声夺人 2、交叉火力网 1、穿云狙击 2 |
| 武器 8 | 游击速射 3、连珠箭雨 3、压制连射 2 |
| 防具 7 | 翻滚披风 3、看破闪避 2、战术隐蔽 2 |
| 秘宝 3 | 顺风充能 2、翠风箭附 1 |

**维汀**（职业 13 / 武器 5 / 防具 8 / 秘宝 4 = 30）

| 来源 | 卡 × 张数 |
| ----- | ----- |
| 职业 13 | 星穹坍缩 1、真理炼金矩阵 1（升变）、灵智回流·常驻 2（持续）、苦痛转导 2、命运锚点·七 1、熵之轮盘 2、暗言术·活力汲取 1、深渊虹吸 2、调合·万能灵药 1 |
| 武器 5 | 弱点解析 3、秘法冲击 2 |
| 防具 8 | 奥术结界 3、睿智法袍 2、幸运改命 2、应急催化剂 1 |
| 秘宝 4 | 痛觉铭刻 2、魔力过载仪式 2 |

> 规则书 2026-11-22 修订已取消主卡组类别配比（[v0_5.md](v0_5.md) §3.2，讨论见 [archive/design_notes_2026-11-22.md](archive/design_notes_2026-11-22.md)），
> 所以这套归类**不再服务于配比校验**。但代码尚未跟上：`DeckValidator` 仍按 职业 12–16 / 武器 3–8 / 防具 3–8 / 秘宝 0–6 校验，
> `LegacyContentTests` 的 T-3 也仍断言上表配比。

### 5.4 角色面板的公式冲突

老游戏 HP/MP 是写死的，和规则书 §2.1（最大 HP = 总等级 + 力量骰 × 5 + 加值；MP 同理用意志骰）对不上：

| 角色 | 骰面 | 公式算出 | 老游戏值 | 差额 |
| ----- | ----- | ----- | ----- | ----- |
| 米阔阔 | 力量 d6 / 意志 d8，总 5 级 | HP 35 / MP 45 | HP 40 / MP 35 | `hpBonus +5` / `mpBonus −10` |
| 维汀 | 力量 d8 / 意志 d8，总 5 级 | HP 45 / MP 45 | HP 35 / MP 50 | `hpBonus −10` / `mpBonus +5` |

取值：用 `hpBonus` / `mpBonus` 把老数值钉死，因为这场 Boss 战的意图伤害（12 / 20 / 28）是按老面板调的。
代价：`CharacterSheet` 的加值语义本是「职业与秘宝提供的加值」，现在被当补正用且出现负值。
最终是改公式还是改面板，由规则设计者决定，定了之后把 bonus 清零。老值记在 `legacy` 里备查。

---

## 6. 现状与缺口

已实现（相对初版文档）：Lua 行为层（47/49 张卡可用）、命刻计数（目前只计数，不触发后果）、首领两阶段意图池、
过热（`overheat` 状态 + `incomingMultiplier`）、`RequiresBackRow`、`RollsOwnCrit` 字段、敌人 MP、状态模板、卡图 / 立绘 / 卡背标识、
状态驱动的元素易损（`vulnerableElement`）。

数据层相关的缺口：

| # | 缺口 | 位置 |
| ----- | ----- | ----- |
| B-5 | 对账只剩 2 条警告（§3.4），但**警告仍然不进游戏内报告**——只有直接看 `ScriptLoadReport` 才看得到 | `LuaScriptHost.BuildRegistry` / `ContentScenario.CreateEngine` |
| B-6 | 构建产物入口和玩家包入口未实现；只加载一个包，无跨包覆盖 | `FileContentSource.cs`（`ContentPaths`） |
| B-8 | 维汀「魔法盾 +1」只在 `legacy.autoMagicShieldBonus`，加载器不读，未生效 | `characters/heroes.json` |
| — | 卡图走 `Resources/Art/`，内容包无法自带图片 | `CardArtLibrary` |
| — | 重跑 `to_content.js` 会覆盖手工增补（§1） | `Tools/godot-legacy-extract/` |
| — | 类型错误静默取默认值，不报错（§3） | `JsonValue.AsInt/AsBool/AsString` |

仍未见实现的场景机制：部位破坏（镇压重盾臂被击破后的首领变化）、倒地 / 复苏 / 淘汰、魔力激发卷轴开关、
「掷骰后」响应窗口（幸运改命）。行为层待办见 [behaviour_layer_handoff.md](behaviour_layer_handoff.md) §7。

---

## 7. 相关裁定

迁移中做出的选择统一记在 [rulings_v0_5.md](rulings_v0_5.md)，本文不再保留裁定正文：

| 裁定 | 内容 | 本文位置 |
| ----- | ----- | ----- |
| R-21 ⚠ | 老数据 `arcane` 属性暂映射为 `Light` | §5.1 |
| R-22 | 卡面文字优先于老版本代码 | §1 |
| R-23 | 多段攻击各自独立投暴击骰（原本文「`hits: N` 展开」已并入此条）。内容里由每条 `Damage` 的 `rollsOwnCrit` 启用 | §5.2 |
| R-25 | 老卡牌的来源归类（原本文的「R-22」，已改号） | §5.3 |

---

## 8. 验收

判据在 `Assets/Scripts/Tests/EditMode/LegacyContentTests.cs`，读真实内容包；Lua 行为的验收在 `LuaBehaviourTests.cs`。

| # | 断言 |
| --- | ----- |
| T-1 | 内容包加载零错误；卡牌 49、角色 2、敌人 2、卡组 2 |
| T-2 | 两份主卡组各 30 张，4 组形态组各 10 张 |
| T-3 | 两份卡组通过 `DeckValidator`，且来源配比与 §5.3 逐项相符（配比已从规则书取消，见 §5.3 注） |
| T-4 | `behaviourId` 不重复；49 张卡都有卡面文字 |
| T-5 | 形态牌用 5 级面板对公式求值 = `unmapped.legacyDamageAtLevel5` |
| T-6 | 角色面板 = 老游戏值（米阔阔 40/35，维汀 35/50）；B_atk / B_def 回到 8 / 7 / 8 |
| T-7 | 用真实数据开局，且整局能推进到分出胜负而不卡死 |

T-1 只看 `LoadReport.Errors`，§3.4 的脚本警告不会让它变红。
Unity 外跑 `dotnet test` 的方法与坑见 [behaviour_layer_handoff.md](behaviour_layer_handoff.md) §8；内容包放在仓库外时设 `TCGINFU_CONTENT_DIR`。

**测试场景**：菜单 `TCGinFU → 生成命刻之战场景` → `Assets/Scenes/ClockworkCollapse.unity`，
按 Play 即为米阔阔 + 维汀 对 克洛诺斯-IV型 + 镇压重盾臂。沙盒窗口的预设列表里也有「命刻之战」。

---

## 9. 玩家自定义：现在能做什么

格式层面已经支持的（直接改 `StreamingAssets/Content` 里的文件）：

1. **改一张卡的数值**——改 JSON 字段；
2. **加一张全新的卡**——新 id；`effects` 用现有 `EffectKind` 即可，不够用时挂 `behaviourId` 并在 `scripts/*.lua` 里写行为；
3. **新状态**——`statuses.json` 加条目，需要逻辑时挂 Lua；
4. **新角色 + 卡组 / 新敌人与意图**——新增 `characters/`、`decks/`、`enemies/` 条目并在 `pack.json` 登记；
5. **自带卡图 / 立绘**——图丢进包的 `art/` 子目录，JSON 的 `art` / `portrait` 字段填相对路径（不带扩展名），见 §3.3。

Lua 运行在 MoonSharp 硬沙箱里（无 io / os / require），所以玩家**可以**新增行为逻辑，不需要重新编译；
一个脚本写坏只影响它挂的卡。写法见 [behaviour_layer_handoff.md](behaviour_layer_handoff.md) §3–§4。

**做不到的**（§6 的缺口）：

- 独立的玩家包：`persistentDataPath` 入口只对**美术**生效，JSON 不支持多包叠加覆盖，改数值仍要动基础包；
- 阵容是写死的：`ContentScenario.ClockworkHeroes` / `ClockworkEnemies`（`miku`、`weiting` 对 `arm`、`boss`），新角色和敌人要改代码才能上场。
