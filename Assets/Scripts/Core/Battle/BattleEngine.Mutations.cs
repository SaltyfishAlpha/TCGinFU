using System.Collections.Generic;

namespace TCGinFU.Core
{
    /// <summary>
    /// 引擎的写入口。<see cref="IEffectHandler"/>、<see cref="ICardBehaviour"/> 和引擎自身的流程代码
    /// 全部经由这些方法改状态——状态变更和事件产出就只有这一处，不会漏发事件。
    /// </summary>
    public sealed partial class BattleEngine
    {
        public DamageResult DealDamage(
            Combatant source,
            Combatant target,
            int amount,
            DamageElement element,
            CritResult crit,
            CardInstance card = null)
        {
            if (target == null || target.IsDown) return null;

            DamageResult result = DamageResolver.Resolve(source, target, amount, element, crit);

            State.Stats.Add(BattleStatKeys.DamageDealt, source?.Id, result.HpLost);
            if (result.IsCrit) State.Stats.Add(BattleStatKeys.CritsDealt, source?.Id);
            if (result.IsSuperCrit) State.Stats.Add(BattleStatKeys.SuperCritsDealt, source?.Id);

            Emit(new BattleEvent(BattleEventType.DamageDealt, source?.Id, target.Id)
            {
                Amount = result.HpLost,
                CardId = card?.Definition.Id,
                Damage = result,
                Round = State.Round,
            });

            if (result.TargetEnteredCrisis)
            {
                Emit(new BattleEvent(BattleEventType.CrisisEntered, target.Id, target.Id) { Round = State.Round });
            }

            if (result.TargetWentDown)
            {
                Emit(new BattleEvent(BattleEventType.CombatantDown, target.Id, target.Id) { Round = State.Round });
                if (target is Enemy enemy)
                {
                    State.Stats.Add(BattleStatKeys.EnemiesDowned, source?.Id);
                    enemy.ClearIntents();
                }
                State.CheckOutcome();
            }

            return result;
        }

        public int Heal(Combatant target, int amount, CardInstance card = null)
        {
            if (target == null || amount <= 0) return 0;

            bool wasInCrisis = target is PlayerCharacter before && before.IsInCrisis;
            int healed = target.GainHp(amount);
            if (healed <= 0) return 0;

            Emit(new BattleEvent(BattleEventType.Healed, card?.OwnerId, target.Id)
            {
                Amount = healed,
                CardId = card?.Definition.Id,
                Round = State.Round,
            });

            if (wasInCrisis && target is PlayerCharacter after && !after.IsInCrisis)
            {
                Emit(new BattleEvent(BattleEventType.CrisisLeft, target.Id, target.Id) { Round = State.Round });
            }

            return healed;
        }

        public void GrantShield(Combatant target, ShieldKind kind, int amount, CardInstance card = null)
        {
            if (target == null || amount <= 0) return;

            target.Shields.Grant(kind, amount);
            Emit(new BattleEvent(BattleEventType.ShieldGained, card?.OwnerId, target.Id)
            {
                Amount = amount,
                CardId = card?.Definition.Id,
                Text = kind.ToString(),
                Round = State.Round,
            });
        }

        public void RestoreMp(PlayerCharacter player, int amount)
        {
            int restored = player.RestoreMp(amount);
            if (restored <= 0) return;
            Emit(new BattleEvent(BattleEventType.MpRestored, player.Id, player.Id)
            {
                Amount = restored,
                Round = State.Round,
            });
        }

        public void GrantAp(PlayerCharacter player, int amount)
        {
            if (amount <= 0) return;
            player.Ap += amount;
            Emit(new BattleEvent(BattleEventType.ApGained, player.Id, player.Id)
            {
                Amount = amount,
                Round = State.Round,
            });
        }

        /// <summary>
        /// 从卡组顶抽 1 张。牌堆空时：未升变则把弃牌堆洗回去再抽；
        /// 升变期间形态卡组是有限的 10 张，抽空就是抽空，不续牌。
        /// </summary>
        public CardInstance DrawFromDeck(PlayerCharacter player)
        {
            PlayerZones zones = player.Zones;

            if (zones.Deck.IsEmpty && !player.Transform.Active)
            {
                if (zones.ReshuffleDiscardIntoDeck(State.Rng))
                {
                    Emit(new BattleEvent(BattleEventType.DeckReshuffled, player.Id) { Round = State.Round });
                }
            }

            return zones.Deck.DrawTop();
        }

