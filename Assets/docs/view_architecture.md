# 表现层架构

> 配套：[规则书](v0_5.md) · [引擎架构](architecture_v0_5.md) · [规则裁定](rulings_v0_5.md)
> 本文只讲 UI。规则一律在 `TCGinFU.Core` 里，表现层不做任何规则判断。

---

## 1. 分层与依赖

```
TCGinFU.View            uGUI 正式界面（本文主体）
TCGinFU.Sandbox         IMGUI 调试沙盒（保留，查战报比正式 UI 方便）
      ↓ 都依赖
TCGinFU.Presentation    BattleSession：引擎 + 选中状态 + 提交入口（零 Unity 依赖）
      ↓
TCGinFU.Core / Content  规则引擎与内容
```

`BattleSession` 是**唯一的推进逻辑**。正式 UI 和沙盒共用它，所以不会出现
「沙盒能打、正式 UI 打不了」这种两套流程对不上的情况。它不引用 Unity，
因此 `自动打完不会卡死`、`同种子重现同一局` 这类行为能在 `dotnet test` 里跑。

**表现层不判断规则。** 「这张牌能不能打」不由 UI 决定，而是直接提交给引擎——
引擎拒绝时原样返回原因且不改状态（这是引擎的契约），UI 把原因显示出来即可。
唯一的例外是拖拽时的「目标类型过滤」（攻击牌不让拖到队友身上），那只是减少
无效操作的手感优化，不是规则判断。

---

## 1.5 3D 视角：世界空间 Canvas + 透视相机

桌面是一块 **World Space Canvas**，被一台**透视相机**斜着看。
靠近手牌的一侧更大、远处的敌人更小，就是真实的 3D 透视。

**为什么不做成真 3D 物体**：`CardView` / `ZoneView` / `CombatantView` 这些
uGUI 组件可以原样复用，拖拽、射线、布局全不用重写。把 Canvas 放进世界空间
就已经是 3D 了——卡牌本来就是平面，没必要建模。

```
BoardCanvas   WorldSpace，绕 X 轴倾斜 boardTilt 度，1 像素 = 0.01 世界单位
BoardCamera   透视，按桌面尺寸与 FOV 反算距离，自带 AudioListener
HudCanvas     ScreenSpaceOverlay，sortingOrder = 100
```

顶栏和各种弹窗放在 **HudCanvas**，**不**跟着桌面倾斜——倾斜的文字没法读。
HUD 的 `sortingOrder` 更高，所以弹窗开着时会优先吃掉点击，自然挡住棋盘操作。

布局资产里的 3D 参数：

| 字段 | 作用 |
| ----- | ----- |
| `boardTilt` | 桌面后仰角度。0 = 平面视角，越大越像趴在桌上看 |
| `cameraFov` | 相机视野。越小透视越弱、越接近平面 |
| `cameraMargin` | 取景余量。1 = 刚好装下桌面 |
| `cameraHeight` | 相机抬高量（按桌面高度比例），越高越俯视 |

两处必须用对相机的地方（搞错就会出现「拖拽的牌跟不上鼠标」）：

- `CardView.OnDrag` 的 `ScreenPointToLocalPointInRectangle`
- `BattleView.FindTargetUnder` 的 `RectangleContainsScreenPoint`

两者都按 `Canvas.renderMode` 决定传 `null` 还是 `Canvas.worldCamera`。

> 战斗场景用 `NewSceneSetup.EmptyScene` 生成：`BattleView` 自己建相机，
> 留着默认的 Main Camera 会变成两台相机同时渲染、天空盒盖在桌面上。

---

## 2. 场地区域：数据驱动

**这是本次设计的核心要求：区域的位置、大小、数量都要能改。**

所以区域不写在代码里，而是 `BattleLayoutAsset` 里的一个列表：

```
BattleLayoutAsset (ScriptableObject)
  referenceResolution   设计分辨率
  tableColor            桌面底色
  zones: List<ZoneLayout>
```

