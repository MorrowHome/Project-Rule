using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 推动探测：每物理帧查一次「我正在顶着的那个东西能不能推」（处理顺序的 ⑪），
    /// 能推就把**力**交给它。只出力，不挪自己。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么报力、不报速度</b>：力 ÷ 对方的质量才是加速度 —— 与 <c>WindSensor</c>
    /// 的「世界报力、实体自己除以质量」同一条分工。于是「质量 × 增强」把箱子变重之后，
    /// 这里一个字都不用改，箱子自然就推不动了。</para>
    ///
    /// <para><b>为什么方向取朝向、不取按键</b>：方向要跟**人实际往哪走**。
    /// 「摩擦 × 反转」生效时按右是往左走（见 <c>VelocitySolver.ResolveMoveDirection</c>），
    /// 用按键方向的话，玩家明明在往左顶箱子、探头却朝右伸，推不动。
    /// <see cref="MotionMover.Facing"/> 取的就是换算后的朝向，所以这里读它。</para>
    ///
    /// <para><b>为什么主动查询、不用碰撞回调</b>：玩家是 Kinematic 刚体、位移走
    /// <c>Rigidbody2D.Slide</c>（`useAttachedTriggers = false`），回调不可靠。
    /// 与 <c>ContactSensor</c> / <c>WindSensor</c> 同一个做法。</para>
    ///
    /// <para>⚠️ <b>它不改变玩家自己的速度</b>：推不动的时候是 <c>Slide</c> 正常把玩家挡在箱子
    /// 前面 —— 这正是「太重推不动 = 过不去」的表现，不需要额外处理。本类一个像素都不挪玩家。</para>
    /// </remarks>
    public sealed class PushSensor
    {
        /// <summary>低于这个输入量算「没按方向」，不推。</summary>
        private const float InputEpsilon = 0.01f;

        private readonly Rigidbody2D _body;
        private readonly RaycastHit2D[] _hits = new RaycastHit2D[4];

        public PushSensor(Rigidbody2D body) => _body = body;

        /// <summary>本物理帧推到了谁。<c>null</c> = 没顶到可推物体。供调试浮层用。</summary>
        public IPushable Target { get; private set; }

        /// <summary>本物理帧推的方向。没推时为 <see cref="Vector2.zero"/>。供调试浮层用。</summary>
        public Vector2 Direction { get; private set; }

        /// <summary>
        /// 跑一次探测并施力。**每物理帧一次**，在位移解算之后（⑪）。
        /// </summary>
        /// <param name="tuning">本帧参数。读 <c>PushStrength</c> / <c>PushProbeDistance</c> / <c>SolidLayer</c>。</param>
        /// <param name="moveInput">本帧原始输入。**只用来判「按没按方向」**。</param>
        /// <param name="facing">换算后的朝向（<see cref="MotionMover.Facing"/>）。推的方向取它。</param>
        /// <returns>真的推到了东西。</returns>
        public bool Push(MotionTuning tuning, Vector2 moveInput, float facing)
        {
            Target = null;
            Direction = Vector2.zero;

            if (Mathf.Abs(moveInput.x) < InputEpsilon) return false;   // 没按方向 = 没在顶东西
            if (tuning.PushStrength <= 0f) return false;              // 0 = 这个实体不会推

            Vector2 direction = facing >= 0f ? Vector2.right : Vector2.left;

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(tuning.SolidLayer);

            int count = _body.Cast(direction, filter, _hits, tuning.PushProbeDistance);
            for (int i = 0; i < count; i++)
            {
                // ⚠️ 必须判这一条：`Rigidbody2D.Cast` 在形状已经与某个碰撞体接触时，
                //    会把它当成 distance = 0 的命中返回，**不管朝哪个方向扫**。
                //    不判的话，站在箱子上按方向键就会去推脚下的箱子（斜着把它推跑）。
                //    判据与 ContactSensor 的墙面探测一致：法线要**顶着**扫描方向。
                if (Vector2.Dot(_hits[i].normal, direction) > -0.5f) continue;

                // 走接口而不是具体类型：推的人不认识任何场景组件。
                var pushable = _hits[i].collider.GetComponentInParent<IPushable>();
                if (pushable == null) continue;

                pushable.ReceivePush(direction * tuning.PushStrength);
                Target    = pushable;
                Direction = direction;
                return true;
            }

            return false;
        }
    }
}
