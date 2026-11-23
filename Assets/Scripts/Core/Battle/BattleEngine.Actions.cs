using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 动作的受理与校验。所有校验都在**改状态之前**完成——
    /// 被拒绝的动作不会留下任何半成品状态，UI 可以放心乐观提交。
    /// </summary>
    public sealed partial class BattleEngine
    {
        public ActionResult Submit(IBattleAction action)
        {
            if (action == null) return ActionResult.Fail("动作为空");
            if (State.Phase == RoundPhase.Finished) return ActionResult.Fail("战斗已结束");

            PlayerCharacter actor = State.FindPlayer(action.ActorId);
            if (actor == null) return ActionResult.Fail($"找不到角色 {action.ActorId}");
            if (actor.IsDown) return ActionResult.Fail($"{actor.Name} 已倒下");

            return action switch
            {
                PlayCardAction a => HandlePlayCard(actor, a),
                PlaceChargeAction a => HandlePlaceCharge(actor, a),
                SpendChargeForApAction _ => HandleSpendChargeForAp(actor),
                SpendChargeForMpAction _ => HandleSpendChargeForMp(actor),
                MoveRowAction move => HandleMoveRow(actor, move),
                ChooseSlotAction a => HandleChooseSlot(actor, a),
                DeclareReadyAction _ => HandleDeclareReady(actor),
                DeclareTransformAction a => HandleDeclareTransform(actor, a),
                DeclineTransformAction _ => HandleDeclineTransform(actor),
                ShuffleBackTransformAction a => HandleShuffleBackTransform(actor, a),
                ShareDamageAction a => HandleShareDamage(actor, a),
                PassResponseAction _ => HandlePassResponse(actor),
                DiscardToHandLimitAction a => HandleHandLimitDiscard(actor, a),
                _ => ActionResult.Fail($"未知动作 {action.GetType().Name}"),
            };
        }

        // ──────────────────────────────── 打牌 ────────────────────────────────

        private ActionResult HandlePlayCard(PlayerCharacter actor, PlayCardAction action)
        {
            bool inResponseWindow = Pending.Kind == DecisionKind.EnemyResponse;
            bool inTransformWindow = Pending.Kind == DecisionKind.TransformDeclaration;

            if (!inResponseWindow && !inTransformWindow && Pending.Kind != DecisionKind.PlayerActions)
            {
                return ActionResult.Fail("当前不是出牌时机");
            }

            if (!Pending.WaitingOn.Contains(actor.Id))
            {
                return ActionResult.Fail(inResponseWindow ? "已放弃响应" : "已宣布行动完毕");
            }

            CardInstance card = actor.Zones.Hand.Find(action.CardUid);
            if (card == null) return ActionResult.Fail("该牌不在手牌中");

            CardDefinition def = card.Definition;

            // 升变牌「打出」就等于宣告升变——对玩家来说它和别的牌一样是从手里打出去的，
            // 不该因为内部走的是另一条动作而需要另一套操作。
            // 真正的升变流程仍然只有 HandleDeclareTransform 一条路径。
            if (def.Role == CardRole.Transform)
            {
                return inTransformWindow
                    ? HandleDeclareTransform(actor, new DeclareTransformAction(actor.Id, card.Uid))
                    : ActionResult.Fail("升变只能在轮始的升变窗口发动");
            }

            // 升变窗口里除了升变牌什么都不能打
            if (inTransformWindow) return ActionResult.Fail("当前只能发动升变");

            // 响应窗口只接受时机牌，与类别无关（§4.3 / §8 ④）：
            // 「防具 · 技能」的战术隐蔽只能在玩家阶段打，「形态 · 时机」的烟幕替身照样能响应
            if (inResponseWindow && def.Play != CardPlay.Timing)
            {
                return ActionResult.Fail("响应窗口只能打出时机牌");
            }

            // 卡面费用只是基准值，「第1轮免除5MP」这类词条在这里生效
            CostContext cost = ResolveCost(actor, def);

            // 裁定 R-18：响应窗口打牌同样从本轮剩余 AP 里付费
            if (actor.Ap < cost.Ap)
            {
                return ActionResult.Fail($"行动力不足（需 {cost.Ap}，剩 {actor.Ap}）");
            }

            if (def.RequiresBackRow && actor.Row != Row.Back)
            {
                return ActionResult.Fail("这张牌仅限在后排打出");
            }

            if (action.ChargeForEffect < 0) return ActionResult.Fail("额外消耗的充能数不能为负");

            // 卡面没声明可选消耗就只能填 0——否则玩家白扔充能，而脚本根本不读这个数
            if (action.ChargeForEffect > 0 && !def.ChargeOptions.Contains(action.ChargeForEffect))
            {
                return ActionResult.Fail(def.ChargeOptions.Count == 0
                    ? $"「{def.Name}」没有可选的充能消耗"
                    : $"「{def.Name}」的可选消耗只有 {string.Join(" / ", def.ChargeOptions)} 个充能");
            }

            int chargesNeeded = action.ChargeForEffect + cost.Charge;
            if (actor.Zones.Charges.Count < chargesNeeded)
            {
                return ActionResult.Fail($"充能不足（需 {chargesNeeded}，有 {actor.Zones.Charges.Count}）");
            }

            // 充能不再抵 MP（旧裁定 R-09 已作废）：要换 MP 先单独提交 SpendChargeForMpAction
            int mpNeeded = cost.Mp;
            if (actor.Mp < mpNeeded)
            {
                return ActionResult.Fail($"MP 不足（需 {mpNeeded}，有 {actor.Mp}）");
            }

            ActionResult targeting = ResolveExplicitTarget(actor, def, action.TargetId, out Combatant chosenTarget);
            if (!targeting.Ok) return targeting;

            var ctx = new EffectContext(this, actor, card)
            {
                ChosenTarget = chosenTarget,
                CurrentTarget = PickCoverTarget(action.TargetId),
                ChargeSpent = chargesNeeded,
                Choices = action.Choices,
            };

            ICardBehaviour behaviour = Behaviours.Resolve(def);
            if (behaviour != null)
            {
                // CanPlay 抛异常时按「不可打出」处理：mod 卡的前置条件写崩了，
                // 宁可这张卡打不出，也不能让它绕过校验把状态改到一半
                bool allowed = true;
                string reason = null;
                Invoke(behaviour, nameof(ICardBehaviour.CanPlay), () => allowed = behaviour.CanPlay(ctx, out reason));
                if (!allowed) return ActionResult.Fail(reason ?? "不满足卡面前置条件");
            }

            // ──── 校验结束，开始改状态 ────

            actor.Zones.Hand.Remove(card);
            actor.Ap -= cost.Ap;

            // 卡面硬费用与玩家额外加的，都是同一种用途：付给这张牌的词条
            for (int i = 0; i < chargesNeeded; i++) SpendCharge(actor, ChargeUse.PayCardCost);

            actor.SpendMp(mpNeeded);

            if (def.HasCrit)
            {
                ctx.Crit = RollCrit(
                    actor, def, chosenTarget ?? State.LivingEnemies.FirstOrDefault(), chargesNeeded);
            }

            State.Stats.Add(BattleStatKeys.CardsPlayed, actor.Id);

            // 「若本轮已打出过**其他**牌」（瞬影突袭）读的是这个计数，
            // 所以必须在本张牌记进去**之前**被行为层看到——加在 OnPlay/OnResolved 之后
            int playedBefore = actor.CardsPlayedThisRound;
            actor.CardsPlayedThisRound++;
            ctx.CardsPlayedBefore = playedBefore;
            Emit(new BattleEvent(BattleEventType.CardPlayed, actor.Id, chosenTarget?.Id)
            {
                CardId = def.Id,
                Card = def,
                Text = def.Name,
                Round = State.Round,
            });

            if (behaviour != null) Invoke(behaviour, nameof(ICardBehaviour.OnPlay), () => behaviour.OnPlay(ctx));
            Effects.ApplyAll(ctx, def.Effects);
            if (behaviour != null) Invoke(behaviour, nameof(ICardBehaviour.OnResolved), () => behaviour.OnResolved(ctx));

            // 行为层可能已经把这张牌安置到别处了（「顺风充能」把自己变成充能指示物）
            if (!ctx.CardRehomed) SendToRestingPlace(actor, card);

            FireTrigger(actor, BattleTrigger.CardPlayed, new TriggerSignal { Card = card });

            State.CheckOutcome();

            // 这张牌要求玩家挑落点的话，窗口开在**整张牌结算完之后**（裁定 R-17c）
            BeginSlotChoiceIfAny();
            return ActionResult.Success;
        }

        /// <summary>
        /// 卡牌结算完后的归宿：持续区 / 形态弃牌堆 / 弃牌堆。本规则没有除外区。
        /// <para>先看打法再看标记：形态牌里若有持续牌，它该进持续区而不是形态弃牌堆。</para>
        /// </summary>
        private void SendToRestingPlace(PlayerCharacter actor, CardInstance card)
        {
            if (card.Definition.Play == CardPlay.Continuous)
            {
                actor.Zones.SetContinuous(card, out CardInstance replaced);
                if (replaced != null)
                {
                    actor.Zones.Discard.AddTop(replaced);
                    Emit(new BattleEvent(BattleEventType.ContinuousReplaced, actor.Id)
                    {
                        CardId = replaced.Definition.Id,
                        Round = State.Round,
                    });
                }

                return;
            }

            if (card.IsFormCard) actor.Zones.FormDiscard.AddTop(card);
            else actor.Zones.Discard.AddTop(card);
        }

        private ActionResult ResolveExplicitTarget(
            PlayerCharacter actor,
            CardDefinition def,
            string targetId,
            out Combatant target)
        {
            target = null;

            // 「指向谁」以卡面声明为准，推导只是缺省值。
            // 从 Effects 反推会漏掉目标只给行为层用的卡（「贴入全队任意角色的充能区」）——
            // 那些卡的数据效果里一条 SingleAlly 都没有，于是玩家点的队友被整个丢掉
            CardTargeting mode = def.ResolvedTargeting;
            if (mode == CardTargeting.None) return ActionResult.Success;

            if (string.IsNullOrEmpty(targetId))
            {
                // 行为层驱动的「自己或任意队友」不选就是自己，不该因此打不出去
                return def.RequiresExplicitTarget
                    ? ActionResult.Fail("该牌需要指定目标")
                    : ActionResult.Success;
            }

            Combatant found = State.Find(targetId);
            if (found == null) return ActionResult.Fail($"找不到目标 {targetId}");
            if (found.IsDown) return ActionResult.Fail("目标已倒下");

            if (mode == CardTargeting.Enemy && found is not Enemy)
            {
                return ActionResult.Fail("该牌须指定敌人为目标");
            }

            if (mode == CardTargeting.Ally && found is not PlayerCharacter)
            {
                return ActionResult.Fail("该牌须指定队友为目标");
            }

            target = found;
            return ActionResult.Success;
        }

        /// <summary>
        /// 站出来分摊一次攻击（B-14）。不花 AP、不打牌，所以**不**顺带视为放弃响应——
        /// 分摊完还可以接着打时机牌，最后再 <see cref="PassResponseAction"/>。
        /// </summary>
        private ActionResult HandleShareDamage(PlayerCharacter actor, ShareDamageAction action)
        {
            if (Pending.Kind != DecisionKind.EnemyResponse) return ActionResult.Fail("当前不是响应窗口");
            if (!Pending.WaitingOn.Contains(actor.Id)) return ActionResult.Fail("已放弃响应");

            Combatant target = PickCoverTarget(action.TargetId);
            if (target == null) return ActionResult.Fail("本次意图没有目标");

            return RegisterDamageShare(actor, target, out string reason)
                ? ActionResult.Success
                : ActionResult.Fail(reason);
        }

        /// <summary>响应窗口里援护的「既定目标」：优先玩家指定的那个，否则取意图的首个目标。</summary>
        private Combatant PickCoverTarget(string targetId)
        {
            if (_currentIntentTargets.Count == 0) return null;
            if (string.IsNullOrEmpty(targetId)) return _currentIntentTargets[0];

            return _currentIntentTargets.FirstOrDefault(t => t.Id == targetId) ?? _currentIntentTargets[0];
        }

        private int CritResistFor(Combatant chosenTarget)
        {
            if (chosenTarget != null) return chosenTarget.CritResist;
            Enemy first = State.LivingEnemies.FirstOrDefault();
            return first?.CritResist ?? 0;
        }

        // ──────────────────────────────── 充能 ────────────────────────────────

        /// <summary>1 个充能换多少 MP（§5.2）。回到上限为止，多出来的部分不找零。</summary>
        public const int MpPerCharge = 5;

        private ActionResult HandlePlaceCharge(PlayerCharacter actor, PlaceChargeAction action)
        {
            if (Pending.Kind != DecisionKind.PlayerActions) return ActionResult.Fail("贴充能只能在玩家阶段");
            if (!Pending.WaitingOn.Contains(actor.Id)) return ActionResult.Fail("已宣布行动完毕");
            // 常规的那一次用完后，才去动卡面额外给的次数
            if (actor.HasPlacedChargeThisRound && actor.ExtraChargePlacements <= 0)
            {
                return ActionResult.Fail("本轮已贴过充能");
            }

            CardInstance card = actor.Zones.Hand.Find(action.CardUid);
            if (card == null) return ActionResult.Fail("该牌不在手牌中");

            PlayerCharacter zoneOwner = State.FindPlayer(action.ChargeZoneOwnerId);
            if (zoneOwner == null) return ActionResult.Fail($"找不到充能区主人 {action.ChargeZoneOwnerId}");
            if (zoneOwner.IsDown) return ActionResult.Fail("不能贴给已倒下的角色");

            actor.Zones.Hand.Remove(card);

            if (actor.HasPlacedChargeThisRound) actor.ExtraChargePlacements--;
            else actor.HasPlacedChargeThisRound = true;

            PlaceCharge(zoneOwner, card);

            return ActionResult.Success;
        }

        private ActionResult HandleSpendChargeForAp(PlayerCharacter actor)
        {
            if (!CanSpendChargeFor(actor, ChargeUse.ConvertToAp, out string reason))
            {
                return ActionResult.Fail(reason);
            }

            SpendCharge(actor, ChargeUse.ConvertToAp);
            GrantAp(actor, 1);
            return ActionResult.Success;
        }

        private ActionResult HandleSpendChargeForMp(PlayerCharacter actor)
        {
            if (!CanSpendChargeFor(actor, ChargeUse.ConvertToMp, out string reason))
            {
                return ActionResult.Fail(reason);
            }

            SpendCharge(actor, ChargeUse.ConvertToMp);

            // 走和卡牌 restore_mp 同一条路径，所以「回复 MP 时」类钩子与回复统计
            // （星穹坍缩的「全队累计回复过 15 MP」）都认这 5 点
            RestoreMp(actor, MpPerCharge);
            return ActionResult.Success;
        }

        /// <summary>
        /// 此刻这名角色能不能把充能用在某个用途上。界面拿它决定菜单项亮不亮、灰掉时写什么理由——
        /// 时机与数量**一概问引擎**，表现层各判一份迟早会分家（表现出来就是「菜单里有、点下去却被拒」）。
        ///
        /// <para><see cref="ChargeUse.PayCardCost"/> 不走这里：它跟着具体某张牌，由
        /// <see cref="HandlePlayCard"/> 连同卡面的 <see cref="CardDefinition.ChargeOptions"/> 一起校验。</para>
        /// </summary>
        public bool CanSpendChargeFor(PlayerCharacter actor, ChargeUse use, out string reason)
        {
            reason = null;
            if (actor == null) { reason = "没有这名角色"; return false; }
            if (actor.IsDown) { reason = $"{actor.Name} 已倒下"; return false; }

            bool inPlayerPhase = Pending.Kind == DecisionKind.PlayerActions;
            bool inResponse = Pending.Kind == DecisionKind.EnemyResponse;

            switch (use)
            {
                case ChargeUse.ConvertToAp when !inPlayerPhase:
                    reason = "只能在玩家阶段换取行动力";
                    return false;

                // 响应牌也要花 MP（看破闪避 5 MP），所以转 MP 在响应窗口里必须可用
                case ChargeUse.ConvertToMp when !inPlayerPhase && !inResponse:
                    reason = "当前不是可以消耗充能的时机";
                    return false;

                case ChargeUse.PayCardCost:
                    reason = "支付卡面词条的充能跟着那张牌一起提交";
                    return false;
            }

            if (!Pending.WaitingOn.Contains(actor.Id))
            {
                reason = inResponse ? "已放弃响应" : "已宣布行动完毕";
                return false;
            }

            if (actor.Zones.Charges.Count == 0) { reason = "没有可消耗的充能"; return false; }

            return true;
        }

        // ──────────────────────────────── 走位 ────────────────────────────────

        private ActionResult HandleMoveRow(PlayerCharacter actor, MoveRowAction action)
        {
            Row from = actor.Row;
            Row target = action.TargetRow ?? Opposite(from);

            if (!CanPayForMove(actor, target, out bool useFree, out string reason))
            {
                return ActionResult.Fail(reason);
            }

            // 没指定落点就沿用老行为：切到对排、自动选同列
            bool moved = action.TargetSlot.HasValue
                ? MoveRow(actor, target, action.TargetSlot.Value, free: useFree)
                : MoveRow(actor, target, free: useFree);

            if (!moved)
            {
                return ActionResult.Fail(target == from
                    ? "原地不动算不上移动"
                    : $"{(target == Row.Front ? "前" : "后")}排已满");
            }

            if (useFree) actor.FreeMoves--;
            else actor.Ap -= 1;

            return ActionResult.Success;
        }

        /// <summary>
        /// 这次走位付得起吗，以及付的是什么。界面画可选范围、引擎受理动作都问这里，
        /// 免得「拖得动却放不下去」。
        ///
        /// <para>攒着的免费移动**只能用来换排**：给出它的那几张卡（游击速射、翻滚披风）
        /// 卡面都写的是「在前后排之间移动 1 次」。同排内改变相对位置不在那句话的范围里，
        /// 只能走常规的 1 AP 走位（裁定 R-17b）。</para>
        /// </summary>
        public bool CanPayForMove(PlayerCharacter actor, Row target, out bool useFree, out string reason)
        {
            useFree = false;
            reason = null;

            if (actor == null) { reason = "没有这名角色"; return false; }
            if (actor.IsDown) { reason = $"{actor.Name} 已倒下"; return false; }

            useFree = target != actor.Row && actor.FreeMoves > 0;

            // 攒着的免费移动在响应窗口里也能兑现——「翻滚披风」给的那次机动，
            // 本来就是为了在敌人落手之前挪开
            bool timingOk = Pending.Kind == DecisionKind.PlayerActions
                            || (useFree && Pending.Kind == DecisionKind.EnemyResponse);

            if (!timingOk) { reason = "走位只能在玩家阶段"; return false; }
            if (!Pending.WaitingOn.Contains(actor.Id)) { reason = "已宣布行动完毕"; return false; }

            if (!useFree)
            {
                if (actor.HasMovedThisRound) { reason = "本轮已经走过位"; return false; }
                if (actor.Ap < 1) { reason = "行动力不足"; return false; }
            }

            return true;
        }

        // ──────────────────────────────── 阶段表态 ────────────────────────────────

        private ActionResult HandleDeclareReady(PlayerCharacter actor)
        {
            if (Pending.Kind != DecisionKind.PlayerActions) return ActionResult.Fail("当前不是玩家阶段");
            if (!Pending.WaitingOn.Remove(actor.Id)) return ActionResult.Fail("已宣布行动完毕");

            actor.IsReady = true;
            if (Pending.IsResolved) ProceedToEnemyPhase();
            return ActionResult.Success;
        }

        private ActionResult HandlePassResponse(PlayerCharacter actor)
        {
            if (Pending.Kind != DecisionKind.EnemyResponse) return ActionResult.Fail("当前不是响应窗口");
            if (!Pending.WaitingOn.Remove(actor.Id)) return ActionResult.Fail("已放弃响应");

            if (Pending.IsResolved) ResolveCurrentIntentAndContinue();
            return ActionResult.Success;
        }

        private ActionResult HandleHandLimitDiscard(PlayerCharacter actor, DiscardToHandLimitAction action)
        {
            if (Pending.Kind != DecisionKind.HandLimitDiscard) return ActionResult.Fail("当前不是轮末弃牌时机");
            if (!Pending.WaitingOn.Contains(actor.Id)) return ActionResult.Fail("无需弃牌");

            int required = RequiredDiscards(actor);
            if (action.CardUids.Count != required)
            {
                return ActionResult.Fail($"需要弃置 {required} 张，收到 {action.CardUids.Count} 张");
            }

            var cards = new List<CardInstance>(required);
            foreach (int uid in action.CardUids)
            {
                CardInstance card = actor.Zones.Hand.Find(uid);
                if (card == null) return ActionResult.Fail($"手牌中没有 Uid={uid} 的牌");
                if (cards.Contains(card)) return ActionResult.Fail("弃牌列表中有重复的牌");
                cards.Add(card);
            }

            foreach (CardInstance card in cards)
            {
                actor.Zones.Hand.Remove(card);
                // 升变期间手牌里是形态牌，弃置后也必须留在形态弃牌堆，不能混进主卡组弃牌堆
                ReturnToOwnerDiscard(card);
                Emit(new BattleEvent(BattleEventType.HandLimitDiscard, actor.Id)
                {
                    CardId = card.Definition.Id,
                    Round = State.Round,
                });
            }

            Pending.WaitingOn.Remove(actor.Id);
            if (Pending.IsResolved) FinishRound();
            return ActionResult.Success;
        }
    }
}
