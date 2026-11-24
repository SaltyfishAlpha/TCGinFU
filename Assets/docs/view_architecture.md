# 表现层架构

> 配套：[规则书](v0_5.md) · [引擎架构](architecture_v0_5.md) · [规则裁定](rulings_v0_5.md) · [UI 待办](ui_todo_handoff.md)
> 本文只讲 UI（`TCGinFU.View`）。规则一律在 `TCGinFU.Core` 里，表现层不做规则判断。
>
> 最后核对：2026-11-22（以当前工作区代码为准）

---

## 1. 分层与依赖

```
TCGinFU.View            3D 桌面 + 屏幕空间 HUD（本文主体）
TCGinFU.Sandbox         IMGUI 调试沙盒（保留，查战报比正式 UI 方便）
      ↓ 都依赖
TCGinFU.Presentation    BattleSession / ContentScenario（noEngineReferences）
      ↓
TCGinFU.Core / Content / Scripting   规则引擎、内容加载、Lua 行为层
```

`BattleSession` 是**唯一的推进逻辑**。正式 UI 和沙盒共用它，所以不会出现
「沙盒能打、正式 UI 打不了」这种两套流程对不上的情况。它不引用 Unity，
因此「自动打完不卡死」「同种子重现同一局」这类行为能在测试里跑。

`BattleSession.Restart()` 按 `EnemyPreset`（`BattleScenario` 枚举）开局：

- `命刻之战` → `ContentScenario.CreateClockworkCollapse` 读 `Assets/StreamingAssets/Content` 的 JSON 内容包 + Lua 脚本。

目前只有这一个值：C# 样例内容（以及它那几个敌人预设）已于 2026-11-22 移除，
见 [archive/legacy_cleanup_handoff.md](archive/legacy_cleanup_handoff.md)。枚举保留下来，是为了以后加内容包阵容时有地方挂。

内容包加载失败时 `Session.IsLoaded` 为 false，`BattleView` 不建棋盘，改为整屏显示加载报告 + 「重试 / 退出」。

**表现层不判断规则。** 「这张牌能不能打」直接提交给引擎——
引擎拒绝时原样返回原因且不改状态（引擎的契约），UI 把原因显示在顶栏即可。
例外只有拖拽时的「目标类型过滤」（攻击牌不让拖到队友身上、升变窗口里只亮升变牌），
那是减少无效操作的手感优化，不是规则判断。

---

## 2. 棋盘是真 3D：每张卡都是带厚度的网格

卡牌不是贴图也不是 UI 元素，而是**真正的 3D 物体**——牌堆叠起来
能直接看出有多少张，卡组、弃牌堆、充能堆都有可观察的厚度。

```
BattleView
  Board            根节点，缩放 1/PixelsPerUnit，绕 X 轴旋转 boardTilt 度
    Table          桌布面片
    Zone_*         区域：底板面片 + BoxCollider + 标题 + 空卡位轮廓
      Card         卡牌（CardView）
        Body       扁盒子网格（w × h × 厚度）——露出来的侧面就是厚度
        FaceQuad   卡面片（卡图），略小一圈，四周留出深色描边
        Highlight  高亮片，平时隐藏
        Face       世界空间 Canvas，画文字（CardFace）
      CharacterCard  角色卡（CombatantView），自带充能堆与升变牌位
    CardPool       回收池（未激活）
  BoardCamera      透视相机 + PhysicsRaycaster + AudioListener
  HudCanvas        ScreenSpaceOverlay，sortingOrder = 100
  EventSystem      InputSystemUIInputModule（见 §8）
```

### 单位：根节点底下一律用设计像素

`Board` 根节点缩放成 `1 / BattleLayoutAsset.PixelsPerUnit`（100），于是它底下的所有坐标、尺寸、
厚度**全部直接写设计像素**，只在根节点换算一次到世界单位。
布局资产里的锚点数值因此可以原样用，不必到处乘换算系数。

### 立体感靠配色，不靠灯光

材质用 **URP Unlit**（`CardMeshFactory.ShaderName`），场景里没有灯。卡的立体感来自「卡身比卡面暗」：
卡身是深色盒子，卡面是亮色的、略小一圈的片，于是边缘自然形成深色描边，
叠起来就是一摞深色层 + 最上面一张亮面。

好处是不用管光照与 URP 灯光配置，颜色所见即所得；代价是没有阴影和高光。

> 桌面上的网格**一律不透明**（Unlit 没开混合），所以区域底板的半透明配色
> 会先被 `ZoneView.OpaqueOnTable` 压到桌布上算成实色。
>
> 构建里要带上 Unlit shader：菜单 `TCGinFU → 检查：运行时 shader 是否会被打进构建`
> （`EnsureRuntimeShadersIncluded`）。

