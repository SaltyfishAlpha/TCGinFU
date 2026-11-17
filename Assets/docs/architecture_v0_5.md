# 打牌战斗系统 · 架构与实现计划 v0.5

> 配套规则书：[v0_5.md](v0_5.md)
> 目标：把 v0.5 规则书落成**可测试的纯 C# 战斗规则引擎**，Unity 表现层后接。
> 约定：本系统所有文档统一放在 `Assets/docs/`。

---

## 0. 本轮范围

| 做 | 不做（留给后续） |
| ----- | ----- |
| 纯 C# 核心规则引擎（角色面板、卡组、轮次状态机、AP/充能、前后排与援护、暴击、伤害管线、升变、构筑校验） | uGUI / UI Toolkit 表现层 |
| ScriptableObject 数据层（把卡牌内容做成资产） | 联网同步、存档格式 |
| EditMode 单元测试 | 完整卡池内容（只做足够测试的样例卡） |

---

## 1. 架构总览

### 1.1 分层

```
┌──────────────────────────────────────────────┐
│  Presentation（未来）  MonoBehaviour / UI     │
│    订阅 BattleEvent 流，提交 IBattleAction    │
├──────────────────────────────────────────────┤
│  Data   TCGinFU.Data                          │
│    ScriptableObject 资产 → 烘焙成 Core 的     │
│    CardDefinition / EnemyDefinition 等 POCO   │
├──────────────────────────────────────────────┤
│  Core   TCGinFU.Core   ★ 本轮主体             │
│    noEngineReferences = true（不引用 Unity）  │
│    BattleEngine / BattleState / 规则计算      │
└──────────────────────────────────────────────┘
```

**为什么 Core 不引用 Unity**：

1. 可以用 `dotnet test` 在 Unity 之外秒级跑完全部规则测试，不必等 Unity 域重载；
2. 规则是纯函数式计算，没有任何理由依赖 `UnityEngine`；
3. 将来要做联网时，这一层可以直接搬到服务端做权威结算。

代价：Core 里不能用 `Vector2`、`Random`、`Debug.Log`、`[SerializeField]`。随机数走 `IRng`，日志走 `BattleEvent` 流。

### 1.2 目录结构

```
Assets/
  docs/
    v0_5.md                     规则书
    architecture_v0_5.md        本文
    rulings_v0_5.md             规则裁定表（规则书留白处的实现约定）
  Scripts/
    Core/                       TCGinFU.Core.asmdef
      Primitives/               枚举、骰子、IRng
      Characters/               属性骰、角色面板、装备档案
      Cards/                    卡牌定义、效果数据、取值表达式、行为钩子
      Zones/                    牌堆与各类区域
      Battle/                   战场、战斗状态、伤害管线、轮次状态机
      Actions/                  玩家动作（命令模式）
      Events/                   BattleEvent 事件流
      Building/                 卡组构筑校验
    Data/                       TCGinFU.Data.asmdef（引用 Core）
    Tests/
      EditMode/                 TCGinFU.Core.Tests.asmdef
```

### 1.3 核心数据流

```
UI/AI ──IBattleAction──▶ BattleEngine ──┬─▶ 校验 ValidationResult（非法则原样拒绝，不改状态）
                                        ├─▶ 变更 BattleState
                                        └─▶ 产出 BattleEvent[] ──▶ UI/日志/回放
```

引擎是**同步、单线程、可重入查询**的：没有协程，没有 `async`。需要玩家做决定的地方（选目标、敌人阶段的响应窗口、升变宣告），引擎把 `PendingDecision` 挂出来并停在那里，等下一个 Action 进来。

---

## 2. 关键设计

### 2.1 角色面板：全部派生，不存冗余

`CharacterSheet` 只存**输入**（职业等级表、四色骰面、职业/秘宝加值），所有面板数值都是只读派生属性：

| 派生值 | 公式（规则书 §2.1） |
| ----- | ----- |
| `TotalLevel` | 各职业等级求和（上限 50，单职业上限 10） |
| `MaxHp` | `TotalLevel + 力量骰面 × 5 + HpBonus` |
| `MaxMp` | `TotalLevel + 意志骰面 × 5 + MpBonus` |
| `CrisisThreshold` | `MaxHp / 2`（整数除法即向下取整） |
| `AutoPhysicalShield` | `敏捷骰面 / 2` |
| `AutoMagicShield` | `洞察骰面 / 2` |
| `LevelBonus` | `TotalLevel / 5` |
| `EquipTier` | `≥40 → 三阶`，`≥20 → 二阶`，否则一阶 |

