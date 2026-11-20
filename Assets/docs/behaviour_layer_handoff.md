# 行为层交接 · 给下一个 agent

> 配套：规则书 [v0_5.md](v0_5.md) · 引擎架构 [architecture_v0_5.md](architecture_v0_5.md) ·
> 裁定表 [rulings_v0_5.md](rulings_v0_5.md) · 数据层 [data_architecture.md](data_architecture.md) ·
> 表现层 [view_architecture.md](view_architecture.md)
>
> **你的任务**：把老版本（Godot）49 张卡的特殊结算逻辑实现出来，让这些卡真的能打。
> 本文交代现状、可用的接口、以及一堆**我已经踩过的坑**。

---

## 1. 一句话现状

**数据全在，逻辑一行没有。**

`ContentScenario` 创建引擎时传的是 `new BehaviourRegistry()`——一个空注册表。
49 张卡的 `behaviourId` 全部写在 JSON 里，但没有任何 C# 实现与之对应。

| 层面 | 状态 | 证据 |
| ----- | ----- | ----- |
| 数据层 | ✅ 完成 | 49 张卡 / 2 角色 / 2 敌人 / 2 卡组全在 `Assets/StreamingAssets/Content/` |
| 基础效果 | ⚠️ 一半 | 25 张卡有 `effects`；**24 张是空数组** |
| 行为钩子 | ❌ 0% | 49 个 `behaviourId` 全部只有 id，零实现 |
| 敌人意图 | ⚠️ 数据齐、机制缺 | 11 条意图数据完整，**6 条的核心机制当前引擎表达不了** |

**那 24 张空 `effects` 的卡，打出去是真的什么都不会发生**——包括全部 4 张升变牌、
3 张持续牌、以及大部分时机牌。

> 这不是意外遗漏，是 [data_architecture.md](data_architecture.md) §0 的既定决策 D-3
> 「只迁卡面数据，行为留桩」。但请注意：**现有验收测试 T-1~T-7 只验了「能加载、能跑完一局」，
> 没有任何一条在验「打出去真的有事发生」**。`T07_整局能推进到结束` 跑得通，
> 恰恰是因为空效果的卡什么都没做。

---

## 2. 可用的接口

### 2.1 两条路：数据驱动 vs 脚本钩子

```
打牌的固定流水线（BattleEngine.HandlePlayCard）
  校验（AP / MP / 充能 / 时机 / 目标 / Behaviour.CanPlay）
    → 支付费用
    → Behaviour.OnPlay(ctx)          ← 数据效果之前
    → 按顺序结算 EffectData[]          ← 纯数据
    → Behaviour.OnResolved(ctx)      ← 数据效果之后
    → 卡进弃牌堆 / 持续区
```

**能用 `EffectData` 表达的就不要写代码。** 现有 `EffectKind`：
`Damage / Heal / GainShield / RestoreMp / GainAp / Draw / Discard / GainCharge /
ApplyStatus / MoveRow / Cover`。
配合 `EffectCondition`（`OnCrit / OnSuperCrit / IfCasterInCrisis / IfChargeSpent /
IfTargetInCrisis`）和 `ValueExpr`（`FormAttack` = B_atk 等），覆盖面比看起来大。

**加一个新的 `EffectKind` 比写一个 behaviour 更有价值**——前者所有卡都能用，
而且玩家自定义卡也能用（见 [data_architecture.md](data_architecture.md) §9：
数据驱动的覆盖面 = `EffectKind` 的覆盖面）。只有真正一次性的逻辑才该进 behaviour。

### 2.2 `ICardBehaviour`

```csharp
public interface ICardBehaviour
{
    string Id { get; }
    bool CanPlay(EffectContext ctx, out string reason);   // 额外前置条件
    void OnPlay(EffectContext ctx);                       // 数据效果之前
    void OnResolved(EffectContext ctx);                   // 数据效果之后
    bool TransformConditionMet(BattleState state, PlayerCharacter owner); // 条件型升变牌
}
```

继承 `CardBehaviour` 可以只覆盖需要的那几个。参考实现看
`Assets/Scripts/Content/SampleBehaviours.cs`——三个示范分别演示了这三类钩子：

| 示范 | 用的钩子 | 为什么数据表达不了 |
| ----- | ----- | ----- |
| `AwakenConditionBehaviour` | `TransformConditionMet` | 要读跨回合累计量（暴击数 / 全队消耗充能数） |
| `ThousandCutsBehaviour` | `OnResolved` | 连斩次数与目标都是随机的 |
| `PerfectParryBehaviour` | `CanPlay` | 要读当前响应窗口里的敌人意图 |

### 2.3 `EffectContext` 能拿到什么

