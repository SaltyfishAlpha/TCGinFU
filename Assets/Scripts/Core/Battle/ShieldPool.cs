namespace TCGinFU.Core
{
    /// <summary>
    /// 角色的护盾池。分两段（裁定 R-06）：
    /// <para>Auto —— 每轮开始自动获得、轮末清零的敏捷/洞察双盾（§2.1）；</para>
    /// <para>Granted —— 卡牌给予的护盾，跨轮保留直到被打掉。</para>
    /// 抵消时先扣会过期的 Auto，再扣 Granted。
    /// </summary>
    public sealed class ShieldPool
    {
        public int AutoPhysical { get; private set; }
        public int AutoMagical { get; private set; }
        public int GrantedPhysical { get; private set; }
        public int GrantedMagical { get; private set; }

        public int Physical => AutoPhysical + GrantedPhysical;
        public int Magical => AutoMagical + GrantedMagical;

        public int Total(ShieldKind kind) => kind == ShieldKind.Physical ? Physical : Magical;

        /// <summary>轮始：清空上一轮残留的自动双盾并重新授予本轮的（§8 ①）。</summary>
        public void RefreshAuto(int physical, int magical)
        {
            AutoPhysical = physical;
            AutoMagical = magical;
        }

        public void ClearAuto()
        {
            AutoPhysical = 0;
            AutoMagical = 0;
        }

        public void Grant(ShieldKind kind, int amount)
        {
            if (amount <= 0) return;
            if (kind == ShieldKind.Physical) GrantedPhysical += amount;
            else GrantedMagical += amount;
        }

        /// <summary>吸收伤害，返回**实际被护盾挡下**的点数。</summary>
        public int Absorb(ShieldKind kind, int damage)
        {
            if (damage <= 0) return 0;

            int remaining = damage;
            int absorbed = 0;

            int auto = kind == ShieldKind.Physical ? AutoPhysical : AutoMagical;
            int fromAuto = auto < remaining ? auto : remaining;
            if (kind == ShieldKind.Physical) AutoPhysical -= fromAuto;
            else AutoMagical -= fromAuto;
            remaining -= fromAuto;
            absorbed += fromAuto;

            int granted = kind == ShieldKind.Physical ? GrantedPhysical : GrantedMagical;
            int fromGranted = granted < remaining ? granted : remaining;
            if (kind == ShieldKind.Physical) GrantedPhysical -= fromGranted;
            else GrantedMagical -= fromGranted;
            absorbed += fromGranted;

            return absorbed;
        }

        public override string ToString() => $"物理盾 {Physical}（自动 {AutoPhysical}）/ 魔法盾 {Magical}（自动 {AutoMagical}）";
    }
}