骰面值直接用 `enum DieRank { D6 = 6, D8 = 8, D10 = 10, D12 = 12 }`，`(int)rank` 就是规则书要的「计算面板数值时取 6/8/10/12」，不需要查表。

20/40 级的属性骰升级机会不是战斗内容，放在 `CharacterProgression` 静态类里做建卡期校验。

### 2.2 效果系统：数据驱动为主 + 脚本钩子为辅

**数据驱动部分** —— 一张卡 = 一串有序 `EffectData`：

```csharp
EffectData {
    EffectKind      Kind;       // Damage / Heal / GainShield / Draw / GainCharge / ...
    ValueExpr       Amount;     // 见 2.3
    TargetSelector  Target;     // Self / SingleEnemy / AllEnemies / FrontRowEnemies / AdjacentAlly / ...
    DamageElement   Element;    // 九大伤害属性
    ShieldKind      ShieldKind;
    string          StatusId;
    EffectCondition Condition;  // Always / OnCrit / OnSuperCrit / IfInCrisis / IfChargeSpent
}
```

`EffectRegistry` 把 `EffectKind` 映射到 `IEffectHandler`。绝大多数卡（攻击、护盾、抽牌、上状态）**不需要写一行新代码**，策划调 ScriptableObject 字段即可。

**脚本钩子部分** —— 卡面可挂一个 `BehaviourId`，由 `BehaviourRegistry` 解析到一个 `ICardBehaviour`：

```csharp
interface ICardBehaviour {
    string Id { get; }
    void OnPlay(CardPlayContext ctx);        // 数据效果之前
    void OnResolved(CardPlayContext ctx);    // 数据效果之后
    bool CanPlay(CardPlayContext ctx, out string reason);  // 额外前置条件
}
```

打牌的固定执行顺序：

```
校验（AP / MP / 充能 / 时机 / 目标 / Behaviour.CanPlay）
  → 支付费用
  → Behaviour.OnPlay
  → 按顺序结算 EffectData[]
  → Behaviour.OnResolved
  → 卡进弃牌堆 / 持续区 / 充能区
```

英雄技能牌、升变牌、带特殊顶替逻辑的牌走钩子；其余走数据。**两套机制共用同一条结算流水线**，钩子只是在固定位置插桩，不另起一套结算路径。

### 2.3 动态数值：`ValueExpr`

规则书 §6.3 要求形态牌写成「造成 `B_atk + 4` 点伤害」而不是固定值，所以任何数值字段都不能是裸 `int`：

```csharp
struct ValueExpr {
    ValueSource Source;  // Flat / FormAttack(B_atk) / FormDefense(B_def)
                         // / WeaponDamage / ArmorShield / LevelBonus / CasterTotalLevel
    int Add;             // 加值
    int Times;           // 倍率分子（默认 1）
    int DividedBy;       // 倍率分母（默认 1，整数除法向下取整）
}
```

其中：

- `B_atk = 当前武器牌基础伤害（含阶梯补正） + LevelBonus`
- `B_def = 当前防具牌基础护盾（含阶梯补正） + LevelBonus`
- 阶梯补正（§7.2）：一阶 +0 / 二阶 +3 / 三阶 +6，由 `EquipTier` 决定
- 一阶武器伤害换算（§7.1）：`WeaponMath.Tier1Damage(fu) = fu / 2 + 3`

这样同一张形态牌资产在 5 级和 50 级分别算出 12 和 27 点伤害，不需要为每个等级做一套卡。

### 2.4 伤害管线：一条固定顺序的流水线

伤害结算涉及暴击、弱点抗性、两种护盾，乘算顺序必须钉死（规则书未明示，见 `rulings_v0_5.md` R-01）：

