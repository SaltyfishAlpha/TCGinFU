# 表现层待办 · 给负责 UI 的那位

> 配套：行为层交接 [behaviour_layer_handoff.md](behaviour_layer_handoff.md)（含现有操作约定）·
> 表现层架构 [view_architecture.md](view_architecture.md) · 规则书 [v0_5.md](v0_5.md)
>
> **分工**：你负责表现层（`Assets/Scripts/View/**`），
> 牌效逻辑那边负责 `Core` / `Scripting` / `StreamingAssets/Content`。
>
> 最后核对：2026-11-23。**现状一句话**：§1、§2 的界面都做完了；§2 后端已接通，
> **§1 后端没接（选了也白选）**，剩下的是牌效那边的活。§3 仍开放，§4 仍要真人试手感。
> 充能的三种用途（§5.2）2026-11-23 已全部有入口，旧 B-15 / B-16 关闭。

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

问法登记在 `CardChoiceCatalog`（键 `mode` / `element`），结果经 `PlayCardAction.With(key, value)`
进 `Choices`，Lua 侧由 `LuaProxies` 的 `ctx.choice(key, 默认值)` 读出。

「消耗几个充能」（键 `charge`）**不在那张表里**：它照卡面的 `chargeOptions` 字段自动生成，
答案填进 `PlayCardAction.ChargeForEffect`（2026-11-23，旧 B-15 / B-16）。
借道 `ChargeForMp` 的那条错链已经拆掉——拿充能抵 MP 现在是独立动作 `SpendChargeForMpAction`，
转掉的充能不再计入 `ctx.charge_spent`。详见 [view_architecture.md](view_architecture.md) §5
与 [data_architecture.md](data_architecture.md) §4.2。

> ⚠ 仍有两张形态牌的选择没登记（B-17）：元素增殖炸弹的 `element`、炼金防护力场的 `row`，
> 现在永远用脚本默认值。

## 3. 「掷骰后」响应窗口（1 张卡 + 1 条词条在等）——仍开放

**幸运改命**：「在你或任意队友**投掷骰子后**立即打出。将其中1颗骰子的结果直接改为7。」

现在引擎只有**一个**响应窗口，开在敌人意图结算之前（`DecisionKind.EnemyResponse`）。
掷骰发生在 `BattleEngine.RollCrit` 里，是个同步调用，中间没有停下来等玩家的机会。

**这条需要引擎先改**（加一个 `DecisionKind.DiceResponse` 之类的时点），
所以请先和牌效那边对齐，不要只在 View 层想办法。
顺带一提：「命运锚点·七」目前只作用于暴击骰，卡面写的是「任何骰子」，同一个缺口。

## 3.5 响应窗口里的「替他分摊」（B-14）——**已接通，待真人试手感**

**因果抹杀令**：「当前HP最高者受到24点物理伤害；可在响应时由相邻队友分摊，两人各12点并分别抵扣护盾。」

入口做在**右键角色卡**上（`BattleView.OnCombatantContextMenu` → `ShareOptionsFor`）：

- 菜单挂在**站出来的那个人**身上，不是被打的人——玩家的动作是「我替他挡一半」，主语是自己。
  右键自己人 → 看到可以替哪个邻座分摊。
- ~~没有可分摊对象时右键**仍然直接开详情面板**~~，2026-11-23 改成**右键一律出菜单**
  （和右键手牌一条口径），否则充能的两种单独用途在角色卡上没有入口（旧 B-15）。
- 菜单项上直接写了拆完之后各自要吃多少点（`PendingAttackOn(target).BaseAmount / 2`）——
  玩家要判断的是「我扛不扛得住这一半」，不该让他自己拿卡面的 24 去除以 2。
- 自己已经站出来过了，留一条不可点的「（已替 X 分摊）」。
  **别人**替谁分摊了不进菜单：那是局面信息，写在角色卡上（`ResponseTagFor`，
  援护与分摊各显示成一行「援护：维汀」「分摊：维汀」，窗口一关就消失）。

用到的引擎接口：

| 接口 | 干什么 |
| ----- | ----- |
| `engine.ShareableTargetsFor(sharer)` | **候选名单**。能不能分摊一概问它，界面不自己判相邻、不自己判意图——各判一份迟早分家，表现出来就是「菜单里有、点下去却被拒」 |
| `engine.CanShareDamage(sharer, target, out reason)` | 同一份判断的单条版本，`RegisterDamageShare` 走的也是它 |
| `engine.SharerFor(target)` / `engine.CovererFor(target)` | 这次攻击已经登记了分摊 / 援护没有 |
| `engine.PendingAttackOn(target)` | 要拆的那次攻击，用来算「各 N 点」 |
| `new ShareDamageAction(站出来的人Id, 原目标Id)` | 提交 |

候选是一**串**不是一个：队伍不一定只有两个人，范围意图也可能同时打中好几个，
所以菜单按名单长度自然展开，没有「第二个队友」这种写死的假设。
IMGUI 沙盒（`SandboxGui.DrawResponsePanel`）用的是同一个名单，不另开一套判断。

不花 AP、不打牌，提交后**不**视为放弃响应——还能接着打时机牌，最后再 `PassResponseAction`。
被拒绝时 `ActionResult.Reason` 已经是给玩家看的中文（「只有相邻队友才能分摊」等），直接提示即可。

> **还没真按过。** EditMode 测的是引擎那条路（`DamageShareTests` + `CardEffectFixTests.F20/F21`），
> 右键菜单本身测不到。请在二阶段打到因果抹杀令时，右键相邻队友确认菜单真的出得来、数字对得上。

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
Unity MCP 的 `run_tests`（EditMode）。测试基线见 [README](../../README.md)，别添红的。
改 View 也要跑——`BattleSessionTests` 里有依赖费用文案的断言。
