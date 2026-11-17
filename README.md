# TCGinFU

基于《最终物语》（Fabula Ultima）TRPG 框架的**局内协同 TCG 对战系统**。

跑团的探索、交涉、休整沿用 FU 原规则；一旦进入战斗冲突，切换成本项目的卡牌系统进行小队协同打牌，
战斗结束后角色的 HP、MP 与持续状态直接带回跑团——**战斗开始与结束都不自动回满**。

> Unity 6000.3.8f1 · URP

---

## 当前进度

规则书 v0.5 的**战斗核心规则引擎**已完成并通过测试（140 个单元/集成测试）。表现层尚未开始。

| 层 | 状态 |
| ----- | ----- |
| 规则引擎（轮次、出牌、升变、伤害、构筑校验） | ✅ 已完成 |
| 样例内容（30 张卡组 + 形态组 + 三档敌人） | ✅ 已完成 |
| 战报渲染 `BattleLog` | ✅ 已完成 |
| ScriptableObject 数据层 | ⚠ 代码已就位，但还没有实际资产，链路未实跑 |
| UI / 表现层 | ⬜ 未开始 |
| 联网 | ⬜ 未开始 |

一局真实战斗的战报长这样（`BattleLog.Format(engine)`）：

```
── 第 3 轮 ──
  石像鬼 意图：石爪（11 点）
  石像鬼 意图：尖啸（6 点）
  ★ 弓手 发动升变（第3轮保底跳出）
  [Draw]
  弓手 抽 5 张
  [PlayerPhase]
  弓手 打出「影袭」 → 石像鬼
    石像鬼 受到 14 点暗伤害
  [EnemyPhase]
    护卫 受到 0 点物理伤害 [盾挡11]
    弓手 受到 2 点风伤害 [盾挡4]
```

---

## 文档

所有设计文档统一放在 `Assets/docs/`：

| 文档 | 内容 |
| ----- | ----- |
| [`v0_5.md`](Assets/docs/v0_5.md) | **规则书**，唯一事实来源 |
| [`architecture_v0_5.md`](Assets/docs/architecture_v0_5.md) | 架构设计、分层理由、实现步骤、测试跑法 |
| [`rulings_v0_5.md`](Assets/docs/rulings_v0_5.md) | **规则裁定表**：规则书未明确、但实现必须做出选择的 18 处 |

改规则前先看裁定表——引擎里每条有争议的取值都在那里标了依据、取值与不确定度。

---

## 代码结构

```
Assets/Scripts/
  Core/        TCGinFU.Core      规则引擎（零 Unity 依赖）
  Content/     TCGinFU.Content   样例卡组、敌人、行为钩子（零 Unity 依赖）
  Data/        TCGinFU.Data      ScriptableObject 资产 → 烘焙成 Core 的 POCO
  Tests/       TCGinFU.Core.Tests  EditMode 测试
```

`Core` 与 `Content` 都设了 `noEngineReferences: true`，不引用 `UnityEngine`。
这样规则测试能脱离 Unity 跑（秒级，不必等域重载），将来做联网也能把这两层直接搬到服务端做权威结算。
代价是里面不能用 `Vector2`、`Random`、`Debug.Log`、`[SerializeField]`——
随机数走 `IRng`，日志走 `BattleEvent` 事件流，序列化字段放到 `Data` 层做镜像。

### 引擎怎么用

引擎是**同步、单线程、无协程**的。需要玩家决策的地方，它把 `Pending` 挂出来停住，等下一个动作提交：

```csharp
var state  = BattleSetup.Create(rng, players, enemies);
var engine = new BattleEngine(state, EffectRegistry.CreateDefault(),
                              SampleBehaviours.CreateRegistry(),
                              SampleDeck.CreateStatusRegistry());
engine.Start();

while (state.Outcome == BattleOutcome.Ongoing)
{
    switch (engine.Pending.Kind)          // 引擎在等什么
    {
        case DecisionKind.PlayerActions:  // 玩家阶段，全队自由穿插
            engine.Submit(new PlayCardAction(id, cardUid, targetId));
            engine.Submit(new DeclareReadyAction(id));
            break;
        case DecisionKind.EnemyResponse:  // 敌人意图结算前的响应窗口
            engine.Submit(new PassResponseAction(id));
            break;
        // TransformDeclaration / HandLimitDiscard ...
    }
}
```

非法动作会被**原样拒绝且不改动任何状态**（返回 `ActionResult.Fail(reason)`），
所以 UI 可以乐观提交，失败了拿 `Reason` 提示即可。

