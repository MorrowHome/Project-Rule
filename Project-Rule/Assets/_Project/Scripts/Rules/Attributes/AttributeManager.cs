using RuleGame.Infrastructure.Rules;

namespace RuleGame.Core.Rules
{
    /// <summary>属性管理器：属性的外部事实来源。</summary>
    public class AttributeManager : IAttributeManager
    {
        public AttributeValues Values { get; private set; }

        public AttributeManager() => ResetToDefault();

        public void ResetToDefault() => Values = AttributeValues.Default;

        public void RestoreValues(AttributeValues values) => Values = values;

        // ⚠️ AttributeValues 是 struct：必须「读出来改再写回」，
        //    不能写 Values.FrictionScale = v（编译不过）。
        public void SetFrictionScale(float v) { var x = Values; x.FrictionScale = v; Values = x; }
        public void SetMassScale(float v)     { var x = Values; x.MassScale     = v; Values = x; }
        public void SetDamageScale(float v)   { var x = Values; x.DamageScale   = v; Values = x; }
        public void SetDamageInverted(bool v) { var x = Values; x.DamageInverted = v; Values = x; }
        public void SetDamageDisabled(bool v) { var x = Values; x.DamageDisabled = v; Values = x; }
    }
}
