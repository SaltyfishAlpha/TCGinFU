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
| 排布 | `arrange`（横排 / 扇形 / 堆叠 / 网格）、`cardSize`、`spacing`、`fanAngle`、`fanArc`、`gridColumns` |
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

### 排布算法

都在 `ZoneView.Arrange()` 里，按 `arrange` 分发：

- **横排**：居中平铺，放不下时自动压缩间距（允许负间距让卡互相叠）
- **扇形**：在横排基础上加弧线下沉与旋转，手牌用
- **堆叠**：只露最上面几张并轻微错位，牌堆/弃牌堆用
- **网格**：左上起按 `gridColumns` 换行

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