状态变更全部产出 `BattleEvent`：表现层订阅它做动画飘字，测试拿它做断言，将来联网时它就是下发给客户端的增量。

### 卡牌怎么做

数据驱动为主、脚本钩子为辅，两者共用同一条结算流水线：

```
校验（AP / MP / 充能 / 时机 / 目标 / Behaviour.CanPlay）
  → 支付费用 → Behaviour.OnPlay
  → 按顺序结算 EffectData[]
  → Behaviour.OnResolved → 卡进弃牌堆 / 持续区
```

绝大多数卡 = 一串 `EffectData`，**不用写代码**：

```csharp
new CardDefinition("wpn_shortbow_shot", "短弓速射", CardCategory.Weapon, CardKind.Skill)
{
    Colors  = new[] { AttributeColor.Dexterity },
    ApCost  = 1,
    Effects = new[] { EffectData.Damage(new ValueExpr(ValueSource.WeaponDamage)) },
};
```

数值用 `ValueExpr` 而不是裸 `int`，这样同一张形态牌在 5 级和 50 级会自动算出 12 和 27 点伤害
（规则书 §6.3 的 `B_atk + 4`），不必为每个等级做一套卡。

数据表达不了的（跨回合累计条件、随机次数、读当前敌人意图）才挂 `ICardBehaviour`。
样例内容里 30 张卡只有 3 张挂了钩子，见 `Content/SampleBehaviours.cs`。

---

## 跑测试

**Unity 内**：`Window → General → Test Runner → EditMode → Run All`

**Unity 外**（推荐，秒级）：`Core` 与 `Content` 不依赖 Unity，可以单独编译。
在**仓库外**建三个临时 `.csproj`（放进 `Assets/` 会被 Unity 导入）：

| 工程 | `TargetFramework` | Include | 引用 |
| ----- | ----- | ----- | ----- |
| `Core.csproj` | `net8.0` | `Assets/Scripts/Core/**/*.cs` | — |
| `Content.csproj` | `net8.0` | `Assets/Scripts/Content/**/*.cs` | Core |
| `Tests.csproj` | `net8.0` | `Assets/Scripts/Tests/EditMode/**/*.cs` | Core, Content, NUnit, NUnit3TestAdapter, Microsoft.NET.Test.Sdk |

三者都要 `<LangVersion>9.0</LangVersion>` 和 `<EnableDefaultCompileItems>false</EnableDefaultCompileItems>`，
然后 `dotnet test Tests.csproj`。

要连 `Data` 层一起验证时，`TargetFramework` 改 `netstandard2.1`，并引用 Unity 安装目录下的
`Editor/Data/Managed/UnityEngine/UnityEngine.CoreModule.dll` 与 `UnityEngine.dll`。

> `Core` 用了 C# 9 的 `init`，而 Unity 的 netstandard2.1 里没有 `IsExternalInit`。
> `Core/Primitives/IsExternalInit.cs` 自备了一份并用 `#if !NET5_0_OR_GREATER` 包住——别删。

---

## 下一步

1. 过一遍 [`rulings_v0_5.md`](Assets/docs/rulings_v0_5.md) 里标 ⚠ 的 6 条，尤其
   **R-18**（响应牌是否消耗 AP）与 **R-11**（响应窗口粒度）——这两条直接影响战斗手感，
   定下来之后再接 UI 比较省返工；
2. 把 `Content/SampleCards.cs` 的样例卡翻成 `CardDefinitionAsset` 资产并跑通往返，
   这是目前唯一没有实跑验证过的链路（需要在 Unity 里做）；
3. 接表现层：订阅 `BattleEvent`，提交 `IBattleAction`。

### 已知缺口

- **数据层未实跑**：`CardDefinitionAsset` → `CardDefinition` 的烘焙只验证过能编译，
  没有真实资产跑过往返。`ScriptableObject` 需要 Unity 运行时，跑不进 `dotnet test`。
- **时机牌的「队友行动时」响应**：§7.1 提到时机牌可在「敌人阶段**或队友行动时**」打出。
  目前玩家阶段本就是全队自由穿插，时机牌随时能打，算是覆盖了；
  但没有「队友某张牌结算前」这种更细的插入点。等 R-11 定了一起做。
- **武器牌卡池与装备的构筑期绑定**：§3.2 说武器牌卡池由装备槽解锁，
  目前 `DeckValidator` 还没校验「卡组里的武器牌必须匹配当前装备」。
