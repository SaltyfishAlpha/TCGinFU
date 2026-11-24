using System.Collections.Generic;
using System.Linq;

namespace TCGinFU.Core
{
    /// <summary>
    /// 引擎的写入口。<see cref="IEffectHandler"/>、<see cref="ICardBehaviour"/> 和引擎自身的流程代码
    /// 全部经由这些方法改状态——状态变更和事件产出就只有这一处，不会漏发事件。
    /// </summary>
    public sealed partial class BattleEngine
    {
        /// <summary>
        /// 造成一次伤害。内部走的是 <see cref="ResolveAttack"/> 那条完整管线——
        /// 也就是说常驻行为（持续牌 / 升变 / 状态 / mod 脚本）同样会过手这次攻击。
        /// </summary>
        public DamageResult DealDamage(
            Combatant source,
            Combatant target,
            int amount,
            DamageElement element,
            CritResult crit,
            CardInstance card = null)
            => ResolveAttack(new AttackContext(source, target, amount, element, crit) { Card = card });

        public int Heal(Combatant target, int amount, CardInstance card = null)
        {
            if (target == null || amount <= 0) return 0;

            bool wasInCrisis = target is PlayerCharacter before && before.IsInCrisis;
            int healed = target.GainHp(amount);
            if (healed <= 0) return 0;

            State.Stats.Add(BattleStatKeys.HpHealed, target.Id, healed);

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

        /// <summary>
        /// 削减护盾（「并削减所有敌人3点物理护盾」）。返回实际削掉的点数。
        ///
        /// <para>复用 <see cref="ShieldPool.Absorb"/> 而不是另写一套扣减：
        /// 自动双盾与卡牌护盾的消耗次序在那里已经定好了，再写一遍迟早会走岔。</para>
        /// </summary>
        public int ReduceShield(Combatant target, ShieldKind kind, int amount)
        {
            if (target == null || amount <= 0) return 0;

            int removed = target.Shields.Absorb(kind, amount);
            if (removed <= 0) return 0;

            Emit(new BattleEvent(BattleEventType.ShieldReduced, null, target.Id)
            {
                Amount = removed,
                Text = kind.ToString(),
                Round = State.Round,
            });

            return removed;
        }

        /// <summary>
        /// 移除宿主身上最多 <paramref name="count"/> 个负面状态（「移除每名受保护角色身上的1个负面状态」）。
        /// 「负面」的口径与脚本里的 <c>has_negative_status</c> 一致：带持续伤害、加重受伤、削弱输出或造成易损。
        /// </summary>
        /// <returns>实际移除的状态 Id。</returns>
        public List<string> RemoveNegativeStatuses(Combatant target, int count = 1)
        {
            var removed = new List<string>();
            if (target == null || count <= 0) return removed;

            foreach (StatusEffect status in target.Statuses.All.ToList())
            {
                if (removed.Count >= count) break;
                if (!IsNegative(status)) continue;

                target.Statuses.Remove(status.Id);
                removed.Add(status.Id);

                Emit(new BattleEvent(BattleEventType.StatusExpired, target.Id, target.Id)
                {
                    Text = status.Id,
                    Round = State.Round,
                });
            }

            return removed;
        }

        private static bool IsNegative(StatusEffect status)
            => status.DamagePerRound > 0
               || status.IncomingDamageBonus > 0
               || status.OutgoingDamageBonus < 0
               || status.VulnerableElement.HasValue
               || (status.IncomingMultiplierDenominator > 0
                   && status.IncomingMultiplierNumerator > status.IncomingMultiplierDenominator);

        public void RestoreMp(PlayerCharacter player, int amount)
        {
            if (player == null) return;

            int restored = player.RestoreMp(amount);
            if (restored <= 0) return;

            State.Stats.Add(BattleStatKeys.MpRestored, player.Id, restored);
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
        /// 从当前该抽的那摞牌顶抽 1 张。升变期间抽的是升变卡组——它只有有限的 10 张，
        /// 抽空就是抽空，不续牌，这期间主卡组碰都不碰。
        /// 未升变时主卡组抽空，把弃牌堆洗回去再抽。
        /// </summary>
        public CardInstance DrawFromDeck(PlayerCharacter player)
        {
            PlayerZones zones = player.Zones;

            if (player.Transform.Active) return zones.FormDeck.DrawTop();

            if (zones.Deck.IsEmpty && zones.ReshuffleDiscardIntoDeck(State.Rng))
            {
                Emit(new BattleEvent(BattleEventType.DeckReshuffled, player.Id) { Round = State.Round });
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
        /// 从手牌弃置若干张（<see cref="EffectKind.Discard"/>）。升变期间的形态牌弃进形态弃牌堆。
        /// </summary>
        /// <returns>实际弃掉的牌——手牌不够时会少于 <paramref name="count"/>。</returns>
        public List<CardInstance> DiscardFromHand(PlayerCharacter player, int count)
        {
            var discarded = new List<CardInstance>();
            if (player == null || count <= 0) return discarded;

            for (int i = 0; i < count; i++)
            {
                CardInstance card = player.Zones.Hand.DrawBottom();
                if (card == null) break;

                ReturnToOwnerDiscard(card);
                discarded.Add(card);

                Emit(new BattleEvent(BattleEventType.CardDiscarded, player.Id)
                {
                    CardId = card.Definition.Id,
                    Round = State.Round,
                });
            }

            return discarded;
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

            ReturnEvictedCharge(zoneOwner, evicted);
        }

        /// <summary>
        /// 被溢出顶替的充能回到**它自己原持有者**的弃牌堆（§4.1）。
        /// 每个写充能区的地方都得走这里——漏一处那张牌就既不在充能区也不在弃牌堆，
        /// 从本局凭空消失。
        /// </summary>
        private void ReturnEvictedCharge(PlayerCharacter zoneOwner, ChargeToken evicted)
        {
            if (evicted == null) return;

            ReturnToOwnerDiscard(evicted.Card);
            Emit(new BattleEvent(BattleEventType.ChargeEvicted, evicted.OwnerId, zoneOwner.Id)
            {
                CardId = evicted.Card.Definition.Id,
                Round = State.Round,
            });
        }

        /// <summary>
        /// 把牌从某个区搬进充能区。老卡里「从弃牌堆/手牌/牌库顶贴 1 个充能」出现了五六次，
        /// 每张卡各写一遍迟早会在「贴完进谁的弃牌堆」这种细节上各走各的。
        /// </summary>
        /// <param name="from">牌从谁的区里出。</param>
        /// <param name="to">贴进谁的充能区。</param>
        /// <returns>实际贴进去的数量——区里不够时会少于 <paramref name="count"/>。</returns>
        public int ChargeFromZone(PlayerCharacter from, PlayerCharacter to, ChargeSource source, int count = 1)
        {
            if (from == null || to == null || count <= 0) return 0;

            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                CardInstance card = source switch
                {
                    ChargeSource.Discard => from.Zones.Discard.DrawTop(),
                    ChargeSource.DeckTop => DrawFromDeck(from),
                    ChargeSource.HandBottom => from.Zones.Hand.DrawBottom(),
                    _ => null,
                };

                if (card == null) break;

                PlaceCharge(to, card);
                placed++;
            }

            return placed;
        }

        /// <summary>
        /// 敌人吞噬玩家的充能（老 Boss 的「以太虹吸」）。
        ///
        /// <para>和 <see cref="SpendCharge"/> 是两回事：这不是玩家**用掉**充能，
        /// 所以不记进 <c>ChargesSpent</c> 统计（否则敌人一发虹吸就能替玩家满足
        /// 「全队累计消耗 2 个充能」的升变条件），也不触发「每当消耗充能时」。
        /// 牌本身仍然回原持有者的弃牌堆——它只是被打掉了，不是被除外。</para>
        /// </summary>
        /// <returns>实际吞掉几个。</returns>
        public int DevourCharges(PlayerCharacter victim, int count = 1)
        {
            if (victim == null || count <= 0) return 0;

            int devoured = 0;
            for (int i = 0; i < count; i++)
            {
                ChargeToken token = victim.Zones.Charges.Pop();
                if (token == null) break;

                ReturnToOwnerDiscard(token.Card);
                devoured++;

                Emit(new BattleEvent(BattleEventType.ChargeDevoured, victim.Id, token.OwnerId)
                {
                    CardId = token.Card.Definition.Id,
                    Round = State.Round,
                });
            }

            return devoured;
        }

        /// <summary>推进场景命刻（老 Boss 的「相位折跃」额外 +1）。轮末那次自动 +1 不走这里。</summary>
        public void AdvanceSceneClock(int amount = 1)
        {
            if (amount <= 0) return;

            State.SceneClock += amount;
            Emit(new BattleEvent(BattleEventType.SceneClockAdvanced)
            {
                Amount = State.SceneClock,
                Round = State.Round,
            });
        }

        /// <summary>消耗 1 个充能，消耗后那张牌进它原持有者的弃牌堆（§5.2）。</summary>
        public bool SpendCharge(PlayerCharacter player, ChargeUse use)
        {
            ChargeToken token = player.Zones.Charges.Pop();
            if (token == null) return false;

            // 「消耗后不进弃牌堆、继续留在充能区」是老卡「真理炼金矩阵」的词条。
            // 它要在**牌已经离开充能区、还没进弃牌堆**的这一刻插手，所以必须是个独立时点
            var spend = new ChargeSpendContext(player, token, use);
            RunChargeSpendModifiers(spend);

            if (spend.Retained)
            {
                // 放回原位而不是当新贴的追加——它从没离开过充能区
                player.Zones.Charges.Restore(token, out ChargeToken evicted);
                ReturnEvictedCharge(player, evicted);

                State.Stats.Add(BattleStatKeys.ChargesSpent, player.Id);
                Emit(new BattleEvent(BattleEventType.ChargeRetained, player.Id, token.OwnerId)
                {
                    CardId = token.Card.Definition.Id,
                    Text = ChargeUseText(use),
                    Round = State.Round,
                });

                FireTrigger(player, BattleTrigger.ChargeSpent, new TriggerSignal { Card = token.Card });
                return true;
            }

            ReturnToOwnerDiscard(token.Card);
            State.Stats.Add(BattleStatKeys.ChargesSpent, player.Id);
            Emit(new BattleEvent(BattleEventType.ChargeSpent, player.Id, token.OwnerId)
            {
                CardId = token.Card.Definition.Id,
                Text = ChargeUseText(use),
                Round = State.Round,
            });

            FireTrigger(player, BattleTrigger.ChargeSpent, new TriggerSignal { Card = token.Card });
            return true;
        }

        /// <summary>战报里写的用途。三种用途在战报上必须分得开，否则对不上账。</summary>
        private static string ChargeUseText(ChargeUse use) => use switch
        {
            ChargeUse.ConvertToMp => $"转 {MpPerCharge} MP",
            ChargeUse.ConvertToAp => "换 1 AP",
            _ => "支付卡面词条",
        };

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
            => MoveRow(combatant, combatant == null ? Row.Front : Opposite(combatant.Row), free);

        /// <summary>
        /// 移动到**指定**排。已在该排则什么都不发生并返回 false——
        /// 「被击退至后排」对后排的人就该是无事发生。
        /// </summary>
        public bool MoveRow(Combatant combatant, Row target, bool free)
        {
            if (combatant == null) return false;

            Battlefield field = State.FieldOf(combatant);
            int fromSlot = combatant.Slot;
            Row from = combatant.Row;
            if (!field.MoveTo(combatant, target)) return false;

            AfterMove(combatant, from, fromSlot, free);
            return true;
        }

        /// <summary>
        /// 移动到**指定排的指定槽位**。跨排换位与同排内改变相对位置都走这里；
        /// 目标槽有队友时挤开（见 <see cref="Battlefield.MoveTo(Combatant, Row, int)"/>）。
        /// 原地不动返回 false。
        /// </summary>
        public bool MoveRow(Combatant combatant, Row target, int slot, bool free)
        {
            if (combatant == null) return false;

            Battlefield field = State.FieldOf(combatant);
            int fromSlot = combatant.Slot;
            Row from = combatant.Row;

            // 挤开会连带挪动队友，播报前先记下他们原来站哪
            var before = new Dictionary<Combatant, int>();
            foreach (Combatant other in field.InRow(target))
            {
                if (other != combatant) before[other] = other.Slot;
            }

            if (!field.MoveTo(combatant, target, slot)) return false;

            AfterMove(combatant, from, fromSlot, free);

            // 被挤开的人不花 AP、不算「本轮走过位」——不是他们自己要动的
            foreach ((Combatant other, int otherFrom) in before)
            {
                if (other.Slot == otherFrom) continue;

                Emit(new BattleEvent(BattleEventType.RowChanged, combatant.Id, other.Id)
                {
                    Text = $"被挤开 {otherFrom}→{other.Slot} 号位",
                    Round = State.Round,
                });
            }

            return true;
        }

        /// <summary>记账 + 播报。同排换位也占「本轮一次」，它同样是一次移动（裁定 R-17b）。</summary>
        private void AfterMove(Combatant combatant, Row from, int fromSlot, bool free)
        {
            if (!free && combatant is PlayerCharacter player)
            {
                player.HasMovedThisRound = true;
            }

            // 同排换位时只报槽位，不然「Front→Front」看着像没动
            string text = from == combatant.Row
                ? $"{from} {fromSlot}→{combatant.Slot} 号位"
                : $"{from} {fromSlot}→{combatant.Row} {combatant.Slot} 号位";

            Emit(new BattleEvent(BattleEventType.RowChanged, combatant.Id, combatant.Id)
            {
                Text = text,
                Round = State.Round,
            });
        }

        /// <summary>玩家方或敌方的前后排整体互换，保持各自的列（老卡「相位折跃」）。</summary>
        public void SwapRows(bool playerSide)
        {
            Battlefield field = playerSide ? State.PlayerField : State.EnemyField;
            field.SwapRows();

            foreach (Combatant combatant in (playerSide
                         ? State.Players.Cast<Combatant>()
                         : State.Enemies.Cast<Combatant>()))
            {
                Emit(new BattleEvent(BattleEventType.RowChanged, combatant.Id, combatant.Id)
                {
                    Text = $"互换→{combatant.Row}",
                    Round = State.Round,
                });
            }
        }

        /// <summary>
        /// 授予「免费移动 1 次」的权利（老卡「翻滚披风」：使**目标**可免费移动）。
        ///
        /// <para>与直接调 <see cref="SwitchRow"/> 的区别是它**不立刻移动**：
        /// 卡面写的是「可」，什么时候动、动不动由玩家决定。存成额度，
        /// 玩家之后提交 <see cref="MoveRowAction"/> 时优先消耗它，不花 AP 也不占本轮唯一一次走位。</para>
        ///
        /// <para><paramref name="range"/> 是卡面对落点的限定，每一次额度各自带着自己的那份：
        /// 「在前后排之间移动」给 <see cref="MoveRange.RowSwitch"/>，
        /// 「打出后移动 1 次」给 <see cref="MoveRange.Any"/>。缺省放最宽，
        /// 卡面没限定就别替它限定。</para>
        /// </summary>
        public void GrantFreeMove(PlayerCharacter player, int count = 1, MoveRange? range = null)
        {
            if (player == null || count <= 0) return;

            MoveRange granted = range ?? MoveRange.Any;
            for (int i = 0; i < count; i++) player.AddFreeMove(granted);

            Emit(new BattleEvent(BattleEventType.FreeMoveGranted, player.Id, player.Id)
            {
                Amount = count,
                Text = granted.ToString(),
                Round = State.Round,
            });
        }

        /// <summary>
        /// 额外给几次「贴充能」的机会（老卡「真理炼金矩阵」：升变期间每轮可贴 2 次）。
        /// 轮始重置，所以给它的脚本要挂在 <see cref="BattleTrigger.RoundStart"/> 上。
        /// </summary>
        /// <summary>
        /// 临时调整充能区上限（老卡「奥术结界」：本轮及下轮 +1）。
        /// <para>调小到低于现有数量时 <see cref="ChargeZone.SetCapacity"/> 会挤掉最早的那几个，
        /// 所以恢复上限要走 <see cref="RestoreChargeCapacity"/>，别直接写死数字。</para>
        /// </summary>
        public void AdjustChargeCapacity(PlayerCharacter player, int delta)
        {
            if (player == null || delta == 0) return;

            player.ChargeCapacityBonus += delta;
            player.Zones.Charges.SetCapacity(State.ChargeCapacity + player.ChargeCapacityBonus);
        }

        /// <summary>把充能上限恢复成「全队人数决定的基准 + 当前加成」。轮始重算时用。</summary>
        internal void RestoreChargeCapacity(PlayerCharacter player)
            => player.Zones.Charges.SetCapacity(State.ChargeCapacity + player.ChargeCapacityBonus);

        public void GrantChargePlacement(PlayerCharacter player, int count = 1)
        {
            if (player == null || count <= 0) return;
            player.ExtraChargePlacements += count;
        }

        private static Row Opposite(Row row) => row == Row.Front ? Row.Back : Row.Front;

        /// <summary>登记一次援护：本次攻击改由 <paramref name="coverer"/> 承受（裁定 R-12）。</summary>
        public void RegisterCover(PlayerCharacter coverer, Combatant originalTarget, CardInstance card = null)
        {
            if (coverer == null || originalTarget == null) return;
            if (_covers.ContainsKey(originalTarget.Id)) return; // 一次攻击只能被援护一次
            if (_shares.ContainsKey(originalTarget.Id)) return; // 已经登记分摊就不能再整份转移（B-14）

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

        /// <summary>
        /// 登记一次分摊：本次攻击拆成两半，由 <paramref name="originalTarget"/> 与
        /// <paramref name="sharer"/> 各承一份、各自抵扣护盾（因果抹杀令，B-14）。
        /// </summary>
        /// <returns>登记成功返回 true；被拒绝时 <paramref name="reason"/> 说明原因，状态不变。</returns>
        public bool RegisterDamageShare(PlayerCharacter sharer, Combatant originalTarget, out string reason)
        {
            if (!CanShareDamage(sharer, originalTarget, out reason)) return false;

            _shares[originalTarget.Id] = sharer;
            Emit(new BattleEvent(BattleEventType.DamageShared, sharer.Id, originalTarget.Id)
            {
                Text = Pending.Intent.Label,
                Round = State.Round,
            });

            return true;
        }

        /// <summary>不关心失败原因时的简写。</summary>
        public bool RegisterDamageShare(PlayerCharacter sharer, Combatant originalTarget)
            => RegisterDamageShare(sharer, originalTarget, out _);

        /// <summary>
        /// 此刻 <paramref name="sharer"/> 能不能替 <paramref name="originalTarget"/> 分摊。
        /// <see cref="RegisterDamageShare"/> 和界面的候选列表走的是**这一份**判断，
        /// 免得「按钮亮着却提交失败」这类两套规则不一致的毛病。
        /// </summary>
        public bool CanShareDamage(PlayerCharacter sharer, Combatant originalTarget, out string reason)
        {
            reason = null;
            if (sharer == null || originalTarget == null) { reason = "分摊缺少角色"; return false; }

            if (Pending.Kind != DecisionKind.EnemyResponse) { reason = "只能在响应窗口里分摊"; return false; }
            if (Pending.Intent is not { Shareable: true }) { reason = "这条意图不能分摊"; return false; }

            if (ReferenceEquals(sharer, originalTarget)) { reason = "不能自己和自己分摊"; return false; }
            if (sharer.IsDown) { reason = $"{sharer.Name} 已倒下"; return false; }
            if (originalTarget.IsDown) { reason = $"{originalTarget.Name} 已倒下"; return false; }

            if (_pendingAttacks.All(a => !ReferenceEquals(a.Target, originalTarget)))
            {
                reason = $"{originalTarget.Name} 不是本次意图的目标";
                return false;
            }

            // 卡面写的是「相邻队友」，和援护同一条口径（§4.3）
            if (!State.PlayerField.AreAdjacent(sharer, originalTarget))
            {
                reason = "只有相邻队友才能分摊";
                return false;
            }

            // 一次攻击只能挑一种：整份转移（援护）或对半拆（分摊）
            if (_covers.ContainsKey(originalTarget.Id)) { reason = "这次攻击已被援护"; return false; }
            if (_shares.ContainsKey(originalTarget.Id)) { reason = "这次攻击已有人分摊"; return false; }

            return true;
        }

        /// <summary>
        /// 此刻这名角色可以替**哪些**队友分摊。界面拿它决定按钮亮不亮、要不要让玩家挑人。
        ///
        /// <para>范围意图可能同时打中好几个人，队伍也不一定只有两个人，
        /// 所以这里返回的是一串而不是一个——界面按长度决定直接提交还是先弹菜单。</para>
        /// </summary>
        public IReadOnlyList<Combatant> ShareableTargetsFor(PlayerCharacter sharer)
        {
            if (sharer == null || !Pending.WaitingOn.Contains(sharer.Id)) return System.Array.Empty<Combatant>();

            return _pendingAttacks
                .Select(a => a.Target)
                .Distinct()
                .Where(t => CanShareDamage(sharer, t, out _))
                .ToList();
        }

        /// <summary>替某人挡下整份攻击的那位援护者，没有则为 null。与 <see cref="SharerFor"/> 对称。</summary>
        public PlayerCharacter CovererFor(Combatant target)
            => target != null && _covers.TryGetValue(target.Id, out PlayerCharacter coverer) ? coverer : null;

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