```
① 基础值      ValueExpr 求值
② 加减修正    持续牌 / 状态 / 词条的 flat 加减
③ 暴击        超暴击 ×2；暴击 ×1.5（向下取整）
④ 属性克制    易损 ×2 / 抗性 ÷2（向下取整） / 免疫 → 0 / 吸收 → 转治疗
⑤ 护盾抵消    物理属性吃物理盾，其余八属性吃魔法盾
⑥ 扣血        Hp 下限钳到 0，跨过危机线时发 CrisisEntered 事件
```

每一步的中间值都写进 `DamageResult`，UI 要做飘字分解、测试要做断言，都从这里取。

**暴击判定** `CritResolver`：

- 单色牌：投 1 颗对应属性骰，`点数 ≥ 敌方暴击抵抗` → 暴击
- 双色牌：投 2 颗，取高的比对；**超暴击** = 两颗都 ≥ 抵抗 **或** 两颗同点且 ≥ 4
- 只有卡面带 `HasCrit` 才投骰
- 暴击抵抗默认：小怪 4 / 精英 6 / 首领 7–8

### 2.5 战场、相邻与仇恨

`Battlefield` 用 `前排[4] / 后排[4]` 两个定长槽位数组，角色存 `(Row, Slot)`。

**相邻**（§4.3）：

1. 同排 `Slot ± 1`；
2. 后排 `Slot i` ↔ 前排 `Slot i`（正前方，同列）。

**默认仇恨**（§4.2）：前排有人 → 前排；前排全空 → 后排**上一轮造成伤害最高**者。所以 `PlayerCharacter` 要同时记 `DamageDealtThisRound` 和 `DamageDealtLastRound`，在轮末做滚动。

### 2.6 区域与充能

```csharp
PlayerZones {
    CardPile Deck, Hand, Discard;
    CardPile FormDeck;             // 场外形态额外卡组（10 张）
    CardPile CompletedTransform;   // 已完成升变区
    CardInstance Continuous;       // 持续区，上限 1，被顶替者进弃牌堆
    List<ChargeToken> Charges;     // 充能区
    StashedDeck Stash;             // 升变期间暂存的原手牌 + 原主卡组
}
```

- **充能区容量** = `max(玩家人数 - 1, 2)`，是**每个角色各自**的区域。
- 溢出时，**最早**置入的那张被顶出，进入**该卡原持有者**的弃牌堆（不是充能区主人的）——所以 `ChargeToken` 必须记 `OwnerId`，不能只存卡。
- 贴充能每角色每轮 1 次、0 AP；消耗充能三选一：抵 5 点 MP / +1 AP / 支付卡面「消耗 X 充能」词条。
- 本规则**无除外区**，只有升变结束的形态牌移出本局（进已完成升变区）。

### 2.7 轮次状态机

```
RoundStart ──▶ Draw ──▶ PlayerPhase ──▶ EnemyPhase ──▶ RoundEnd ──┐
    ▲                                                              │
    └──────────────────────────────────────────────────────────────┘
```

| 阶段 | 引擎做的事 |
| ----- | ----- |
| `RoundStart` | 清空上轮自动双盾 → 重新授予（敏捷÷2 / 洞察÷2）→ 敌人揭示意图 → 结算退出升变 → 开放进入升变宣告 |
| `Draw` | 常态：第 1 轮 5 张，之后每轮 3 张；升变第 1 轮：形态组抽 5；升变第 2 轮：形态组剩余 5 张全抽 |
| `PlayerPhase` | 每人 3 AP，全队自由穿插。玩家逐个 `DeclareReady`，全员 ready 后推进 |
| `EnemyPhase` | 按意图自上而下结算，每个意图前开**响应窗口**（可打防具牌/时机牌） |
| `RoundEnd` | 场景命刻 +1 → 轮末持续伤害/恢复 → 清理临时状态 → 手牌超 7 弃置 → 伤害统计滚动 → 升变计时 -1 |

`PlayerPhase` 和 `EnemyPhase` 的响应窗口都通过 `PendingDecision` 暴露给外部，引擎自己不推进，等 Action。

### 2.8 升变

```
【进入】RoundStart 宣告 → 手牌+主卡组 暂存到 Stash（持续区与充能区原样保留）
       → 10 张形态组洗入卡组区 → 获得形态被动
       → Draw 阶段从形态组抽 5（代替常规抽牌）
【维持】下一轮 Draw：把形态组剩余 5 张全部抽上手
【退出】第 3 轮 RoundStart：升变牌 + 全部形态牌 → 已完成升变区（移出本局）
       → 从 Stash 取回原手牌与原主卡组 → 本轮正常抽 3
```