### 拾取与拖拽都走物理射线

相机上挂 `PhysicsRaycaster`，卡牌、角色卡和区域各有 `BoxCollider`，
所以 `IPointerClickHandler` / `IBeginDragHandler` 这些接口和做 uGUI 时**完全一样**。

- **拖拽**：屏幕射线 → 与桌面平面求交 → 卡贴着平面移动（抬高 `CardView.DragLift` = 46 设计像素）
- **命中放置目标**：`Physics.RaycastNonAlloc` 取全部命中，**按距离排序**，
  依次找 `CombatantView` / `ZoneView`，角色卡优先于它底下的区域底板
- 拖起来的卡 / 角色卡会**关掉自己的碰撞体**，否则射线永远先打中它自己，找不到下面的目标

### 顶栏与弹窗是屏幕空间 uGUI

倾斜的文字没法读，所以顶栏、牌堆查看、右键菜单、确认框、详情面板都挂在
`HudCanvas`（`ScreenSpaceOverlay`）。它的 `sortingOrder` 更高，弹窗开着时自然优先吃掉点击。

牌堆查看面板里的卡用 `PeekCardView`（平面 uGUI 版），不碰 `BattleView` 的对象池，
免得查看时把棋盘上的卡抽走。

### 3D 参数

| 字段 | 作用 | 默认 |
| ----- | ----- | ----- |
| `boardTilt` | 桌面后仰角度。0 = 正对镜头，越大越像趴在桌上看 | 20 |
| `cameraFov` | 相机视野。越小透视越弱 | 38 |
| `cameraMargin` | 取景余量。1 = 刚好装下桌面 | 1.28 |
| `cameraHeight` | 相机抬高量（按桌面高度比例），越高越俯视 | 0.18 |
| `CardMeshFactory.CardThickness` | 卡厚（设计像素，代码常量） | 3.2 |

> 战斗场景用 `NewSceneSetup.EmptyScene` 生成：`BattleView` 自己建相机，
> 留着默认的 Main Camera 会变成两台相机同时渲染、天空盒盖在桌面上。

---

## 3. 场地区域：数据驱动

**区域的位置、大小、数量都要能改，而且不用改代码。** 所以区域是 `BattleLayoutAsset` 里的一个列表：

```
BattleLayoutAsset (ScriptableObject)
  referenceResolution   设计分辨率（1920×1080）
  cardWidth             标准卡宽；高度按扑克比例自动算
  tableColor            桌面底色
  boardTilt / cameraFov / cameraMargin / cameraHeight
  zones: List<ZoneLayout>
```

每个 `ZoneLayout` 是纯数据：

| 字段组 | 内容 |
| ----- | ----- |
| 标识 | `key`、`displayName`、`visible` |
| 内容 | `source`（手牌 / 卡组 / 弃牌堆 / 充能区 / 持续区 / 形态卡组 / 除外区 / 我方前排 / 我方后排 / 敌方前排 / 敌方后排 / 空 / 升变卡组）、`playerIndex` |
| 位置 | `anchorMin/Max` + `offsetMin/Max`——锚点是 0–1 相对坐标，分辨率自适应 |
| 排布 | `arrange`（横排 / 扇形 / 堆叠 / 网格 / 横置）、`cardScale`、`stackStep`、`spacing`、`fanAngle`、`fanArc`、`gridColumns`、`reservedSlots` |
| 交互 | `draggable`、`acceptsDrop`、`faceDown`、`peekable` |
| 外观 | `background`、`border`、`showLabel` |

于是：

- **挪位置 / 改大小** → 改 `anchorMin/Max`
- **加一个区域** → 列表里加一条，填 `source` 和锚点
- **删 / 隐藏** → 删一条，或把 `visible` 关掉
- **四人分屏** → 复制四份手牌区，各自 `playerIndex` 填 0/1/2/3

`playerIndex = -1` 表示「当前操作的角色」，内容跟着选中的角色走。默认布局是单人视角。

**真要加一种全新的内容来源**（比如「秘宝展示区」），才需要动两处代码：
`ZoneSource` 加一个枚举值，`BattleView.ResolveCards` / `ResolveCombatants` 加一个分支。
枚举值**只能往末尾加**——布局资产里存的是整数下标，往中间插会把已有区域的来源整体串位。

### 默认布局（`BattleLayoutAsset.CreateDefault()`）

