# 打牌战斗系统 · 架构 v0.5

> 最后核对：2026-11-22（按工作区代码）
> 配套规则书：[v0_5.md](v0_5.md) · 裁定表：[rulings_v0_5.md](rulings_v0_5.md)
> 内容包格式：[data_architecture.md](data_architecture.md) · Lua 行为层：[behaviour_layer_handoff.md](behaviour_layer_handoff.md) · 表现层：[view_architecture.md](view_architecture.md)
> 本文只讲**引擎架构和设计取舍的理由**。卡怎么写、JSON 字段、界面操作请看上面几份。

---

## 0. 现状一览

| 层 | 状态 |
| ----- | ----- |
| 纯 C# 规则引擎（角色面板、卡组、轮次状态机、AP/充能、前后排与援护、暴击、伤害管线、升变、构筑校验） | ✅ |
| 行为钩子（`ICardBehaviour`）+ Lua 实现（MoonSharp） | ✅ |
| JSON 内容包（`Assets/StreamingAssets/Content`）+ 加载器 | ✅ 只加载这一个包 |
| 组局层（`BattleSession` / `ContentScenario`） | ✅ |
| 3D 桌面表现层（`TCGinFU.View`）+ IMGUI 沙盒（`TCGinFU.Sandbox`） | ✅ |
| EditMode 测试 | ✅ 数量与最近一次结果见 README |

工程里只剩一个场景——样例场景与沙盒场景已于 2026-11-22 移除
（见 [archive/legacy_cleanup_handoff.md](archive/legacy_cleanup_handoff.md)）：

| 场景 | 菜单 | 内容 |
| ----- | ----- | ----- |
| `Assets/Scenes/ClockworkCollapse.unity` | `TCGinFU/生成命刻之战场景` | `BattleScenario.命刻之战`，读 JSON 内容包 + Lua（米阔阔 + 维汀 对 克洛诺斯-IV型）。**唯一的场景** |

IMGUI 调试沙盒改为只有编辑器窗口入口（`TCGinFU/战斗沙盒`），不再有对应场景。

---

## 1. 架构总览

### 1.1 程序集（9 个 asmdef）

```
                         ┌──────────────┐
                         │ TCGinFU.Core │  零依赖、noEngineReferences
                         └──────┬───────┘
         ┌──────────────────────┴───────────┐
         ▼                                  ▼
 ┌──────────────┐      ┌──────────────────┐
 │   Content    │      │    Scripting     │
 │  JSON 加载   │      │ + MoonSharp.dll  │
 └──────┬───────┘      └────────┬─────────┘
        └───────────┬───────────┘
                    ▼
          ┌──────────────────┐
          │   Presentation   │  组局、会话驱动
          └────────┬─────────┘
       ┌───────────┼───────────────────┐
       ▼           ▼                   ▼
   ┌────────┐  ┌─────────┐   ┌────────────────────┐
   │  View  │  │ Sandbox │   │ Core.Tests (Editor)│  另引用 Content / Scripting
   └───┬────┘  └────┬────┘   └────────────────────┘
       ▼            ▼
  View.Editor  Sandbox.Editor
（上层各程序集都同时直接引用 Core；Presentation 以下的也都直接引用 Content，见下表）
```

| asmdef | 引用 | 平台 / 备注 |
| ----- | ----- | ----- |
| `TCGinFU.Core` | — | `noEngineReferences`。规则引擎、`ICardBehaviour` 接口 |
| `TCGinFU.Content` | Core | `noEngineReferences`。自写 JSON 解析（`Json`）、`ContentLoader`、`ContentDatabase` |
| `TCGinFU.Scripting` | Core + `MoonSharp.Interpreter.dll`（precompiled） | `noEngineReferences`。`LuaScriptHost`、`LuaCardBehaviour`、看门狗 |
| `TCGinFU.Presentation` | Core / Content / Scripting | `noEngineReferences`。`BattleSession`、`ContentScenario` |
| `TCGinFU.View` | Core / Content / Presentation / `UnityEngine.UI` / `Unity.InputSystem` | 3D 桌面、卡牌、面板 |
| `TCGinFU.View.Editor` | Core / Content / Presentation / View / `UnityEngine.UI` | Editor。`BattleSceneBuilder` 等 |
| `TCGinFU.Sandbox` | Core / Content / Presentation | IMGUI 沙盒 |
| `TCGinFU.Sandbox.Editor` | Core / Content / Presentation / Sandbox | Editor。`BattleSandboxWindow` |
| `TCGinFU.Core.Tests` | Core / Content / Presentation / Scripting / TestRunner + `nunit.framework.dll` | Editor，`UNITY_INCLUDE_TESTS` |

MoonSharp 只出现在 `TCGinFU.Scripting`；View 不直接碰 Lua，经 Presentation 组局。

**为什么 Core 不引用 Unity**：

1. 可以在 Unity 之外编译、跑规则测试，不必等 Unity 域重载；
2. 规则是纯函数式计算，没有任何理由依赖 `UnityEngine`；
3. 将来要做联网时，这一层可以直接搬到服务端做权威结算。

