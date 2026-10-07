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
    /// <para>写入只能通过 <see cref="Request"/>，且必须经由 <c>RuleReleaseService</c>。</para>
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
        //  重开关卡
        // ------------------------------------------------------------------

        /// <summary>清空所有规则并重置属性管理器。</summary>
        void ClearAll();

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
