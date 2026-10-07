namespace RuleGame.Infrastructure.Rules
{
    /// <summary>
    /// 属性管理器：属性的**外部事实来源**。
    /// </summary>
    /// <remarks>
    /// 这是**只读查询 + 写入**接口，但写入只应由属性规则类调用。
    /// 外部系统读 <see cref="Values"/> 即可。
    /// </remarks>
    public interface IAttributeManager
    {
        /// <summary>当前属性值。</summary>
        AttributeValues Values { get; }

        void SetFrictionScale(float v);
        void SetMassScale(float v);
        void SetDamageScale(float v);
        void SetDamageInverted(bool v);
        void SetDamageDisabled(bool v);

        /// <summary>全部恢复默认值。重开关卡 / 规则全清时调用。</summary>
        void ResetToDefault();

        /// <summary>用快照覆盖当前值。供快照系统调用。</summary>
        void RestoreValues(AttributeValues values);
    }
}