代价：Core 里不能用 `Vector2`、`Random`、`Debug.Log`、`[SerializeField]`。随机数走 `IRng`，日志走 `BattleEvent` 流。
同样的约束现在也加在 Content / Scripting / Presentation 上——加载、脚本宿主和组局都不需要 Unity。

> ⚠️ Core / Content / Scripting 用了 C# 9 的 `init`，而 Unity 走的 `netstandard2.1` 没有
> `System.Runtime.CompilerServices.IsExternalInit`。这三个程序集各自备了一份 `internal` 的
> （如 `Core/Primitives/IsExternalInit.cs`），用 `#if !NET5_0_OR_GREATER` 包住，免得 net8.0 编译时和 BCL 撞类型。
> 不能共用一份：`internal` 跨程序集看不见。

### 1.2 目录结构

```
Assets/
  docs/                         所有设计/架构文档
  Plugins/MoonSharp/            MoonSharp.Interpreter.dll
  Scenes/                       ClockworkCollapse（唯一的场景）
  StreamingAssets/Content/      JSON 内容包（格式见 data_architecture.md）
    pack.json                   包清单
    statuses.json               状态定义
    cards/                      forms / miku / weiting
    characters/heroes.json
    decks/                      miku_starter / weiting_starter
    enemies/chronos_iv.json
    scripts/*.lua               行为脚本
  Scripts/
    Core/                       TCGinFU.Core
      Primitives/               枚举、骰子、IRng、IsExternalInit
      Characters/               属性骰、角色面板、EquipMath、成长校验
      Cards/                    CardDefinition / CardInstance / EffectData / ValueExpr / TargetSelector
      Effects/                  EffectRegistry + 默认 Handler、ICardBehaviour、EffectContext、AttackContext 等可变结算单
      Zones/                    CardPile / ChargeZone / PlayerZones
      Battle/                   BattleEngine（5 个 partial）、BattleState、伤害/暴击、状态、升变、战场
      Actions/                  IBattleAction 各实现（命令模式）
      Events/                   BattleEvent 事件流、BattleLog
      Building/                 DeckList / DeckValidator
    Content/                    TCGinFU.Content（Json/、Loading/）
    Scripting/                  TCGinFU.Scripting
    Presentation/               TCGinFU.Presentation
    View/                       TCGinFU.View（Board/、Layout/、Ui/、Debugging/、Editor/）
    Sandbox/                    TCGinFU.Sandbox（+ Editor/）
    Tests/EditMode/             TCGinFU.Core.Tests
Tools/check-asmdefs.sh          按 asmdef 边界编译（仓库根目录）
```

### 1.3 核心数据流

```
UI/AI ──IBattleAction──▶ BattleEngine.Submit ──┬─▶ 校验 ActionResult（非法则原样拒绝，不改状态）
                                               ├─▶ 变更 BattleState
                                               └─▶ 产出 BattleEvent ──▶ View / BattleLog / 测试
                                    ▲
                     ICardBehaviour（C# 或 LuaCardBehaviour）在固定时点插桩
```

引擎是**同步、单线程、可重入查询**的：没有协程，没有 `async`。需要玩家做决定的地方（升变宣告、玩家行动、敌人意图前的响应窗口、轮末弃牌），引擎把 `PendingDecision` 挂在 `BattleEngine.Pending` 上并停在那里，等下一个 Action 进来。

组局入口只有一条（C# 样例内容已于 2026-11-22 移除）：

- **内容包**（`BattleScenario.命刻之战`）：`BattleSession.Restart` → `ContentScenario.CreateEngine` → 阵容由 `ContentDatabase`（`ContentLoader.LoadPack` 读出）构建 → `LuaScriptHost.BuildRegistry` 把脚本行为装进 `BehaviourRegistry` → `new BattleEngine(state, EffectRegistry.CreateDefault(), behaviours, db.BuildStatusRegistry())` → `host.BindEngine(engine)` → `engine.Start()`。
  绑定必须在 `Start()` 之前：轮始触发在 `Start()` 里就会广播。

内容包根目录由 `ContentPaths.Resolve` 定位：显式参数 → 环境变量 `TCGINFU_CONTENT_DIR` → 平台目录（`streamingAssetsPath` / `persistentDataPath`，由表现层的 `UnityContentRoots` 装进 `ContentPaths.PlatformRoots`）→ 从工作目录和程序集目录逐级向上找 `Assets/StreamingAssets/Content/pack.json`。美术素材另走 `ContentPaths.ResolveAll`，细节见 [data_architecture.md](data_architecture.md) §3.1–§3.3。

---

## 2. 关键设计

### 2.1 角色面板：全部派生，不存冗余

`CharacterSheet` 只存**输入**（职业等级表、四色骰面、HP/MP 加值），所有面板数值都是只读派生属性：

