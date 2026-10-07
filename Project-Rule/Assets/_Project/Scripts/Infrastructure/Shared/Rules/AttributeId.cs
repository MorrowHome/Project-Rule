namespace RuleGame.Infrastructure.Rules
{
    /// <summary>
    /// 规则系统的 7 个属性（已砍掉「空间」和「方向」）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 枚举顺序即规则维护表的槽位下标。**不要调整顺序**，
    /// 也不要插入新值——那会让已存档的规则对不上槽位。
    /// </remarks>
    public enum AttributeId
    {
        Gravity,    // 重力
        Time,       // 时间
        Friction,   // 摩擦
        Mass,       // 质量
        Relation,   // 关系
        Damage,     // 伤害/生命
        Causality,  // 因果
    }
}