| key | 来源 | 排布 | 备注 |
| ----- | ----- | ----- | ----- |
| `enemy_back` / `enemy_front` | 敌方后排 / 前排 | 横排 | |
| `party_front` / `party_back` | 我方前排 / 后排 | 横排 | 走位拖拽的落点 |
| `hand` | 手牌 | 扇形 | `draggable` |
| `deck` | 卡组 | 堆叠 | `faceDown`，不可查看。**主卡组与升变卡组共用这一块**：升变期间摆的是 `FormDeck`，标题自动换成「升变卡组」（`ResolveZoneTitle`） |
| `discard` | 弃牌堆 | 堆叠 | 可查看。显示 `AllDiscards` = 形态弃牌 + 主弃牌，**两边的弃牌都在这一块里** |
| `continuous` | 持续区 | 堆叠 | 可查看 |
| `exile` | 除外区 | 堆叠 | 可查看。一次性牌（卡面「消耗」）打出后进这儿，升变结束的整套升变牌与形态牌也进这儿 |

默认布局**也没有独立的升变卡组区**：它和主卡组轮流用 `deck` 那一块。真想把两摞同时摆出来，
在布局里另加一条 `source = ZoneSource.升变卡组` 的区域即可，引擎侧 `Deck` 与 `FormDeck` 本来就是并存的两摞。

默认布局**没有独立充能区**：充能显示在角色卡背后（见下），`ZoneSource.充能区` 枚举仍保留，想要可以自己加。
默认布局里也没有任何非角色区域开 `acceptsDrop`，所以拖拽的放置目标实际只有角色卡（和走位时的我方排位区）。

> **注意布局资产的现状。** `ClockworkCollapse.unity` 里 `BattleView.layout` 目前是空的，
> 运行时走 `CreateDefault()`，也就是上表。仓库里的 `Assets/Settings/BattleLayout.asset` 是旧版字段生成的
> （有独立的「充能区」且 `acceptsDrop = 1`、没有除外区、没有 3D 参数和 `cardScale` 等字段），
> 但**重新执行生成场景菜单会把这份旧资产绑进场景**（`BattleSceneBuilder.LoadOrCreateLayout`）。
> 想用新默认值，先跑菜单 `TCGinFU → 重建默认布局资产`。

### 一切都是卡

卡牌比例**不可单独调**：布局资产上只有一个 `cardWidth`，高度恒按美式扑克
（2.5 × 3.5 英寸，`PokerAspect` = 1.4）算出。各区域只能用 `cardScale` 整体缩放，
所以无论怎么改都不会出现被拉扁拉长的卡。

**棋盘上的角色也是卡**，和手牌同一个比例、同一套尺寸来源，版式按实体角色卡来：
顶上名条 → 插画位（内容包 `portrait`，缺图按兜底链）→ HP 条 → 数值栏 → 底部意图栏。

### 空卡位只在区域空着时出现

真卡**只按实际张数**排布，所以手牌永远居中，间距随张数动态收放——和实体卡牌游戏一致。

`reservedSlots` 现在的含义是「区域空着时画几个占位轮廓」（默认全部为 1，0 = 一个都不画）。
一旦有牌，占位一个都不画：真卡自己就标出了区域的位置，空轮廓反而是噪音。
区域不会因此塌陷——底板 `_plate` 的尺寸来自布局锚点，和区域里有几张牌无关。

> 旧版 `reservedSlots` 是「至少预留几个卡位」，会参与 `ComputePoses`。
> 手牌的 `reservedSlots = 5` 于是把 5 个槽钉死了：手里只有 2 张时，
> 两张牌贴在最左边两格上，右边空着三格。

> 旧版曾有「区域装不下一个卡位就打警告」的自检，现在代码里也已经没有了；
> 改锚点把区域调得过小时，卡会直接画到框外，需要自己看。

### 排布算法

都在 `ZoneView.Arrange()` 里，按 `arrange` 分发。先算出 N 个卡位姿态（位置 + 旋转），
真卡占前面几个，剩下的画成空卡位：

- **横排**：居中平铺，放不下时自动压缩间距（允许负间距让卡互相叠）
- **扇形**：在横排基础上加弧线下沉与旋转，手牌用
- **堆叠**：沿桌面法线逐张抬高一个卡厚，只露最上面，牌堆/弃牌堆用
- **网格**：左上起按 `gridColumns` 换行
- **横置**：卡旋转 90° 依次压住，充能堆用（见下）

**动画只有一处**：角色区域里的角色卡通过 `SlideTween` 落位（0.22 秒缓出），
走位时能看到角色从一排滑到另一排；首次摆放不动画。其他卡牌仍是瞬间刷新。

### 排位的朝向

敌方按**敌人自己的视角**排：它们的后排在更远处，也就是屏幕**更上方**。
我方按玩家视角排：前排靠近敌人（上），后排靠近自己（下）。
从上到下依次是：敌后排 → 敌前排 → 我前排 → 我后排 → 手牌。