| 派生值 | 公式（规则书 §2.1） |
| ----- | ----- |
| `TotalLevel` | 各职业等级求和（上限 `MaxTotalLevel` = 50） |
| `MaxHp` | `TotalLevel + 力量骰面 × 5 + HpBonus` |
| `MaxMp` | `TotalLevel + 意志骰面 × 5 + MpBonus` |
| `CrisisThreshold` | `MaxHp / 2`（整数除法即向下取整） |
| `AutoPhysicalShield` | `敏捷骰面 / 2` |
| `AutoMagicShield` | `洞察骰面 / 2` |
| `LevelBonus` | `TotalLevel / 5` |
| `EquipTier` | `≥40 → 三阶`，`≥20 → 二阶`，否则一阶 |

骰面值直接用 `enum DieRank { D6 = 6, D8 = 8, D10 = 10, D12 = 12 }`，`(int)rank` 就是规则书要的「计算面板数值时取 6/8/10/12」，不需要查表。

20/40 级的属性骰升级机会不是战斗内容，放在 `CharacterProgression` 静态类里做建卡期校验。

### 2.2 效果系统：数据驱动为主 + 行为钩子为辅

**数据驱动部分** —— 一张卡 = 一串有序 `EffectData`：

```csharp
EffectData {
    EffectKind      Kind;
    ValueExpr       Amount;       // 见 2.3
    TargetSelector  Target;       // TargetScope，见下表
    DamageElement   Element;      // 九大伤害属性
    ShieldKind      ShieldKind;
    string          StatusId;
    int             Duration;     // 状态持续轮数，默认 1
    EffectCondition Condition;
    bool            RollsOwnCrit; // 这一段单独投暴击骰（裁定 R-23）
    Row?            RowTarget;    // MoveRow 的目标排；null = 前后排切换
}
```

| 枚举 | 取值 |
| ----- | ----- |
| `EffectKind` | `Damage` / `Heal` / `GainShield` / `RestoreMp` / `GainAp` / `Draw` / `Discard` / `GainCharge` / `ApplyStatus` / `MoveRow` / `GrantFreeMove` / `SwapRows` / `Cover` |
| `EffectCondition` | `Always` / `OnCrit` / `OnSuperCrit` / `IfCasterInCrisis` / `IfChargeSpent` / `IfTargetInCrisis` |
| `TargetScope` | `Self` / `SingleEnemy` / `AllEnemies` / `FrontRowEnemies` / `BackRowEnemies` / `SingleAlly` / `AllAllies` / `AdjacentAllies` / `CurrentTarget` |

几个字段的取舍：

- **`RowTarget` 与切换分开**：「被击退至后排」对已经在后排的人应当无事发生，用切换会把他拉到前排，效果正好相反。
- **`RollsOwnCrit`**：多段攻击（「压制连射」两次射击各自判定暴击）展开成多条 `Damage` 后若共用一次骰，期望对、手感不对——两段要么全暴要么全不暴。标了这个字段的 `Damage` 由 `EffectContext.RollCritFor` 为该段重投。
- **`Cover`** 作用域固定为 `CurrentTarget`（`EffectData.Cover()`），执行时调 `BattleEngine.RegisterCover`：只有相邻队友（或自己）能援护，一次攻击只能被援护一次（R-12）。

`EffectRegistry` 把 `EffectKind` 映射到 `IEffectHandler`；`EffectRegistry.CreateDefault()` 内置默认执行器。`EffectHandlerBase` 统一做「展开目标 → 逐个检查 Condition → 作用」。绝大多数卡（攻击、护盾、抽牌、上状态、走位）**不需要写一行新代码**，在 JSON 里填字段即可。

**行为钩子部分** —— 卡面可挂一个 `BehaviourId`，由 `BehaviourRegistry` 解析到一个 `ICardBehaviour`。状态（`StatusEffect.BehaviourId`）和敌人意图挂的牌也走同一个注册表：

| 成员 | 时机 |
| ----- | ----- |
| `Id` | 注册键 |
| `CanPlay(EffectContext, out reason)` | 出牌校验的最后一步，额外前置条件。抛异常按「不可打出」处理 |
| `OnPlay(EffectContext)` | 数据效果**之前** |
| `OnResolved(EffectContext)` | 数据效果**之后** |
| `TransformConditionMet(BattleState, PlayerCharacter)` | 条件型升变牌的前置条件 |
| `ModifyAttack(AttackContext, Combatant host, AttackSide)` | 常驻：伤害结算前过手一次攻击，可加减伤、改属性、无视护盾、闪避、改暴击倍率 |
| `OnTrigger(TriggerContext)` | 常驻：「每当……时」，见 `BattleTrigger` |
| `ModifyCritRoll(CritRollContext, Combatant host)` | 常驻：骰子已投、暴击未判定时改骰值 / 抵抗线 |
| `ModifyCost(CostContext, PlayerCharacter host)` | 常驻：费用校验前改 AP/MP/充能费。**必须无副作用**（UI 显示费用也会调，且牌可能最终没打出） |
| `ModifyChargeSpend(ChargeSpendContext, PlayerCharacter host)` | 常驻、**全队广播**：充能离开充能区、尚未进弃牌堆时，可置 `Retained` 留在区里 |

`BattleTrigger`：`DamageTaken` / `DamageDealt` / `CritDealt` / `CardPlayed` / `ChargeSpent` / `RoundStart` / `RoundEnd`。

