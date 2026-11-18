using System.Linq;
using NUnit.Framework;
using TCGinFU.Core;
using TCGinFU.Presentation;

namespace TCGinFU.Core.Tests
{
    /// <summary>
    /// 沙盒会话的逻辑部分（<see cref="BattleSession"/> 不碰任何 Unity 类型，所以能在这里跑）。
    /// 界面本身没法自动测，但「每个预设都能开局」「自动打完不会卡住」这两条能测——
    /// 而它们正是沙盒最容易坏掉的地方。
    /// </summary>
    public class BattleSessionTests
    {
        [Test]
        public void 默认开局停在可操作的状态()
        {
            var session = new BattleSession();

            Assert.AreEqual(1, session.State.Round);
            Assert.AreEqual(2, session.State.Players.Count);
            Assert.AreNotEqual(DecisionKind.None, session.Engine.Pending.Kind);
            Assert.IsNotNull(session.SelectedPlayer, "必须有一个默认选中的角色，否则界面是空的");
            Assert.IsNotNull(session.State.Find(session.SelectedEnemyId), "必须有默认目标");
            Assert.IsFalse(session.LastActionFailed);
        }

        [Test]
        public void 每位角色开局都有五张起手牌()
        {
            var session = new BattleSession { PartySize = 4 };
            session.Restart();

            foreach (PlayerCharacter player in session.State.Players)
            {
                Assert.AreEqual(5, player.Zones.Hand.Count, $"{player.Name} 起手应为 5 张");
            }
        }

        [TestCase(BattleScenario.训练木桩)]
        [TestCase(BattleScenario.哥布林)]
        [TestCase(BattleScenario.石像鬼)]
        [TestCase(BattleScenario.深渊领主)]
        [TestCase(BattleScenario.哥布林小队)]
        public void 每个敌人预设都能正常开局(BattleScenario preset)
        {
            var session = new BattleSession { EnemyPreset = preset };
            session.Restart();

            Assert.IsTrue(session.State.Enemies.Count > 0);
            Assert.AreEqual(BattleOutcome.Ongoing, session.State.Outcome);
            Assert.DoesNotThrow(() => _ = session.Log, "战报渲染不应抛异常");
        }

        /// <summary>
        /// 「自动打完」靠反复跳过决策点推进。只要有一个阶段忘了推进，这里就会跳满上限——
        /// 所以这条测试实际上在验证整个轮次状态机不会卡死。
        /// </summary>
        [TestCase(BattleScenario.哥布林, 1)]
        [TestCase(BattleScenario.哥布林, 3)]
        [TestCase(BattleScenario.石像鬼, 2)]
        [TestCase(BattleScenario.深渊领主, 4)]
        [TestCase(BattleScenario.哥布林小队, 2)]
        public void 自动打完总能分出胜负(BattleScenario preset, int partySize)
        {
            var session = new BattleSession { EnemyPreset = preset, PartySize = partySize };
            session.Restart();

            session.SkipToEnd();

            Assert.AreNotEqual(BattleOutcome.Ongoing, session.State.Outcome,
                $"{preset} / {partySize} 人：{session.Message}");
            Assert.AreEqual(RoundPhase.Finished, session.State.Phase);
        }

        [Test]
        public void 同种子重开产生完全相同的一局()
        {
            var a = new BattleSession { Seed = 777, EnemyPreset = BattleScenario.石像鬼 };
            a.Restart();
            a.SkipToEnd();

            var b = new BattleSession { Seed = 777, EnemyPreset = BattleScenario.石像鬼 };
            b.Restart();
            b.SkipToEnd();

            Assert.AreEqual(a.Log, b.Log, "洗牌与骰点都该是确定性的");
        }

        [Test]
        public void 不同种子产生不同的一局()
        {
            var a = new BattleSession { Seed = 1, EnemyPreset = BattleScenario.石像鬼 };
            a.Restart();
            a.SkipToEnd();

            var b = new BattleSession { Seed = 2, EnemyPreset = BattleScenario.石像鬼 };
            b.Restart();
            b.SkipToEnd();

            Assert.AreNotEqual(a.Log, b.Log);
        }

        [Test]
        public void 非法动作只改提示不改状态()
        {
            var session = new BattleSession();
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
            var session = new BattleSession { EnemyPreset = BattleScenario.哥布林 };
            session.Restart();

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
            var session = new BattleSession { EnemyPreset = BattleScenario.训练木桩, PartySize = 1 };
            session.Restart();

            // 推到第 2 轮轮末：起手 5 张 + 补 3 张 = 8 张，需要弃 1 张
            while (session.Engine.Pending.Kind != DecisionKind.HandLimitDiscard)
            {
                session.SkipPending();
                Assert.AreEqual(BattleOutcome.Ongoing, session.State.Outcome, "木桩局不该结束");
                if (session.State.Round > 4) Assert.Fail("4 轮内应该出现轮末弃牌");
            }

            PlayerCharacter player = session.State.Players[0];
            session.SelectedPlayerId = player.Id;
            int required = session.Engine.RequiredDiscards(player);
            Assert.AreEqual(1, required);

            // 选多了应被拒绝
            session.ToggleDiscard(player.Zones.Hand[0].Uid);
            session.ToggleDiscard(player.Zones.Hand[1].Uid);
            session.ConfirmDiscard(player);
            Assert.IsTrue(session.LastActionFailed);

            // 取消一张后正好
            session.ToggleDiscard(player.Zones.Hand[1].Uid);
            Assert.AreEqual(1, session.DiscardSelectionCount);
            session.ConfirmDiscard(player);

            Assert.IsFalse(session.LastActionFailed, session.Message);
            Assert.AreEqual(0, session.DiscardSelectionCount, "提交成功后应清空选择");
        }

        [Test]
        public void 克制摘要能读()
        {
            var session = new BattleSession { EnemyPreset = BattleScenario.深渊领主 };
            session.Restart();

            string summary = BattleSession.AffinitySummary((Enemy)session.State.Enemies[0]);

            StringAssert.Contains("免暗", summary);
            StringAssert.Contains("吸毒", summary);
            StringAssert.Contains("弱光", summary);
        }

        [Test]
        public void 费用摘要能读()
        {
            Assert.AreEqual("免费", BattleSession.CostText(
                new CardDefinition("x", "x", CardCategory.Treasure, CardKind.Skill)));

            string text = BattleSession.CostText(TCGinFU.Content.SampleCards.PiercingShot);
            StringAssert.Contains("2AP", text);
            StringAssert.Contains("5MP", text);
            StringAssert.Contains("【暴击】", text);
        }
    }
}
