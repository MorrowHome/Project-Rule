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

        /// <summary>
        /// 摩擦力方向反转。
        /// </summary>
        /// <remarks>
        /// <para>与 <see cref="FrictionScale"/> 是**两件事**：缩放改的是摩擦力大小，
        /// 这个标志表达的是「摩擦力的方向反过来」。所以反转生效时
        /// <see cref="FrictionScale"/> 应该保持 1.0，而不是变成负数——
        /// Unity 会把 <c>PhysicsMaterial2D.friction</c> 的负值夹到 0，
        /// 用负数表达反转会退化成「失效」。</para>
        /// <para>具体怎么响应由**读这个值的实体自己决定**，不在规则系统的职责内。</para>
        /// </remarks>
        public bool FrictionInverted;

        /// <summary>质量缩放。1.0 = 正常。</summary>
        public float MassScale;

        /// <summary>
        /// 质量反转：重的变轻、轻的变重。
        /// </summary>
        /// <remarks>
        /// ⚠️ **不能用单一的 <see cref="MassScale"/> 表达** —— 反转是**逐实体**的，
        /// 而缩放是全局倍率。所以这里用标志位，由实体自己按参考质量取倒数。
        /// 与 <see cref="DamageInverted"/> 同一个套路。
        /// </remarks>
        public bool MassInverted;

        /// <summary>伤害缩放。1.0 = 正常。</summary>
        public float DamageScale;

        /// <summary>攻防反转：攻击变回血。</summary>
        public bool DamageInverted;

        /// <summary>伤害失效。</summary>
        public bool DamageDisabled;

        /// <summary>
        /// 伤害挂账：受到的伤害不立即扣血，先记账。
        /// </summary>
        /// <remarks>
        /// <para>规则矩阵（伤害/生命 × 恒定）：「受到的伤害不立即扣血，先记账；
        /// 规则结束时一次性结算。窗口内的治疗可抵消挂账（按净额结算）」。</para>
        ///
        /// <para>⚠️ <b>规则系统只负责置位这个标志并广播，到此为止。</b>
        /// 账本、净额结算、结算时机属于**伤害处理模块**
        /// （<c>Gameplay/Combat/DamageProcessor</c>）—— 账本是逐实体的，
        /// 放不进属性规则类（架构 §4.6 的「行为型规则由属性规则类内部实现」在这里不适用）。</para>
        ///
        /// <para>模块靠**观察本标志 true → false 的翻转**触发结算，
        /// 从而仍然只读 <see cref="IAttributeManager"/>，不必去读 <c>IRuleSystem</c>。</para>
        /// </remarks>
        public bool DamageDeferred;

        // ==================================================================
        //  因果（规则矩阵「因果」一列）
        // ==================================================================

        /// <summary>因果效果缩放。1.0 = 正常。</summary>
        public float CausalityScale;

        /// <summary>
        /// 因果反转：**伤害和治疗的作用对象转换**。
        /// </summary>
        /// <remarks>
        /// 规则矩阵：「攻击敌人自己扣血」。注意它是**一次替换**（承受方 ← 发起方），
        /// **不是反弹** —— 所以不会递归，不需要深度上限。
        /// </remarks>
        public bool CausalityInverted;

        /// <summary>
        /// 因果失效之一：效果倍率与作用对象**都随机**。
        /// </summary>
        /// <remarks>
        /// 规则矩阵：「效果倍率在正常 / 2 倍 / -1 倍中随机（-1 倍 = 效果反向），
        /// 且作用对象也在可命中范围内随机」。掷骰子是**伤害处理模块**的事，
        /// 规则系统只置位。
        /// </remarks>
        public bool CausalityRandom;

        /// <summary>
        /// 因果失效之二：恢复能力不再分敌我，作用于所有人。
        /// </summary>
        /// <remarks>消费方是回能系统（<c>EntityStatus</c> 一侧），不是伤害处理模块。</remarks>
        public bool CausalityRecoveryShared;

        /// <summary>
        /// 因果增强：攻击扩散到周围、治疗扩散到全队。
        /// </summary>
        /// <remarks>
        /// 扩散目标由**伤害处理模块**自己查对象注册表得到
        /// （发起方只填精确命中的结果，拿不到候选集）。
        /// </remarks>
        public bool CausalitySpread;

        /// <summary>默认值（全部正常）。</summary>
        public static AttributeValues Default => new AttributeValues
        {
            FrictionScale    = 1f,
            FrictionInverted = false,
            MassScale        = 1f,
            MassInverted     = false,
            DamageScale      = 1f,
            DamageInverted   = false,
            DamageDisabled   = false,
            DamageDeferred   = false,
            CausalityScale   = 1f,
            CausalityInverted        = false,
            CausalityRandom          = false,
            CausalityRecoveryShared  = false,
            CausalitySpread          = false,
        };
    }
}