`CardBehaviour` 是全空默认实现的抽象基类（C# 行为可从它派生；样例实现已移除，目前内容包不用它）。
**`LuaCardBehaviour`（`TCGinFU.Scripting`，internal）直接实现 `ICardBehaviour`**，把各钩子转给 Lua 函数，内容包里的卡全部走它。Lua API、动词表、看门狗、容错规则见 [behaviour_layer_handoff.md](behaviour_layer_handoff.md)，本文不重复。

「常驻」钩子问的是 `BattleEngine.ActiveBehaviours(host)`——此刻挂在某角色身上、处于生效中的全部行为：持续区的牌、升变期间的升变牌、身上的状态（敌人也有）。来源用 `BehaviourSource`（`Card` / `Continuous` / `Transform` / `Status`）区分。

**为什么每次现场枚举而不维护订阅表**：持续牌会被顶替、升变会结束、状态会过期，维护订阅就要在每个这些地方记得退订，漏一处就是幽灵触发器。现场枚举的代价是每次攻击几次字典查询，换来的是**不可能失效**。

**为什么攻击要做成可变结算单（`AttackContext`）**：「下一次攻击+3」「本轮物理转风属性」「无视 2 点物理护盾」「完全闪避」这类词条有十几种，如果每条都在 `DealDamage` 里特判，伤害管线会变成一坨 if，玩家自定义卡也无法参与。做成一个过手的可变对象，脚本就和内置逻辑站在同一个位置上。同理有 `CritRollContext`、`CostContext`、`ChargeSpendContext`。

#### 打牌的固定执行顺序（`BattleEngine.HandlePlayCard`）

```
时机校验（PlayerActions；EnemyResponse 只收打法为 Timing 的牌，与类别无关；
          TransformDeclaration 只收升变牌，转给 HandleDeclareTransform）
  → ResolveCost：卡自己的 ModifyCost → ActiveBehaviours 的 ModifyCost
  → 校验 AP / RequiresBackRow / 充能（卡面硬费用 + 玩家额外选的 ChargeForEffect，
    后者必须是卡面 chargeOptions 里的一档）/ MP（充能**不**在这里抵 MP，R-09 已改写）
  → 校验显式目标
  → 建 EffectContext（ChosenTarget / CurrentTarget / ChargeSpent / Choices）
  → Behaviour.CanPlay
  ──── 校验结束，开始改状态 ────
  → 手牌移出、扣 AP、SpendCharge（每个都过 ModifyChargeSpend）、扣 MP
  → 若 HasCrit：RollCrit（投骰 → ModifyCritRoll → 判定）
  → 发 CardPlayed 事件
  → Behaviour.OnPlay
  → 按顺序结算 EffectData[]
  → Behaviour.OnResolved
  → 若 !ctx.CardRehomed：SendToRestingPlace（持续区 / 形态弃牌堆 / 弃牌堆）
  → FireTrigger(BattleTrigger.CardPlayed)
```

`EffectContext.CardRehomed`：行为层已经把这张牌安置到别处（「顺风充能」把自己变成充能指示物），引擎就不再送它进弃牌堆，免得一张牌同时躺在两个区。

敌人意图挂的那张「敌人的牌」（`EnemyIntent.Card`）在意图结算后由 `PlayIntentCard` 打出，同样是 `发 CardPlayed 事件 → OnPlay → EffectData[] → OnResolved`。**两套机制共用同一条结算流水线**，钩子只是在固定位置插桩，不另起一套结算路径。

`CardPlayed` 事件带 `Card`（`CardDefinition`）而不只是 `CardId`：敌人的意图牌内嵌在 `EnemyContent` 里，不进内容包的卡牌索引，按 id 查不到。表现层的出牌演出靠它拿卡面。注意这个字段**不可序列化**，将来下发给远端客户端时要落回 `CardId`。

敌人还多发一条 `IntentDeclared`，在 `OpenNextIntentWindow()` 里、设 `Pending` 之**前**发出。它和 `CardPlayed` 的区别是时机：表现层要在响应窗口**打开时**就亮出首领的卡面，玩家才来得及据此决定打不打时机牌；等到 `CardPlayed` 时窗口早已关闭、伤害也结算完了。（与轮始的 `IntentRevealed` 也不同：那是「这一轮我打算做这些事」的总览。）

### 2.3 动态数值：`ValueExpr`

规则书 §6.3 要求形态牌写成「造成 `B_atk + 4` 点伤害」而不是固定值，所以任何数值字段都不能是裸 `int`：

```csharp
struct ValueExpr {
    ValueSource Source;  // Flat / FormAttack(B_atk) / FormDefense(B_def) / WeaponDamage
                         // / ArmorShield / LevelBonus / CasterTotalLevel / ChargeSpent
    int Add;             // 加值
    int Times;           // 倍率分子（默认 1）
    int DividedBy;       // 倍率分母（默认 1，整数除法向下取整）
}
// 求值 = Source 取值 × Times ÷ DividedBy + Add
```

其中：

