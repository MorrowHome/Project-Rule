using UnityEngine;

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

        /// <summary>被**攻击盒 / 投射物打中**了（边沿事件，没有「离开」这回事）。</summary>
        /// <param name="sourcePoint">**打过来的东西在哪儿**（世界坐标）—— 玩家传自己的位置，投射物传自己那一刻的位置。
        /// 多档拉杆靠它决定往哪边扳：站在左边打、中间打、右边打，停在不同档。</param>
        /// <remarks>
        /// <para>与 <see cref="PressByContact"/> 的区别：那一条是「压住 → 离开」的**保持**语义
        /// （石头 / 敌人 / 巨石压在压板上），摇杆靠它做压板；这一条是「打一下就响」，
        /// 没有谁一直压着我，所以只能是一次性的边沿。</para>
        /// <para><b>切换型开关（<c>Toggle = 1</c>）在这里翻转</b>；单次锁定的按钮仍然只按下。</para>
        /// <para>⚠️ 调用方负责**一次只调一次** —— 同一发投射物反复扫到同一个 Trigger 是常态
        /// （16 m/s 一帧走 0.32 m，摇杆触发区 0.65 m 宽），所以两边都各自去重。</para>
        /// </remarks>
        void ImpactByContact(Vector2 sourcePoint);
    }
}
