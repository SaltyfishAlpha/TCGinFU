using System.Collections.Generic;
using System.Linq;
using TCGinFU.Core;
using UnityEngine;

namespace TCGinFU.Data
{
    /// <summary>
    /// 一张卡的策划资产。绝大多数卡只靠调这里的字段就能做出来（数据驱动）；
    /// 需要自定义判定的卡再填 <see cref="behaviourId"/> 挂脚本钩子。
    /// </summary>
    [CreateAssetMenu(menuName = "TCGinFU/卡牌定义", fileName = "Card_")]
    public sealed class CardDefinitionAsset : ScriptableObject
    {
        [Header("标识")]
        [Tooltip("卡牌唯一 Id。构筑的同名限制按它判定，命名后不要再改")]
        [SerializeField] private string id = "";

        [SerializeField] private string displayName = "";

        [Header("分类")]
        [SerializeField] private CardCategory category = CardCategory.Class;
        [SerializeField] private CardKind kind = CardKind.Skill;

        [Tooltip("所属职业。职业牌 / 升变牌 / 形态牌 / 英雄牌用它配对形态卡组")]
        [SerializeField] private string classId = "";

        [Tooltip("卡牌颜色（§2.2）。填 2 项即为还原双属性检定的双色牌")]
        [SerializeField] private List<AttributeColor> colors = new();

        [Header("费用")]
        [Min(0)] [SerializeField] private int apCost = 1;
        [Min(0)] [SerializeField] private int mpCost = 0;

        [Min(0)]
        [Tooltip("卡面「消耗 X 个充能」词条的 X")]
        [SerializeField] private int chargeCost = 0;

        [Header("结算")]
        [Tooltip("卡面是否带【暴击】标识。只有勾上才投暴击骰（§7.3）")]
        [SerializeField] private bool hasCrit = false;

        [SerializeField] private List<SerializedEffect> effects = new();

        [Tooltip("可选的脚本化行为钩子 Id，需要先在 BehaviourRegistry 里注册")]
        [SerializeField] private string behaviourId = "";

        [Header("装备牌数值")]
        [Tooltip("武器牌的一阶基础伤害（3–10 点，§7.2）")]
        [Min(0)] [SerializeField] private int weaponBaseDamage = 0;

        [Tooltip("防具牌的一阶基础护盾（6–10 点，§7.2）")]
        [Min(0)] [SerializeField] private int armorBaseShield = 0;

        [Header("升变牌")]
        [SerializeField] private TransformTrigger transformTrigger = TransformTrigger.Conditional;

        [TextArea]
        [Tooltip("条件型升变牌的前置条件说明，仅供展示；实际判定走 Behaviour")]
        [SerializeField] private string transformConditionText = "";

        private CardDefinition _cached;

        public string Id => string.IsNullOrEmpty(id) ? name : id;

        /// <summary>
        /// 烘焙成 Core 的 POCO 定义。结果缓存，保证同一资产始终返回同一个实例——
        /// 构筑校验与卡牌实例都按引用持有它。
        /// </summary>
        public CardDefinition ToDefinition()
        {
            return _cached ??= new CardDefinition(Id, string.IsNullOrEmpty(displayName) ? Id : displayName, category, kind)
            {
                Colors = colors.ToArray(),
                ApCost = apCost,
                MpCost = mpCost,
                ChargeCost = chargeCost,
                HasCrit = hasCrit,
                Effects = effects.Select(e => e.ToEffectData()).ToArray(),
                BehaviourId = string.IsNullOrWhiteSpace(behaviourId) ? null : behaviourId,
                ClassId = string.IsNullOrWhiteSpace(classId) ? null : classId,
                WeaponBaseDamage = weaponBaseDamage,
                ArmorBaseShield = armorBaseShield,
                TransformTrigger = transformTrigger,
                TransformConditionText = transformConditionText,
            };
        }

        private void OnValidate()
        {
            // 改了字段就让缓存失效，否则编辑器里调完数值还得重启播放才生效
            _cached = null;

            if (colors.Count > 2)
            {
                Debug.LogWarning($"[{name}] 卡牌最多 2 个颜色（双色牌），当前 {colors.Count} 个", this);
            }
        }
    }
}
