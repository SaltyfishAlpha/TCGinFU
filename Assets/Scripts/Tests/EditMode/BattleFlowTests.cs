using System.Linq;
using NUnit.Framework;
using TCGinFU.Core;

namespace TCGinFU.Core.Tests
{
    /// <summary>§8 标准战斗轮次流程。</summary>
    public class BattleFlowTests
    {
        private static (BattleEngine engine, PlayerCharacter player, Enemy enemy) Setup(
            int deckSize = 40,
            params EnemyIntent[] intents)
        {
            PlayerCharacter player = TestFactory.Player();
            Enemy enemy = TestFactory.Enemy(intents: intents);
            TestFactory.FillDeck(player, deckSize);

            (BattleEngine engine, _, _) = TestFactory.Duel(new XorShift32Rng(7), player, enemy);
            return (engine, player, enemy);
        }

        [Test]
        public void 第一轮抽五张第二轮抽三张()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            Assert.AreEqual(1, engine.State.Round);
            Assert.AreEqual(RoundPhase.PlayerPhase, engine.State.Phase);
            Assert.AreEqual(5, player.Zones.Hand.Count);

            AdvanceOneRound(engine, player);

            Assert.AreEqual(2, engine.State.Round);
            Assert.AreEqual(8, player.Zones.Hand.Count, "第 2 轮补 3 张");
        }

        [Test]
        public void 轮始获得三点行动力且不累加()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            Assert.AreEqual(3, player.Ap);

