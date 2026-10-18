namespace RuleGame.Gameplay.Common
{
    /// <summary>
    /// 被开关（<c>CitySwitch</c>：CityButton / 摇杆）驱动的**东西**。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决什么</b>：<c>CitySwitch.FollowMotor</c> 原来只认 <c>CityPathMotor</c>
    /// （门 / 电梯 / 站间列车）。巨石也要能被同一条线驱动，但它不是路径运动。
    /// 把字段放宽成 <c>MonoBehaviour</c> + 这个接口之后，**拖什么上去就驱动什么** ——
    /// 配法与 Gate 完全一样（把目标拖到按钮的 FollowMotor 上）。</para>
    ///
    /// <para>它住在 <c>Gameplay/Common</c>，与 <c>IPushable</c> / <c>IContactSwitch</c> /
    /// <c>IMovingSurface</c> 同一层 —— 组件之间**唯一的耦合点**。</para>
    /// </remarks>
    public interface ISwitchTarget
    {
        /// <summary>开关被打开 / 关闭。</summary>
        void SetSwitchState(bool on);
    }
}
