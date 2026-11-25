# TCGinFU

基于《最终物语》（Fabula Ultima，FU）TRPG 框架的**协同卡牌战斗游戏**。

设计上，跑团式的探索、交涉、休整沿用 FU 规则；进入战斗冲突后切换成本项目的卡牌系统，小队协同打牌，
战斗结束后 HP、MP 与持续状态直接带回跑团。
**目前只实现了战斗部分**，地图探索第一版已写好交接单、待开发（见 [交接单](Assets/docs/map_exploration_handoff.md)）；叙事 / 战役层还没有。

> Unity 6000.3.8f1 · URP · Input System Package
>
> 最后核对：2026-11-25

---

## 怎么跑起来

### 主入口：命刻之战（JSON 内容包）

> 菜单 `TCGinFU → 生成命刻之战场景` → 生成并打开 `Assets/Scenes/ClockworkCollapse.unity` → Play

米阔阔 + 维汀 对 克洛诺斯-IV型。内容全部读 `Assets/StreamingAssets/Content`（JSON 数据 + Lua 行为脚本）。
**这是构建设置里唯一的场景，也是仓库里唯一的场景。**

内容包缺失或出错时**不开局**，整屏显示加载报告（哪个文件、哪一行出错）并提供「重试 / 退出」。
改完 JSON 点「重试」即可，不必重开游戏。

### 其他入口

- **调试沙盒（IMGUI）**：`TCGinFU → 战斗沙盒`（编辑器窗口，不用进 Play）。全部状态和完整战报平铺，
  可调种子，能手动挑弃牌。**同一个种子必定重现同一局**，发现规则问题记下种子就能复现。
  阵容与卡池由内容包决定，所以没有队伍人数 / 敌人预设可调。
- **F1**：开关调试浮层（完整战报 + 全体状态快照）。非编辑器、非 Development Build 时自动销毁。
- **Esc** 或顶栏「退出」：编辑器里退出 Play，构建里退出程序。
- 改布局：`Assets/Settings/BattleLayout.asset` / 菜单 `TCGinFU → 重建默认布局资产`。
  注意场景目前没绑布局资产（运行时用代码默认值），而仓库里这份资产是旧格式，
  详见 [表现层架构 §3](Assets/docs/view_architecture.md)。

### 操作

| 操作 | 效果 |
| ----- | ----- |
| 左键点手牌 | 只弹提示，不出牌 |
| 长按任意正面朝上的牌（0.35 秒） | 看卡牌详情 |
| 右键手牌 | 菜单：`打出` / `贴充能…` / `查看详情` |
| 拖手牌到角色卡 | 对该目标打出 |
| 升变宣告时拖升变牌到自己的角色卡 | 发动升变 |
| 贴充能 | 右键 →「贴充能…」→ 点一名队友的角色卡 → 确认。点空处**不**取消，用顶栏「取消充能」 |
| 左键点角色卡 | 敌人 = 设为目标；队友 = 切换操作角色 |
| 右键我方角色卡 | 菜单：`充能换 1 AP` / `充能回复 5 MP` / `替 X 分摊`（响应窗口）/ `查看详情`。此刻做不了的条目**置灰并写明原因** |
| 右键敌方角色卡 | 菜单：`设为目标` / `查看详情` |
| 长按角色卡 | 看角色详情 |
| 拖自己的角色卡到我方另一排 | 走位，确认后消耗 1 AP（有免费移动则免费） |
| 点弃牌堆 / 持续区 | 摊开查看 |
| 顶栏主按钮（随阶段变） | `结束行动` / `不升变` / `放弃响应` / `自动弃牌`；选充能目标时变成 `取消充能` |
| 顶栏其余按钮 | `自动出牌` / `跳过本步` / `自动打完` / `重开` / `退出` |
| F1 / Esc | 调试浮层 / 退出 |

拖拽时合法目标亮绿、非法亮红。棋盘是真 3D（卡有厚度），区域布局全部是数据，
细节见 [表现层架构](Assets/docs/view_architecture.md)。