        public List<CardInstance> DrawCards(PlayerCharacter player, int count)
        {
            var drawn = new List<CardInstance>();
            for (int i = 0; i < count; i++)
            {
                CardInstance card = DrawFromDeck(player);
                if (card == null) break;
                player.Zones.Hand.AddBottom(card);
                drawn.Add(card);
            }

            if (drawn.Count > 0)
            {
                Emit(new BattleEvent(BattleEventType.CardDrawn, player.Id)
                {
                    Amount = drawn.Count,
                    Round = State.Round,
                });
            }

            return drawn;
        }

        /// <summary>
        /// 把一张牌作为充能指示物贴进某人的充能区。溢出时顶替最早的那个，
        /// 被顶出的牌回到**它自己原持有者**的弃牌堆（§4.1）。
        /// </summary>
        public void PlaceCharge(PlayerCharacter zoneOwner, CardInstance card)
        {
            zoneOwner.Zones.Charges.Push(new ChargeToken(card), out ChargeToken evicted);

            Emit(new BattleEvent(BattleEventType.ChargePlaced, card.OwnerId, zoneOwner.Id)
            {
                CardId = card.Definition.Id,
                Round = State.Round,
            });

            if (evicted == null) return;

            ReturnToOwnerDiscard(evicted.Card);
            Emit(new BattleEvent(BattleEventType.ChargeEvicted, evicted.OwnerId, zoneOwner.Id)
            {
                CardId = evicted.Card.Definition.Id,
                Round = State.Round,
            });
        }

        /// <summary>消耗 1 个充能，消耗后那张牌进它原持有者的弃牌堆（§5.2）。</summary>
        public bool SpendCharge(PlayerCharacter player, ChargeUse use)
        {
            ChargeToken token = player.Zones.Charges.Pop();
            if (token == null) return false;

            ReturnToOwnerDiscard(token.Card);
            State.Stats.Add(BattleStatKeys.ChargesSpent, player.Id);
            Emit(new BattleEvent(BattleEventType.ChargeSpent, player.Id, token.OwnerId)
            {
                CardId = token.Card.Definition.Id,
                Text = use.ToString(),
                Round = State.Round,
            });

            return true;
        }

        public void ApplyStatus(Combatant target, StatusEffect status)
        {
            target.Statuses.Apply(status);
            Emit(new BattleEvent(BattleEventType.StatusApplied, null, target.Id)
            {
                Amount = status.Stacks,
                Text = status.Id,
                Round = State.Round,
            });
        }

        /// <summary>切换前后排。<paramref name="free"/> 为 true 时不检查 AP 与每轮限次（由卡牌效果驱动）。</summary>
        public bool SwitchRow(Combatant combatant, bool free)
        {
            Battlefield field = State.FieldOf(combatant);
            Row from = combatant.Row;
            if (!field.SwitchRow(combatant)) return false;

            if (!free && combatant is PlayerCharacter player)
            {
                player.HasMovedThisRound = true;
            }

            Emit(new BattleEvent(BattleEventType.RowChanged, combatant.Id, combatant.Id)
            {
                Text = $"{from}→{combatant.Row}",
                Round = State.Round,
            });

            return true;
        }

        /// <summary>登记一次援护：本次攻击改由 <paramref name="coverer"/> 承受（裁定 R-12）。</summary>
        public void RegisterCover(PlayerCharacter coverer, Combatant originalTarget, CardInstance card = null)
        {
            if (coverer == null || originalTarget == null) return;
            if (_covers.ContainsKey(originalTarget.Id)) return; // 一次攻击只能被援护一次

            // §4.3：只有「相邻队友」成为目标时才能援护
            if (!ReferenceEquals(coverer, originalTarget)
                && !State.PlayerField.AreAdjacent(coverer, originalTarget))
            {
                return;
            }

            _covers[originalTarget.Id] = coverer;
            Emit(new BattleEvent(BattleEventType.Covered, coverer.Id, originalTarget.Id)
            {
                CardId = card?.Definition.Id,
                Round = State.Round,
            });
        }

        /// <summary>把牌送回它原持有者的弃牌堆。</summary>
        private void ReturnToOwnerDiscard(CardInstance card)
        {
            PlayerCharacter owner = State.FindPlayer(card.OwnerId);
            if (owner == null) return;

            if (card.IsFormCard) owner.Zones.FormDiscard.AddTop(card);
            else owner.Zones.Discard.AddTop(card);
        }
    }
}
