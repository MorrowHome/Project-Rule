namespace RuleGame.Infrastructure
{
    /// <summary>
    /// 提供自己阵营的实体。
    /// </summary>
    /// <remarks>
    /// <para>由 <c>EntityDamageable</c> 实现 ——「可攻击实体」的定义本来就等于
    /// 「有 <see cref="IDamageable"/> 的实体」，不必新增一个组件去挂阵营。</para>
    /// <para>消费方是**伤害处理模块**：算因果扩散时用它过滤目标。</para>
    /// </remarks>
    public interface IFactionMember
    {
        Faction Faction { get; }
    }
}
