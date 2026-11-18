# TCGinFU

基于《最终物语》（Fabula Ultima）TRPG 框架的**局内协同 TCG 对战系统**。

跑团的探索、交涉、休整沿用 FU 原规则；一旦进入战斗冲突，切换成本项目的卡牌系统进行小队协同打牌，
战斗结束后角色的 HP、MP 与持续状态直接带回跑团——**战斗开始与结束都不自动回满**。

> Unity 6000.3.8f1 · URP

---

## 怎么跑起来

### 正式界面（uGUI）

> 菜单 `TCGinFU → 生成战斗场景` → 生成并打开 `Assets/Scenes/Battle.unity` → 按 Play

棋盘是**真 3D**：每张卡都是带厚度的网格物体，所以**卡组、弃牌堆、充能堆都能看出有多厚**。
透视相机斜着看桌面，近处手牌大、远处敌人小。倾斜角度、视野、俯视高度都在布局资产里调
（`boardTilt` / `cameraFov` / `cameraHeight`），卡厚是 `CardMeshFactory.CardThickness`。

操作：

- **右键手牌** → 菜单：`打出` / `贴充能…`
  - 选 `贴充能…` → 点一名队友的角色卡 → 确认弹窗 → 确定
- **左键拖**手牌到角色卡上 = 对这个目标打出（和菜单里的「打出」等价）
- **点弃牌堆 / 持续区 / 已完成升变区** = 摊开查看里面所有的牌
- 点角色卡切换目标与操作角色；左键点手牌 = 对当前选中目标出牌
- 拖拽时合法目标亮绿、非法亮红
- 顶栏：`自动出牌 / 跳过本步 / 自动打完 / 重开`
- **按 F1 开关调试浮层**：完整战报 + 全体状态快照（见下）

**棋盘上的一切都是卡**。角色和手牌同一个扑克比例（2.5 × 3.5 英寸），版式按实体角色卡来：
名条 → 插画位 → HP 条 → 数值栏 → 意图栏。布局资产里只有一个 `cardWidth`，高度自动算，
各区域只能整体 `cardScale` 缩放，不会被拉变形。

**充能按实体牌的做法叠放**：与角色卡等大的牌横置（旋转 90°）压在队友角色卡**背后**、
卡中心偏下的位置，一张比一张低。

排位从上到下：敌后排 → 敌前排 → 我前排 → 我后排 → 手牌。
敌方按它们自己的视角排，所以敌人的后排在更上方。

区域即使没有牌也会画出空卡位轮廓，不会塌陷、牌位也不会乱跳。

**场地区域全部是数据**，配置在 `Assets/Settings/BattleLayout.asset`：

- 挪位置、改大小 → 改该区域的 `anchorMin/Max`（0–1 相对坐标，分辨率自适应）
- 加一个区域 → 列表里加一条，选 `source` 和锚点，**不用改代码**
- 删 / 临时隐藏 → 删一条或关 `visible`
- 想同屏显示四个人的手牌 → 复制四份手牌区，`playerIndex` 分别填 0/1/2/3

改完直接 Play 就生效，界面每次都按这个资产重建。误改了用菜单
`TCGinFU → 重建默认布局资产` 恢复。区域调得太小装不下一张牌时，控制台会给出警告。

详见 [表现层架构](Assets/docs/view_architecture.md)。

### 调试浮层（正式发版要删掉）

按 **F1** 开关。显示完整战报、全体角色状态快照（含两种护盾中自动盾的部分、
充能牌的原持有者、升变剩余轮数等）、本局累计统计。

移除方法：删掉 `Assets/Scripts/View/Debugging/BattleDebugOverlay.cs`，
再删掉 `BattleSceneBuilder` 里 `AddComponent<BattleDebugOverlay>()` 那一行。
不删也行——正式构建下它会在 `Awake` 里自我销毁。

### 调试沙盒（IMGUI，保留）

> 菜单 `TCGinFU → 战斗沙盒`（编辑器窗口，不用进 Play 模式）
> 或 `TCGinFU → 生成战斗沙盒场景` → `Assets/Scenes/BattleSandbox.unity` → Play

正式界面做好之前的临时工具，现在仍然留着：它把全部状态和完整战报平铺出来，
查规则问题比正式 UI 方便。可以调种子、队伍人数（1–4）、敌人预设（木桩 / 哥布林 / 石像鬼 / 深渊领主 / 哥布林小队），
点手牌出牌或贴充能，宣告升变，敌人阶段打时机牌响应，右侧实时看战报。
`自动出牌` 让傻瓜 AI 替当前待行动的角色打完手牌，`自动打完` 直接演完整局。

