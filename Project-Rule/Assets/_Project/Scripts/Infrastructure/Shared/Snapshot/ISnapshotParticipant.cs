namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// 扩展实体的内存快照。调用方先恢复物理和 EntityStatus，再恢复模块状态。
    /// 用于 AI 阶段/计时/移动速度等，不修改原有规则槽位、能量恢复策略。
    /// </summary>
    public interface ISnapshotParticipant
    {
        object CaptureSnapshotState();
        void RestoreSnapshotState(object state);
        bool DiscardWhenMissing { get; }
    }
}