---

## 当前进度

| 部分 | 状态 |
| ----- | ----- |
| 规则引擎（轮次、出牌、升变、伤害、响应窗口、走位、构筑校验） | ✅ |
| JSON 内容包「命刻之战」：2 名角色、1 个首领（另带伴生部位「镇压重盾臂」）、49 张卡，其中 47 张可用 | ✅（缺陷见下） |
| Lua 行为层（MoonSharp，卡牌 / 状态 / 敌人意图脚本） | ✅ |
| 3D 战斗界面（拖拽、右键菜单、长按详情、走位、升变、充能） | ✅ 可用 |
| 出牌前的选择界面（二选一、选元素、挑牌） | ⚠ 部分：界面已做，挑牌结果后端未接（B-2） |
| 美术 | ⚠ 7 张 beta 占位图 + 按类型兜底 |
| 探索 / 叙事 / 战役 / 构筑界面 / 成长 | ⬜ 未开始 |
| 联网 | ⬜ 未开始 |

测试：EditMode 约 **299 个**（静态计数：262 个 `[Test]` + 37 个 `[TestCase]`），位于 `Assets/Scripts/Tests/EditMode`。
最近一次全量运行是 2026-11-23 卡牌重新分类交接单验收时 **299 条全绿**——但跑的是拿
`check-asmdefs.sh` 产出的 dll 搭的反射式运行器，不是 Unity Test Runner（fixture 隔离、
`[OneTimeTearDown]` 这些没有完全复刻），**以 Unity 里跑出来的为准**。
**测试数量只在这里维护**，其他文档不再各写一份。

### 已知问题

全部缺陷、规则书与代码的差距、未实现的规则，统一记在 [`known_issues.md`](Assets/docs/known_issues.md)，**只维护这一份**。
目前最急的是 B-2（玩家挑的牌被忽略）、B-17（两张形态牌的选择没有入口），以及规则书新加的角色被动 / 专属牌代码还没跟上。

---

## 代码结构

`Assets/Scripts/` 下按 asmdef 分为 Core（规则引擎）/ Content（内容包加载）/ Scripting（Lua，MoonSharp 只在这里）/
Presentation（组局）四个 `noEngineReferences` 程序集，加 View（3D 界面）、Sandbox（IMGUI 沙盒）与 EditMode 测试；
内容包在 `Assets/StreamingAssets/Content/`。程序集边界与目录树见 [引擎架构 §1](Assets/docs/architecture_v0_5.md)。

---

## 文档

都在 `Assets/docs/`。

| 文档 | 内容 | 状态 |
| ----- | ----- | ----- |
| [`v0_5.md`](Assets/docs/v0_5.md) | **规则书**，唯一事实来源 | 当前 |
| [`rulings_v0_5.md`](Assets/docs/rulings_v0_5.md) | 规则裁定表：规则书没写清、实现必须选定的取值 | 当前 |
| [`design_backlog.md`](Assets/docs/design_backlog.md) | 设计待办：还没写进规则书的方向、提议、待定问题（含数值基准） | 常驻 |
| [`known_issues.md`](Assets/docs/known_issues.md) | 已知问题：缺陷、规则书与代码的差距、未实现的规则 | 常驻 |
| [`architecture_v0_5.md`](Assets/docs/architecture_v0_5.md) | 引擎架构、分层理由 | 当前 |
| [`data_architecture.md`](Assets/docs/data_architecture.md) | 内容包格式与加载、Lua 行为脚本写法（钩子、动词） | 当前 |
| [`view_architecture.md`](Assets/docs/view_architecture.md) | 表现层：3D 桌面、布局数据、交互 | 当前 |
| [`map_exploration_handoff.md`](Assets/docs/map_exploration_handoff.md) | 交接单：地图探索第一版（地图、小队、路标、角色面板、卡组配置） | 待执行，做完归档 |
| [`archive/`](Assets/docs/archive/) | 一次性记录：讨论纪要、核对报告、已执行的交接单（含行为层、UI 待办两份交接） | 已归档，仅供查历史 |

