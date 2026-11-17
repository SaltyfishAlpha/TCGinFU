using System;
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
                MoveRowAction _ => HandleMoveRow(actor),
                DeclareReadyAction _ => HandleDeclareReady(actor),
                DeclareTransformAction a => HandleDeclareTransform(actor, a),
                DeclineTransformAction _ => HandleDeclineTransform(actor),
                ShuffleBackTransformAction a => HandleShuffleBackTransform(actor, a),
                PassResponseAction _ => HandlePassResponse(actor),
                DiscardToHandLimitAction a => HandleHandLimitDiscard(actor, a),
                _ => ActionResult.Fail($"未知动作 {action.GetType().Name}"),
            };
        }

        // ──────────────────────────────── 打牌 ────────────────────────────────

        private ActionResult HandlePlayCard(PlayerCharacter actor, PlayCardAction action)
        {
            bool inResponseWindow = Pending.Kind == DecisionKind.EnemyResponse;
            if (!inResponseWindow && Pending.Kind != DecisionKind.PlayerActions)
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

            if (def.Kind == CardKind.Transform)
            {
                return ActionResult.Fail("升变牌须通过宣告升变使用");
            }

            // 响应窗口只接受时机牌与防具牌（§4.3 / §8 ④）
            if (inResponseWindow && !def.IsReactive)
            {
                return ActionResult.Fail("响应窗口只能打出时机牌或防具牌");
            }

            // 裁定 R-18：响应窗口打牌同样从本轮剩余 AP 里付费
            if (actor.Ap < def.ApCost)
            {
                return ActionResult.Fail($"行动力不足（需 {def.ApCost}，剩 {actor.Ap}）");
            }

            if (action.ChargeForMp < 0) return ActionResult.Fail("充能抵扣数不能为负");

            int chargesNeeded = action.ChargeForMp + def.ChargeCost;
            if (actor.Zones.Charges.Count < chargesNeeded)
            {
                return ActionResult.Fail($"充能不足（需 {chargesNeeded}，有 {actor.Zones.Charges.Count}）");
            }

            // 裁定 R-09：每个充能固定抵 5 点 MP，多余不找零
            int mpNeeded = Math.Max(0, def.MpCost - action.ChargeForMp * 5);
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
            };

            ICardBehaviour behaviour = Behaviours.Resolve(def);
            if (behaviour != null && !behaviour.CanPlay(ctx, out string reason))
            {
                return ActionResult.Fail(reason ?? "不满足卡面前置条件");
            }

            // ──── 校验结束，开始改状态 ────

            actor.Zones.Hand.Remove(card);
            actor.Ap -= def.ApCost;

            for (int i = 0; i < action.ChargeForMp; i++) SpendCharge(actor, ChargeUse.ConvertToMp);
            for (int i = 0; i < def.ChargeCost; i++) SpendCharge(actor, ChargeUse.PayCardCost);

            actor.SpendMp(mpNeeded);

            if (def.HasCrit)
            {
                ctx.Crit = CritResolver.Roll(State.Rng, actor.Sheet.Dice, def.Colors, CritResistFor(chosenTarget));
            }

            Emit(new BattleEvent(BattleEventType.CardPlayed, actor.Id, chosenTarget?.Id)
            {
                CardId = def.Id,
                Text = def.Name,
                Round = State.Round,
            });

            behaviour?.OnPlay(ctx);
            Effects.ApplyAll(ctx, def.Effects);
            behaviour?.OnResolved(ctx);

            SendToRestingPlace(actor, card);

            State.CheckOutcome();
            return ActionResult.Success;
        }

        /// <summary>卡牌结算完后的归宿：持续区 / 形态弃牌堆 / 弃牌堆。本规则没有除外区。</summary>
        private void SendToRestingPlace(PlayerCharacter actor, CardInstance card)
        {
            if (card.Definition.Kind == CardKind.Continuous)
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

            bool needsEnemy = def.Effects.Any(e => e.Target.Scope == TargetScope.SingleEnemy);
            bool needsAlly = def.Effects.Any(e => e.Target.Scope == TargetScope.SingleAlly);
            if (!needsEnemy && !needsAlly) return ActionResult.Success;

            if (string.IsNullOrEmpty(targetId)) return ActionResult.Fail("该牌需要指定目标");

            Combatant found = State.Find(targetId);
            if (found == null) return ActionResult.Fail($"找不到目标 {targetId}");
            if (found.IsDown) return ActionResult.Fail("目标已倒下");

            if (needsEnemy && found is not Enemy) return ActionResult.Fail("该牌须指定敌人为目标");
            if (needsAlly && found is not PlayerCharacter) return ActionResult.Fail("该牌须指定队友为目标");

            target = found;
            return ActionResult.Success;
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

        private ActionResult HandlePlaceCharge(PlayerCharacter actor, PlaceChargeAction action)
        {
            if (Pending.Kind != DecisionKind.PlayerActions) return ActionResult.Fail("贴充能只能在玩家阶段");
            if (!Pending.WaitingOn.Contains(actor.Id)) return ActionResult.Fail("已宣布行动完毕");
            if (actor.HasPlacedChargeThisRound) return ActionResult.Fail("本轮已贴过充能");

            CardInstance card = actor.Zones.Hand.Find(action.CardUid);
            if (card == null) return ActionResult.Fail("该牌不在手牌中");

            PlayerCharacter zoneOwner = State.FindPlayer(action.ChargeZoneOwnerId);
            if (zoneOwner == null) return ActionResult.Fail($"找不到充能区主人 {action.ChargeZoneOwnerId}");
            if (zoneOwner.IsDown) return ActionResult.Fail("不能贴给已倒下的角色");

            actor.Zones.Hand.Remove(card);
            actor.HasPlacedChargeThisRound = true;
            PlaceCharge(zoneOwner, card);

            return ActionResult.Success;
        }

        private ActionResult HandleSpendChargeForAp(PlayerCharacter actor)
        {
            if (Pending.Kind != DecisionKind.PlayerActions) return ActionResult.Fail("只能在玩家阶段换取行动力");
            if (!Pending.WaitingOn.Contains(actor.Id)) return ActionResult.Fail("已宣布行动完毕");
            if (actor.Zones.Charges.Count == 0) return ActionResult.Fail("没有可消耗的充能");

            SpendCharge(actor, ChargeUse.ConvertToAp);
            GrantAp(actor, 1);
            return ActionResult.Success;
        }

        // ──────────────────────────────── 走位 ────────────────────────────────

        private ActionResult HandleMoveRow(PlayerCharacter actor)
        {
            if (Pending.Kind != DecisionKind.PlayerActions) return ActionResult.Fail("走位只能在玩家阶段");
            if (!Pending.WaitingOn.Contains(actor.Id)) return ActionResult.Fail("已宣布行动完毕");
            if (actor.HasMovedThisRound) return ActionResult.Fail("本轮已经走过位");
            if (actor.Ap < 1) return ActionResult.Fail("行动力不足");

            Row from = actor.Row;
            if (!SwitchRow(actor, free: false)) return ActionResult.Fail($"{(from == Row.Front ? "后" : "前")}排已满");

            actor.Ap -= 1;
            return ActionResult.Success;
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
