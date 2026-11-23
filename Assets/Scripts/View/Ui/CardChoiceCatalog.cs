using System;
using System.Collections.Generic;
using System.Linq;
using TCGinFU.Core;

namespace TCGinFU.View
{
    /// <summary>选牌提示的候选来源。</summary>
    public enum ChoiceSource
    {
        无,
        手牌,
        弃牌堆,
    }

    /// <summary>
    /// 打出某张牌之前要向玩家问的**一个**问题。
    ///
    /// <para>为什么把它做成数据而不是给每张卡写一段流程代码：这些问题全都长一个样
    /// （几选一、或者从某个牌堆里挑几张），差别只在选项文案和键名。写成流程代码的话，
    /// 每补一张卡就要在 <see cref="BattleView"/> 里塞一段新的回调嵌套，
    /// 而回调嵌套一多就很难保证「中途取消」这条路径每处都处理到。</para>
    /// </summary>
    public sealed class ChoicePrompt
    {
        /// <summary>回传给行为层的键名，对应 Lua 里的 <c>ctx.choice("…")</c>。</summary>
        public string Key;

        /// <summary>问题标题，直接显示给玩家。</summary>
        public string Title;

        /// <summary>几选一的选项。选牌提示留空。</summary>
        public IReadOnlyList<(string Label, int Value)> Options = Array.Empty<(string, int)>();

        /// <summary>
        /// 选项可用性。打出者的状态决定某些选项根本选不了
        /// （「消耗 2 个充能」在只有 1 个充能时就不该出现在菜单里——
        /// 让玩家点完才被引擎拒绝是最差的反馈）。
        /// </summary>
        public Func<PlayerCharacter, int, bool> OptionAvailable;

        /// <summary>选牌提示：从哪个区域挑。</summary>
        public ChoiceSource PickFrom = ChoiceSource.无;

        /// <summary>选牌提示：至少/最多挑几张。「最多2张」就是 0–2。</summary>
        public int MinPick;

        public int MaxPick;

        /// <summary>
        /// 这个问题要不要问。参数是此前已经收集到的答案——
        /// 「选了元素涂油才需要问是哪个元素」这类依赖关系靠它表达。
        /// </summary>
        public Func<IReadOnlyDictionary<string, int>, bool> When;

        public bool IsPick => PickFrom != ChoiceSource.无;
    }

    /// <summary>
    /// 哪张卡要问玩家什么。键是 <see cref="CardDefinition.Id"/>。
    ///
    /// <para>这张表是**表现层单方面的约定**：引擎不认识任何一个键，只负责把
    /// <see cref="PlayCardAction.Choices"/> 原样递给 Lua。所以这里的键名必须和
    /// <c>StreamingAssets/Content/scripts/*.lua</c> 里 <c>ctx.choice(...)</c> 读的键名对上，
    /// 对不上不会报错，只会**静默地退回脚本里的默认值**——改键名时务必两边一起改。</para>
    /// </summary>
    public static class CardChoiceCatalog
    {
        /// <summary>元素涂油的属性序号，和 statuses.lua 里 status_oil 的层数含义一致。</summary>
        private static readonly (string, int)[] Elements =
        {
            ("火", 1), ("冰", 2), ("雷", 3), ("风", 4), ("毒", 5),
        };

