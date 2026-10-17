using System;
using System.Collections.Generic;

namespace RuleGame.Infrastructure.Rules
{
    /// <summary>
    /// 规则系统门面。
    /// </summary>
    /// <remarks>
    /// <para><b>它不认识能量，也不认识冷却。</b>（架构约束 C4）</para>
    /// <para>费用检查与扣除在 <c>RuleReleaseService</c> 完成。</para>
    /// <para>写入只能通过 <see cref="Request"/> 与 <see cref="Cancel"/>，
    /// 且必须经由 <c>RuleReleaseService</c>。</para>
    /// </remarks>
    public interface IRuleSystem
    {
        IRuleTable        Table      { get; }
        IAttributeManager Attributes { get; }

        // ------------------------------------------------------------------
        //  写入入口（外部一律用枚举，不传 RuleDef）
        // ------------------------------------------------------------------

        /// <summary>
        /// 请求规则生效。
        /// </summary>
        /// <remarks>
        /// 内部流程固定为：<b>注册 → 执行 → 失败回滚 → 广播</b>。
        /// 调用方**不应**在返回失败时结算费用。
        /// </remarks>
        RuleRequestResult Request(AttributeId attr, RelationId rel, RuleSource source);

        /// <summary>查询一次释放的费用。该格位未定义时返回 <c>default</c>。</summary>
        RuleCost GetCost(AttributeId attr, RelationId rel);

        // ------------------------------------------------------------------
        //  主动结束
        // ------------------------------------------------------------------

        /// <summary>
        /// 主动结束某个属性当前生效的规则（玩家的能力）。
        /// </summary>
        /// <remarks>
        /// <para><b>不消耗能量、不影响冷却</b> —— 取消免费，既不退款也不加罚。</para>
        /// <para>走的是与「超时 / 被覆盖」同一条收尾路径：先让规则类把写在引擎上的外部状态撤掉
        /// （<c>Clear()</c>），再清槽位，然后触发 <see cref="OnRuleExpired"/> 与
        /// <see cref="OnRulesChanged"/>。</para>
        /// <para>该属性没有生效中的规则时<b>什么也不做、也不广播</b>。</para>
        /// </remarks>
        /// <returns>确实结束了一条规则时返回 true。</returns>
        bool Cancel(AttributeId attr);

        // ------------------------------------------------------------------
        //  重开关卡
        // ------------------------------------------------------------------

        /// <summary>清空所有规则并重置属性管理器。</summary>
        void ClearAll();

        // ------------------------------------------------------------------
        //  快照恢复后重建规则效果
        // ------------------------------------------------------------------

        /// <summary>
        /// 按当前规则表<b>静默重施</b>所有规则效果，并让实体重读属性值。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么需要它</b>：快照恢复直接覆盖槽位与属性值，按契约<b>不触发任何规则事件</b>
        /// （<c>OnRuleActivated</c> / <c>OnRuleExpired</c> / <c>OnRulesChanged</c> 全都不发）。
        /// 于是「值回去了，但世界上的效果没回去」—— 重力写在 <c>Physics2D.gravity</c> 上、
        /// 质量 / 摩擦要实体重新读值才落到刚体与材质上。</para>
        /// <para><b>静默的含义</b>：本方法**不触发规则生命周期事件**（不调 <c>Request</c> 的事件路径），
        /// 因此可以安全地被 <c>ISnapshotSystem.Restore</c> 在广播 <c>OnRestored</c> 之前调用。
        /// 需要收尾的表现层仍应订阅 <c>ISnapshotSystem.OnRestored</c>。</para>
        /// <para>实现遍历 6 个规则类按当前槽位重施，再遍历活动实体注册表对
        /// <c>IRuleApplier</c> 调一次 <c>ApplyRuleValues()</c>。</para>
        /// </remarks>
        void ReapplyActiveEffects();

        // ------------------------------------------------------------------
        //  存档
        // ------------------------------------------------------------------

        /// <summary>导出当前生效规则的 Id 列表，供存档使用。</summary>
        List<string> ExportActiveIds();

        // ------------------------------------------------------------------
        //  事件
        // ------------------------------------------------------------------

        /// <summary>规则成功生效后触发。<b>状态已写完才会触发。</b></summary>
        event Action<AttributeId, RelationId, RuleSource> OnRuleActivated;

        /// <summary>规则到期或被覆盖后触发。</summary>
        event Action<AttributeId> OnRuleExpired;

        /// <summary>规则表发生任何变化时触发（供 UI 整体刷新）。</summary>
        event Action OnRulesChanged;
    }
}
