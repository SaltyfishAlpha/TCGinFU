using NUnit.Framework;
using TCGinFU.Content.Loading;
using TCGinFU.Core;
using TCGinFU.Presentation;

namespace TCGinFU.Core.Tests
{
    /// <summary>
    /// 驱动层的逻辑部分（<see cref="BattleSession"/> 不碰任何 Unity 类型，所以能在这里跑）。
    ///
    /// <para>界面本身没法自动测，但「能开局」「自动打完不会卡住」「同种子可复现」这几条能测——
    /// 而它们正是最容易坏掉的地方。数据一律走内容包「命刻之战」：
    /// C# 样例内容已于 2026-11-22 移除，这里原先按样例预设参数化的用例改成按**种子**参数化。</para>
    /// </summary>
    public class BattleSessionTests
    {
        private static BattleSession Fresh(int seed = 20251117)
        {
            var session = new BattleSession { Seed = seed };
            session.Restart();

            Assert.IsTrue(session.IsLoaded, "内容包没加载起来：" + session.Message);
            return session;
        }

        [Test]
        public void 默认开局停在可操作的状态()
        {
            var session = new BattleSession();

            Assert.IsTrue(session.IsLoaded, session.Message);
            Assert.AreEqual(1, session.State.Round);
            Assert.AreEqual(2, session.State.Players.Count, "命刻之战是米阔阔 + 维汀两人");
            Assert.AreNotEqual(DecisionKind.None, session.Engine.Pending.Kind);
            Assert.IsNotNull(session.SelectedPlayer, "必须有一个默认选中的角色，否则界面是空的");
            Assert.IsNotNull(session.State.Find(session.SelectedEnemyId), "必须有默认目标");
            Assert.IsFalse(session.LastActionFailed);
        }

        [Test]
        public void 每位角色开局都有五张起手牌()
        {
            BattleSession session = Fresh();

            foreach (PlayerCharacter player in session.State.Players)
            {
                Assert.AreEqual(5, player.Zones.Hand.Count, $"{player.Name} 起手应为 5 张");
            }
        }

        [Test]
        public void 开局就能渲染战报()
        {
            BattleSession session = Fresh();

            Assert.IsTrue(session.State.Enemies.Count > 0);
            Assert.AreEqual(BattleOutcome.Ongoing, session.State.Outcome);
            Assert.DoesNotThrow(() => _ = session.Log, "战报渲染不应抛异常");
        }

        /// <summary>
        /// 「自动打完」靠反复跳过决策点推进。只要有一个阶段忘了推进，这里就会跳满上限——
        /// 所以这条实际上在验证整个轮次状态机不会卡死。多跑几个种子，
        /// 免得某条只在特定洗牌顺序下才走到的分支蒙混过关。
        /// </summary>
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(20251117)]
        [TestCase(20261118)]
        [TestCase(999983)]
        public void 自动打完总能分出胜负(int seed)
        {
            BattleSession session = Fresh(seed);

            session.SkipToEnd();

            Assert.AreNotEqual(BattleOutcome.Ongoing, session.State.Outcome,
                $"种子 {seed}：{session.Message}");
            Assert.AreEqual(RoundPhase.Finished, session.State.Phase);
        }

        [Test]
        public void 同种子重开产生完全相同的一局()
        {
            BattleSession a = Fresh(777);
            a.SkipToEnd();

            BattleSession b = Fresh(777);
            b.SkipToEnd();

            Assert.AreEqual(a.Log, b.Log, "洗牌与骰点都该是确定性的");
        }

        [Test]
        public void 不同种子产生不同的一局()
        {
            BattleSession a = Fresh(1);
            a.SkipToEnd();

            BattleSession b = Fresh(2);
            b.SkipToEnd();

            Assert.AreNotEqual(a.Log, b.Log);
        }

        [Test]
        public void 非法动作只改提示不改状态()
        {
            BattleSession session = Fresh();
            PlayerCharacter player = session.SelectedPlayer;

            CardInstance card = player.Zones.Hand[0];
            player.Ap = 0;

            int handBefore = player.Zones.Hand.Count;
            session.Submit(new PlayCardAction(player.Id, card.Uid, session.SelectedEnemyId));

            Assert.IsTrue(session.LastActionFailed);
            StringAssert.Contains("行动力", session.Message, "应把引擎给的拒绝原因显示出来");
            Assert.AreEqual(handBefore, player.Zones.Hand.Count, "被拒绝的动作不该动手牌");
        }

        [Test]
        public void 跳过本步会推进到下一个决策点()
        {
            BattleSession session = Fresh();

            DecisionKind before = session.Engine.Pending.Kind;
            int roundBefore = session.State.Round;

            for (int i = 0; i < 6; i++) session.SkipPending();

            Assert.IsTrue(
                session.State.Round > roundBefore || session.Engine.Pending.Kind != before,
                "连跳 6 步总该推进到新的轮次或新的决策点");
        }