每个 `ZoneLayout` 是纯数据：

| 字段组 | 内容 |
| ----- | ----- |
| 标识 | `key`（代码里找它用）、`displayName`、`visible` |
| 内容 | `source`（手牌 / 卡组 / 弃牌堆 / 充能区 / 持续区 / 形态卡组 / 已完成升变区 / 四个排位 / 空）、`playerIndex` |
| 位置 | `anchorMin/Max` + `offsetMin/Max`——锚点是 0–1 相对坐标，所以分辨率自适应 |
| 排布 | `arrange`（横排 / 扇形 / 堆叠 / 网格 / 横置）、`cardScale`、`combatantSize`、`spacing`、`fanAngle`、`fanArc`、`gridColumns`、`reservedSlots` |
| 交互 | `draggable`（里面的卡能拖出去）、`acceptsDrop`（能拖进来）、`faceDown` |
| 外观 | `background`、`border`、`showLabel` |

于是：

- **挪位置 / 改大小** → 改 `anchorMin/Max`
- **加一个区域** → 列表里加一条，填 `source` 和锚点，**不用改代码**
- **删一个区域** → 删一条，或者把 `visible` 关掉
- **改成四人分屏** → 复制四份手牌区，各自 `playerIndex` 填 0/1/2/3

`playerIndex = -1` 表示「当前操作的角色」，区域内容会跟着选中目标走。
默认布局是单人视角：手牌 / 卡组 / 弃牌堆 / 充能区 / 持续区都用 -1。

**真要加一种全新的内容来源**（比如「秘宝展示区」），才需要动两处代码：
`ZoneSource` 加一个枚举值，`BattleView.ResolveCards` 加一个分支。

### 一切都是卡

卡牌比例**不可单独调**：布局资产上只有一个 `cardWidth`，高度恒按美式扑克
（2.5 × 3.5 英寸，即 1 : 1.4）算出。各区域只能用 `cardScale` 整体缩放，
所以无论怎么改都不会出现被拉扁拉长的卡。

**棋盘上的角色也是卡**，和手牌同一个比例、同一套尺寸来源，版式按实体角色卡来：
顶上名条 → 中间插画位（目前是纯色块，将来换立绘）→ HP 条 → 数值栏 → 底部意图栏。
所以没有「角色面板尺寸」这种单独的字段，角色区域照样用 `cardScale`。

### 空区域不塌陷

每个区域有 `reservedSlots`：即使一张牌都没有，也会画出这么多个**空卡位轮廓**。
好处有两个——区域永远看得出有多大，以及牌位固定，不会因为张数变化整排乱跳。

另外 `ZoneView` 会在区域实际尺寸装不下一个卡位时**打一次警告**
（`[战斗布局] 区域「X」实际大小 … 装不下一个卡位 …`）。
改锚点很容易把某块调得过小，与其让卡默默画到框外，不如直接报出来。

### 排布算法

都在 `ZoneView.Arrange()` 里，按 `arrange` 分发。它统一先算出 N 个「卡位姿态」
（位置 + 旋转），真卡占前面几个，剩下的画成空卡位：

- **横排**：居中平铺，放不下时自动压缩间距（允许负间距让卡互相叠）
- **扇形**：在横排基础上加弧线下沉与旋转，手牌用
- **堆叠**：只露最上面几张并轻微错位，牌堆/弃牌堆用
- **网格**：左上起按 `gridColumns` 换行
- **横置**：卡旋转 90° 依次压住，充能用（见下）

### 排位的朝向

敌方按**敌人自己的视角**排：它们的后排在更远处，也就是屏幕**更上方**。
我方按玩家视角排：前排靠近敌人（上），后排靠近自己（下）。
所以从上到下依次是：敌后排 → 敌前排 → 我前排 → 我后排 → 手牌。

### 充能牌塞在角色卡背后

