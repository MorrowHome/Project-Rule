namespace RuleGame.Infrastructure.Rules
{
    /// <summary>
    /// 规则维护表里一个槽位的内容。
    /// </summary>
    /// <remarks>
    /// 每个属性同时最多只有一条规则生效（属性词条每种只有一个）。
    /// </remarks>
    public struct ActiveRule
    {
        /// <summary>当前生效的关系。</summary>
        public RelationId Relation;

        /// <summary>剩余生效时间（秒）。**&lt; 0 表示关卡内永久**。</summary>
        public float Remaining;

        /// <summary>规则来源。仅用于表现层。</summary>
        public RuleSource Source;
    }
}