        [Test]
        public void 轮末弃牌的多选能正确提交()
        {
            BattleSession session = Fresh();

            // 推到出现轮末弃牌为止：起手 5 张 + 每轮补 3 张，不出牌迟早会超过手牌上限
            while (session.Engine.Pending.Kind != DecisionKind.HandLimitDiscard)
            {
                Assert.AreEqual(BattleOutcome.Ongoing, session.State.Outcome,
                    "还没轮到弃牌，战斗就结束了");
                if (session.State.Round > 8) Assert.Fail("8 轮内应该出现轮末弃牌");

                session.SkipPending();
            }

            PlayerCharacter player = session.State.FindPlayer(session.Engine.Pending.WaitingOn[0]);
            session.SelectedPlayerId = player.Id;

            int required = session.Engine.RequiredDiscards(player);
            Assert.Greater(required, 0);

            // 多选一张应被拒绝
            for (int i = 0; i <= required; i++) session.ToggleDiscard(player.Zones.Hand[i].Uid);
            session.ConfirmDiscard(player);
            Assert.IsTrue(session.LastActionFailed, "选多了应当被拒绝");

            // 取消一张后正好
            session.ToggleDiscard(player.Zones.Hand[required].Uid);
            Assert.AreEqual(required, session.DiscardSelectionCount);
            session.ConfirmDiscard(player);

            Assert.IsFalse(session.LastActionFailed, session.Message);
            Assert.AreEqual(0, session.DiscardSelectionCount, "提交成功后应清空选择");
        }

        [Test]
        public void 克制摘要能读()
        {
            // 纯函数，不依赖任何内容：就地造一只带四种克制关系的敌人
            Enemy enemy = TestFactory.Enemy("dummy").WithAffinities(
                new AffinityTable()
                    .Immune(DamageElement.Dark)
                    .Absorb(DamageElement.Poison)
                    .Vulnerable(DamageElement.Light)
                    .Resistant(DamageElement.Fire));

            string summary = BattleSession.AffinitySummary(enemy);

            StringAssert.Contains("免暗", summary);
            StringAssert.Contains("吸毒", summary);
            StringAssert.Contains("弱光", summary);
            StringAssert.Contains("抗火", summary);
        }

        [Test]
        public void 无克制的敌人摘要写无克制()
        {
            Assert.AreEqual("无克制", BattleSession.AffinitySummary(TestFactory.Enemy("plain")));
        }

        // ───────────────────────── 内容包坏掉时 ─────────────────────────

        /// <summary>
        /// 内容包有错时**不开局**，只把加载报告交出来。
        ///
        /// <para>这里曾经会静默退回一套 C# 样例卡组。那样做最糟的地方不是少了提示，
        /// 而是「内容包坏了」看起来会像「游戏能玩」——玩家改坏自己的 JSON 之后
        /// 会以为是自己的卡没生效，而不是整个包没加载进去。</para>
        /// </summary>
        [Test]
        public void 内容包有错时不开局并交出加载报告()
        {
            string original = System.Environment.GetEnvironmentVariable(ContentPaths.EnvironmentVariable);
            string temp = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "tcginfu_broken_" + System.Guid.NewGuid().ToString("N"));

            try
            {
                CopyPack(ContentPaths.Resolve(), temp);

                // 把一个卡牌文件改成非法 JSON
                string broken = System.IO.Path.Combine(temp, "cards", "forms.json");
                Assert.IsTrue(System.IO.File.Exists(broken), "内容包结构变了，这条测试要跟着改");
                System.IO.File.WriteAllText(broken, "{ 这不是 JSON");

                System.Environment.SetEnvironmentVariable(ContentPaths.EnvironmentVariable, temp);

                var session = new BattleSession();

                Assert.IsFalse(session.IsLoaded, "内容包是坏的，不该开出一局来");
                Assert.IsNull(session.State);
                Assert.IsTrue(session.LastActionFailed, "应当标成警告态");
                StringAssert.Contains("forms.json", session.Message, "报告要指得出是哪个文件：" + session.Message);
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(ContentPaths.EnvironmentVariable, original);
                if (System.IO.Directory.Exists(temp)) System.IO.Directory.Delete(temp, recursive: true);
            }
        }

        private static void CopyPack(string from, string to)
        {
            Assert.IsNotNull(from, "找不到内容包目录");

            foreach (string dir in System.IO.Directory.GetDirectories(from, "*", System.IO.SearchOption.AllDirectories))
            {
                System.IO.Directory.CreateDirectory(dir.Replace(from, to));
            }

            System.IO.Directory.CreateDirectory(to);
            foreach (string file in System.IO.Directory.GetFiles(from, "*", System.IO.SearchOption.AllDirectories))
            {
                System.IO.File.Copy(file, file.Replace(from, to), overwrite: true);
            }
        }

        /// <summary>
        /// 费用摘要的减免染色与零费显示由 <c>LuaBehaviourTests</c> 的 C01–C04 守着（那边有真实的费用钩子）。
        /// 这里只守一条它们没覆盖的：【暴击】标识要跟在费用后面。
        /// </summary>
        [Test]
        public void 费用摘要带暴击标识()
        {
            var def = new CardDefinition("shot", "穿心射", CardCategory.Class, CardKind.Skill)
            {
                ApCost = 2,
                MpCost = 5,
                HasCrit = true,
            };

            string text = BattleSession.CostText(def);

            StringAssert.Contains("2AP", text);
            StringAssert.Contains("5MP", text);
            StringAssert.Contains("【暴击】", text);
            StringAssert.DoesNotContain("<color", text, "没有减免时不该掺颜色标记");
        }
    }
}
