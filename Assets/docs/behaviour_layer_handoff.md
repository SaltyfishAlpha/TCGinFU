# 行为层交接 · 给下一个 agent

> 配套：规则书 [v0_5.md](v0_5.md) · 引擎架构 [architecture_v0_5.md](architecture_v0_5.md) ·
> 裁定表 [rulings_v0_5.md](rulings_v0_5.md) · 数据层 [data_architecture.md](data_architecture.md) ·
> 表现层 [view_architecture.md](view_architecture.md)
>
> **你的任务**：49 张卡只剩 2 张哑火；11 条敌人意图都已是敌人的牌（见 §1）。
> **先修 B-2（玩家挑的牌被忽略）**，再做场景机制的剩余部分
> （命刻时钟的后果 / 部位破坏 / 倒地复苏）。
>
> 最后核对：2026-11-22（对照当前工作区）

---

## 0. 这一版改了什么（必读）

上一版交接说「把 49 张卡的逻辑用 C# 写出来」。**那条路已经废弃**，原因是新增的产品约束：

> 发布后玩家要能自定义卡牌与角色。

C# behaviour 要重新编译、要随版本分发 dll，玩家做不到。所以行为层换成**嵌入 Lua（MoonSharp）**，
官方卡和玩家 mod 走**完全同一条路径**——官方能做到的事，mod 也能做到。

另一条同样重要的修正：

> **以卡面文字为准，不要照抄 `card_resolver.gd`。老版本代码本身有 bug。**

已经发现 5 处老代码与卡面不符，都按卡面实现了，列在 §5。

第三条——**敌人的意图就是敌人的牌**：

> Boss 持有一套玩家看不到的卡牌，按阶段从备选池里挑一张宣告出来。

所以意图**不是**另一套机制。我一度给它加过 `on_intent_declared` / `on_intent_resolved` /
`on_intent_attack` 三个平行钩子，那是错的抽象，已经删掉。现在意图挂一张
`CardDefinition`（`CardRole.EnemyIntent`），结算走**和玩家出牌完全相同**的那条管线：
同样的 `EffectData`、同样的 `ICardBehaviour` 钩子、同样的伤害管线。

分成两套的代价很具体：数据驱动效果、状态、触发器全都要实现两遍，
而 mod 作者想做自定义敌人还得再学一套 API。

---

## 1. 现状

| 层面 | 状态 |
| ----- | ----- |
| 数据层 | ✅ 49 张卡 / 2 角色 / 2 敌人 / 2 卡组 |
| 脚本宿主 | ✅ MoonSharp + 沙箱 + 异常隔离 + 加载期对账 |
| 引擎钩子面 | ✅ 可变攻击管线 / 触发器 / 掷骰改写 / 响应窗口改写 / 费用改写 / 充能留存 |
| 敌人意图 | ✅ **11 / 11 条都是敌人的牌**，写在 `scripts/enemies.lua`（意图**可以**用 Lua 写）。9 条挂了行为，重锤震地与因果抹杀令是纯数据（后者的「相邻队友分摊」是意图上的开关 `shareable`，不是行为）。归零坍缩已放进二阶段意图池 |
| 卡牌行为 | ✅ **47 / 49**（脚本 `miku.lua` / `weiting.lua` / `forms.lua` + 少量纯数据卡） |
| 场景机制 | 🟡 部分实现：命刻时钟会计数（`State.SceneClock`，`advance_clock` / 轮末推进），但**到点没有任何后果**；首领两阶段按 HP 切意图池（`phaseTwoHpThreshold`，缺省为最大 HP 的一半，见 `ContentDatabase` 里的选池逻辑）。部位破坏、倒地 / 复苏 / 淘汰未做 |
| 加载期对账 | 🟡 还剩 **2 条警告**（`LuaScriptHost`）：哑火的那 2 张声明了 `behaviourId` 却没有脚本。曾经有 24 条——其中 21 条被当成「纯数据卡多余声明」放过了，实际上那些卡**只兑现了卡面前半句**（回血、抽牌、削盾、净化全部静默失效）。警告噪音淹掉真问题，这就是代价 |

**仍然哑火的只剩 2 张**，都卡在同一个缺口上：

| 卡 | 缺什么 |
| ----- | ----- |
| 幸运改命 | 卡面写「在你或任意队友**投掷骰子后**立即打出」——引擎没有「掷骰后」这个响应窗口。现有的响应窗口只开在敌人意图结算前。 |
| 魔力过载仪式 | 前置条件是「【魔力激发卷轴】处于激活状态」，而这件秘宝还不在 49 张卡里（见 §7 待办）。 |