**同一个种子必定重现完全相同的一局**（洗牌与骰点都是确定性的），所以发现规则问题时把种子记下来就能复现。

> 沙盒刻意用 IMGUI 而不是 uGUI：同一份绘制代码在编辑器窗口和 Play 模式里都能跑，
> 不用拼 Canvas / 预制体 / 事件系统。这是**验规则的工具，不是游戏 UI**——
> 正式表现层等 [`rulings_v0_5.md`](Assets/docs/rulings_v0_5.md) 里标 ⚠ 的几条定稿后再做。

---

## 当前进度

规则书 v0.5 的**战斗核心规则引擎**已完成并通过测试（140 个单元/集成测试）。表现层尚未开始。

| 层 | 状态 |
| ----- | ----- |
| 规则引擎（轮次、出牌、升变、伤害、构筑校验） | ✅ 已完成 |
| 样例内容（30 张卡组 + 形态组 + 三档敌人） | ✅ 已完成 |
| 战报渲染 `BattleLog` | ✅ 已完成 |
| 调试沙盒（编辑器窗口 + 可运行场景） | ✅ 已完成 |
| 正式 UI：场地区域、抽牌、拖拽、高亮 | ⚠ 代码完成且编译通过，**但未在 Unity 里实跑** |
| ScriptableObject 卡牌数据层 | ⚠ 代码已就位，但还没有实际资产，链路未实跑 |
| UI 动效 / 美术 | ⬜ 未开始 |
| 联网 | ⬜ 未开始 |

沙盒里傻瓜 AI 自动打完的结果（种子 20251117），可以看出难度曲线是成立的：

| 敌人 | 1 人 | 2 人 | 3 人 | 4 人 |
| ----- | ----- | ----- | ----- | ----- |
| 哥布林 | 胜 4 轮 | 胜 4 轮 | 胜 4 轮 | 胜 1 轮 |
| 石像鬼（精英） | 胜 12 轮 | 胜 7 轮 | 胜 5 轮 | 胜 4 轮 |
| 深渊领主（首领） | **负** 3 轮 | **负** 6 轮 | 胜 10 轮 | 胜 8 轮 |

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
  Core/         TCGinFU.Core           规则引擎（零 Unity 依赖）
  Content/      TCGinFU.Content        样例卡组、敌人、行为钩子（零 Unity 依赖）
  Presentation/ TCGinFU.Presentation   BattleSession：引擎 + 选中状态 + 提交入口（零 Unity 依赖）
  Data/         TCGinFU.Data           ScriptableObject 卡牌资产 → 烘焙成 Core 的 POCO
  View/         TCGinFU.View           正式 uGUI 界面
    Editor/     TCGinFU.View.Editor    战斗场景与布局资产生成
  Sandbox/      TCGinFU.Sandbox        调试沙盒（IMGUI）
    Editor/     TCGinFU.Sandbox.Editor 编辑器窗口 + 场景生成
  Tests/        TCGinFU.Core.Tests     EditMode 测试
```

`BattleSession` 是唯一的推进逻辑，正式 UI 和沙盒共用，不会出现两套流程对不上。
它不引用 Unity，所以「自动打完不卡死」「同种子重现同一局」这类行为能在 `dotnet test` 里跑。

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

## 不启动 Unity 做验证

```bash
bash Tools/check-asmdefs.sh      # 按 asmdef 的真实程序集边界编译整个工程
```

**必须用这个，不能把所有 .cs 塞进一个 csproj 编译。** 后者会把程序集边界整个抹掉，
asmdef 少写一条 `references` 这种错误一个都抓不到——踩过：单工程编译全绿，
Unity 里直接 `CS0234: 命名空间 TCGinFU 中不存在 Presentation`。

脚本刻意**不用 ProjectReference**：MSBuild 的 P2P 引用是传递的，A→B→C 时 A 不写 C 也能编过；
而 Unity 的 asmdef 引用不传递，用到谁就必须写谁。所以它按依赖顺序逐个编译，
各自只引用显式列出的那几个 dll，与 Unity 行为一致。

> 需要先在 Unity 里打开过一次工程（生成 `.csproj` 与 `Library/ScriptAssemblies`），
> 脚本靠它们定位 Unity 安装目录和包程序集。

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