        private static readonly Dictionary<string, ChoicePrompt[]> Table = new()
        {
            // 「将你手牌中的1张牌直接贴入该队友的充能区，并令该队友获得4点全能护盾，
            //   或使其本轮下一次暴击掷骰结果+1。」
            ["emergency_catalyst"] = new[]
            {
                new ChoicePrompt
                {
                    Key = "card",
                    Title = "选择要贴成充能的手牌",
                    PickFrom = ChoiceSource.手牌,
                    MinPick = 1,
                    MaxPick = 1,
                },
                new ChoicePrompt
                {
                    Key = "mode",
                    Title = "应急催化剂：二选一",
                    Options = new[] { ("获得 4 点全能护盾", 0), ("本轮下一次暴击掷骰 +1", 1) },
                },
            },

            // 「打出时需消耗1个充能指示物，从以下两项炼金效果中二选一；
            //   若消耗2个充能指示物则两项同时生效。」
            // 「消耗几个充能」那一问不在这里——它由卡面的 chargeOptions 自动生成（见 For）
            ["universal_elixir"] = new[]
            {
                new ChoicePrompt
                {
                    Key = "mode",
                    Title = "选择炼金效果",
                    Options = new[] { ("① 生命灵药：回复 10 HP / 10 MP", 0), ("② 元素涂油", 1) },
                    // 消耗 2 个充能时两项都生效，再问「选哪一项」就是在骗玩家
                    When = answers => Answer(answers, "charge", 1) < 2,
                },
                new ChoicePrompt
                {
                    Key = "element",
                    Title = "元素涂油：附加哪种属性？",
                    Options = Elements,
                    // 只有真会涂油时才问属性
                    When = answers => Answer(answers, "charge", 1) >= 2 || Answer(answers, "mode", 0) == 1,
                },
            },

            // 「从弃牌堆选择1张牌直接置入自己或队友的充能区。」
            ["thieves_trick"] = new[]
            {
                new ChoicePrompt
                {
                    Key = "card",
                    Title = "从弃牌堆选择 1 张牌贴成充能",
                    PickFrom = ChoiceSource.弃牌堆,
                    MinPick = 1,
                    MaxPick = 1,
                },
            },

            // 「从你的弃牌堆将最多2张牌直接贴入全队任意角色的充能区。」
            // 「最多」——所以下限是 0，一张都不选也合法。
            ["aether_reassembly_crucible"] = new[]
            {
                new ChoicePrompt
                {
                    Key = "card",
                    Title = "从弃牌堆选择最多 2 张牌贴成充能",
                    PickFrom = ChoiceSource.弃牌堆,
                    MinPick = 0,
                    MaxPick = 2,
                },
            },
        };

        private static readonly ChoicePrompt[] None = Array.Empty<ChoicePrompt>();

        /// <summary>
        /// 这张牌打出前要问的全部问题。「消耗几个充能？」排在最前面——
        /// 后面的问题可能依赖它（万能灵药消耗 2 个时就不该再问「二选一选哪项」）。
        ///
        /// <para>充能那一问不写在 <see cref="Table"/> 里，而是照卡面的
        /// <see cref="CardDefinition.ChargeOptions"/> 现生成：带这种词条的牌会越来越多
        /// （design_backlog O-10 的角色专属牌以充能为轴），每张都在表现层写死一份
        /// 迟早会漏掉一张——漏掉的表现是「卡面写着可以消耗，界面却没有入口」，正是 B-15。</para>
        /// </summary>
        public static IReadOnlyList<ChoicePrompt> For(CardDefinition definition)
        {
            if (definition == null) return None;

            ChoicePrompt[] table = Table.TryGetValue(definition.Id, out ChoicePrompt[] found) ? found : None;
            ChoicePrompt charge = ChargePromptFor(definition);
            if (charge == null) return table;

            var prompts = new List<ChoicePrompt>(table.Length + 1) { charge };
            prompts.AddRange(table);
            return prompts;
        }

        /// <summary>卡面声明了可选消耗就生成一问，没声明就没有这一问。</summary>
        private static ChoicePrompt ChargePromptFor(CardDefinition definition)
        {
            IReadOnlyList<int> options = definition.ChargeOptions;
            if (options == null || options.Count == 0) return null;

            return new ChoicePrompt
            {
                Key = "charge",
                Title = $"{definition.Name}：额外消耗几个充能？",
                Options = options.Select(n => (n == 0 ? "不消耗" : $"{n} 个充能", n)).ToArray(),
                // 卡面硬费用那几个充能也要从同一个充能区出，所以可选的那部分只能用剩下的
                OptionAvailable = (player, count) =>
                    player.Zones.Charges.Count >= count + definition.ChargeCost,
            };
        }

        private static int Answer(IReadOnlyDictionary<string, int> answers, string key, int fallback)
            => answers.TryGetValue(key, out int value) ? value : fallback;
    }
}