还有几处玩家选择，**UI 已经做了，后端只接了一半**：

- 「二选一 / 任选元素」✅ 已接通：界面收集后放进 `PlayCardAction.Choices`，
  Lua 用 `ctx.choice("mode", 默认)` / `ctx.choice("element", 默认)` 读（应急催化剂、调合·万能灵药）。
- 「从弃牌堆**选择** 1 张」（盗贼戏法、以太重组坩埚）、「将手牌中的 1 张贴入」（应急催化剂）
  ❌ **后端没接（B-2）**：View 用 `ZonePeekPanel.OpenForSelection` 让玩家挑了牌，
  选中的 uid 以 `WithMany("card", …)` 写进 `Choices`（键为 `card0`、`card1`…、`card_count`），
  但 `charge_from` → `BattleEngine.ChargeFromZone` 仍然取弃牌堆顶 / 手牌末位，脚本也没读这些键。
  **挑了也白挑**——这是牌效这边的活，接口约定见 [ui_todo_handoff.md](ui_todo_handoff.md) §1。

验收测试在 `Assets/Scripts/Tests/EditMode/`，**断言的是 HP / 护盾 / 状态真的变了**，
不是「能跑完一局」：

- `LuaBehaviourTests.cs`——脚本宿主、沙箱、时限，以及各张卡的主要词条。
- `CardEffectFixTests.cs`——专盯「只兑现了卡面前半句」那一类：回血、抽牌、削盾、净化、
  必定暴击、完全抵消才反击。这类 bug 不会让战斗崩，所以**必须直接断言后半句**，
  否则一张只打伤害的卡照样全绿。

---

## 2. 程序集边界

```
TCGinFU.Core        零依赖、零 Unity。规则引擎 + ICardBehaviour 接口
TCGinFU.Scripting   引用 Core + MoonSharp.Interpreter.dll。Lua 宿主
TCGinFU.Content     引用 Core。JSON 加载
TCGinFU.Presentation 引用 Core / Content / Scripting。组局
```

**MoonSharp 只出现在 `TCGinFU.Scripting`。** Core 仍然零依赖，`dotnet test` 照样能跑。
dll 在 `Assets/Plugins/MoonSharp/`（3.0.0-beta.1，netstandard2.0）。

---

## 3. 怎么写一张卡

脚本在 `Assets/StreamingAssets/Content/scripts/*.lua`，在 `pack.json` 的 `scripts` 里登记。

```lua
behaviour("<卡的 behaviourId>", {
  on_resolved = function(ctx)
    apply_status(ctx.target, "exposed", 1)
  end,
})
```

**写了哪个钩子就有哪个钩子**，没写的就是没挂。全部钩子：

| 钩子 | 时机 | 典型用途 |
| ----- | ----- | ----- |
| `can_play(ctx)` | 费用未付、效果未结算 | 额外前置条件，`return false, "理由"` |
| `on_play(ctx)` | 数据效果之前 | 先改局面再让数据效果落地 |
| `on_resolved(ctx)` | 数据效果之后 | 「命中后……」 |
| `transform_condition(owner)` | 轮始 | 条件型升变牌 |
| `on_attack(atk, me)` | 我方出手，伤害未结算 | 增伤 / 改属性 / 无视护盾 / 改暴击倍率 |
| `on_defend(atk, me)` | 我方挨打，伤害未结算 | 减伤 / 闪避 |
| `on_crit_roll(roll, me)` | 骰已投、暴击未判 | 掷骰 +1、把某颗骰视为 7 |
| `on_cost(cost, me)` | 校验费用**之前** | 「免除5MP」「下一张熵系魔法-5MP」 |
| `on_damage_taken` / `on_damage_dealt` / `on_crit_dealt` | | 「每当……时」 |
| `on_attack_resolved(ctx)` | 一次攻击结算完毕，**掉 0 血也广播** | 「若完全抵消本次攻击伤害则反击」。`on_damage_taken` 掉 0 血不广播，等不到这个时点。读 `ctx.hp_lost` / `ctx.absorbed` / `ctx.fully_absorbed` |
| `on_card_played` / `on_charge_spent` | | |
| `on_charge_spend(spend, me)` | 充能离开充能区、未进弃牌堆 | 「消耗后不进弃牌堆」（**全队广播**）|
| `on_round_start` / `on_round_end` | | 敌人身上的状态也收得到 |