实体打牌时「给队友充能」就是**把一张牌横着塞到他的角色卡下面**。
所以充能不单独占场地：每个玩家角色卡带一个 `ChargeStack`
（`arrange = 横置` 的 `ZoneView`），牌**与角色卡等大**、旋转 90°、
压在卡**中心偏下**的位置，一张比一张低地叠下去。

它在 `CombatantView.Create` 里**最先**创建 → 兄弟序最小 → 画在角色卡底板**之下**，
被不透明的卡面挡住中段，只露出两侧和下缘。这就是实体牌堆的层次关系。

两个细节：

- 充能堆的底板是**全透明**的。`ZoneView` 对 alpha 为 0 的底板会关掉 `raycastTarget`，
  否则它压在角色卡上，点角色卡就选不中人了。
- 它**故意**画到自己矩形外面（牌要露在卡外），所以区域尺寸自检对 `横置` 排布不生效。

**它只负责显示，不是放置目标。** 充能走右键菜单（见下），
不靠「拖到卡的哪一小块」来区分打出和充能——那种命中区分又难发现又容易点错。

`ZoneSource.充能区` 枚举仍然保留，想要一块独立的充能区照样可以在布局里加。

### 出牌 vs 充能：右键菜单 + 二次确认

```
右键手牌 → CardContextMenu
            ├─ 打出（→ 当前选中目标）   立即提交
            └─ 贴充能…                 进入选目标状态
                        ↓
                 点一名队友的角色卡
                        ↓
                 ConfirmDialog「把「X」横置到 Y 的角色卡上作为充能？」
                        ↓
                    确定 → 提交
```

选目标期间点棋盘空处即取消；点敌人会提示「充能只能贴给队友」并保持选择状态。

左键拖拽出牌仍然保留（拖到目标角色卡上），它和右键菜单的「打出」等价。


### 查看牌堆

弃牌堆这类叠放区域在实体桌上是随时可以翻看的公开信息，所以区域有个 `peekable` 开关：
点一下就弹出 `ZonePeekPanel`，把里面的牌全部摊成网格，点空白处或「关闭」收起。

默认开着的是：弃牌堆、持续区、已完成升变区。**卡组没有开**——那是暗信息。
要调试时看牌组，用调试浮层而不是改这个开关。

面板里的卡牌视图是它自己的一套，不碰 `BattleView` 的对象池，免得查看时把棋盘上的卡抽走。

---

## 3. 卡牌交互

### 拖拽

`CardView` 自己实现 `IBeginDragHandler/IDragHandler/IEndDragHandler`，
拖拽过程中的协调交给 `ICardDragContext`（由 `BattleView` 实现）：

```
按下     → 记录原父节点与层级 → 提到 DragLayer（保证画在最上层）
         → 所有放置目标亮「合法/不合法」底色
拖动     → 跟手移动 → 命中的目标高亮加深，卡牌边框转绿/转红
松手     → 命中且可接收 → 提交动作
         → 否则 ReturnHome() 原样退回
```

命中判定用 `RectTransformUtility.RectangleContainsScreenPoint` 逐个测试已注册的
放置目标，**而不是 GraphicRaycast**——拖拽中的卡本身会挡住射线，而且这样
区域底板勾没勾 `raycastTarget` 都不影响能不能放。倒序遍历保证角色面板
压在区域底板之上。

### 放置目标

```csharp
interface IDropTarget {
    RectTransform Rect { get; }
    bool CanAccept(CardView card);
    void AcceptCard(CardView card);
    void SetDropHighlight(CardHighlight state);
}
```

实现者两个：

- **`CombatantView`**（角色面板）→ 接收即「对这个目标打出此牌」
- **`ZoneView`**（`acceptsDrop = true` 的区域）→ 目前只有充能区，接收即「贴充能」

加一种新的放置行为，就是让新对象实现这个接口并注册进 `_dropTargets`。

### 高亮

