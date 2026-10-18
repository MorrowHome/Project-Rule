namespace RuleGame.Gameplay.Common
{
    /// <summary>
    /// 「被东西**碰到**就算按下」的开关。
    /// </summary>
    /// <remarks>
    /// <para><b>它解决什么</b>：<c>CitySwitch</c>（积木城的按钮 / 摇杆）原本只认**玩家交互**
    /// （<c>CityInteractor</c> → <c>CityInteractable.Interact</c>）。但关卡里还有一类开关是
    /// **被重物压到**才响的 —— 巨石 / 铁箱 / 弹珠。那些东西不认识 <c>CityInteractable</c>，
    /// 也不该认识（它们不是玩家）。于是把「碰到我 = 按下我」抽成这个接口。</para>
    ///
    /// <para>它住在 <c>Gameplay/Common</c>，与 <c>IPushable</c> / <c>IMovingSurface</c> /
    /// <c>ISpringSurface</c> 同一个位置 —— 组件之间**唯一的耦合点**。</para>
    ///
    /// <para><b>谁调它</b>：压过来的那一方（例如 <c>GravityBoulder</c>）在触发 / 接触回调里调；
    /// 开关自己**不扫描**，与「世界报力、实体自己决定」同一条分工。</para>
    /// </remarks>
    public interface IContactSwitch
    {
        /// <summary>被**非玩家**的东西压到了。</summary>
        void PressByContact();

        /// <summary>压着的东西走了。**单次锁定的开关应当忽略它**（按下去就锁住，不会自己弹回来）。</summary>
        void ReleaseByContact();
    }
}