**敌人的牌用的是同一张表**——`on_play` / `on_resolved` / `on_attack` 对意图一样生效。
注意两点：
- `ctx.caster` 是敌人，所以要用 `opponents(me)` / `same_side(me)` 而**不是** `enemies()` / `allies()`；
  后者是玩家视角写死的，敌人用它会把自己人当成「敌方」。
- 意图宣告的那段伤害先结算（响应窗口里玩家看得到也改得了），**之后**才跑 `on_resolved`。
  所以 `on_resolved` 里看到的已经是打完之后的局面。

**常驻钩子（`on_attack` 往下）只在这段行为「生效中」时被问到**：持续区的牌、升变期间的升变牌、
挂在身上的状态。这套是每次现场枚举的，不维护订阅表——持续牌会被顶替、升变会结束、状态会过期，
维护订阅就要在每处记得退订，漏一处就是幽灵触发器。

动词表（全局函数）：

- 伤害与恢复：`damage` `heal`（返回**实际**回复量，满血时为 0）`shield`
  `strip_shield`（直接削减护盾，不是伤害）`restore_mp` `gain_ap` `draw` `discard`
- 状态：`apply_status` `has_status` `status_stacks` `remove_status`
  `remove_negative_status(who, n)`（「移除1个负面状态」，哪些算负面由引擎统一判）
- 走位：`move_row`（切换）`move_to`（定向到排）`move_slot`（定向到格，会挤开）
  `choose_move`（玩家挑落点）`grant_free_move` `swap_rows`。
  后两个带**落点范围**参数：不写 = 哪都能去，`"switch"` = 必须换排，`"back"` / `"front", 2` = 指定排 / 格
- 充能：`charge_from`（弃牌堆/牌库/手牌 → 充能区）`charge_this`（把正在打出的这张牌变成充能）
  `grant_charge_placement` `adjust_charge_cap`
- 查询：`stat` `team_charges` `round` `weakness_of(who)`（第一个【易损】属性，没有则 nil）
  `damaged_this_round_by(target, who)`（本轮谁打过他）；**阵营相对**的 `opponents(me)` / `same_side(me)`
  （`enemies()` / `allies()` 是玩家视角的简写，敌人脚本不要用）
- 敌人专用：`devour_charges`（吞噬≠消耗，不计入 ChargesSpent）`advance_clock` `pierce`（无视护盾）
- 掷骰：`roll` `roll_attribute`
- 其它：`add_stat`（记自己的累计量）`log`

出牌上下文 `ctx` 上几个容易漏掉的字段：

- `ctx.damage_dealt`：数据层那几条 `Damage` 到此为止**实打实**打掉的 HP。
  「回复等同于本次实际伤害一半的HP」读它，别拿卡面基础值去算——护盾与克制会让两者差很远。
- `ctx.cards_played_before`：本次打出**之前**本轮已经打过几张牌。
  「若本轮已打出过**其他**牌」要用它；读角色身上的计数会把这张牌自己算进去。
- `ctx.charge_spent`：本次打出**付给卡面词条**的充能数（卡面硬费用 + 玩家额外选的那几个）。
  玩家能选几个由卡面的 `chargeOptions` 声明（见 [data_architecture.md](data_architecture.md) §4.2）。
  拿充能换 MP / 换 AP 是独立动作，**不**计入这里（§5.2 三选一）——所以 `can_play` 要问的是
  「这次打算付几个」（`ctx.charge_spent`），不是「充能区里有几个」（`ctx.caster.charges`）。
- `ctx.choice(key, 默认值)`：玩家在界面上做的额外选择。键名由卡自己定，和 `CardChoiceCatalog`
  约好即可；**对不上不报错，只会静默退回默认值**，所以一律要写默认值。

掷骰结算单 `roll` 上：`roll.charge_spent` 可读，`roll.force_crit = true` 可写
（「必定暴击」）。它只能把结果**抬成暴击、抬不成超暴击**——超暴击是双色两颗骰的关系判出来的，
凭一个布尔开关造出来会把 §7.3 绕过去。

### 三条硬规矩

1. **所有状态变更必须走这些函数。** 参战者句柄是只读的，写它会直接报错。
   这些函数才会发 `BattleEvent`，绕过去战报和 UI 就对不上了。
2. **不许自带随机源。** 掷骰一律 `roll` / `roll_attribute`，它们走引擎的 `IRng`。
   否则同一个种子复盘不出同一局，联机和战报回放全废。
