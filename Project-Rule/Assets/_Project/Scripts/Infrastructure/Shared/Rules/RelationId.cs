namespace RuleGame.Infrastructure.Rules
{
    /// <summary>
    /// 规则系统的 5 种关系（已砍掉「循环」）。
    /// </summary>
    public enum RelationId
    {
        Revert,     // 反转
        Nullify,    // 失效
        Constant,   // 恒定
        Enhance,    // 增强
        Weaken,     // 削弱
    }
}
