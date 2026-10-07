using UnityEngine;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Core.Rules
{
    /// <summary>一条规则的静态定义。所有数值由策划在这里调，不要硬编码到算法里。</summary>
    [CreateAssetMenu(menuName = "规则游戏/RuleDef", fileName = "NewRuleDef")]
    public class RuleDef : ScriptableObject
    {
        [Header("标识")]
        public AttributeId Attribute;
        public RelationId  Relation;

        [Header("数值")]
        [Tooltip("生效时长（秒）。<= 0 表示关卡内永久。")]
        public float Duration = 10f;

        [Tooltip("生效消耗的能量。这是唯一的费用字段。")]
        public float ActivationCost = 20f;

        [Tooltip("冷却秒数。0 表示无冷却。")]
        public float Cooldown = 0f;

        [Header("显示")]
        public string DisplayName;

        [TextArea(2, 4)]
        public string Description;

        /// <summary>规则 Id，形如 "gravity.revert"。存档与历史记录用。</summary>
        public string Id => $"{Attribute}.{Relation}".ToLowerInvariant();
    }
}