`CardHighlight`：`正常 / 悬停 / 拖拽中 / 合法 / 非法`。
卡牌用一圈描边表达，放置目标用整块半透明底色表达，两者共用同一个枚举。

### 点击

拖拽之外还留了点击出牌：点一下卡 = 对**当前选中的目标**打出。
测试时比拖拽快，也方便验证「选中目标」这条链路。

---

## 4. 界面重建策略

`BattleView.Refresh()` 在每次动作后被调用，做的事很直白：

```
对每个区域：
  BeginRebuild()                       清空子项列表
  解析内容（卡牌列表 or 角色列表）
  为每个内容取/建一个视图，Bind 数据，Add 进区域
  Arrange()                            按配置摆好
最后 Prune()                           回收这一帧没用到的视图
```

卡牌视图按 `CardInstance.Uid` 做字典缓存 + 对象池，角色视图按 `Combatant.Id` 缓存，
所以刷新不会每帧 `Instantiate`。没有动画、没有补间——**先把状态显示对，动效之后再说**。

---

## 4.5 调试浮层

`BattleDebugOverlay`（`View/Debugging/`）是**调试工具，不是游戏功能**。按 **F1** 开关，显示：

- 种子、轮次、阶段、命刻、当前 `PendingDecision` 与待结算的敌人意图
- 全体角色的完整状态快照（HP/MP/AP、两种护盾及其中自动盾的部分、各区域张数、
  充能牌及其**原持有者**、升变状态、状态列表、本轮/上轮造成伤害）
- 本局累计统计
- 完整战报（可滚动，可关掉自动跟随）

**怎么移除**：删掉 `BattleDebugOverlay.cs`，再删掉 `BattleSceneBuilder` 里
`AddComponent<BattleDebugOverlay>()` 那一行。`BattleView` 不引用它。

不删也没关系：正式构建（非 Development Build）下它在 `Awake` 里直接销毁自己。
**刻意没有把整个类包进 `#if UNITY_EDITOR`**——那样做会让正式包里的场景留下
missing script 警告。

它用 IMGUI 而不是 uGUI：不往场景层级里留任何节点，删掉就是真的干净。

---

## 5. 已知限制

- **没有动画**。抽牌、出牌、受击都是瞬间刷新。做动效需要把 `Refresh()` 拆成
  「算出差异 → 播放过渡」，等规则定稿后再说。
- **多人局只显示一套手牌**（当前选中角色的）。要同屏四人，在布局资产里
  复制四份手牌区、各自指定 `playerIndex` 即可，不用改代码。
- **升变宣告、轮末弃牌的多选** 目前只有顶栏那个按钮能处理（跳过/自动弃牌），
  没做专门的选择界面。要精细操作先用 IMGUI 沙盒。
- **字体靠系统字体**。uGUI 内置字体没有 CJK 字形，`UiBuilder.Font` 会从
  微软雅黑 / 黑体 / 思源黑体里挑一个。都没有时会退回内置字体，中文会显示成方块。
- **尚未在 Unity 里实跑**。全部程序集对着本机 Unity 6000.3.8f1 的
  `UnityEngine.dll` / `UnityEngine.UI.dll` / `Unity.InputSystem.dll` / `UnityEditor.dll`
  编译 0 错误，但渲染效果、布局是否好看、事件是否真的连通，需要打开 Unity 才能确认。

---

## 6. 输入系统的坑

本工程 `activeInputHandler = 1`（**Input System Package only**）。
这种设置下老的 `StandaloneInputModule` 会在运行时抛异常，
整个 uGUI 的点击与拖拽会完全失效。所以 `BattleView.EnsureEventSystem()` 里是：

```csharp
#if ENABLE_INPUT_SYSTEM
    go.AddComponent<InputSystemUIInputModule>();
#else
    go.AddComponent<StandaloneInputModule>();
#endif
```

如果以后把 Active Input Handling 改成 Both 或 Old，这段条件编译会自动走另一条分支。