3. **能用 `EffectData` 表达的就别写脚本。** 数据效果能被可视化编辑器生成，脚本不能。
   先声夺人就是例子：「移动1次并抽1张牌」是纯数据（`MoveRow` + `Draw`），
   只有「第1轮免除5MP、额外+1AP」那半句进了脚本。
4. **`on_cost` 必须无副作用。** 费用要在校验阶段算出来、还要给 UI 显示，
   所以同一张牌可能被问很多次，而且最终未必真打得出去。
   「每轮限一次的折扣」这种要记账的，请在 `on_card_played` 里挂个状态来记。

---

## 4. 沙箱与容错

- `CoreModules.Preset_HardSandbox`：没有 `io` / `os` / `debug` / `dofile` / `require`。
  mod 脚本碰不到文件系统，也没法再从磁盘拉一段代码进来。
- **手写 API 绑定，不用 `UserData.RegisterType` 反射。** 两个原因：反射绑定在 IL2CPP/AOT 下
  要靠 link.xml 保活，发布后装 mod 最容易炸的就是这里；而且反射会把 C# 对象的全部公开成员
  暴露出去，脚本能顺着 `engine.State.Players[0]` 摸到任何东西，等于没有沙箱。
- **每个钩子调用都被异常隔离**，抛错只产出一条 `BehaviourFaulted` 事件，那个钩子本次失效，战斗继续。
  一张写坏的 mod 卡不该让人连游戏都进不去。
- **加载期对账**：卡面声明了 `behaviourId` 却没有脚本、脚本注册了没人用的 Id、
  注册了但一个钩子都没挂——全部记进 `ScriptLoadReport`。拼错 id 是最常见也最难查的错。

- **执行时限**（`ScriptWatchdog`）。沙箱挡得住「乱碰东西」，挡不住「不还回来」——
  `while true do end` 不抛任何异常，只是让主线程再也不返回。
  MoonSharp 没有现成的指令预算，但它的 `IDebugger.IsPauseRequested()` 会被虚拟机按指令轮询，
  借这个钩子超时后抛 `ScriptRuntimeException`，再顺着已有的异常隔离变成一条 `BehaviourFaulted`。

  > **两档预算，不能共用一个。** 钩子 100ms、加载 5000ms。
  > 加载要编译整个文件，真实内容包的 `miku.lua` 就超过 50ms——
  > 用钩子那档预算会把**正常内容包**直接误杀。
  > 误杀比超时难查得多：一张写对的卡静默失效，而且只在慢机器上复现。
  > 所以两档都宁可放宽：它要拦的是「永不返回」，不是「有点慢」。

---

## 5. 老代码与卡面不符的地方（已按卡面实现）

| 卡 | 卡面 | `card_resolver.gd` | 处理 |
| ----- | ----- | ----- | ----- |
| 压制连射 | 「两次射击**分别独立**投掷敏捷骰」 | 老引擎确实分开投；**新引擎**每次打牌只投一次 | 加 `EffectData.RollsOwnCrit`，裁定 R-23：按卡面分开投。**⚠ 但 `cards/miku.json` 没设 `rollsOwnCrit`，游戏里仍共用一次骰（B-1）**；测试 E07 用 `WithOwnCrit` 现场补字段，掩盖了漏写 |
| 鹰眼锁定 | 「你的**远程**攻击无视目标2点物理护盾」 | 多加了「伤害属性必须是物理」 | 按卡面：只看远程，不看属性 |
| 看破闪避 | 「≥**敌方**暴击抵抗」 | 写死成 `shield_quake 就是 arm，否则 boss` | 按卡面取本次攻击实际来源 |
| 游击速射 | 「**可**免费在前后排之间移动1次，打出前**或**结算后」 | `if row == front: move_hero(id, true)` | 三处不符（强制／只许前→后／无时点选择），按卡面给一次可兑现的免费移动 |
| 先声夺人 | 「在前后排之间移动1次」 | `move_hero(id, true)`（定向到后排） | 按卡面做切换 |

`ranged` 与 `requires_back_row` 原本躺在 `unmapped` 里，现在是 `CardDefinition.IsRanged` /
`RequiresBackRow` 正式字段（两处都认，老包不用改）。**「仅限后排打出」此前完全没生效**——
穿云狙击在前排也打得出来。

### 走位：一个动词不够用

卡面上的「移动」至少有 7 种语义，`SwitchRow` 只覆盖第 1 种：

