namespace RuleGame.Infrastructure.Rules
{
    /// <summary>
    /// 规则来源。
    /// </summary>
    /// <remarks>
    /// ⚠️ **仅用于表现层**（例如敌人改规则时屏幕变色）。
    /// 绝不参与优先级判断——单一规则表是「后写覆盖先写」。
    /// </remarks>
    public enum RuleSource
    {
        Player,
        Enemy,
        LevelScript,
    }
}
