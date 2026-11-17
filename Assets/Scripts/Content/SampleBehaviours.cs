using System.Collections.Generic;
using System.Linq;
using TCGinFU.Core;

namespace TCGinFU.Content
{
    /// <summary>
    /// 示范「脚本化钩子」这条路：下面三张卡做的事都是 <see cref="EffectData"/> 表达不了的，
    /// 其余 27 张样例卡全部纯数据驱动，一行代码都不用写。
    /// </summary>
    public static class SampleBehaviours
    {
        public const string Awaken = "beh_rogue_awaken";
        public const string ThousandCuts = "beh_thousand_cuts";
        public const string PerfectParry = "beh_perfect_parry";

        public static BehaviourRegistry CreateRegistry()
        {
            var registry = new BehaviourRegistry();
            registry.Register(new AwakenConditionBehaviour());
            registry.Register(new ThousandCutsBehaviour());
            registry.Register(new PerfectParryBehaviour());
            return registry;
        }
    }

    /// <summary>
    /// 「影舞觉醒」的前置条件（§6.1 条件型升变牌）：
    /// 本局你造成过暴击，**或**全队累计消耗过 3 个充能。
    /// 这种跨回合的累计判定数据层表达不了，必须走钩子。
    /// </summary>
    public sealed class AwakenConditionBehaviour : CardBehaviour
    {
        public const int RequiredTeamCharges = 3;

        public override string Id => SampleBehaviours.Awaken;

        public override bool TransformConditionMet(BattleState state, PlayerCharacter owner)
            => state.Stats.For(owner.Id, BattleStatKeys.CritsDealt) > 0
               || state.Stats.Team(BattleStatKeys.ChargesSpent) >= RequiredTeamCharges;
    }

    /// <summary>
    /// 英雄技能牌「千刃乱舞」（10 级精通，§3.3）：
    /// 随机挑敌人连斩 <c>3 + 已消耗充能数</c> 次，每次 ⌊B_atk ÷ 2⌋ 点物理伤害。
    /// 「随机次数 × 随机目标」是数据驱动做不到的，所以整段逻辑放在 OnResolved 里。
    /// </summary>
    public sealed class ThousandCutsBehaviour : CardBehaviour
    {
        public const int BaseHits = 3;

        public override string Id => SampleBehaviours.ThousandCuts;

        public override void OnResolved(EffectContext ctx)
        {
            int hits = BaseHits + ctx.ChargeSpent;
            int perHit = ctx.Caster.FormAttackBase / 2;

            for (int i = 0; i < hits; i++)
            {
                List<Enemy> alive = ctx.State.LivingEnemies.ToList();
                if (alive.Count == 0) break;

                Enemy target = alive[ctx.State.Rng.NextInt(0, alive.Count)];
                ctx.Engine.DealDamage(ctx.Caster, target, perHit, DamageElement.Physical, CritResult.None, ctx.Card);
            }
        }
    }

    /// <summary>
    /// 时机牌「见切」：只有在敌人**正要打出物理攻击**时才能打出。
    /// 这条前置条件要读当前响应窗口里的敌人意图，数据层没有这个入口。
    /// </summary>
    public sealed class PerfectParryBehaviour : CardBehaviour
    {
        public override string Id => SampleBehaviours.PerfectParry;

        public override bool CanPlay(EffectContext ctx, out string reason)
        {
            EnemyIntent intent = ctx.Engine.Pending?.Intent;

            if (intent == null)
            {
                reason = "见切只能在敌人意图结算前打出";
                return false;
            }

            if (intent.Damage <= 0 || intent.Element != DamageElement.Physical)
            {
                reason = "见切只能应对物理攻击";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