| 语义 | 卡 | 接口 |
| ----- | ----- | ----- |
| 在前后排之间切换 | —— | `SwitchRow` / `move_row(who)` |
| 定向移动到某排（落点自动取同列） | 重力横扫「击退至后排」 | `MoveRow(c, row, free)` / `move_to(who, "back")` |
| 定向移动到某排的**某一格** | 同排换位、挤开 | `MoveRow(c, row, slot, free)` / `move_slot(who, "back", 2)` |
| 移动，**落点由玩家在范围内挑** | 先声夺人 | `RequestSlotChoice` / `choose_move(who, "switch")`，或数据层 `playerChoosesSlot: true` |
| **可**免费移动（玩家决定动不动、何时动） | 游击速射、翻滚披风 | `GrantFreeMove` / `grant_free_move(who, 1, "switch")` |
| 把移动权授予**别人** | 翻滚披风（给被保护的目标） | 同上，传目标 |
| 前后排整体互换，保持列 | 相位折跃 | `SwapRows(playerSide)` / `swap_rows()` |

> ⚠️ **用切换做击退是真 bug**：「被击退至后排」对已经在后排的人应当无事发生，
> 用 `SwitchRow` 会把他拉到前排——效果正好相反。测试 `M01` 守住这一条。
>
> ⚠️ **整体互换必须原子**：逐个 `SwitchRow` 会让先动的人占掉对排槽位，
> 后动的人被挤到别的列，§4.3 的「正前方」相邻关系就乱了。测试 `M02` 守住这一条。

免费移动额度存在 `PlayerCharacter.FreeMoves`，轮末清零（卡面给的是「本轮」的一次机动），
兑现时不花 AP、不占本轮唯一一次常规走位，且**在敌人响应窗口里也能兑现**——
翻滚披风给的那次机动本来就是为了在敌人落手之前挪开。

### 裁定 R-24：意图索敌的时点

分界线在**意图文本**，不在伤害形状：

| 意图文本 | 时点 | 走位能否躲开 |
| ----- | ----- | ----- |
| 「**选择/随机选择一个**位于后排的角色，攻击」 | 宣告时锁定具体某人 | ❌ 他挪到哪都算数 |
| 「攻击**前排角色**」 | 结算时才定 | ✅ 挪出去就躲开，**挪进来也会被打到** |

实现：`EnemyIntent.LocksTarget` 按 `Targeting` 推导（`DefaultSingle` / `DesignatedRowSingle`
锁定，其余不锁），内容包可用 `locksTarget` 字段覆盖。不锁定的意图在
`ResolveCurrentIntentAndContinue` 里按此刻站位重新解析目标——
**已经被时机牌处理过的那几次攻击原样保留**（玩家付出的减伤不该因重新解析而丢），
刚挪进来的人拿到一次全新的攻击（别人打的时机牌保护不了他）。
三条测试 `R24a/b/c` 守住这套，其中 `R24c` 专门验「挪进来会被打到」——
只验「挪出去能躲」的话，一个只在目标离开时才收缩的实现也能蒙混过关。

### 表现层的操作约定

棋盘上的动作一律在棋盘上做，顶栏只留**阶段推进**与**调试**入口。
把走位、升变做成顶栏按钮试过一版，结果是它们和「重开」「退出」混在一起，反而更难找。

| 操作 | 手势 |
| ----- | ----- |
| 打出一张牌 | 拖到目标身上；或**右键手牌** → 「打出（→ 目标）」 |
| **发动升变** | 把升变牌拖到**自己的角色卡**上——升变牌就是这一局的新角色卡（§6.2 ②） |
| **走位** | 把角色卡拖到对排，松手后确认（确认框写清是 1 AP 还是免费） |
| 贴充能 | **右键手牌** → 「贴充能…」→ **点一名队友的角色卡** → 确认框确认。选目标期间是模态的，取消用顶栏的「取消充能」。**不能拖**：默认布局没有独立的充能区，角色卡上的充能堆也不是放置目标（B-7） |
| 手牌右键菜单 | 「打出（→ 目标）」/「贴充能…」/「查看详情」 |
| **看详情**（卡牌 / 角色） | **左键长按 0.35s**；手牌也可右键 →「查看详情」；**右键角色卡**直接看详情 |
| 单击角色卡 | 选中：点敌人 = 设为目标，点队友 = 设为当前行动者；选充能目标期间则是选这名队友 |
| 单击手牌 | 什么都不做，只提示「长按看详情，拖到目标上打出」 |

几条实现上的注意：