### 充能牌塞在角色卡背后

实体打牌时「给队友充能」就是**把一张牌横着塞到他的角色卡下面**。
所以充能不单独占场地：每个玩家角色卡带一个 `ChargeStack`
（`arrange = 横置`、`faceDown` 的 `ZoneView`），牌**与角色卡等大**、旋转 90°、
压在卡**中心偏下**的位置，一张比一张低地叠下去。它在角色卡背后，被不透明的卡面挡住中段，
只露出两侧和下缘。

两个细节：

- 充能堆的底板是**全透明**的。`ZoneView.Create` 只在底板 alpha > 0 或 `acceptsDrop` 时
  才启用自己的 `BoxCollider`，否则它压在角色卡上，点角色卡就选不中人了。
- 它**只负责显示，不是放置目标**（`acceptsDrop = false`）。充能走右键菜单（§4），
  不靠「拖到卡的哪一小块」来区分打出和充能——那种命中区分又难发现又容易点错。

### 升变牌盖在角色卡上

每个玩家角色卡还带一个 `TransformSlot`。升变期间，升变牌盖在角色卡上方、往上错开约 22%：
挡住名条和插画位表达「换了形态」，下面的 HP 条、数值栏、意图栏仍然看得见。

### 查看牌堆

**正面朝上的叠放区域一律可以点开查看**（`ZoneLayout.CanPeek`：`peekable` 或「非背面且 `arrange = 堆叠`」），
点一下弹出 `ZonePeekPanel`，把里面的牌摊成网格（6 列），点空白处或「关闭」收起；
面板里点一张牌 → `CardDetailPanel` 看详情。

卡组是背面朝上的暗信息，**不能查看**；长按背面朝上的牌（卡组、充能）只会提示看不到。
调试时要看牌组，用 F1 调试浮层。

---

## 4. 交互

### 操作一览

| 操作 | 效果 | 入口 |
| ----- | ----- | ----- |
| 左键点手牌 | **只弹提示**（「长按看详情，拖到目标上打出」），不改局面 | `BattleView.OnCardClick` |
| 左键长按任意正面朝上的牌（0.35 秒） | 卡牌详情 | `LongPress.Threshold` → `OnCardLongPress` |
| 右键手牌 | 菜单：`打出（→ 目标）` / `贴充能…` / `查看详情` | `OnCardContextMenu` → `CardContextMenu` |
| 拖手牌到角色卡上 | 对这个目标打出（与菜单「打出」等价） | `HandleCombatantDrop` |
| 升变宣告窗口里拖升变牌到**自己**的角色卡 | 发动升变 | `CombatantAcceptsCard` / `HandleCombatantDrop` |
| 左键点角色卡 | 敌人 → 设为出牌目标；队友 → 切换当前操作角色 | `HandleCombatantClick` |
| 右键我方角色卡 | 菜单：`充能换 1 AP` / `充能回复 5 MP` / `替 X 分摊` / `查看详情` | `OnCombatantContextMenu` → `ChargeOptionsFor` / `ShareOptionsFor` |
| 右键敌方角色卡 | 菜单：`设为目标` / `查看详情` | `OnCombatantContextMenu` |
| 长按角色卡 | 角色详情 | `CombatantView.LongPressHandler` |
| 拖自己的角色卡到我方另一排 | 走位，弹确认（「消耗 1 AP」或「免费（剩 N 次）」） | `OnCombatantEndDrag` → `MoveRowAction` |
| 点弃牌堆 / 持续区 | 摊开查看 | `HandleZoneClick` |
| F1 | 开关调试浮层 | `BattleDebugOverlay` |
| Esc / 顶栏「退出」 | 编辑器里退出 Play，构建里退出程序 | `QuitOnEscape` / `AppExit.Quit` |

右键菜单的「打出」目标：指向队友的牌（`BattleSession.NeedsAllyTarget`）打给**自己**，
其余打给当前选中的敌人。要对某个队友打出，用拖拽。

**右键一律出菜单**，手牌和角色卡同一条口径（左键选中 / 长按详情 / 右键菜单 / 拖拽移动或出牌）。
角色卡以前是「默认直接开详情，只有能分摊时才出菜单」，省掉一次点击，
代价是充能的两种单独用途在正式界面上根本没有入口（旧 B-15）。

**充能菜单项的主语是被右键的那名角色**，用的是他自己的充能区。
能不能点**一概问引擎**（`BattleEngine.CanSpendChargeFor(actor, use, out reason)`），
界面不自己判阶段和数量——各判一份迟早分家，表现出来就是「菜单里有、点下去却被拒」。
不可用时写成 `(文案——引擎给的理由, null)`，`CardContextMenu` 会把无动作的条目置灰。
MP 满了**不**禁用，只在文案里加「（MP 已满）」：玩家可能就是想清掉充能区。