            AdvanceOneRound(engine, player);
            Assert.AreEqual(3, player.Ap, "AP 是赋值不是累加（裁定 R-10）");
        }

        [Test]
        public void 轮始重新获得自动双盾()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            Assert.AreEqual(player.Sheet.AutoPhysicalShield, player.Shields.AutoPhysical);
            Assert.AreEqual(player.Sheet.AutoMagicShield, player.Shields.AutoMagical);
        }

        [Test]
        public void 打出武器牌消耗行动力并造成伤害()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup();
            engine.Start();

            CardInstance card = TestFactory.AddToHand(player, TestFactory.StrikeCard(damage: 7));
            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));

            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(93, enemy.Hp);
            Assert.AreEqual(2, player.Ap);
            Assert.AreEqual(1, player.Zones.Discard.Count);
        }

        [Test]
        public void 行动力不足时拒绝出牌且不改状态()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup();
            engine.Start();

            player.Ap = 0;
            CardInstance card = TestFactory.AddToHand(player, TestFactory.StrikeCard(apCost: 1));
            int handBefore = player.Zones.Hand.Count;

            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(handBefore, player.Zones.Hand.Count, "被拒绝的动作不应改动手牌");
            Assert.AreEqual(100, enemy.Hp);
        }

        [Test]
        public void 充能转MP后才打得出原本MP不足的牌()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup();
            engine.Start();

            CardDefinition spell = new("spell", "法术", CardCategory.Class, CardPlay.Skill)
            {
                ApCost = 1,
                MpCost = 5,
                Colors = new[] { AttributeColor.Willpower },
                Effects = new[] { EffectData.Damage(ValueExpr.Flat(10)) },
            };

            CardInstance card = TestFactory.AddToHand(player, spell);
            player.SpendMp(player.Mp); // 清空 MP
            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("fuel"), player.Id));

            ActionResult withoutCharge = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));
            Assert.IsFalse(withoutCharge.Ok, "MP 不够应被拒绝");

            // 充能不再在出牌时抵扣（旧裁定 R-09 已作废），得先单独转成 MP
            Assert.IsTrue(engine.Submit(new SpendChargeForMpAction(player.Id)).Ok);
            Assert.AreEqual(5, player.Mp);

            ActionResult afterConvert = engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));

            Assert.IsTrue(afterConvert.Ok, afterConvert.Reason);
            Assert.AreEqual(90, enemy.Hp);
            Assert.AreEqual(0, player.Zones.Charges.Count);
            Assert.AreEqual(0, player.Mp);
        }

        [Test]
        public void 充能转MP回到上限为止且满蓝时照样消耗()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            // 只差 2 点就满：转换回 2 点而不是 5 点，多出来的不找零
            player.SpendMp(2);
            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("fuel"), player.Id));
            Assert.IsTrue(engine.Submit(new SpendChargeForMpAction(player.Id)).Ok);
            Assert.AreEqual(player.MaxMp, player.Mp);

            // 满蓝时不拒绝：玩家可能就是想清掉充能区（界面负责提示会浪费）
            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("fuel2"), player.Id));
            Assert.IsTrue(engine.Submit(new SpendChargeForMpAction(player.Id)).Ok);
            Assert.AreEqual(player.MaxMp, player.Mp);
            Assert.AreEqual(0, player.Zones.Charges.Count);
        }

        [Test]
        public void 充能转MP计入回复统计()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            player.SpendMp(player.Mp);
            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("fuel"), player.Id));
            Assert.IsTrue(engine.Submit(new SpendChargeForMpAction(player.Id)).Ok);

            // 走的是和卡牌 restore_mp 同一条路径，所以「全队累计回复过 15 MP」
            // 这类升变前置（星穹坍缩）认这 5 点
            Assert.AreEqual(BattleEngine.MpPerCharge, engine.State.Stats.Team(BattleStatKeys.MpRestored));
            Assert.AreEqual(
                BattleEngine.MpPerCharge, engine.State.Stats.For(player.Id, BattleStatKeys.MpRestored));
        }

        [Test]
        public void 没有充能时不能转MP()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            player.SpendMp(player.Mp);
            ActionResult result = engine.Submit(new SpendChargeForMpAction(player.Id));

            Assert.IsFalse(result.Ok);
            Assert.AreEqual(0, player.Mp, "被拒绝的动作不该回任何 MP");
        }

        [Test]
        public void 贴充能不耗行动力且每轮限一次()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            int apBefore = player.Ap;
            CardInstance first = player.Zones.Hand[0];
            CardInstance second = player.Zones.Hand[1];

            Assert.IsTrue(engine.Submit(new PlaceChargeAction(player.Id, first.Uid)).Ok);
            Assert.AreEqual(apBefore, player.Ap, "贴充能消耗 0 AP");
            Assert.AreEqual(1, player.Zones.Charges.Count);

            ActionResult again = engine.Submit(new PlaceChargeAction(player.Id, second.Uid));
            Assert.IsFalse(again.Ok, "每轮只能贴 1 次");
        }

        [Test]
        public void 充能可换取一点行动力()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            engine.Submit(new PlaceChargeAction(player.Id, player.Zones.Hand[0].Uid));
            int apBefore = player.Ap;

            Assert.IsTrue(engine.Submit(new SpendChargeForApAction(player.Id)).Ok);
            Assert.AreEqual(apBefore + 1, player.Ap);
        }

        [Test]
        public void 走位消耗一点行动力且每轮限一次()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            Assert.AreEqual(Row.Front, player.Row);
            Assert.IsTrue(engine.Submit(new MoveRowAction(player.Id)).Ok);
            Assert.AreEqual(Row.Back, player.Row);
            Assert.AreEqual(2, player.Ap);

            Assert.IsFalse(engine.Submit(new MoveRowAction(player.Id)).Ok, "每轮只能走位 1 次（裁定 R-16）");
        }

        [Test]
        public void 同排内换位同样消耗一点行动力且占掉本轮那一次()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            Assert.AreEqual((Row.Front, 0), (player.Row, player.Slot));

            // 同排内改变相对位置也是「移动 1 次」（裁定 R-17b）
            Assert.IsTrue(engine.Submit(new MoveRowAction(player.Id, Row.Front, 2)).Ok);
            Assert.AreEqual((Row.Front, 2), (player.Row, player.Slot));
            Assert.AreEqual(2, player.Ap);

            Assert.IsFalse(
                engine.Submit(new MoveRowAction(player.Id, Row.Back, 0)).Ok,
                "同排换位已经用掉本轮那一次走位");
        }

        [Test]
        public void 原地不动的走位会被拒绝且不扣行动力()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            int apBefore = player.Ap;
            Assert.IsFalse(engine.Submit(new MoveRowAction(player.Id, player.Row, player.Slot)).Ok);
            Assert.AreEqual(apBefore, player.Ap, "被拒绝的走位不该留下任何代价");
            Assert.IsFalse(player.HasMovedThisRound);
        }

        [Test]
        public void 限定换排的免费移动付不了同排换位的账()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            // 游击速射、翻滚披风卡面都写「在前后排之间移动 1 次」——
            // 额度带着这份限定，同排换位不在它的范围里（裁定 R-17b）
            engine.GrantFreeMove(player, 1, MoveRange.RowSwitch);
            player.Ap = 0;

            Assert.IsFalse(
                engine.Submit(new MoveRowAction(player.Id, Row.Front, 2)).Ok,
                "这一次额度只能用来换排");
            Assert.AreEqual(1, player.FreeMoves, "被拒绝的走位不该消耗额度");

            Assert.IsTrue(engine.Submit(new MoveRowAction(player.Id, Row.Back, 3)).Ok);
            Assert.AreEqual((Row.Back, 3), (player.Row, player.Slot), "换排时排内落点可以自选");
            Assert.AreEqual(0, player.FreeMoves);
            Assert.IsFalse(player.HasMovedThisRound, "免费移动不占本轮那一次常规走位");
        }

        [Test]
        public void 没限定的免费移动同排换位也付得起()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            // 「打出后移动 1 次」没说去哪——范围就该是最宽的，
            // 把它一律当成「只能换排」是对那三张卡的过度拟合
            engine.GrantFreeMove(player, 1, MoveRange.Any);
            player.Ap = 0;

            Assert.IsTrue(engine.Submit(new MoveRowAction(player.Id, Row.Front, 2)).Ok);
            Assert.AreEqual((Row.Front, 2), (player.Row, player.Slot));
            Assert.AreEqual(0, player.FreeMoves);
            Assert.IsFalse(player.HasMovedThisRound);
        }

        [Test]
        public void 同时攒着两种范围的免费移动时各走各的账()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            engine.GrantFreeMove(player, 1, MoveRange.RowSwitch);
            engine.GrantFreeMove(player, 1, MoveRange.Exactly(Row.Back, 0));
            player.Ap = 0;
            Assert.AreEqual(2, player.FreeMoves);

            // 同排换位两份额度都付不起
            Assert.IsFalse(engine.Submit(new MoveRowAction(player.Id, Row.Front, 1)).Ok);

            // 后排 2 号位只有「必须换排」那份付得起，「只能去后排 0 号位」那份不行
            Assert.IsTrue(engine.Submit(new MoveRowAction(player.Id, Row.Back, 2)).Ok);
            Assert.AreEqual(1, player.FreeMoves, "该消耗的是换排那一份");

            // 剩下那份限定到后排 0 号位，而人已经在后排了——它仍然付得起同排挪到 0 号位
            Assert.IsTrue(engine.Submit(new MoveRowAction(player.Id, Row.Back, 0)).Ok);
            Assert.AreEqual((Row.Back, 0), (player.Row, player.Slot));
            Assert.AreEqual(0, player.FreeMoves);
        }

        // ──────────────── 卡牌给的定向移动让玩家选位（裁定 R-17c） ────────────────

        /// <summary>
        /// 「移动 1 次，然后抽 1 张」。<paramref name="mustChangeRow"/> 对应卡面写没写
        /// 「在前后排之间」。抽牌排在移动**之后**，用来守住「挂起发生在结算中途」这件事。
        /// </summary>
        private static CardDefinition ChooseSlotMoveCard(bool mustChangeRow = true)
            => new("choose_move", "choose_move", CardCategory.Class, CardPlay.Skill)
            {
                ApCost = 0,
                Colors = new[] { AttributeColor.Dexterity },
                Effects = new[]
                {
                    new EffectData
                    {
                        Kind = EffectKind.MoveRow,
                        Target = TargetSelector.Self,
                        MustChangeRow = mustChangeRow,
                        PlayerChoosesSlot = true,
                    },
                    EffectData.Draw(1),
                },
            };

        [Test]
        public void 移动卡结算到选落点那条就停下来()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            int hand = player.Zones.Hand.Count;
            CardInstance card = TestFactory.AddToHand(player, ChooseSlotMoveCard());
            Assert.IsTrue(engine.Submit(new PlayCardAction(player.Id, card.Uid)).Ok);

            Assert.AreEqual(DecisionKind.ChooseSlot, engine.Pending.Kind);
            Assert.IsTrue(engine.Pending.SlotChoiceRange.MustChangeRow, "卡面写的是「在前后排之间移动」");
            CollectionAssert.Contains(engine.Pending.WaitingOn, player.Id);
            Assert.AreEqual(Row.Front, player.Row, "落点没选定之前人还没动");

            // 关键：停的是**结算中途**，所以排在移动后面的抽牌这会儿还没发生。
            // 要是挪到整张牌结算完之后才开窗口，这里就已经抽完了——
            // 「移动到目标敌人正前方后造成伤害」那类卡会按移动**前**的站位结算，结果是错的
            Assert.AreEqual(hand, player.Zones.Hand.Count, "后续效果必须等落点定下来");
            Assert.AreEqual(0, player.Zones.Discard.Count, "牌也还没进弃牌堆");

            Assert.IsTrue(engine.Submit(new ChooseSlotAction(player.Id, 3)).Ok);

            Assert.AreEqual((Row.Back, 3), (player.Row, player.Slot));
            Assert.AreEqual(hand + 1, player.Zones.Hand.Count, "选完之后剩下的效果接着结算");
            Assert.AreEqual(1, player.Zones.Discard.Count, "收尾也跑完了");
            Assert.AreEqual(3, player.Ap, "卡牌效果给的移动不花 AP");
            Assert.IsFalse(player.HasMovedThisRound, "也不占本轮那一次常规走位");
        }

        [Test]
        public void 卡面没限定落点时两排都能挑()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            CardInstance card = TestFactory.AddToHand(player, ChooseSlotMoveCard(mustChangeRow: false));
            engine.Submit(new PlayCardAction(player.Id, card.Uid));

            MoveRange range = engine.Pending.SlotChoiceRange;
            Assert.IsFalse(range.MustChangeRow);
            Assert.IsNull(range.Row, "「移动 1 次」没说去哪排，就不该替它限定");

            // 同排换位也在范围内
            Assert.IsTrue(engine.Submit(new ChooseSlotAction(player.Id, Row.Front, 2)).Ok);
            Assert.AreEqual((Row.Front, 2), (player.Row, player.Slot));
        }

        [Test]
        public void 限定换排时不能用选位窗口留在原排()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            CardInstance card = TestFactory.AddToHand(player, ChooseSlotMoveCard());
            engine.Submit(new PlayCardAction(player.Id, card.Uid));

            Assert.IsFalse(
                engine.Submit(new ChooseSlotAction(player.Id, Row.Front, 2)).Ok,
                "卡面写「在前后排之间移动」，留在原排不在范围内");
            Assert.AreEqual(DecisionKind.ChooseSlot, engine.Pending.Kind, "拒绝之后窗口还开着");
        }

        [Test]
        public void 选完落点后回到被打断的那个窗口()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            CardInstance card = TestFactory.AddToHand(player, ChooseSlotMoveCard());
            engine.Submit(new PlayCardAction(player.Id, card.Uid));
            engine.Submit(new ChooseSlotAction(player.Id, 1));

            // 还原的必须是原来那个 PendingDecision 对象：重建一个会把谁已经宣布完毕弄丢
            Assert.AreEqual(DecisionKind.PlayerActions, engine.Pending.Kind);
            CollectionAssert.Contains(engine.Pending.WaitingOn, player.Id);
            Assert.IsTrue(engine.Submit(new DeclareReadyAction(player.Id)).Ok);
        }

        [Test]
        public void 选位窗口里选非法落点会被拒绝()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            CardInstance card = TestFactory.AddToHand(player, ChooseSlotMoveCard());
            engine.Submit(new PlayCardAction(player.Id, card.Uid));

            Assert.IsFalse(engine.Submit(new ChooseSlotAction(player.Id, 9)).Ok, "槽位越界");
            Assert.AreEqual(DecisionKind.ChooseSlot, engine.Pending.Kind, "拒绝之后窗口还开着");
        }

        [Test]
        public void 选位窗口没关之前不能做别的事()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            CardInstance card = TestFactory.AddToHand(player, ChooseSlotMoveCard());
            engine.Submit(new PlayCardAction(player.Id, card.Uid));

            Assert.IsFalse(engine.Submit(new DeclareReadyAction(player.Id)).Ok);
            Assert.IsFalse(engine.Submit(new MoveRowAction(player.Id)).Ok);
        }

        [Test]
        public void 走位范围覆盖两排且与引擎受理口径一致()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            // 界面画高亮用的就是这个列表，所以亮出来的格子必须真的落得下去
            CollectionAssert.DoesNotContain(
                engine.AffordableMoveSlots(player, player.Row), player.Slot,
                "自己脚下那一格不是合法落点");

            Assert.IsNotEmpty(engine.AffordableMoveSlots(player, Row.Front));
            Assert.IsNotEmpty(engine.AffordableMoveSlots(player, Row.Back));

            player.Ap = 0;
            Assert.IsEmpty(engine.AffordableMoveSlots(player, Row.Back), "付不起就一格都不该亮");
        }

        [Test]
        public void 全员宣布完毕后进入敌人阶段并开启响应窗口()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup(intents: TestFactory.Attack(6));
            engine.Start();

            engine.Submit(new DeclareReadyAction(player.Id));

            Assert.AreEqual(RoundPhase.EnemyPhase, engine.State.Phase);
            Assert.AreEqual(DecisionKind.EnemyResponse, engine.Pending.Kind);
            Assert.AreSame(player, engine.CurrentIntentTargets.Single());
        }

        [Test]
        public void 响应窗口里能转MP但不能换AP()
        {
            // 响应牌也要花 MP（看破闪避 5 MP），所以转 MP 在响应窗口里必须可用；
            // 换 AP 不行——那 1 点 AP 轮末就清零了（裁定 R-10）
            (BattleEngine engine, PlayerCharacter player, _) = Setup(intents: TestFactory.Attack(6));
            engine.Start();

            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("fuel"), player.Id));
            engine.PlaceCharge(player, new CardInstance(TestFactory.FillerCard("fuel2"), player.Id));
            player.SpendMp(player.Mp);

            engine.Submit(new DeclareReadyAction(player.Id));
            Assert.AreEqual(DecisionKind.EnemyResponse, engine.Pending.Kind);

            Assert.IsFalse(engine.Submit(new SpendChargeForApAction(player.Id)).Ok, "响应窗口里不能换 AP");

            ActionResult mp = engine.Submit(new SpendChargeForMpAction(player.Id));
            Assert.IsTrue(mp.Ok, mp.Reason);
            Assert.AreEqual(BattleEngine.MpPerCharge, player.Mp);
        }

        /// <summary>
        /// 多人局才测得到的那一半：只有**全员**表态才推进。
        /// 单人局里「一个人宣布完毕」和「全员宣布完毕」是同一件事，
        /// 漏掉等待逻辑也照样绿，所以这两条必须用三人局守。
        /// </summary>
        [Test]
        public void 尚有队友未宣布时不进入敌人阶段()
        {
            (BattleEngine engine, PlayerCharacter[] party, _) = Party();

            engine.Submit(new DeclareReadyAction(party[0].Id));
            Assert.AreEqual(DecisionKind.PlayerActions, engine.Pending.Kind, "还有两人没表态");

            engine.Submit(new DeclareReadyAction(party[1].Id));
            Assert.AreEqual(DecisionKind.PlayerActions, engine.Pending.Kind, "还有一人没表态");

            engine.Submit(new DeclareReadyAction(party[2].Id));
            Assert.AreEqual(DecisionKind.EnemyResponse, engine.Pending.Kind, "全员表态后才进敌人阶段");
        }

        [Test]
        public void 已宣布完毕的角色不能再出牌()
        {
            (BattleEngine engine, PlayerCharacter[] party, Enemy enemy) = Party();

            CardInstance card = TestFactory.AddToHand(party[0], TestFactory.StrikeCard());
            engine.Submit(new DeclareReadyAction(party[0].Id));

            ActionResult result = engine.Submit(new PlayCardAction(party[0].Id, card.Uid, enemy.Id));

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("已宣布行动完毕", result.Reason);
            // 队友还没表态，局面仍停在玩家阶段——被拒绝的动作没把流程推歪
            Assert.AreEqual(DecisionKind.PlayerActions, engine.Pending.Kind);
        }

        /// <summary>三人小队 + 一只木桩，已 Start。</summary>
        private static (BattleEngine engine, PlayerCharacter[] party, Enemy enemy) Party()
        {
            PlayerCharacter[] party =
            {
                TestFactory.Player("tank", TestFactory.Sheet("护卫")),
                TestFactory.Player("archer", TestFactory.Sheet("弓手")),
                TestFactory.Player("mage", TestFactory.Sheet("术士")),
            };

            foreach (PlayerCharacter member in party) TestFactory.FillDeck(member, 20);

            Enemy enemy = TestFactory.Enemy(hp: 200, intents: new[] { TestFactory.Attack(6) });

            BattleState state = BattleSetup.Create(
                new XorShift32Rng(99),
                new[] { (party[0], Row.Front), (party[1], Row.Back), (party[2], Row.Back) },
                new[] { (enemy, Row.Front) });

            var engine = new BattleEngine(state);
            engine.Start();
            return (engine, party, enemy);
        }

        [Test]
        public void 响应窗口全体放弃后意图结算()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup(intents: TestFactory.Attack(6));
            engine.Start();

            int hpBefore = player.Hp;
            int shieldBefore = player.Shields.Physical;

            engine.Submit(new DeclareReadyAction(player.Id));
            engine.Submit(new PassResponseAction(player.Id));

            // 6 点物理伤害先被自动物理盾吃掉
            Assert.AreEqual(hpBefore - System.Math.Max(0, 6 - shieldBefore), player.Hp);
        }

        [Test]
        public void 响应窗口不接受技能牌()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup(intents: TestFactory.Attack(6));
            engine.Start();

            CardInstance skill = TestFactory.AddToHand(player, TestFactory.StrikeCard());
            engine.Submit(new DeclareReadyAction(player.Id));

            ActionResult result = engine.Submit(new PlayCardAction(player.Id, skill.Uid, enemy.Id));
            Assert.IsFalse(result.Ok);
            StringAssert.Contains("时机牌", result.Reason);
        }

        [Test]
        public void 响应窗口可以打出防具牌加盾()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup(intents: TestFactory.Attack(10));
            engine.Start();

            CardDefinition guard = new("guard", "格挡", CardCategory.Armor, CardPlay.Timing)
            {
                ApCost = 0,
                Effects = new[] { EffectData.Shield(ValueExpr.Flat(8), ShieldKind.Physical, TargetScope.Self) },
            };

            CardInstance card = TestFactory.AddToHand(player, guard);
            engine.Submit(new DeclareReadyAction(player.Id));

            ActionResult result = engine.Submit(new PlayCardAction(player.Id, card.Uid));
            Assert.IsTrue(result.Ok, result.Reason);
            Assert.AreEqual(8, player.Shields.GrantedPhysical);
        }

        [Test]
        public void 轮末场景命刻加一()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            Assert.AreEqual(0, engine.State.SceneClock);
            AdvanceOneRound(engine, player);
            Assert.AreEqual(1, engine.State.SceneClock);
        }

        [Test]
        public void 轮末手牌超过七张需要弃置()
        {
            (BattleEngine engine, PlayerCharacter player, _) = Setup();
            engine.Start();

            // 第 1 轮 5 张不触发，走到第 2 轮补成 8 张
            AdvanceOneRound(engine, player);
            Assert.AreEqual(8, player.Zones.Hand.Count);

            engine.Submit(new DeclareReadyAction(player.Id));
            engine.Submit(new PassResponseAction(player.Id));

            Assert.AreEqual(DecisionKind.HandLimitDiscard, engine.Pending.Kind);
            Assert.AreEqual(1, engine.RequiredDiscards(player));

            ActionResult wrongCount = engine.Submit(
                new DiscardToHandLimitAction(player.Id, new[] { player.Zones.Hand[0].Uid, player.Zones.Hand[1].Uid }));
            Assert.IsFalse(wrongCount.Ok, "弃牌张数必须正好等于超出的张数");

            Assert.IsTrue(engine.Submit(
                new DiscardToHandLimitAction(player.Id, new[] { player.Zones.Hand[0].Uid })).Ok);

            Assert.AreEqual(3, engine.State.Round);
            Assert.AreEqual(BattleEngine.HandLimit + 3, player.Zones.Hand.Count, "弃到 7 张后第 3 轮再补 3 张");
        }

        [Test]
        public void 击杀全部敌人即胜利()
        {
            (BattleEngine engine, PlayerCharacter player, Enemy enemy) = Setup();
            engine.Start();

            CardInstance card = TestFactory.AddToHand(player, TestFactory.StrikeCard(damage: 999));
            engine.Submit(new PlayCardAction(player.Id, card.Uid, enemy.Id));

            Assert.AreEqual(BattleOutcome.Victory, engine.State.Outcome);
        }

        [Test]
        public void 战斗入场不回满HP与MP()
        {
            CharacterSheet sheet = TestFactory.Sheet();
            PlayerCharacter player = TestFactory.Player(sheet: sheet, hp: 12, mp: 3);
            TestFactory.FillDeck(player, 20);

            (BattleEngine engine, _, _) = TestFactory.Duel(player: player);
            engine.Start();

            Assert.AreEqual(12, player.Hp, "按跑团剩余 HP 入场（§1）");
            Assert.AreEqual(3, player.Mp);
        }

        /// <summary>把当前轮推完，停在下一轮的玩家阶段。</summary>
        private static void AdvanceOneRound(BattleEngine engine, PlayerCharacter player)
        {
            engine.Submit(new DeclareReadyAction(player.Id));

            while (engine.Pending.Kind == DecisionKind.EnemyResponse)
            {
                engine.Submit(new PassResponseAction(player.Id));
            }

            if (engine.Pending.Kind == DecisionKind.HandLimitDiscard)
            {
                int required = engine.RequiredDiscards(player);
                engine.Submit(new DiscardToHandLimitAction(
                    player.Id,
                    player.Zones.Hand.Take(required).Select(c => c.Uid).ToList()));
            }
        }
    }
}
