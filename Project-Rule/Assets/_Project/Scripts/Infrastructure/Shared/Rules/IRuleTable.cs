using System.Collections.Generic;

namespace RuleGame.Infrastructure.Rules
{
    /// <summary>
    /// 规则维护表：7 个固定槽位，每属性一个。
    /// 同时是**规则剩余时间的唯一持有者**。
    /// </summary>
    /// <remarks>
    /// 这是**只读查询**接口。写入只能通过 <see cref="IRuleSystem.Request"/>。
    /// </remarks>
    public interface IRuleTable
    {
        /// <summary>该属性当前是否有规则生效。</summary>
        bool IsActive(AttributeId attr);

        /// <summary>该属性当前生效的是否就是这条关系。</summary>
        bool IsActive(AttributeId attr, RelationId rel);

        /// <summary>取该属性的槽位；无规则时返回 null。</summary>
        ActiveRule? GetSlot(AttributeId attr);

        /// <summary>全部槽位的只读投影（供 UI 遍历）。</summary>
        IReadOnlyDictionary<AttributeId, ActiveRule> Slots { get; }

        // ------------------------------------------------------------------
        //  以下两个方法供快照系统直接读写。
        //  这是「接口层下沉到 Infrastructure」的原因——快照系统需要它们。
        // ------------------------------------------------------------------

        /// <summary>复制一份槽位快照。返回的数组可安全保存。</summary>
        ActiveRule?[] SnapshotSlots();

        /// <summary>用快照覆盖当前槽位。数组长度必须与槽位数一致。</summary>
        void RestoreSlots(ActiveRule?[] slots);
    }
}