充能的第三种用途（支付卡面「消耗 X 个充能」词条）不在这个菜单里，它跟着具体某张牌，在出牌时问（见 §5）。

### 顶栏

从左到右：状态（轮次 · 阶段 · 命刻 · 胜负）、消息、**随阶段变化的主按钮**，然后是
`自动出牌` / `跳过本步` / `自动打完` / `重开` / `退出`。

主按钮（`UpdateContextButton` / `SubmitContextAction`）：

| 当前等待 | 按钮 | 提交 |
| ----- | ----- | ----- |
| 升变宣告 `TransformDeclaration` | 不升变 | `DeclineTransformAction` |
| 玩家阶段 `PlayerActions` | 结束行动 | `DeclareReadyAction` |
| 敌人意图响应 `EnemyResponse` | 放弃响应 | `PassResponseAction` |
| 轮末弃牌 `HandLimitDiscard` | 自动弃牌 | `Session.SkipPending()` |
| 正在选充能目标 | 取消充能 | 退出选目标状态 |

按钮作用于当前选中的角色；选中的角色不在等待列表里时，自动切到第一个还在等的角色。

走位与升变**不在**顶栏：两者都是棋盘上的拖拽动作，顶栏只留阶段推进和调试入口。

### 拖拽

`CardView` 实现 `IBeginDragHandler/IDragHandler/IEndDragHandler`，
拖拽过程中的协调交给 `ICardDragContext`（由 `BattleView` 实现）：

```
按下     → 记录原父节点与层级 → 挂到 Board 根节点、关掉自身碰撞体
         → 所有放置目标按 CanAccept 亮「合法」
拖动     → 跟手移动 → 命中的目标亮「悬停 / 非法」，卡牌描边转绿/转红
松手     → 命中且可接收 → AcceptCard → 提交
         → 否则 ReturnHome() 原样退回
```

开始拖拽会取消长按（`LongPress.Cancel`）：拖拽优先，否则拖久一点就会弹出详情挡住视线。

**选充能目标期间整张手牌不可拖**，避免中途把别的牌打出去。

### 放置目标

```csharp
public interface IDropTarget {
    Transform Root { get; }
    bool CanAccept(CardView card);
    void AcceptCard(CardView card);
    void SetDropHighlight(CardHighlight state);
}
```

实现者两个：

- **`CombatantView`**（角色卡）→ 打出 / 升变。`CombatantAcceptsCard` 做目标类型过滤：
  单体敌方效果只接受敌人、单体友方效果只接受队友；升变宣告窗口里只接受升变候选牌、且只能放到持有者自己身上，
  其余窗口里升变牌不可放置。
- **`ZoneView`**（`acceptsDrop = true` 且不是角色区域）→ 只处理 `ZoneSource.充能区`，接收即贴充能。
  默认布局没有这种区域（见 §3）。

加一种新的放置行为，就是让新对象实现这个接口并注册进 `_dropTargets`。

### 走位拖拽

`CombatantView` 自己也能拖：只有玩家角色、且此刻走得动时（`BattleView.CanMoveRow`：
玩家阶段，或持有免费移动时的响应窗口；未倒下；有免费移动，或本轮没走过且 AP ≥ 1）才可拖。
拖动中对面排的我方区域高亮；松手落在对面排 → `ConfirmDialog` → `MoveRowAction`。
落点用 `FindRowZoneUnder`，同样是 `Physics.RaycastNonAlloc` 按距离排序。

### 贴充能：右键菜单 + 选目标 + 二次确认

```
右键手牌 → CardContextMenu → 贴充能…
            （非玩家阶段 / 本轮已贴过 → 直接在顶栏说明原因，不进入选目标）
                        ↓
                 进入选目标状态：那张牌高亮「选中」，可贴的队友角色卡亮绿
                        ↓
                 点一名队友的角色卡
                        ↓
                 ConfirmDialog「把「X」横置到 Y 的角色卡上作为充能？（当前充能 n/上限）」
                        ↓
                    确定 → PlaceChargeAction
```

选目标是模态的：

- **点棋盘空处不会取消**——点在角色卡边缘稍偏就会命中区域底板，顺手取消会让人以为「贴充能没反应」。
  取消要按顶栏的 **「取消充能」**。
- 点敌人会提示「充能只能贴给队友」并保持选择状态。
- 期间再开右键菜单会被挡下并说明原因；长按看详情是只读的，照样允许。

### 出牌前的玩家选择

