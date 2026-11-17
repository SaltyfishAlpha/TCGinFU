using System;
using TCGinFU.Core;
using UnityEngine;

namespace TCGinFU.Data
{
    /// <summary>
    /// <see cref="ValueExpr"/> 的可序列化镜像。Core 里那个是只读 struct，
    /// Unity 的序列化器要的是带公开字段的可变类型，所以在数据层做一层转换。
    /// </summary>
    [Serializable]
    public struct SerializedValue
    {
        [Tooltip("取值来源。Flat = 固定值；FormAttack/FormDefense 对应 §6.3 的 B_atk / B_def")]
        public ValueSource source;

        [Tooltip("加值。形态牌「B_atk + 4」里的那个 4")]
        public int add;

        [Tooltip("倍率分子，留 0 视为 1")]
        public int times;

        [Tooltip("倍率分母，留 0 视为 1（整数除法向下取整）")]
        public int dividedBy;

        public ValueExpr ToValueExpr()
            => new(source, add, times <= 0 ? 1 : times, dividedBy <= 0 ? 1 : dividedBy);

        public static SerializedValue Flat(int value)
            => new() { source = ValueSource.Flat, add = value, times = 1, dividedBy = 1 };
    }

    /// <summary><see cref="EffectData"/> 的可序列化镜像。</summary>
    [Serializable]
    public sealed class SerializedEffect
    {
        public EffectKind kind = EffectKind.Damage;
        public SerializedValue amount = SerializedValue.Flat(0);
        public TargetScope target = TargetScope.SingleEnemy;

        [Tooltip("伤害属性（§7.4 九大属性）。非伤害效果忽略")]
        public DamageElement element = DamageElement.Physical;

        [Tooltip("护盾种类。非护盾效果忽略")]
        public ShieldKind shieldKind = ShieldKind.Physical;

        [Tooltip("ApplyStatus 用的状态 Id")]
        public string statusId;

        [Min(-1)]
        [Tooltip("状态持续轮数，-1 表示本局持续")]
        public int duration = 1;

        [Tooltip("附加生效条件，不满足则整条效果跳过")]
        public EffectCondition condition = EffectCondition.Always;

        public EffectData ToEffectData() => new()
        {
            Kind = kind,
            Amount = amount.ToValueExpr(),
            // 援护改写的是「本次结算的既定目标」，作用域强制成 CurrentTarget，避免编辑器里配错
            Target = new TargetSelector(kind == EffectKind.Cover ? TargetScope.CurrentTarget : target),
            Element = element,
            ShieldKind = shieldKind,
            StatusId = statusId,
            Duration = duration,
            Condition = condition,
        };
    }
}