`Engine`（全部写入口）、`State`、`Caster`、`Card`、`ChosenTarget`、
`CurrentTarget`（响应窗口里的既定目标）、`Crit`、`ChargeSpent`、`Scratch`（临时标记）、
`Resolve(ValueExpr)`、`ResolveTargets(TargetSelector)`。

**所有状态变更都要走 `ctx.Engine` 上的方法**（`DealDamage` / `Heal` / `GrantShield` /
`DrawCards` / `PlaceCharge` / `ApplyStatus` / `SwitchRow` / `RegisterCover` …）。
它们负责发 `BattleEvent`。直接改 `PlayerCharacter` 的字段会让战报和 UI 失去同步。

### 2.4 跨回合累计量

`BattleState.Stats`（`BattleStatKeys`）已经在记：`CardsPlayed / DamageDealt /
CritsDealt / SuperCritsDealt / ChargesSpent / EnemiesDowned`，
同时有「全队合计」和「每个角色各自」两种口径。老卡的前置条件大量用到这些。
不够就加新 key，在引擎对应位置 `State.Stats.Add(...)`。

### 2.5 状态数值放 `StatusRegistry`

`ApplyStatus` 只写 id / 层数 / 持续轮数；**每层扣几点、算什么属性**属于状态本身，
注册在 `StatusRegistry` 里（参考 `SampleDeck.CreateStatusRegistry`）。
不要让每张挂流血的卡各自重抄一遍数值。

---

## 3. 接线点（第一件该做的事）

`Assets/Scripts/Presentation/ContentScenario.cs` 里创建引擎时传的是空注册表。
新建 `Assets/Scripts/Content/LegacyBehaviours.cs`，照 `SampleBehaviours.CreateRegistry()`
的样子返回一个填好的 `BehaviourRegistry`，然后在 `ContentScenario` 里换掉。

**建议先打通链路再铺量**：挑 3 张代表性的卡（一张有 effects 的技能牌、
一张空 effects 的持续牌、一张时机牌）实现出来，补一条验证效果真生效的测试，
跑通之后再批量迁剩下的。

---

## 4. 老逻辑在哪

反编译产物在 `Tools/godot-legacy-extract/src/`：

| 文件 | 内容 |
| ----- | ----- |
| `card_resolver.gd` | **主要参照**。`_execute_effect()` 里 34 张卡的主动逻辑，`play_reaction()` 里 8 张时机牌的响应逻辑 |
| `battle_state.gd` | 命刻 / 过热 / 相位等场景机制、角色与敌人面板 |
| `boss_database.gd` | 11 条首领意图 |
| `card_database.gd` | 49 张卡的原始字段（已迁完，对账用） |

JSON 里每张卡都保留了 `unmapped` 字段，存着当前 Core 没有对应概念的老字段
（`ranged` / `requires_back_row` / `can_target_self` / `damage_options`）。
实现对应机制时直接对账，不用回头翻反编译源码。

---

## 5. 引擎目前表达不了的东西

这些**需要先改引擎**，不是写个 behaviour 就能绕过去的。

### 5.1 敌人意图（6 条）

`EnemyIntent` 现在只有 `Label / Targeting / Damage / Element / DesignatedRow / InflictedStatus`。
以下机制没有容身之处：

| 意图 | 缺的机制 |
| ----- | ----- |
| 以太虹吸 | 吞噬玩家充能 + 首领回复 |
| 相位折跃 | 前后排强制互换 + 命刻 +1 |
| 装甲重构 | 护盾触发的后续伤害加成 |
| 命运乱流 | 按手牌数量惩罚 |
| 因果抹杀令 | 伤害可由队友分摊 |
| 归零坍缩 | 后续「过热」状态 |

另有两条是部分失真：重力横扫的**击退**、时空断头台的**前排物理/后排魔法混伤**。

> `Enemy.IntentPlanner` 本来就是个委托，所以「按阶段抽意图」不用改引擎；
> 但上面这些是**结算效果**，必须扩 `EnemyIntent` + 在 `BattleEngine.ResolveCurrentIntentAndContinue`
> 里加处理。

### 5.2 多段攻击的独立暴击（裁定 R-23）

当前**每次打牌投一次暴击骰**，不是每条效果投一次。老卡「压制连射」
写的是「两次射击分别独立投掷敏捷骰判定暴击」——展开成 2 条 `Damage` 后
两条共享同一个暴击结果，**数值对、手感不对**。

### 5.3 §6 里记的其他待办

命刻时钟、首领两阶段切换、部位破坏、倒地/复苏/淘汰、
魔力激发卷轴、维汀自动魔法护盾 +1 的特判。详见
[data_architecture.md](data_architecture.md) §6 末尾的清单。

---

## 6. 需要规则设计者拍板的

不要自己替用户决定这两条：