- `B_atk = 当前武器牌基础伤害（含阶梯补正） + LevelBonus`
- `B_def = 当前防具牌基础护盾（含阶梯补正） + LevelBonus`
- 阶梯补正（§7.2）：一阶 +0 / 二阶 +3 / 三阶 +6，`EquipMath.TierBonus(tier)`
- 一阶武器伤害换算（§7.1）：`EquipMath.Tier1WeaponDamage(fu) = fu / 2 + 3`
- `ChargeSpent`：本次打出这张牌付给**卡面词条**的充能数（硬费用 + 玩家额外选的），
  经 `ValueContext.ChargeSpent` 带入。换 MP / 换 AP 的充能是独立动作，不计入这里（§5.2 三选一）

这样同一张形态牌在 5 级和 50 级分别算出 12 和 27 点伤害，不需要为每个等级做一套卡。

### 2.4 伤害管线：一条固定顺序的流水线

伤害结算涉及暴击、弱点抗性、受伤倍率、两种护盾，乘算顺序必须钉死（规则书未明示，见 `rulings_v0_5.md` R-01/R-03/R-05）。入口是 `BattleEngine.ResolveAttack(AttackContext)`，`DealDamage` 只是它的便捷包装：

```
── 出手前（BattleEngine.ResolveAttack）──
   来源牌/意图牌自己的 ModifyAttack（Attacker）
 → 攻方 ActiveBehaviours 的 ModifyAttack（Attacker）
 → 守方 ActiveBehaviours 的 ModifyAttack（Defender）
 → 若 Avoided：发 AttackAvoided，结束（不扣血、不触发命中后效果）
── DamageResolver.Resolve ──
① 基础值      AttackContext.FinalAmount = BaseAmount + FlatBonus − Reduction（下限 0）
② 加减修正    攻方状态增伤 + 守方状态易伤 − 守方状态减伤
③ 暴击        超暴击 ×2；暴击 ×1.5；AttackContext.CritMultiplier 可改写（仅在真暴击时生效）
④ 属性克制    易损 ×2 / 抗性 ÷2 / 免疫 → 0 / 吸收 → 按③的值转治疗，到此结束（R-05）
④.5 受伤倍率  守方状态的 IncomingMultiplier 连乘（「过热：受到最终伤害 ×1.5」写 3/2）
⑤ 护盾抵消    先扣掉 IgnoreShield 点不让盾挡，剩下的由盾吸收（盾不因被无视而消耗）
              物理属性吃物理盾，其余八属性吃魔法盾（R-02，ToShieldKind）
⑥ 扣血        HP 下限钳到 0，跨过危机线标 TargetEnteredCrisis
── 之后 ──
   消耗双方一次性状态（ConsumeTriggeredStatuses）
 → PublishDamage：统计、DamageDealt / CrisisEntered / CombatantDown 事件，
   DamageTaken（受伤方，HpLost>0）/ DamageDealt / CritDealt 触发
```

每一步都独立向下取整；所有倍率（暴击改写、受伤倍率）都用**分数**而不是 float，保证「×1.75 再向下取整」在任何平台上都是同一个整数。④.5 放在克制之后、护盾之前：它放大的是「最终伤害」，而护盾扣的就是最终伤害。

每一步的中间值都写进 `DamageResult`，UI 要做飘字分解、测试要做断言，都从这里取。

**暴击判定**（`CritResolver.Roll` 投骰，`CritResolver.Evaluate` 判定）：

- 单色牌：投 1 颗对应属性骰，`点数 ≥ 暴击抵抗` → 暴击
- 双色牌：投 2 颗；**超暴击** = 两颗都 ≥ 抵抗 **或** 两颗同点且 ≥ 4（`MatchedPairThreshold`，R-04）
- 只有卡面带 `HasCrit` 才投骰；所有暴击掷骰都走 `BattleEngine.RollCrit`，中间过一次 `ModifyCritRoll`
- 投骰和判定拆开，是因为骰值会被词条改（「掷骰结果+1」「把一颗骰视为 7」）；改完必须用同一套规则重判，拿到布尔结果后再改就丢了双色超暴击的信息
- 暴击抵抗默认（`EnemyRank` 的 `DefaultCritResist`）：小怪 4 / 精英 6 / 首领 7；状态的 `CritResistDelta` 可修正

### 2.5 战场、相邻与仇恨

`Battlefield` 用 `前排[4] / 后排[4]` 两个定长槽位数组（`SlotsPerRow` = 4），玩家和敌人各一个（`BattleState.PlayerField` / `EnemyField`）。

**相邻**（§4.3，`AreAdjacent` / `AdjacentTo`）：

1. 同排 `Slot ± 1`；
2. 后排 `Slot i` ↔ 前排 `Slot i`（正前方，同列）。

**默认仇恨**（§4.2，`BattleState.DefaultAggroTarget`）：前排有人 → 前排；前排全空 → 后排**上一轮造成伤害最高**者。所以 `Combatant` 同时记 `DamageDealtThisRound` 和 `DamageDealtLastRound`，在轮末滚动。

**走位**：`MoveRowAction` 每轮 1 次、1 AP；`GrantFreeMove` 给的免费移动（`PlayerCharacter.FreeMoves`）不受每轮一次的限制，也可以在 `EnemyResponse` 窗口里兑现，轮末清零。效果侧还有 `MoveRow`（定向或切换）和 `SwapRows`（整排互换、保持各自的列）。

