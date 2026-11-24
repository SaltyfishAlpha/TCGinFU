using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>升变机制（§6）。</summary>
    public sealed partial class BattleEngine
    {
        /// <summary>第 3 轮保底跳出只在这一轮提供（§6.1，裁定 R-13）。</summary>
        public const int GuaranteeRound = 3;

        /// <summary>
        /// 该角色此刻可以用来发动升变的牌：
        /// 手牌中的即发型牌、条件已达成的条件型牌，以及第 3 轮保底时主卡组里那张。
        /// </summary>
        public IReadOnlyList<CardInstance> TransformCandidates(PlayerCharacter player)
        {
            var result = new List<CardInstance>();

            if (player.IsDown || player.Transform.Active || player.Transform.HasTransformedThisBattle)
            {
                return result;
            }

            foreach (CardInstance card in player.Zones.Hand.Where(IsUsableTransformCard))
            {
                if (TransformConditionMet(card, player)) result.Add(card);
            }

            // 第 3 轮保底：主卡组中仍未摸到且条件已满足的升变牌可直接跳出
            if (State.Round == GuaranteeRound && !player.Transform.UsedThirdRoundGuarantee)
            {
                foreach (CardInstance card in player.Zones.Deck.Where(IsUsableTransformCard))
                {
                    if (TransformConditionMet(card, player)) result.Add(card);
                }
            }

            return result;

            bool IsUsableTransformCard(CardInstance card)
                => card.Definition.Role == CardRole.Transform
                   && player.Zones.HasFormDeck(card.Definition.Id);
        }

        /// <summary>
        /// 即发型升变牌条件恒真；条件型交给卡面挂的 Behaviour 判定，
        /// 没挂 Behaviour 的条件型牌视为条件已满足（方便做占位内容）。
        /// </summary>
        private bool TransformConditionMet(CardInstance card, PlayerCharacter player)
        {
            if (card.Definition.TransformTrigger == TransformTrigger.Immediate) return true;

            ICardBehaviour behaviour = Behaviours.Resolve(card.Definition);
            return behaviour == null || behaviour.TransformConditionMet(State, player);
        }

        private ActionResult HandleDeclareTransform(PlayerCharacter actor, DeclareTransformAction action)
        {
            if (Pending.Kind != DecisionKind.TransformDeclaration)
            {
                return ActionResult.Fail("升变只能在轮始宣告（意图揭示后、常规抽牌前）");
            }

            if (!Pending.WaitingOn.Contains(actor.Id)) return ActionResult.Fail("本轮已表态");

            CardInstance card = TransformCandidates(actor).FirstOrDefault(c => c.Uid == action.CardUid);
            if (card == null) return ActionResult.Fail("该升变牌当前不可发动");

            bool fromDeck = actor.Zones.Deck.Contains(card);

            EnterTransform(actor, card, fromDeck);
            if (fromDeck) actor.Transform.UsedThirdRoundGuarantee = true;

            Pending.WaitingOn.Remove(actor.Id);
            if (Pending.IsResolved) ProceedToDraw();
            return ActionResult.Success;
        }

        private ActionResult HandleDeclineTransform(PlayerCharacter actor)
        {
            if (Pending.Kind != DecisionKind.TransformDeclaration) return ActionResult.Fail("当前不是升变宣告时机");
            if (!Pending.WaitingOn.Remove(actor.Id)) return ActionResult.Fail("本轮已表态");

            if (Pending.IsResolved) ProceedToDraw();
            return ActionResult.Success;
        }

        /// <summary>
        /// 条件未达成时的手里效果：展示该牌 → 洗回主卡组 → 抽 1 张（§6.1）。免费且不耗 AP。
        /// </summary>
        private ActionResult HandleShuffleBackTransform(PlayerCharacter actor, ShuffleBackTransformAction action)
        {
            if (Pending.Kind is not (DecisionKind.PlayerActions or DecisionKind.TransformDeclaration))
            {
                return ActionResult.Fail("当前不能洗回升变牌");
            }

            if (actor.Transform.Active) return ActionResult.Fail("升变期间不能洗回升变牌");

            CardInstance card = actor.Zones.Hand.Find(action.CardUid);
            if (card == null) return ActionResult.Fail("该牌不在手牌中");
            if (card.Definition.Role != CardRole.Transform) return ActionResult.Fail("只有升变牌可以这样洗回");

            actor.Zones.Hand.Remove(card);
            actor.Zones.Deck.AddBottom(card);
            actor.Zones.Deck.Shuffle(State.Rng);

            Emit(new BattleEvent(BattleEventType.TransformShuffledBack, actor.Id)
            {
                CardId = card.Definition.Id,
                Round = State.Round,
            });

            DrawCards(actor, 1);
            return ActionResult.Success;
        }

        /// <summary>
        /// 进入升变（§6.2 ①②）：原手牌暂存到场外，形态组洗进升变卡组区。
        /// **主卡组、持续区与充能区的牌都留在原位**——只是这期间不从主卡组抽牌。
        /// </summary>
        private void EnterTransform(PlayerCharacter player, CardInstance transformCard, bool fromDeck)
        {
            PlayerZones zones = player.Zones;

            if (fromDeck) zones.Deck.Remove(transformCard);
            else zones.Hand.Remove(transformCard);

            zones.StashedHand.AddRange(zones.Hand.TakeAll());

            CardPile formDeck = zones.GetFormDeck(transformCard.Definition.Id);
            zones.FormDeck.AddRange(formDeck.TakeAll());
            zones.FormDeck.Shuffle(State.Rng);

            player.Transform.Enter(transformCard, transformCard.Definition.ClassId);

            Emit(new BattleEvent(BattleEventType.TransformEntered, player.Id)
            {
                CardId = transformCard.Definition.Id,
                Text = fromDeck ? "第3轮保底跳出" : "手牌发动",
                Round = State.Round,
            });
        }

        /// <summary>
        /// 退出升变（§6.2 ⑤⑥）：升变牌与全部形态牌整套进除外区，取回暂存的原手牌。
        /// 主卡组全程没动过，不需要还原。本轮随后按常规抽 3 张。
        /// </summary>
        private void ExitTransform(PlayerCharacter player)
        {
            PlayerZones zones = player.Zones;
            CardPile exile = zones.Exile;

            // 升变卡组与手牌里剩下的形态牌移出本局；万一混进了非形态牌，还回它们原本该在的区域
            foreach (CardInstance card in zones.FormDeck.TakeAll())
            {
                if (card.IsFormCard) exile.AddBottom(card);
                else zones.Deck.AddBottom(card);
            }

            var strays = new List<CardInstance>();
            foreach (CardInstance card in zones.Hand.TakeAll())
            {
                if (card.IsFormCard) exile.AddBottom(card);
                else strays.Add(card);
            }

            exile.AddRange(zones.FormDiscard.TakeAll());

            if (player.Transform.TransformCard != null) exile.AddBottom(player.Transform.TransformCard);

            zones.Hand.AddRange(zones.StashedHand.TakeAll());
            zones.Hand.AddRange(strays);

            Emit(new BattleEvent(BattleEventType.TransformExited, player.Id)
            {
                CardId = player.Transform.TransformCard?.Definition.Id,
                Round = State.Round,
            });

            player.Transform.Exit();
        }
    }
}