- **升变走的仍然是 `DeclareTransformAction` 那一条路径**。`HandlePlayCard` 遇到升变牌时
  转交给 `HandleDeclareTransform`，没有第二套升变流程。
- **落点判定要提前到高亮阶段**：条件没达成的升变牌、以及升变窗口里的普通牌，
  在 `CombatantAcceptsCard` 里就返回 false，所以拖过去是红色而不是「放下才被一句话挡回来」。
- **长按和拖拽抢同一个手势**，所以 `LongPress` 必须能被 `OnBeginDrag` 取消；
  长按触发过之后要吞掉随后的 `OnPointerClick`，否则抬手还会再触发一次选中。
  判断「指针动没动」只在拖拽侧做一次——两边各判一次迟早调出互相打架的阈值。
- **角色详情**要能看到叠在角色卡上的东西：升变牌、持续牌、充能区。
  充能是背面朝下的暗信息（§5.2），所以只报「有几个、谁贴的」，**不报是哪张牌**；
  「谁贴的」要显示角色名而不是内部 id。
- **`SlideTween`**：角色卡换排时滑过去而不是瞬移。跨 `ZoneView` 会重新挂父节点，
  所以要在改 `localPosition` **之前**取世界坐标、再换算回新父节点下的局部坐标做起点。
  首次摆放不动画（否则开局一片乱飞）；手牌仍然直接落位（每次 Refresh 都整体重排，
  补间只会让扇形抖个不停）。

> 这两套入口此前都**完全不存在**：`MoveRowAction` 引擎里一直有但 View 层点不到，
> `RowChanged` 事件零订阅者；升变只能靠「自动出牌」替你随便选一张。
> 改引擎之前先去界面上确认一遍「这个操作玩家到底做不做得到」。

**迁移只剩 2 张哑火（幸运改命、魔力过载仪式，见 §1）。做它们、或复核已迁的卡时，请逐张对卡面读一遍，发现不符就记到这张表里。**

---

## 6. 为了这些代表卡给 Core 加了什么

每一条都是写卡时撞上的，不是预先设计的——这正是先打通少数几张再铺量的理由。

| 新增 | 为什么数据层表达不了 |
| ----- | ----- |
| `AttackContext` 可变攻击管线 | 十几种「下一次攻击+3」「转风属性」「无视2点盾」，逐条特判会把伤害管线写成一坨 if，而且 mod 根本插不进来 |
| `CritRollContext` | 暴击由骰值和抵抗线共同决定，拿到布尔结果再改就只能硬掰，双色超暴击规则直接丢失 |
| `AttackContext.CritMultiplier` | 暴击倍率原本写死 ×1.5/×2；用分数而不是 float，是为了「×1.75 再取整」在任何平台都是同一个整数 |
| `BattleTrigger` + `ActiveBehaviours` | 「每当……时」完全没有落脚点；持续牌放进持续区后，以前没有任何代码会去读它 |
| `PendingAttacks` | 响应窗口以前只能加盾，改不了那次伤害，8 张时机牌全部做不出来 |
| `StatusEffect.IncomingDamageBonus` + `ConsumedOnTrigger` | 【破绽】是「**下一次**受到的伤害+3」，靠持续轮数做不到——持续 1 轮的状态在本轮内会生效任意多次 |
| `Combatant.DamageTakenThisRound` | 「若目标本轮已受到过伤害」，「本轮」这个口径状态表达不了 |
| `statuses.json` + `ContentDatabase.BuildStatusRegistry` | 状态数值属于状态本身；否则每张挂流血的卡都要重抄一遍数值 |
| `CostContext` + `ResolveCost` | 费用原本直接从 `CardDefinition` 读，「免除5MP」这类词条**完全无处落脚**。至少卡住 3 张：先声夺人（首轮免除）、维汀的法术折扣、魔力过载仪式（下张熵系魔法 -5MP） |
| `MoveRow` / `SwapRows` / `GrantFreeMove` / `RequiresBackRow` | 见 §5「走位：一个动词不够用」 |