**意图索敌**（`IntentTargeting`）：`DefaultSingle` / `FrontRowAoe` / `BackRowAoe` / `AllPlayers` / `DesignatedRowSingle` / `HighestHpSingle`。不锁定目标的意图（`EnemyIntent.LocksTarget` 为假）在**结算时**按此刻站位重新解析（R-24），所以响应窗口里的走位能真的躲开。

### 2.6 区域与充能

```csharp
PlayerZones {
    CardPile Deck, Hand, Discard;
    CardPile FormDeck;             // 升变卡组：升变期间实际在抽的那摞形态牌，平时为空
    IReadOnlyDictionary<string, CardPile> FormDecks; // 每张升变牌一组 10 张形态额外卡组，按升变牌 Id 索引
    CardPile FormDiscard;          // 升变期间打出的形态牌，退出升变时一起移出本局（R-14）
    IEnumerable<CardInstance> AllDiscards; // FormDiscard + Discard，墓地对外算一个
    CardPile Exile;                // 除外区：一次性牌 + 升变结束的整套升变牌与形态牌
    CardInstance Continuous;       // 持续区，上限 1，被顶替者进弃牌堆
    ChargeZone Charges;            // 充能区，内部是 ChargeToken 序列
    CardPile StashedHand;          // 升变期间暂存的原手牌（主卡组不暂存，留在原位）
}
```

- **主卡组与升变卡组是并存的两摞，不是一摞切来切去**。升变期间 `Deck` 原封不动，只是 `DrawFromDeck` 这两轮改从 `FormDeck` 抽、抽空了洗 `FormDiscard` 续牌。**两套牌各自循环，互不相通**：升变期间不碰主卡组与主弃牌堆，形态牌也绝不会被洗进主卡组。（界面上两摞**共用卡组那一块地**显示，但那只是布局选择，见 [view_architecture.md](view_architecture.md) §3。）
- **墓地在规则上是两摞、对外是一个**：形态牌必须单独堆在 `FormDiscard` 才能在退出升变时整体移出本局（R-14），但界面与「从墓地回收」这类效果一律走 `AllDiscards`，同时够得着两边。

- **充能区容量** 默认 `ChargeZone.CapacityFor(玩家人数) = max(玩家人数 - 1, 2)`，是**每个角色各自**的区域；效果可临时调整（`AdjustChargeCapacity`）。
- 溢出时，**最早**置入的那张被顶出，进入**该卡原持有者**的弃牌堆（不是充能区主人的）——所以 `ChargeToken` 必须记 `OwnerId`，不能只存卡。
- 贴充能每角色每轮 1 次、0 AP，卡面可追加次数（`ExtraChargePlacements`，经 `GrantChargePlacement`）。消耗充能三选一（`ChargeUse`）：抵 5 点 MP / +1 AP / 支付卡面「消耗 X 充能」。
- 敌人吞噬充能走 `DevourCharges`，**不算**玩家消耗：不记 `ChargesSpent` 统计、不触发 `ChargeSpent`，否则敌人一发虹吸就能替玩家满足「累计消耗充能」类条件。
- 从某区搬牌进充能区统一走 `ChargeFromZone(from, to, ChargeSource)`，`ChargeSource` = `Discard`（弃牌堆顶）/ `DeckTop` / `HandBottom`。
- **除外区**（§4.1）收两类牌：卡面写「消耗」的一次性牌（`CardDefinition.ExileOnResolve`，内容包里写 `"exile": true`）打出结算完直接进；升变牌与它那 10 张形态牌在弹出升变时整套进。进了除外区的牌不会再回到卡组、手牌或弃牌堆。
- `ExileOnResolve` 刻意做成**声明式字段而不是脚本钩子**：「打出后去哪」对绝大多数卡是无条件的静态归宿，引擎读一个 bool 就够，构筑校验与卡面 UI 也能读到它。等出现「有条件地除外」或「跨回合置入」的卡，再加一个 Lua 可调的除外原语（与 `ChargeFromZone` 同级），而不是让每张卡自己判断归宿。
- 升变牌**没被打出**而是被弃掉时进主弃牌堆——那时它还只是一张普通手牌。

### 2.7 轮次状态机

```
RoundStart ──▶ Draw ──▶ PlayerPhase ──▶ EnemyPhase ──▶ RoundEnd ──┐
    ▲                                                              │
    └──────────────────────────────────────────────────────────────┘
                      分出胜负 ──▶ Finished
```

`RoundPhase`：`RoundStart` / `Draw` / `PlayerPhase` / `EnemyPhase` / `RoundEnd` / `Finished`。

