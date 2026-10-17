using UnityEngine;

namespace RuleGame.Gameplay.Common
{
    /// <summary>
    /// 会自己移动的**支撑面**：站在它上面的实体会被它的速度带走。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它</b>：被承载方的位移是自己解算的（<c>Rigidbody2D.Slide</c>），
    /// 引擎不会把「脚下那块地在动」告诉它 —— 只能每个物理帧自己问一次。
    /// 有了这个接口，被承载方就**不需要认识**具体是哪个场景组件（滑板车、升降台、传送带……），
    /// 谁实现了它谁就能带人。</para>
    ///
    /// <para><b>为什么给速度而不是位移</b>：调用方要把这个值加进自己的速度再交给
    /// <c>Slide</c> 解算，这样「被平台顶着撞到墙」仍然会被正常挡住。
    /// 直接搬位移会绕过碰撞。</para>
    ///
    /// <para><b>它是被动承载</b>：只有**踩在上面**的实体读得到它。
    /// 平台不会去主动推谁，也不会把速度转成动量 —— 唯一的效果是「脚底跟着面一起走」。
    /// 所以「跳离平台」不用额外处理：离开支撑面就读不到了，惯性由实体自己的速度决定。</para>
    /// </remarks>
    public interface IMovingSurface
    {
        /// <summary>
        /// 本物理帧这个面的速度（单位 / 秒）。静止时为 <see cref="Vector2.zero"/>。
        /// </summary>
        /// <remarks>⚠️ 必须与调用方处于**同一个物理步**才有意义 —— 两边都按
        /// <c>Time.fixedDeltaTime</c> 推进时，「面走多远」与「被带走多远」严格相等，不会累积漂移。</remarks>
        Vector2 Velocity { get; }
    }
}
