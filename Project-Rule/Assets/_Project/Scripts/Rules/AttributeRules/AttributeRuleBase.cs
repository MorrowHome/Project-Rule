using RuleGame.Infrastructure.Rules;

namespace RuleGame.Core.Rules
{
    /// <summary>
    /// 属性规则类基类。6 个属性各实现一个。
    /// </summary>
    /// <remarks>
    /// <para>基类**不设 AttributeId 属性** —— 类名本身就是标识。</para>
    /// <para>基类**不接收 RuleDef** —— 只接收关系枚举。</para>
    /// <para>基类**不持有计时器** —— 剩余时间由规则维护表持有。</para>
    /// <para>基类**持有 <see cref="IAttributeManager"/>** —— 没有引擎全局载体的属性
    /// （摩擦 / 质量 / 伤害）必须通过它写值。有全局载体的（重力 / 时间）拿到也不用。</para>
    /// </remarks>
    public abstract class AttributeRuleBase
    {
        /// <summary>属性管理器。规则类通过它写属性值。</summary>
        /// <remarks>
        /// 🔒 <b>构造函数注入（决策 2026-10-12）</b>：曾有一版用 <c>Bind()</c> 事后注入，
        /// 已废弃 —— 构造函数注入让「忘了注入」变成编译错误，而不是运行时 NRE。
        /// 子类必须写一行转发构造，用不到属性管理器的（重力）也照写。
        /// </remarks>
        protected IAttributeManager Attributes { get; }

        /// <param name="attributes">由 <c>RuleSystem</c> 注入，与它自己用的是同一个实例。</param>
        protected AttributeRuleBase(IAttributeManager attributes)
        {
            Attributes = attributes ?? throw new System.ArgumentNullException(nameof(attributes));
        }

        // ------------------------------------------------------------------
        //  五种关系算法
        //
        //  返回 false = 「这个格位存在，但算法跑不通」→ RuleRequestStatus.NoResource。
        //
        //  ⚠️ 「格位在矩阵里不存在」是**另一回事**，由 IRuleCatalog.Get 返回 null 决定，
        //  RuleSystem 在那一步就会返回 Undefined，根本走不到规则类。
        // ------------------------------------------------------------------

        protected abstract bool OnRevert();
        protected abstract bool OnNullify();
        protected abstract bool OnConstant();
        protected abstract bool OnEnhance();
        protected abstract bool OnWeaken();

        /// <summary>
        /// 统一入口：路由到对应的算法。
        /// </summary>
        /// <returns>算法是否成功。失败时调用方（RuleSystem）会回滚槽位。</returns>
        public RuleRequestResult Request(RelationId rel)
        {
            bool ok = rel switch
            {
                RelationId.Revert   => OnRevert(),
                RelationId.Nullify  => OnNullify(),
                RelationId.Constant => OnConstant(),
                RelationId.Enhance  => OnEnhance(),
                RelationId.Weaken   => OnWeaken(),
                _                   => false,
            };

            return ok
                ? RuleRequestResult.Ok()
                : RuleRequestResult.Fail(RuleRequestStatus.NoResource);
        }

        /// <summary>每帧维护。行为型规则（时间恒定 / 反转）覆写它。</summary>
        public virtual void Tick(float deltaTime) { }

        /// <summary>
        /// 按规则表当前状态<b>静默重施</b>效果。快照恢复后由 <c>RuleSystem.ReapplyActiveEffects()</c> 调用。
        /// </summary>
        /// <remarks>
        /// <para>默认实现：槽位存在 → <see cref="Request"/> 一次（幂等写值）；槽位为空 → <see cref="Clear"/>。</para>
        /// <para>⚠️ <b>行为型规则必须覆写</b>：时间规则的「反转」会执行一次回溯、
        /// 「恒定」会 Pin 一张新快照 —— 直接 <c>Request</c> 会造成副作用。见 <c>TimeRule.Reapply</c>。</para>
        /// <para>本方法**不触发任何事件**（事件由 <c>RuleSystem</c> 在 <c>Request</c> 路径上广播）。</para>
        /// </remarks>
        public virtual void Reapply(ActiveRule? slot)
        {
            if (slot.HasValue) Request(slot.Value.Relation);
            else Clear();
        }

        /// <summary>
        /// 规则到期 / 被覆盖 / 重开关卡时调用。
        /// </summary>
        /// <remarks>⚠️ 必须把世界恢复原样。这是最容易漏的一步。</remarks>
        public virtual void Clear() { }
    }
}