| 阶段 | 引擎做的事（按代码顺序） | 挂出的 `DecisionKind` |
| ----- | ----- | ----- |
| `RoundStart`（`BeginRound`） | 轮数 +1 → 各玩家 `BeginRound`（AP 重置、贴充能/走位次数重置、自动双盾按敏捷÷2 / 洞察÷2 重新授予）→ **结算退出升变** → **`RoundStart` 触发** → **敌人揭示意图** | `TransformDeclaration`（等能升变的人宣告或放弃，含第 3 轮保底） |
| `Draw`（`ProceedToDraw`） | 常态：第 1 轮 5 张，之后每轮 3 张；升变期间两轮都从升变卡组抽 5（`DrawCountFor`） | — |
| `PlayerPhase` | 每人 3 AP，全队自由穿插。逐个 `DeclareReady`，全员 ready 后推进 | `PlayerActions` |
| `EnemyPhase`（`ProceedToEnemyPhase` / `OpenNextIntentWindow`） | 按意图自上而下：先把这次攻击摆成 `AttackContext`，发 `IntentDeclared`，开响应窗口；全员 Pass 后结算（重解析目标 → 援护改写 → `ResolveAttack` → 上状态 → `PlayIntentCard`） | 每条意图一次 `EnemyResponse`（R-11） |
| `RoundEnd`（`BeginRoundEnd`） | 场景命刻 +1 → 轮末持续伤害/恢复 → **`RoundEnd` 触发** → 状态递减与过期 → 判胜负 | `HandLimitDiscard`（手牌超 7 的人，R-15） |
| （`FinishRound`） | 升变计时 −1 → 各玩家 `EndRound`（清 AP、免费移动、自动盾，伤害统计滚动）→ 敌人统计滚动、清意图 → 下一轮 | — |

`DecisionKind` 全部取值：`None` / `TransformDeclaration` / `PlayerActions` / `EnemyResponse` / `HandLimitDiscard`。战斗结束时 `Pending = PendingDecision.None`。

两处顺序是有意为之：

- **轮始触发在意图揭示之前**：持续牌「轮始获得 X」这类词条要先于意图落地。退出升变又排在触发之前，所以到期的升变牌收不到退出那一轮的 `RoundStart`。
- **轮末触发在持续伤害之后、状态递减之前**：脚本还能看到本轮挂着的状态。

首领两阶段由内容层实现：`EnemyContent` 给敌人装 `IntentPlanner`，按 `PhaseTwoHpThreshold` 在 `PhaseOne` / `PhaseTwo` 意图池之间切换，每轮出 `IntentsPerRound` 条；MP 不够的意图（`Enemy.CanAfford`）不会被选。

### 2.8 升变

```
【进入】RoundStart 的 TransformDeclaration 窗口宣告（手牌里直接打出升变牌也等于宣告）
       → 原手牌暂存到 StashedHand（主卡组、持续区与充能区都原样留在场上）
       → FormDecks 里对应的 10 张形态组洗进 FormDeck → 升变牌的行为以 Transform 来源生效
       → Draw 阶段从 FormDeck 抽 5（代替常规抽牌）
【维持】下一轮 Draw：同样从 FormDeck 抽 5；打出或弃掉的形态牌进 FormDiscard
       FormDeck 抽空就把 FormDiscard 洗回去继续抽——这 10 张在升变期间一直循环
【退出】第 3 轮 RoundStart：升变牌 + 全部形态牌（含 FormDiscard）→ 除外区（移出本局）
       → 从 StashedHand 取回原手牌 → 本轮从主卡组正常抽 3
```

三条触发路径（`TransformTrigger`）：

1. **条件型** `Conditional`：由行为的 `TransformConditionMet` 判定；**没挂行为的条件型牌视为条件已满足**（方便做占位内容）。条件未达成或第 3 轮前抽到 → 可免费「展示 → 洗回主卡组 → 抽 1 张」（`ShuffleBackTransformAction`）；
2. **即发型** `Immediate`：在手即可开，允许第 1–2 轮直接升变；
3. **第 3 轮保底**（`GuaranteeRound` = 3）：升变牌仍在主卡组内且条件已满足 → 可让它直接跳出并发动（`UsedThirdRoundGuarantee` 记账）。

每局每角色只能升变一次（`HasTransformedThisBattle`）。

### 2.9 构筑校验

`DeckValidator` 做纯静态校验（§3.2），返回**全部**违规项而不是第一条：

| 校验项 | 规则 |
| ----- | ----- |
| 总张数 | 恒定 30 |
| 职业牌（含升变） | 12–16 |
| 武器牌 | 3–8 |
| 防具牌 | 3–8 |
| 秘宝牌 | 0–6 |
| 同名上限 | 普通牌 3；**升变牌 1** |
| 形态组 | 每张升变牌必须配一组对应职业的 10 张形态额外卡组 |

> 规则书 2026-11-22 调整了配比与角色体系，校验器还没跟上，以规则书为准（见 [known_issues.md](known_issues.md)）。

### 2.10 `BattleEngine` 的 5 个 partial

