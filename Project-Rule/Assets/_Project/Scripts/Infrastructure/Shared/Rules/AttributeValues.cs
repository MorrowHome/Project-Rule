namespace RuleGame.Infrastructure.Rules
{
    /// <summary>
    /// 属性管理器的值。
    /// </summary>
    /// <remarks>
    /// **只保存「没有引擎全局载体」的属性**：
    /// 重力直接写 <c>Physics2D.gravity</c>、时间速率直接写 <c>Time.timeScale</c>，
    /// 都不在这里。
    /// </remarks>
    public struct AttributeValues
    {
        /// <summary>摩擦力缩放。1.0 = 正常。</summary>
        public float FrictionScale;

        /// <summary>质量缩放。1.0 = 正常。</summary>
        public float MassScale;

        /// <summary>伤害缩放。1.0 = 正常。</summary>
        public float DamageScale;

        /// <summary>攻防反转：攻击变回血。</summary>
        public bool DamageInverted;

        /// <summary>伤害失效。</summary>
        public bool DamageDisabled;

        /// <summary>默认值（全部正常）。</summary>
        public static AttributeValues Default => new AttributeValues
        {
            FrictionScale = 1f,
            MassScale    = 1f,
            DamageScale  = 1f,
        };
    }
}