三条触发路径：

1. **条件型**：卡面条件达成后宣告；条件未达成或第 3 轮前抽到 → 可免费「展示 → 洗回主卡组 → 抽 1 张」；
2. **即发型**：在手即可开，允许第 1–2 轮直接升变；
3. **第 3 轮保底**：第 3 轮 `RoundStart` 时，升变牌仍在主卡组内且条件已满足 → 可让它直接跳出并发动。

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

---

## 3. 实现步骤与现状

| # | 步骤 | 产出 | 状态 |
| --- | ----- | ----- | ----- |
| 1 | 程序集骨架 | `TCGinFU.Core` / `TCGinFU.Data` / `TCGinFU.Core.Tests` | ✅ |
| 2 | 基础原语 | 枚举、`IRng`、`DieRank` | ✅ |
| 3 | 角色面板 | `AttributeDice` / `CharacterSheet` / 装备档案 | ✅ |
| 4 | 卡牌模型 | `CardDefinition` / `CardInstance` / `ValueExpr` / `EffectData` | ✅ |
| 5 | 区域系统 | `CardPile` / `PlayerZones` / `ChargeZone` | ✅ |
| 6 | 战场 | `Battlefield` / 相邻 / 仇恨 | ✅ |
| 7 | 伤害管线 | `CritResolver` / `DamageResolver` / `ShieldPool` | ✅ |
| 8 | 效果执行 | `EffectRegistry` + 各 Handler + `BehaviourRegistry` | ✅ |
| 9 | 动作与状态机 | `IBattleAction` 各实现 + `BattleEngine`（4 个 partial） | ✅ |
| 10 | 升变 | `TransformState` + 进入/维持/退出 + 第 3 轮保底 | ✅ |
| 11 | 构筑校验 | `DeckList` / `DeckValidator` | ✅ |
| 12 | 数据层 | `CardDefinitionAsset` / `DeckListAsset` / `SerializedEffect` | ✅ |

当前测试：**103 个，全部通过**。

### 3.1 怎么跑测试

**Unity 内**：`Window → General → Test Runner → EditMode`。

**Unity 外（推荐，秒级）**：Core 设了 `noEngineReferences`，所以能脱离 Unity 单独编译。
在仓库外建两个临时 `.csproj`（不要放进 `Assets/`，否则 Unity 会去导入它）：

```xml
<!-- Core.csproj：TargetFramework net8.0，LangVersion 9.0 -->
<EnableDefaultCompileItems>false</EnableDefaultCompileItems>
<Compile Include="<仓库>\Assets\Scripts\Core\**\*.cs" />

<!-- Tests.csproj：引用 Core.csproj + NUnit + NUnit3TestAdapter + Microsoft.NET.Test.Sdk -->
<Compile Include="<仓库>\Assets\Scripts\Tests\EditMode\**\*.cs" />
```

然后 `dotnet test Tests.csproj`。

数据层要连 Unity 一起编译验证时，把 `Assets\Scripts\Data\**\*.cs` 也 Include 进来，
`TargetFramework` 改 `netstandard2.1`，并引用 Unity 安装目录下的
`Editor\Data\Managed\UnityEngine\UnityEngine.CoreModule.dll` 与 `UnityEngine.dll`。

> ⚠️ Core 用了 C# 9 的 `init` 访问器，而 Unity 走的 `netstandard2.1` 里没有
> `System.Runtime.CompilerServices.IsExternalInit`。`Primitives/IsExternalInit.cs` 自备了一份，
> 并用 `#if !NET5_0_OR_GREATER` 包住，免得 net8.0 编译时和 BCL 撞类型。

---

## 4. 已知留白

规则书有若干处需要实现时做裁定（乘算顺序、护盾与属性的对应、响应窗口时机等）。这些**不在本文里拍脑袋决定**，统一记在 [rulings_v0_5.md](rulings_v0_5.md)，每条标注「规则书依据 / 实现取值 / 不确定度」，便于后续和规则设计者对账。
