namespace RuleGame.Infrastructure.Common
{
    /// <summary>
    /// 「能把自己的规则属性重新落到自身」的实体。
    /// </summary>
    /// <remarks>
    /// <para>规则效果平时由实体订阅 <c>OnRulesChanged</c> 自读施加；
    /// 但<b>快照恢复不触发任何规则事件</b>（见 <c>ISnapshotSystem</c> 契约），
    /// 所以恢复后必须由 <c>IRuleSystem.ReapplyActiveEffects()</c> 遍历注册表，
    /// 对每个实现了本接口的实体调一次 <see cref="ApplyRuleValues"/>。</para>
    /// <para>实现者：<c>RuleAffectedBody</c>（质量 / 摩擦）。重力有引擎全局载体，不走这里。</para>
    /// </remarks>
    public interface IRuleApplier
    {
        /// <summary>读 <c>IAttributeManager</c> 当前值并落到自身。</summary>
        void ApplyRuleValues();
    }
}