改规则前先看裁定表。

**文档维护约定**：
- 一次性的文档（讨论纪要、核对报告、交接单），内容落实之后移进 `archive/`，并在开头标注「已归档」。
- 仍然有效的内容先搬进常驻文档（规则书、`design_backlog.md`、`known_issues.md`），再归档。
- 常驻文档只写现状，不留「已修复」的记录。

---

## 不启动 Unity 做验证

```bash
bash Tools/check-asmdefs.sh      # 按 asmdef 的真实程序集边界编译整个工程
```

**必须用这个，不能把所有 .cs 塞进一个 csproj 编译。** 后者会把程序集边界整个抹掉，
asmdef 少写一条 `references` 这种错误一个都抓不到——踩过：单工程编译全绿，
Unity 里直接 `CS0234`。脚本刻意不用 ProjectReference（MSBuild 的 P2P 引用是传递的，
Unity 的 asmdef 引用不是），按依赖顺序逐个编译、只引用显式列出的 dll，也处理 `precompiledReferences`（MoonSharp）。

> 需要先在 Unity 里打开过一次工程（生成 `.csproj` 与 `Library/ScriptAssemblies`），
> 脚本靠它们定位 Unity 安装目录和包程序集；也可以用环境变量 `UNITY_MANAGED` 指定。

## 跑测试

- **Unity 内**：`Window → General → Test Runner → EditMode → Run All`，或通过 Unity MCP 的 `run_tests`。
- **Unity 外**：可以拿 `check-asmdefs.sh` 产出的 dll 自建测试 / 控制台工程。在临时目录跑时要设
  `TCGINFU_CONTENT_DIR` 指向内容包，否则 `ContentPaths` 找不到 `Assets/StreamingAssets/Content`。

几个坑：

- **asmdef 边界**：Core、Content、Scripting、Presentation 都是 `noEngineReferences`；测试程序集引用
  Core、Content、Presentation、Scripting。新增依赖要在 asmdef 里显式写，不传递。
- **`IsExternalInit`**：C# 9 的 `init` 在 netstandard2.1 里缺这个类型。`Core/Primitives/`、`Content/`、`Scripting/`
  各自备了一份 `internal` shim（跨程序集看不见别人的 internal）——别删。
- **NUnit 版本**：Unity 自带 NUnit 3.5，**没有 `Assert.Multiple`**（3.6 才加）。
  用 NuGet 新版在 Unity 外验证会编译通过、到 Unity 里报 `CS0117`。
- **改 View 也要跑测试**：`BattleSessionTests` 里有依赖费用文案的断言。
- **测血量别跨轮末**：自动双盾轮末清零、轮始刷新，跨轮 diff `Hp` / `Shields` 会被刷新骗到，
  直接断言 `BattleEvent` 里的 `DamageResult` 分解。
- **新增序列化字段在旧资产上是零值**：Unity 给已存在的资产补字段用类型零值，不是 C# 字段初始值。
  写 `Normalize()` 自愈（如 `BattleLayoutAsset`），或别依赖会静默默认错的 bool。
- **Python 在 conda 里**：本机 `python` 不在 PATH，用 `D:\miniconda3\python.exe`；打印中文要设 `PYTHONIOENCODING=utf-8`。

---

## Unity MCP（让 AI 能自己看 Unity）

- Unity 侧：`Packages/manifest.json` 里有 `com.coplaydev.unity-mcp`，打开 Unity 自动拉取。
- 服务端：`.mcp.json` 用 `uvx --from <本机路径>/.mcp-servers/unity-mcp/Server mcp-for-unity` 启动，
  服务端是稀疏检出的 `Server/` 目录。MCP 只在启动时连接，改配置后要重启 Claude Code。

> 为什么不直接 `uvx --from git+https://...`：那样会完整克隆仓库，而仓库里 `TestProjects/` 有超长路径，
> Windows 下 checkout 报 `Filename too long`。
>
> ⚠ `.mcp.json` 里是**绝对路径**，换机器要改。
