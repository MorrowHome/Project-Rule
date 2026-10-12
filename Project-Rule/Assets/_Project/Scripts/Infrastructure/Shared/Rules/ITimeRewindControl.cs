namespace RuleGame.Infrastructure.Rules
{
    /// <summary>
    /// 时间恒定（定点回溯窗口）的**控制入口**。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么要单独一个接口</b>：恒定需要「独占一个键」来触发回溯，
    /// 而 <c>RuleGame.Core</c> 不引用 <c>Unity.InputSystem</c>，读不到输入 ——
    /// 按键检测归 Gameplay，由它调这里。</para>
    /// <para>不并入 <see cref="IRuleSystem"/>：那个门面的定位是「不认识具体属性」，
    /// 塞进时间专属的方法会破坏它。</para>
    /// <para>放在 <c>Infrastructure/Shared/Rules/</c>，Gameplay 与 Core 都能看到（D6）。</para>
    /// <para>取用方式：<c>RuleSystem.Instance is ITimeRewindControl tsc</c>。</para>
    /// </remarks>
    public interface ITimeRewindControl
    {
        /// <summary>回溯窗口是否开着（恒定规则生效中且定点快照就绪）。</summary>
        bool IsWindowOpen { get; }

        /// <summary>
        /// 回溯到恒定开启时那一张定点快照。
        /// </summary>
        /// <remarks>
        /// <b>窗口内不限次数、不耗能</b>（§4.10：恒定的限制是时间窗口，不是能量）。
        /// 回溯不退能量（<c>RestoreMode.KeepEnergy</c>）。
        /// </remarks>
        /// <returns>窗口没开或快照系统不可用时返回 false。</returns>
        bool TryRewind();
    }
}