> **费用减免必须同时改 UI。** 「先声夺人」卡面印 5 MP、第 1 轮实收 0 MP——
> 界面只显示卡面基准值的话，玩家看到费用对不上，还会以为 MP 不够而不敢打。
> `BattleSession.CostText(def, engine, actor)` 返回真实费用。显示规则（炉石式）：
>
> - **零费印「0」，不写「免费」。** 费用位永远是数字才好横向比较，
>   写「免费」还容易被看成「这里没加载出来」。
> - **减免染绿 `#7dff7d`，加价染红 `#ff9a9a`。** 加价用默认色会误导玩家，所以反向也要有色。
> - **费用为 0 且没被改过的项整项省略**（否则每张牌都挂一串「0AP 0充能」），
>   但**只要被改过就一定显示**——减免到 0 恰恰是最该让玩家看见的那一次。
> - 没有减免时**不掺任何颜色标记**，纯文本场合（战报、测试断言）才不会被 `<color=...>` 污染。
>
> 手牌、详情面板都要用带 `engine`/`actor` 的重载：详情面板印 5MP、手里那张印绿色 0MP
> 会让人以为自己看错了。`CardDetailPanel.Engine` **每次 Refresh 都要重指**——
> 重开一局会换掉引擎，只在建面板时设一次就会留下陈旧引用。

---

## 7. 待办（按优先级）

**最高优先：文档说好了、游戏里不对的真实缺陷**（编号同 [known_issues.md](known_issues.md)，都是小改动）

- **B-2 玩家选的牌被忽略**：View 已经把选中的 uid 写进 `Choices`（`card0`…、`card_count`），
  但 `BattleEngine.ChargeFromZone` 只认 `Discard` 取顶 / `HandBottom` 取末位，Lua 也没读。
  要给 `charge_from` 加一个「按 uid 取」的入口（或在 `ChargeFromZone` 里优先读选择），
  再改 `miku.lua`（盗贼戏法）、`weiting.lua`（以太重组坩埚、应急催化剂）。接口见 [ui_todo_handoff.md](ui_todo_handoff.md) §1。
- **B-13 交叉火力网两半句口径不同**：`ResolveCost(actor, card)` 拿不到目标（校验期玩家还没选，
  而且 UI 要靠它显示费用），所以 AP 减免只能问「本轮有没有队友打过人」，
  而 +4 伤害在 `on_attack` 里才精确到「**这个**目标」。队友打了别人时会便宜 1 AP 却不加伤。
  要修得让费用钩子看得见目标——注意 `ModifyCost` 必须保持无副作用，它会被反复调用。

**之后**

1. **补「掷骰后」响应窗口**，解锁「幸运改命」；顺带让「命运锚点·七」也能作用于非暴击骰。
2. ~~**理清「为卡面词条付充能」的入口**~~ —— **已做（2026-11-23）**：卡面用 `chargeOptions` 声明可选档位，
   动作字段改成 `PlayCardAction.ChargeForEffect`，抵 MP 拆成独立的 `SpendChargeForMpAction`。
   顺带修掉 `ctx.choice` 读错参数下标、所有二选一静默退回默认值的 bug。
   仍开放的是 `CardChoiceCatalog` 还缺两张形态牌的条目（B-17）。
3. **场景机制的剩余部分**：命刻时钟**已计数**（轮末 +1，`advance_clock` 可推进）但到点没有后果；
   首领两阶段**已按 HP 切意图池**；部位破坏、倒地 / 复苏 / 淘汰未做。
4. **加载期警告（B-5）已降到 2 条**，就是上面那 2 张哑火卡，等各自的前置做完即可。
   ~~从纯数据卡上删掉 `behaviourId`~~ / ~~让对账对「纯数据卡」不报~~ —— **这两个方向都别走**：
   当初正是把这 21 条当成「纯数据卡的多余声明」整类放过，才让它们半实现的状态藏了这么久
   （详见 [data_architecture.md](data_architecture.md) 对账表下面那段）。
5. **R-21 要规则设计者拍板**：老数据的 `arcane`（奥术）伤害属性，规则书 §7.4 只有九大属性。
   当前暂时映射成 `Light`（对本场 Boss 行为等价，但语义是错的）。正解可能是新增 `DamageElement.Arcane`。
6. 小出入：`Enemy.IntentFullyVisible` 只区分「小怪 / 其他」，于是首领显示成「半公开」；
   规则书 §7.3 写的是首领「按阶段与机制公开」，和精英的「半公开」不是一回事。

> 自定义敌人：意图行为已经能用 Lua 写（`scripts/enemies.lua`，和玩家卡同一张钩子表，见 §3），
> 这条早先的「待办」已不成立。

---

## 7.5 美术占位字段（已有 7 张 beta 占位图）

数据层只存**字符串标识**，不碰 Unity 类型——Core / Content 都是 `noEngineReferences`。

