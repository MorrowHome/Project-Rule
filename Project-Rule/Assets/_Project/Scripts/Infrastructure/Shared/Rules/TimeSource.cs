namespace RuleGame.Infrastructure.Rules
{
    /// <summary>
    /// 时间倍率的来源标识。时间管理器按来源去重并乘法合成。
    /// </summary>
    public enum TimeSource
    {
        Rule,   // 时间规则（增强 / 削弱）
        UI,     // 界面（如拼装界面暂停）
    }
}
