namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// 快照恢复的范围。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要分两种</b>：关卡存档点与时间系能力共用同一套快照系统，
    /// 但能量在两者里的地位不同 —— 存档点要保存能量，时间系回溯**不该退能量**。</para>
    /// <para>采集侧不分模式（<c>Capture</c> 照常记能量，存档点要用），
    /// 区别只在恢复时写不写回。</para>
    /// </remarks>
    public enum RestoreMode
    {
        /// <summary>全部写回，含能量。关卡存档点用。</summary>
        Full,

        /// <summary>除能量外全部写回。时间系能力用 —— 回溯不退能量。</summary>
        KeepEnergy,
    }
}