| 字段 | JSON | 现在的值 |
| ----- | ----- | ----- |
| `CardDefinition.ArtId` | 卡牌的 `art` | `cards/<卡id>`（专属卡图，目前一张都没有） |
| `CardDefinition.BackArtId` | 卡牌的 `backArt` | 未设，退回包默认 |
| `ContentDatabase.DefaultCardBackArtId` | pack.json 的 `defaultCardBack` | `card bg beta` |
| `ContentDatabase.ArtFallbacks` | pack.json 的 `artFallback` | 按类型兜底：`back` / `transform` / `guard` / `attack` / `spell` / `character` → 对应的 `card … beta` |
| `CharacterContent.PortraitId` | 角色的 `portrait` | `portraits/<角色id>` |

`Resources/Art/` 下现有 7 张 beta 占位图：`card attack beta`、`card bg beta`、`card character beta`、
`card character beta1`、`card guard beta`、`card levelup beta`、`card spell beta`
（另有 `portraits/alian.jpg`，是未来角色的立绘，还没有对应角色数据）。

解析走 `CardArtLibrary.Resolve(artId)` → `Resources/Art/<artId>`；带兜底的是
`ResolveWithFallback(artId, fallbackKey)`。**兜底顺序：专属卡图 → 按类型兜底（`artFallback`）→ 牌背**。
**纯色占位那条路已经删了**，缺图统一显示牌背，不报错也不出粉色方块。
`BattleView` 启动时把 `DefaultCardBackArtId` / `ArtFallbacks` 交给 `CardView` / `CardArtLibrary`。
`Resources` 这层将来换成 Addressables 或 mod 目录里的 png 都不用动数据层。

> 卡图 id 直接用卡 id，所以美术文件按 id 命名就能自动对上，不必再维护一张映射表。
> `CardArtLibrary` **连「没找到」也缓存**——否则每帧 Refresh 都会对同一个不存在的路径
> 重新发起一次 `Resources.Load`。

## 8. 工程上的坑

### 8.1 验证必须用 `Tools/check-asmdefs.sh`

```bash
bash Tools/check-asmdefs.sh
```

不要把所有 `.cs` 塞进一个 csproj——那会把 asmdef 的程序集边界整个抹掉，
漏写 `references` 这类错误一个都抓不到。脚本按真实边界逐个编译，**刻意不用 ProjectReference**
（MSBuild 的 P2P 引用是传递的，Unity 的 asmdef 不是）。
已经教会它处理 `precompiledReferences`（MoonSharp 走这条）。

### 8.2 当前测试基线

测试数量与最近一次运行结果统一记在 [README](../../README.md)（行为层迁移开始前是 175 个）。
跑法：Unity MCP 的 `run_tests`（EditMode）。
表现层改动也要跑——`BattleSessionTests` 里有依赖费用文案的断言。

### 8.3 Unity 不在线时怎么跑测试

Unity MCP 断线时，可以拿 `check-asmdefs.sh` 产出的 dll
（`$TMPDIR/tcginfu-asmdef-check/bin`）搭一个控制台工程直接驱动引擎，
比对着代码推演靠谱得多。本轮 16 条断言就是这么跑绿的。
记得设 `TCGINFU_CONTENT_DIR`，否则 `ContentPaths.Resolve` 在临时目录里找不到内容包。

### 8.4 NUnit 版本

Unity 自带 **NUnit 3.5.0**，**没有 `Assert.Multiple`**（3.6 才加）。
用 NuGet 的新版验证会编译通过而 Unity 里报 `CS0117`——假阳性。

### 8.5 `init` 访问器需要每个程序集自备 `IsExternalInit`

`Core` / `Content` / `Presentation` / `Scripting` 都设了 `noEngineReferences`
（netstandard2.1 没有 `IsExternalInit`）。各有一份 `internal` shim——跨程序集看不见别人的 internal。

### 8.6 新增序列化字段在旧资产上是零值

Unity 给**已存在的资产**补新字段用的是**类型零值**，不是 C# 的字段初始值。
要么写 `Normalize()` 自愈，要么别依赖会静默默认错的 bool。

### 8.7 测血量要小心轮末

自动双盾在轮末清零、轮始刷新。跨越轮边界去 diff `player.Hp` 或 `Shields.Physical`
会被刷新骗到——直接断言 `BattleEvent` 里的 `DamageResult` 分解。这个坑在 E10 上踩过一次。

### 8.8 Python 在 conda 里

`python` 不在 PATH，实际在 `D:\miniconda3\python.exe`；
打印中文必须 `PYTHONIOENCODING=utf-8`（默认 cp1252 会抛 `UnicodeEncodeError`）。
