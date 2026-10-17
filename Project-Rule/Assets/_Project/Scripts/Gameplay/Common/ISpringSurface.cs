using UnityEngine;

namespace RuleGame.Gameplay.Common
{
    /// <summary>
    /// 会**把踩在上面的人弹出去**的支撑面：弹簧平台 / 弹跳板 / 蹦床。
    /// </summary>
    /// <remarks>
    /// <para><b>与 <see cref="IMovingSurface"/> 的分工</b>：那个是「脚底跟着面一起走」——
    /// 位置跟随，**不改变实体自己的速度**，人离开面就没惯性了。这个是「面把实体**推出去**」——
    /// 面给的是一个**速度**，实体带着它离开面继续飞。弹簧平台两者都实现：
    /// 压下去的时候带人一起落，释放的时候把人弹飞。</para>
    ///
    /// <para><b>为什么是接口，而不是让面主动推人</b>：与 <see cref="IMovingSurface"/> /
    /// <see cref="IWindField"/> 同一个模式 —— 实体是 Kinematic 刚体，引擎的力对它无效，
    /// 只能由实体每个物理帧自己查一次「我脚下这个面正在发射吗」。
    /// 于是实体**不认识任何具体弹簧**，谁实现了本接口谁就能弹人。</para>
    ///
    /// <para><b>它是被动探测的</b>：只有**正踩在上面**的实体读得到它。面不会去主动找谁，也不会
    /// 「记着刚才谁踩过」。所以「被弹到一半又落回面上」会被再弹一次 —— 弹簧还在回弹，这是对的。</para>
    ///
    /// <para><b>弹射速度怎么用</b>：由实体在 <c>VelocitySolver</c> 的解算链里当作一步变化量叠加，
    /// **与跳跃同待遇** —— 所以重力、终端速度、撞顶、贴地抵消都照常生效，
    /// 弹簧不会把人送穿天花板。</para>
    /// </remarks>
    public interface ISpringSurface
    {
        /// <summary>
        /// 本物理帧这个面要**把接触者推出去**的速度（单位 / 秒，世界坐标向量）。
        /// 不在发射状态时返回 <see cref="Vector2.zero"/>。
        /// </summary>
        /// <remarks>⚠️ 与 <see cref="IMovingSurface.Velocity"/> 一样，必须与调用方处于
        /// **同一个物理步**才有意义。</remarks>
        Vector2 LaunchVelocity { get; }
    }
}