右键「打出」和拖拽出牌都走**唯一入口** `BattleView.PlayCardWithChoices`：
先查 `CardChoiceCatalog.For(definition)`，有问题就逐个问完，再带着答案提交 `PlayCardAction`。

- **几选一**（二选一、选元素、消耗几个充能）：弹 `CardContextMenu`；`OptionAvailable` 过滤掉此刻选不了的项，
  `When` 表达依赖（「选了涂油才问元素」）。答案以 `action.With(key, value)` 传给 Lua 的 `ctx.choice(key)`。
- **从手牌 / 弃牌堆挑牌**：`ZonePeekPanel.OpenForSelection`（满足 `MinPick..MaxPick` 才能确认），
  结果以 `action.WithMany("card", uids)` 提交。正在打出的那张不在候选里。
- 任何一步取消 = 不打出。

**「额外消耗几个充能？」这一问不在 `Table` 里**，而是照卡面的 `chargeOptions` 字段
（见 [data_architecture.md](data_architecture.md)）现生成，并**排在所有问题最前面**——
后面的问题可能依赖它（万能灵药消耗 2 个时两项都生效，就不该再问「二选一选哪项」）。
答案填进 `PlayCardAction.ChargeForEffect`，引擎按同一个 `chargeOptions` 校验。
带这种词条的牌会越来越多，每张都在表现层写死一份迟早会漏——漏掉的表现正是旧 B-15。

`Table` 里登记的卡：`emergency_catalyst`（应急催化剂）、`universal_elixir`（调合·万能灵药）、
`thieves_trick`（盗贼戏法）、`aether_reassembly_crucible`（以太重组坩埚）。

> ⚠ **选牌的后端没接**（[known_issues.md](known_issues.md) B-2）：界面会把选中的牌传下去，但引擎的 `ChargeFromZone`
> 仍然取弃牌堆顶 / 手牌底，不读选择结果。上面三张挑牌的卡「挑了也白挑」。
>
> ⚠ **还有两张形态牌没登记**（B-17）：元素增殖炸弹读 `ctx.choice("element")`、
> 炼金防护力场读 `ctx.choice("row")`，`Table` 里没有它们，于是永远用默认值。
>
> 键名是表现层单方面的约定：`CardChoiceCatalog` 的 key 必须和 `scripts/*.lua` 里 `ctx.choice(...)` 读的一致，
> 对不上不报错，只会静默退回脚本默认值。

### 高亮

`CardHighlight`：`正常 / 悬停 / 拖拽中 / 选中 / 合法 / 非法`。
卡牌用一圈描边表达，放置目标用整块底色表达，两者共用同一个枚举。
`选中` 用于「选完贴充能、等着点目标」的那张牌；`Refresh()` 会重置高亮，
所以 `ApplyChargeTargetingHighlights()` 必须放在重建之后。

---

## 5. 卡面与详情

### 费用文案

棋盘上正面朝上的牌显示**持有者此刻的真实费用**：`BattleSession.CostText(def, engine, actor)`，
被减免的项染绿（`DiscountColor`）、被加价的染红（`SurchargeColor`），减到 0 也照样显示。
背面朝上的牌不算费用。

### `CardDetailPanel`

屏幕空间的详情面板，带滚动条。三种打开方式：长按正面朝上的牌、右键菜单「查看详情」、
在牌堆查看面板里点一张牌；右键或长按角色卡打开角色详情（同一个面板）。

- 卡牌：标题、副标题（含 `BattleSession.CostText` 算出的真实费用）、正文；
  公式按「这张牌所属角色」的面板算成具体数值。
- 角色：排位、等级与职业、状态等。
- 每次 `Refresh()` 都把 `Engine` 重新指过去——重开一局会换掉引擎。

### 左侧卡图：`CardPreviewStage`

详情面板是「左卡图 + 右文字」两块并排，整体居中。左边那块**不是面板里的一张贴图**，
而是一座离屏摄影棚拍出来的 `RenderTexture`：

- 31 号层 `CardPreview`（`ProjectSettings/TagManager.asset`）只有预览相机拍得到；
  `BoardCamera` 的 `cullingMask` 和 `PhysicsRaycaster.eventMask` 都把它剔掉了。
- 棚子搭在 `(0, 100000, 0)`，常驻一张 `CardView` 和一张 `CombatantView`，换内容只是重新 `Bind`。
- 预览相机正交、`depth = -10`（排在桌面相机前面渲染，否则 `RawImage` 这一帧拿到的是上一张卡），
  清成不透明实色而不是靠 alpha——透明通道能不能穿过 `RenderTexture` 取决于管线配置，不值得赌。
- 面板关闭时整个棚子 `SetActive(false)`，相机就不再渲染。