| 文件 | 内容 |
| ----- | ----- |
| `BattleEngine.cs` | 构造、`Start`、轮次流程（`BeginRound` / `ProceedToDraw` / `ProceedToEnemyPhase` / `OpenNextIntentWindow` / `ResolveCurrentIntentAndContinue` / `BeginRoundEnd` / `FinishRound` / `Finish`）、意图索敌 |
| `BattleEngine.Actions.cs` | `Submit` 分发与各 `Handle*`：出牌、贴充能、充能换 AP、走位、Ready、Pass、轮末弃牌 |
| `BattleEngine.Mutations.cs` | 公开的状态变更动词，Handler 和脚本共用：`DealDamage` / `Heal` / `GrantShield` / `RestoreMp` / `GrantAp` / `DrawCards` / `PlaceCharge` / `ChargeFromZone` / `DevourCharges` / `SpendCharge` / `AdvanceSceneClock` / `ApplyStatus` / `SwitchRow` / `MoveRow` / `SwapRows` / `GrantFreeMove` / `AdjustChargeCapacity` / `GrantChargePlacement` / `RegisterCover` |
| `BattleEngine.Hooks.cs` | `ActiveBehaviours`、意图牌打出、`ResolveCost`、`RollCrit`、`ResolveAttack` 攻击管线、触发广播（`FireTrigger` / `FireTriggerAll`）、行为调用的异常隔离 |
| `BattleEngine.Transform.cs` | `TransformCandidates`、宣告 / 放弃 / 洗回、`EnterTransform` / `ExitTransform` |

玩家动作（`IBattleAction`）：`PlayCardAction`（带 `ChargeForEffect`、`Choices`）/ `PlaceChargeAction` / `SpendChargeForApAction` / `SpendChargeForMpAction`（消耗 1 个充能回复 5 MP，玩家阶段与响应窗口都可用） / `MoveRowAction` / `DeclareReadyAction` / `DeclareTransformAction` / `DeclineTransformAction` / `ShuffleBackTransformAction` / `ShareDamageAction`（响应窗口里替相邻队友分摊，伤害对半拆） / `PassResponseAction` / `DiscardToHandLimitAction`。

---

## 3. 测试与编译检查

测试数量与最近一次运行结果统一记在 [README](../../README.md)。`Core` 通过 `InternalsVisibleTo("TCGinFU.Core.Tests")` 让测试直接摆出中间状态，不必先跑完一整轮。

测试不再依赖任何成套内容：引擎规则一律用 `TestFactory` 就地造最小卡（见 `CardEffectRuleTests`），
「整套内容接上去能不能跑完一局」那一层交给读 JSON 内容包的 `LegacyContentTests` / `LuaBehaviourTests`。

**Unity 内**：`Window → General → Test Runner → EditMode`（或 Unity MCP 的 `run_tests`）。表现层改动也要跑——`BattleSessionTests` 依赖费用文案。

**编译检查（不启动 Unity）**：

```bash
bash Tools/check-asmdefs.sh
```

按每个 asmdef 的真实引用逐个编译（刻意不用 ProjectReference：MSBuild 的引用会传递，asmdef 不会）。需要本机 Unity 生成过 `.csproj` 和 `Library/ScriptAssemblies`。它只编译，不跑测试。

**Unity 外跑测试（dotnet test）**：测试程序集现在引用 Content / Presentation / Scripting，所以临时测试工程（建在仓库外，别放进 `Assets/`）要 Include
`Core`、`Content`、`Scripting`、`Presentation`、`Tests/EditMode` 五处的 `*.cs`，引用 `Assets/Plugins/MoonSharp/MoonSharp.Interpreter.dll`，再加 NUnit 与测试适配器。注意：

- 只用 Unity 自带 NUnit 3.5 有的 API（没有 `Assert.Multiple`），NuGet 新版会编过而 Unity 里报错；
- 读内容包的测试靠 `ContentPaths.Resolve` 向上找 `Assets/StreamingAssets/Content`，工作目录不在仓库下时设 `TCGINFU_CONTENT_DIR`。

---

## 4. 已知缺口（实现层面）

规则书留白处的实现取值统一记在 [rulings_v0_5.md](rulings_v0_5.md)，这里只列**代码还没做或没接通**的：

| 缺口 | 现状 |
| ----- | ----- |
| 「掷骰后」响应窗口 | 没有 `DiceResponse` 类决策；暴击骰只能被常驻 `ModifyCritRoll` 改，玩家无法在掷骰后打牌响应 |
| 场景命刻 | `SceneClock` 轮末 +1、`AdvanceSceneClock` 可额外推进，但**不触发任何后果** |
| 部位破坏、倒地 / 复苏 / 淘汰 | 未实现。HP 归零即 `IsDown`，之后不再参与 |
| 选牌后端 | `ChargeFromZone` 固定取弃牌堆顶 / 牌库顶 / 手牌底，不读玩家选择；View 的选牌界面（`ZonePeekPanel.OpenForSelection`）已做但没接上（审计 B-2） |
| `EffectKind.Discard` | 枚举有，但 `EffectRegistry.CreateDefault()` 没注册执行器，数据里用它会在结算时抛「没有注册 Discard 的执行器」 |
| 内容包入口 | JSON 只加载一个完整包，不支持多包按 id 叠加覆盖。构建产物入口与玩家包的**美术**覆盖已实现（审计 B-6） |

内容层面的缺陷（`RollsOwnCrit` 未在 JSON 里启用、首领意图池漏项等）见 [known_issues.md](known_issues.md)。
