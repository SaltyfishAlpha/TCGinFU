# 表现层待办 · 给负责 UI 的那位

> 配套：行为层交接 [behaviour_layer_handoff.md](behaviour_layer_handoff.md)（含现有操作约定）·
> 表现层架构 [view_architecture.md](view_architecture.md) · 规则书 [v0_5.md](v0_5.md)
>
> **分工**：你负责表现层（`Assets/Scripts/View/**`），
> 牌效逻辑那边负责 `Core` / `Scripting` / `StreamingAssets/Content`。
>
> 最后核对：2026-11-22。**现状一句话**：§1、§2 的界面都做完了；§2 后端已接通，
> **§1 后端没接（选了也白选）**，剩下的是牌效那边的活。§3 仍开放，§4 仍要真人试手感。

---

## 0. 为什么这些是 UI 的事

下面每一条背后的**规则逻辑都已经实现并测过了**，卡打得出、效果结算得对。
缺的是「让玩家做出那个选择」的界面——现在一律由代码替玩家做了确定性的默认选择。

这些默认选择当初都写在 Lua 脚本的注释里（`Assets/StreamingAssets/Content/scripts/*.lua`）。
界面做完后注释没有全部跟上：现在搜 **「UI 还没有」** 只剩 `miku.lua:192`（盗贼戏法）一处，
而且这句已经**不准确**——UI 有了，缺的是后端（见 §1）。修 B-2 时顺手改掉。

> ⚠️ **不要在 View 层重新实现规则判定。** 所有合法性都已经在引擎里了：
> 能不能打走 `ActionResult`，能不能放走 `CombatantAcceptsCard` 那类过滤器。
> 界面只负责「收集玩家的选择」并塞进动作里。

---

## 1. 选牌 UI（3 张卡在等）——界面 ✅ 已做，**后端 ❌ 没接**

| 卡 | 卡面 | 现在的行为 |
| ----- | ----- | ----- |
| 盗贼戏法 | 「从弃牌堆**选择**1张牌」 | 界面让玩家挑，但结算仍取弃牌堆**顶** |
| 以太重组坩埚 | 「从你的弃牌堆将最多2张牌」 | 界面让玩家挑，但结算仍取弃牌堆顶 2 张 |
| 应急催化剂 | 「将你手牌中的**1张**牌」 | 界面让玩家挑，但结算仍取手牌**末位** |

**已做的（View）**：
- `ZonePeekPanel.OpenForSelection(title, cards, minPick, maxPick, onConfirm, onCancel)`：选择模式，
  只管「选够张数」这条纯界面约束，不判合法性。
- 哪张卡要问什么写在 `CardChoiceCatalog`；`BattleView.PlayCardWithChoices` 依次问完，
  `SubmitPlay` 把选中的 uid 用 `PlayCardAction.WithMany("card", uids)` 写进 `Choices`，
  键为 `card0`、`card1`…，外加 `card_count`。

**没接的（牌效那边，B-2）**：
- `BattleEngine.ChargeFromZone`（`BattleEngine.Mutations.cs` 约 163–185 行）仍按 `ChargeSource`
  取 `Discard.DrawTop()` / `Hand.DrawBottom()`，不看选择；
- 三张卡的脚本（`miku.lua` 盗贼戏法、`weiting.lua` 以太重组坩埚 / 应急催化剂）只调
  `charge_from(..., "discard"/"hand", n)`，**没有读 `ctx.choice("card0")` 这些键**。

所以**现在的待办是后端接线**，不是 UI：给 `charge_from` / `ChargeFromZone` 加「按 uid 取」的入口，
脚本读 `card_count` / `cardN`，选择为空时退回现有的取顶 / 取末位。请和行为层（牌效那边）一起改，
见 [behaviour_layer_handoff.md](behaviour_layer_handoff.md) §7 B-2。UI 这边只需在接好后回归一遍。

## 2. 二选一 / 选元素 UI ✅ 已完成

| 卡 | 卡面 | 现在的行为 |
| ----- | ----- | ----- |
| 应急催化剂 | 「获得4点全能护盾，**或**使其本轮下一次暴击掷骰+1」 | 玩家选；Lua 读 `ctx.choice("mode", 0)`（0=护盾，默认） |
| 调合·万能灵药 | 「①生命灵药 ②元素涂油（火/冰/雷/风/毒**任选**）」 | 玩家选消耗几个充能、选哪项、选元素；Lua 读 `ctx.choice("mode")` / `ctx.choice("element", 3)`，消耗 2 个时两项都生效 |

问法登记在 `CardChoiceCatalog`（键 `mode` / `element` / `charge`），结果经 `PlayCardAction.With(key, value)`
进 `Choices`，Lua 侧由 `LuaProxies` 的 `ctx.choice(key, 默认值)` 读出。

**剩下的一点（牌效那边）**：「消耗几个充能」仍然借道 `PlayCardAction.ChargeForMp` 传
（`BattleView.SubmitPlay` 把 `charge` 这个答案塞进 `chargeForMp`，脚本从 `ctx.charge_spent` 读）。
那个字段的本意是「拿充能抵 MP」，不是「为卡面词条付充能」——这条链是错的，只是因为这张卡 MP 费用为 0
才暂时没有副作用。需要一个独立的「为词条付充能」入口。

## 3. 「掷骰后」响应窗口（1 张卡 + 1 条词条在等）——仍开放

**幸运改命**：「在你或任意队友**投掷骰子后**立即打出。将其中1颗骰子的结果直接改为7。」

现在引擎只有**一个**响应窗口，开在敌人意图结算之前（`DecisionKind.EnemyResponse`）。
掷骰发生在 `BattleEngine.RollCrit` 里，是个同步调用，中间没有停下来等玩家的机会。

**这条需要引擎先改**（加一个 `DecisionKind.DiceResponse` 之类的时点），
所以请先和牌效那边对齐，不要只在 View 层想办法。
顺带一提：「命运锚点·七」目前只作用于暴击骰，卡面写的是「任何骰子」，同一个缺口。

## 4. 没实测过的交互（我做的，但没能真按出来）——仍需真人试手感

- **长按 0.35s 看详情**：接线、拖拽取消、吞掉随后的 Click 都验过了，
  但 MCP 模拟不了「按住不放」。阈值在 `LongPress.Threshold`。
  **请真的按一按**，不跟手就调这个常数。
- **拖角色卡走位**：落点判定、确认框文案都验过，滑行动画（`SlideTween`）也接了。
  同样没有真的拖过。

## 5. 美术接线现状

- 卡图：`art: "cards/<卡id>"`（每张卡自己的），解析不到**按类型兜底**，
  再解析不到退到牌背。兜底表在 `pack.json` 的 `artFallback`。
- 角色立绘：`portrait: "portraits/<角色id>"`，兜底到 `character`。
  `Art/portraits/alian.jpg` 是未来角色雅利安的图，已提前放好但还没有对应角色数据。
- **纯色占位那条路已经删了**：缺图统一显示牌背，不再出现「几张纯色卡混在有图的卡里」。
- 卡面文字都加了描边（`Outline`），因为铺上插画之后底色不再可控。

## 6. 跑测试

```bash
bash Tools/check-asmdefs.sh          # 编译（按 asmdef 真实边界，别塞进一个 csproj）
```
Unity MCP 的 `run_tests`（EditMode）。**当前基线 205 个**（177 个 `[Test]` + 28 个 `[TestCase]`，
2026-11-22 移除遗留内容后计数；此前为 227）。已知 1 个红：`L02`（缺陷 B-4），别再添新的。
改 View 也要跑——`BattleSessionTests` 里有依赖费用文案的断言。