- **R-21**：老数据的 `arcane`（奥术）伤害属性。规则书 §7.4 只有九大属性。
  当前暂时映射成 `Light`（对本场 Boss 行为等价，但语义是错的）。
  正解可能是新增 `DamageElement.Arcane`。
- **R-23**：多段攻击是否应当各自独立判定暴击（见 5.2）。

另有一条我发现但没改的小出入：`Enemy.IntentFullyVisible` 只区分「小怪 / 其他」，
于是首领显示成「半公开」；而规则书 §7.3 写的是首领「按阶段与机制公开」，
和精英的「半公开」不是一回事。

---

## 7. 工程上的坑（都是我真踩过的）

### 7.1 验证必须用 `Tools/check-asmdefs.sh`

**不要把所有 `.cs` 塞进一个 csproj 编译**——那会把 asmdef 的程序集边界整个抹掉，
漏写 `references` 这类错误一个都抓不到。脚本按真实边界逐个编译，
并且**刻意不用 ProjectReference**（MSBuild 的 P2P 引用是传递的，Unity 的 asmdef 不是）。

```bash
bash Tools/check-asmdefs.sh
```

### 7.2 NUnit 版本

Unity 自带 **NUnit 3.5.0**，**没有 `Assert.Multiple`**（3.6 才加）。
用 NuGet 的新版 NUnit 验证会编译通过而 Unity 里报 `CS0117`——假阳性。
`check-asmdefs.sh` 已经改成直接引用 `Library/PackageCache` 里 Unity 那份 dll。

### 7.3 `init` 访问器需要每个程序集自备 `IsExternalInit`

`TCGinFU.Core` / `TCGinFU.Content` / `TCGinFU.Presentation` 都设了
`noEngineReferences`（netstandard2.1 没有 `IsExternalInit`）。
Core 和 Content 各有一份 `internal` 的 shim。**新建 Unity-free 程序集并用 `init` 时，
记得也放一份**——跨程序集看不见别人的 internal。

### 7.4 新增序列化字段在旧资产上是零值

Unity 给**已存在的资产**补新字段用的是**类型零值**，不是 C# 的字段初始值。
加过 `cardScale`（变成 0，卡牌缩没）和 `peekable`（变成 false，弃牌堆点不开）
两次都踩了。要么写 `Normalize()` 自愈，要么别依赖会静默默认错的 bool
（`ZoneLayout.CanPeek` 就是改成由排布方式推导的）。

### 7.5 Unity MCP 可用

Unity 编辑器开着时可以直接 `read_console` / `execute_code` / `run_tests` /
进出 Play 模式。**遇到「看起来不对」的问题就进去量，不要靠推演**——
我有好几轮是靠推断交付、结果错了。

### 7.6 Python 在 conda 里

`python` 不在 PATH，实际在 `D:\miniconda3\python.exe`；
打印中文必须 `PYTHONIOENCODING=utf-8`（默认 cp1252 会抛 `UnicodeEncodeError`）。

---

## 8. 验收：请比 T-1~T-7 更进一步

现有测试在 `Assets/Scripts/Tests/EditMode/LegacyContentTests.cs`，
只验了数据能加载、数量对、配比合法、公式求值对、能跑完一局。

**你至少要补上「效果真的生效」这类断言**，例如：

- 打出某张卡后，目标 HP / 护盾 / 状态确实按卡面变化
- 空 `effects` + 有 behaviour 的卡，behaviour 确实被调用了
- 条件型升变牌在条件未达成时**不可**发动，达成后可以
- 时机牌在响应窗口里确实改变了那次攻击的结果
- 敌人意图的高级机制确实改变了战斗状态（吞噬充能、排位互换…）

引擎的 `BattleEvent` 流很适合做断言，`BattleLog.Format(engine)` 能直接出可读战报。

跑测试：`bash Tools/check-asmdefs.sh` 编译 + Unity MCP 的 `run_tests`（EditMode）。
当前基线是 **175 个全过**，不要让它变红。

---

## 9. 建议的推进顺序

1. **接线**：`LegacyBehaviours.cs` + `ContentScenario` 换掉空注册表（小）
2. **打通链路**：3 张代表卡 + 对应的「效果真生效」测试（小）
3. **批量迁 `card_resolver.gd`**（最大头，34 张主动 + 8 张响应）
4. **扩 `EnemyIntent`** 支持 6 条意图的机制（中大，要动引擎）
5. 补齐 §5.3 的场景机制（命刻 / 阶段 / 部位破坏 / 倒地复苏）
6. 维汀自动魔法护盾 +1 的特判（几小时）

动手前先读 [data_architecture.md](data_architecture.md) §5（老数据到新模型的完整映射表）
和 [rulings_v0_5.md](rulings_v0_5.md)（20 条已有裁定，其中 6 条标 ⚠ 仍待确认）。
