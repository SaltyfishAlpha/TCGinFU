using System.Collections.Generic;
using System.Text;

namespace TCGinFU.Core
{
    /// <summary>
    /// 把 <see cref="BattleEvent"/> 流渲染成人能读的战斗日志。
    /// 调试规则、测试失败时看现场、将来做战报回放都用它——
    /// Core 不能 Debug.Log，所以「可观测性」只能靠这一层。
    /// </summary>
    public static class BattleLog
    {
        /// <summary>渲染单条事件。名字解析交给可选的 <paramref name="nameOf"/>，没有就直接显示 Id。</summary>
        public static string Format(BattleEvent e, System.Func<string, string> nameOf = null)
        {
            string Name(string id) => id == null ? "?" : nameOf?.Invoke(id) ?? id;

            return e.Type switch
            {
                BattleEventType.BattleStarted => "=== 战斗开始 ===",
                BattleEventType.RoundStarted => $"── 第 {e.Amount} 轮 ──",
                BattleEventType.PhaseChanged => $"  [{e.Text}]",
                BattleEventType.IntentRevealed =>
                    $"  {Name(e.ActorId)} 意图：{e.Text}" + (e.Amount > 0 ? $"（{e.Amount} 点）" : ""),
                BattleEventType.IntentDeclared =>
                    $"  {Name(e.ActorId)} 宣告「{e.Text}」" + (e.Amount > 0 ? $"（{e.Amount} 点）" : "")
                    + (e.TargetId != null ? $" → {Name(e.TargetId)}" : "") + "，响应窗口打开",
                BattleEventType.CardDrawn => $"  {Name(e.ActorId)} 抽 {e.Amount} 张",
                BattleEventType.DeckReshuffled => $"  {Name(e.ActorId)} 弃牌堆洗回卡组",
                BattleEventType.CardPlayed =>
                    $"  {Name(e.ActorId)} 打出「{e.Text}」" + (e.TargetId != null ? $" → {Name(e.TargetId)}" : ""),
                BattleEventType.DamageDealt => FormatDamage(e, Name),
                BattleEventType.Healed => $"    {Name(e.TargetId)} 回复 {e.Amount} HP",
                BattleEventType.ShieldGained => $"    {Name(e.TargetId)} 获得 {e.Amount} 点{ShieldName(e.Text)}",
                BattleEventType.ShieldRefreshed => $"  {Name(e.ActorId)} 重获自动双盾",
                BattleEventType.StatusApplied => $"    {Name(e.TargetId)} 获得状态 {e.Text}×{e.Amount}",
                BattleEventType.StatusExpired => $"    {Name(e.TargetId)} 的 {e.Text} 已失效",
                BattleEventType.ChargePlaced => $"  {Name(e.ActorId)} 贴 1 个充能到 {Name(e.TargetId)}",
                BattleEventType.ChargeEvicted => $"    {Name(e.TargetId)} 充能区溢出，退还给 {Name(e.ActorId)}",
                BattleEventType.ChargeSpent => $"  {Name(e.ActorId)} 消耗 1 个充能（{e.Text}）",
                BattleEventType.ContinuousReplaced => $"    持续牌被顶替，旧牌进弃牌堆",
                BattleEventType.RowChanged => $"  {Name(e.ActorId)} 走位 {e.Text}",
                BattleEventType.Covered => $"    {Name(e.ActorId)} 援护 {Name(e.TargetId)}",
                BattleEventType.CrisisEntered => $"  ⚠ {Name(e.TargetId)} 进入危机状态",
                BattleEventType.CrisisLeft => $"  {Name(e.TargetId)} 脱离危机状态",
                BattleEventType.CombatantDown => $"  ✖ {Name(e.TargetId)} 倒下",
                BattleEventType.TransformEntered => $"  ★ {Name(e.ActorId)} 发动升变（{e.Text}）",
                BattleEventType.TransformExited => $"  {Name(e.ActorId)} 退出升变，归还原卡组",
                BattleEventType.TransformShuffledBack => $"  {Name(e.ActorId)} 将升变牌洗回卡组并补抽 1 张",
                BattleEventType.ApGained => $"  {Name(e.ActorId)} 获得 {e.Amount} AP",
                BattleEventType.MpRestored => $"  {Name(e.ActorId)} 回复 {e.Amount} MP",
                BattleEventType.HandLimitDiscard => $"  {Name(e.ActorId)} 轮末弃牌",
                BattleEventType.SceneClockAdvanced => $"  场景命刻 → {e.Amount}",
                BattleEventType.BattleEnded => $"=== 战斗结束：{e.Text} ===",
                _ => e.ToString(),
            };
        }

        private static string FormatDamage(BattleEvent e, System.Func<string, string> name)
        {
            DamageResult d = e.Damage;
            if (d == null) return $"    {name(e.TargetId)} 受到 {e.Amount} 点伤害";

            var tags = new List<string>();
            if (d.IsSuperCrit) tags.Add("超暴击");
            else if (d.IsCrit) tags.Add("暴击");

            switch (d.Affinity)
            {
                case Affinity.Vulnerable: tags.Add("弱点"); break;
                case Affinity.Resistant: tags.Add("抗性"); break;
                case Affinity.Immune: tags.Add("免疫"); break;
                case Affinity.Absorb: tags.Add("吸收"); break;
            }

            if (d.AbsorbedByShield > 0) tags.Add($"盾挡{d.AbsorbedByShield}");

            string suffix = tags.Count > 0 ? $" [{string.Join(" ", tags)}]" : "";

            return d.HealedInstead > 0
                ? $"    {name(e.TargetId)} 吸收伤害，回复 {d.HealedInstead} HP{suffix}"
                : $"    {name(e.TargetId)} 受到 {d.HpLost} 点{d.Element.DisplayName()}伤害{suffix}";
        }

        private static string ShieldName(string kind)
            => kind == nameof(ShieldKind.Magical) ? "魔法护盾" : "物理护盾";

        /// <summary>渲染整条事件流。</summary>
        public static string Format(IEnumerable<BattleEvent> events, System.Func<string, string> nameOf = null)
        {
            var builder = new StringBuilder();
            foreach (BattleEvent e in events) builder.AppendLine(Format(e, nameOf));
            return builder.ToString();
        }

        /// <summary>最常用的重载：拿引擎直接出一份完整战报，参战者名字自动解析。</summary>
        public static string Format(BattleEngine engine)
            => Format(engine.Events, id => engine.State.Find(id)?.Name ?? id);
    }
}