**为什么不直接把 3D 卡摆在面板旁边**：`HudCanvas` 是 `ScreenSpaceOverlay`，永远盖在所有
相机的渲染结果之上，详情面板还带一层 alpha 0.62 的全屏遮幕——3D 卡摆在哪都会被它吃掉。

**为什么不另写一个 2D 版卡面**：卡面上的名字、费用、类型是会变的（真实费用含减免），
版式也还在动。复用 `CardView` / `CombatantView` 之后，卡面以后怎么改详情页跟着一起改。
`CardView.BindDefinition` 就是为此拆出来的：只按 `CardDefinition` 摆卡，不需要 `CardInstance`，
也不进任何区域。

> 充能区的牌是背面朝上的暗信息，点不开详情（见 §4），所以左侧卡图不会泄露它。
> `CardPreviewStage.ShowCard` 留了 `faceDown` 参数，哪天要显示「一张盖着的牌」时给卡背用。

### 出牌演出：`CardShowcase`

谁要打一张牌，就在画面**左侧**把卡面亮出来。卡面同样来自 `CardPreviewStage`，但**单开一座棚子**：
和详情面板共用的话，打牌时正好开着详情就会两边抢同一张 `RenderTexture`。

**玩家的牌和敌人的牌挂在不同的事件上，因为要亮的时机不同：**

| 谁 | 事件 | 时机 | 时长 |
| ----- | ----- | ----- | ----- |
| 玩家 | `CardPlayed` | 打出时 | 闪一下（淡入 0.12 / 停 1.05 / 淡出 0.25） |
| 敌人 | `IntentDeclared` | **响应窗口打开的那一刻** | **钉住**，直到窗口关闭才淡出 |

敌人这条是关键：`IntentDeclared` 在 `OpenNextIntentWindow()` 里、设 `Pending` 之**前**发出，
所以玩家是先看到首领打的是哪张牌，再决定打不打时机牌。敌人也发 `CardPlayed`，
但那是响应窗口关闭、伤害结算完之后的事——那时亮出来只剩「回去翻战报」的价值，所以演出不看它。

钉住而不是定时淡出，也是同一个道理：响应窗口会一直开着等玩家，
定时淡出等于逼他想得久一点就得去翻战报。`Hold()` 把计时卡在完全不透明那一刻，`Release()` 接上淡出。

- 整块 `CanvasGroup` 的 `blocksRaycasts = false`——演出不该挡住操作。
- 队列上限 4，满了丢最早的；`Hold()` 直接清空队列：此刻最要紧的是「首领要干什么」。
- **特效还没有。** 现在只负责「亮出来、停一下、收回去」，等特效系统到位时挂在 `CardShowcase.Play` 里。

**状态走 `Refresh` 整体重建，演出走事件。** 「现在手里有哪些牌」是状态；
「刚才打出了什么」只有事件说得清——等 `Refresh` 跑完，那张牌早就离开手牌进弃牌堆了。
`BattleView.DrainPresentationEvents()` 在引擎只追加的 `Events` 表上记一个游标，
每次刷新取出新事件；换引擎（重开一局）时游标归零。
一次提交里可能连着开过好几个响应窗口（前一条意图结算完就开下一条），所以只留最后一条 `IntentDeclared`。

> 2026-11-23 补的两处 Core 缺口：`PlayIntentCard` 以前不发 `CardPlayed`，战报里看不到敌人打出了哪张牌；
> `IntentDeclared` 是新加的事件类型，专门给「响应窗口开了」这个时机。

### 卡图：`CardArtLibrary`

数据层只存字符串 id（Core/Content 碰不到 `Texture2D`），由表现层解析成 `Texture2D`，连「没找到」也缓存。

**单个 id 的查找顺序**（`Resolve`）：内容包的 `art/<id>.png|.jpg|.jpeg` → 包体内的 `Resources/Art/<id>`。
内容包目录来自 `ContentPaths.ResolveAll()`，玩家包排在官方包前面，所以玩家不改官方文件也能换掉官方卡的图。
完整说明见 [data_architecture.md](data_architecture.md) §3.3。

**兜底顺序**（`ResolveWithFallback`，每一环都走上面那条查找链）：

1. 专属卡图（`CardDefinition.ArtId`；角色用内容包 `portrait`）
2. 按类型兜底（`pack.json` 的 `artFallback`：`attack / guard / spell / transform / character …`）
3. 最后一环 `back`（牌背）

**刻意不返回 null**：零星几张纯色卡混在有图的卡里像渲染坏了，统一兜到牌背至少一致。
牌背用卡自己的 `BackArtId`，否则用内容包的 `defaultCardBack`（当前是 `card bg beta`）。

