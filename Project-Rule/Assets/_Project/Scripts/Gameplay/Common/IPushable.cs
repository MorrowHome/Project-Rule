using UnityEngine;

namespace RuleGame.Gameplay.Common
{
    /// <summary>
    /// 可被推动的物体：**受力方**。推的人报一个力，物体自己决定怎么响应。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么给力、不给位移或速度</b>：与 <see cref="IWindField"/> 同一条分工 ——
    /// 世界报**力**，「除以自己的质量」是每个实体自己的事。推的人于是不需要知道对方有多重，
    /// 「质量 × 增强」把箱子变重之后，同一个推力自然就推不动它了。</para>
    ///
    /// <para><b>为什么是接口</b>：推的人（玩家 / 敌人 / 机关）不需要认识具体是哪个场景组件，
    /// 谁实现了它谁就能被推 —— 与 <see cref="IMovingSurface"/> / <see cref="ISpringSurface"/>
    /// 同一套做法。</para>
    ///
    /// <para><b>它是持续力，不是一次性冲量</b>：调用方每物理帧在接触期间调一次。
    /// 于是「推不推得动」由推力与摩擦的**平衡**决定（判据 ≈ <c>F &gt; μ·m·g</c>），
    /// 而不是由某一次判定决定 —— 推力小一点是缓慢挪动，大一点是被推开。</para>
    /// </remarks>
    public interface IPushable
    {
        /// <summary>
        /// 施加一个**力**（牛顿）。
        /// </summary>
        /// <param name="force">世界坐标下的力向量：方向是推的方向，模长是推力大小。</param>
        void ReceivePush(Vector2 force);
    }
}