`Assets/Resources/Art/` 下只留 7 张 beta 兜底图。它们必须在一个内容包都没装时也存在，所以不跟着包走；
正式卡图一律进内容包的 `art/`。

---

## 6. 界面重建策略

`BattleView.Refresh()` 在每次动作后被调用：

```
对每个可见区域：
  BeginRebuild()                       清空子项列表
  解析内容（卡牌列表 or 角色列表）
  为每个内容取/建一个视图，Bind 数据，Add 进区域
  角色卡顺带填充能堆 FillChargeStack、升变牌位 FillTransformSlot
  Arrange()                            按配置摆好
Prune()                                回收这一帧没用到的视图
ApplyChargeTargetingHighlights()       选充能目标时的高亮（必须在重建之后）
```

卡牌视图按 `CardInstance.Uid` 字典缓存 + 对象池，角色视图按 `Combatant.Id` 缓存，
所以刷新不会每帧 `Instantiate`。除角色卡的 `SlideTween` 外没有补间——**先把状态显示对，动效之后再说**。

**重建只管状态，不管演出。** `Refresh()` 开头调一次 `DrainPresentationEvents()`，
从引擎的事件表里取新的 `CardPlayed` / `IntentDeclared` 送去 `CardShowcase`（见 §5）。
这是全部的事件消费——其余一切仍然是「动作之后整体重建」。

---

## 7. 调试浮层

`BattleDebugOverlay`（`View/Debugging/`）是**调试工具，不是游戏功能**。按 **F1** 开关，显示：

- 种子、轮次、阶段、命刻、当前 `PendingDecision` 与待结算的敌人意图
- 全体角色的完整状态快照（HP/MP/AP、两种护盾及其中自动盾的部分、各区域张数、
  充能牌及其**原持有者**、升变状态、状态列表、本轮/上轮造成伤害）
- 本局累计统计
- 完整战报（可滚动）

**怎么移除**：删掉 `BattleDebugOverlay.cs`，再删掉 `BattleSceneBuilder` 里
`AddComponent<Debugging.BattleDebugOverlay>()` 那一行。`BattleView` 不引用它。

不删也没关系：非编辑器、非 Development Build 时它在 `Awake` 里销毁自己。
**刻意没有把整个类包进 `#if UNITY_EDITOR`**——那样正式包里的场景会留下 missing script 警告。
它用 IMGUI：不往场景层级里留任何节点，删掉就是真的干净。

`QuitOnEscape` 同样由 `BattleSceneBuilder` 挂上：构建产物是全屏的，没有退出键就只能用任务管理器。

---

## 8. 输入系统的坑

本工程 `activeInputHandler = 1`（**Input System Package only**）。
这种设置下老的 `StandaloneInputModule` 会在运行时抛异常，整个 UI 的点击与拖拽会完全失效；
`Input.GetKeyDown` 也会直接抛异常。所以：

```csharp
// BattleView.EnsureEventSystem()
#if ENABLE_INPUT_SYSTEM
    go.AddComponent<InputSystemUIInputModule>();
#else
    go.AddComponent<StandaloneInputModule>();
#endif
```

`QuitOnEscape` 与 `BattleDebugOverlay` 读按键也是同样的条件编译（`Keyboard.current.escapeKey` / `f1Key`）。
以后把 Active Input Handling 改成 Both 或 Old，会自动走另一条分支。

---

## 9. 已知限制（当前）

- **选牌后端没接**（B-2）：`ZonePeekPanel.OpenForSelection` 的结果被引擎忽略，见 §4。
- **没有「拖到队友充能区」**（B-7）：默认布局没有独立充能区，角色卡背后的充能堆也不是放置目标，
  贴充能只能走右键菜单。
- **轮末弃牌没有多选界面**：顶栏只有「自动弃牌」；手动挑弃哪几张目前只有 IMGUI 沙盒能做
  （`BattleSession.ToggleDiscard / ConfirmDiscard`）。
- **右键「打出」只能打给自己或当前选中的敌人**：要对某个队友打出指向队友的牌，用拖拽。
- **动效只有角色走位的滑行**：抽牌、出牌、受击都是瞬间刷新。
- **多人局只显示一套手牌**（当前选中角色的）。要同屏四人，在布局里复制四份手牌区、各自指定 `playerIndex`。
- **长按阈值与走位拖拽的手感**还需要真人试手（`ui_todo_handoff.md` §4）。
- **仓库里的 `BattleLayout.asset` 是旧格式**，重新生成场景会被绑上，见 §3。
- **字体靠系统字体**：uGUI 内置字体没有 CJK 字形，`UiBuilder.Font` 从微软雅黑 / 黑体 / 宋体 / Noto Sans CJK / 思源黑体里挑；
  都没有时退回内置字体，中文会显示成方块。
