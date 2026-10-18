using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 风场探测：每物理帧查一次「我在哪个风场里、受到多大的力」（处理顺序的 ①′），
    /// 除以自己的质量得到**风加速度**。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么除以质量住在这里</b>：风场报的是**力**（世界的性质），
    /// 除以谁的质量是**这个实体自己的事** —— 与 <c>EnemyMotor.AddImpulse</c>
    /// 那句 <c>impulse / mass</c> 同一个分工。玩家质量由 <c>RuleAffectedBody</c>
    /// 按质量规则写进 <c>Rigidbody2D.mass</c>，本类只是读它。</para>
    ///
    /// <para><b>为什么主动查询、不用 trigger 回调</b>：玩家是 Kinematic 刚体，
    /// 位移走 <c>Rigidbody2D.Slide</c>（且 <c>useAttachedTriggers = false</c>），
    /// trigger 回调不可靠。与 <see cref="ContactSensor"/> 的接地探测同一个做法。</para>
    ///
    /// <para>探测点是**碰撞体中心**（<c>collider.bounds.center</c>）——
    /// 身体中心落在场内就受力，与区域边界的判定只差半个身位，摆关卡时够用。</para>
    /// </remarks>
    public sealed class WindSensor
    {
        private readonly Rigidbody2D _body;
        private readonly Collider2D  _bodyCollider;
        private readonly Collider2D[] _hits = new Collider2D[8];

        public WindSensor(Rigidbody2D body, Collider2D bodyCollider)
        {
            _body         = body;
            _bodyCollider = bodyCollider;
        }

        /// <summary>本物理帧的风加速度（合力 ÷ 质量）。没风时为 <see cref="Vector2.zero"/>。</summary>
        public Vector2 Acceleration { get; private set; }

        /// <summary>本物理帧的**合力**（牛顿，尚未除以质量）。供调试浮层显示与调参对账。</summary>
        public Vector2 Force { get; private set; }

        /// <summary>本物理帧的空气阻力系数（每秒速度衰减率，多个风场取和）。风力以外的那一半。</summary>
        public float Drag { get; private set; }

        /// <summary>本物理帧的竖直阻尼系数（每秒速度衰减率，多个风场取和）。只作用于竖直方向。</summary>
        public float VerticalDamping { get; private set; }

        /// <summary>本物理帧是否在某个**生效中**的风场里。</summary>
        /// <remarks>
        /// <para>判据：力 / 空气阻力 / 竖直阻尼有一项非零。**禁用的风场不算**（<see cref="IWindField"/>
        /// 的三个方法在组件禁用时都返回 0）—— 「关掉风场 = 不存在」这条约定在这里也成立。</para>
        /// <para>调用方用它来决定「风场内的重力是否统一为上升重力」（<c>VelocitySolver.GravityDelta</c>）：
        /// 风场内不再区分上升 / 下落重力，否则方向依赖的重力会持续给振荡泵能量。</para>
        /// </remarks>
        public bool InField { get; private set; }

        /// <summary>
        /// 跑一次探测，返回风加速度（单位 / 秒²）。**每物理帧一次**，在接触探测之后、
        /// 速度解算之前。
        /// </summary>
        public Vector2 Sense(MotionTuning tuning)
        {
            Force = Vector2.zero;
            Acceleration = Vector2.zero;
            Drag = 0f;
            VerticalDamping = 0f;
            InField = false;

            // 没配风场层 = 这个实体不响应风。短路掉物理查询。
            if (tuning.WindLayer.value == 0) return Acceleration;

            var filter = new ContactFilter2D { useTriggers = true };   // 风场是 trigger
            filter.SetLayerMask(tuning.WindLayer);

            Vector2 point = _bodyCollider.bounds.center;
            int count = Physics2D.OverlapPoint(point, filter, _hits);

            for (int i = 0; i < count; i++)
            {
                // 走接口而不是具体类型：不认识的场景组件照样吹得动。
                var field = _hits[i].GetComponentInParent<IWindField>();
                if (field == null) continue;

                Vector2 force   = field.ForceAt(point);
                float   drag    = field.DragAt(point);
                float   damping = field.VerticalDampingAt(point);

                Force           += force;
                Drag            += drag;
                VerticalDamping += damping;

                // 三项有一项非零 = 这个风场真的在起作用。禁用的风场三项全 0，不算在场内。
                if (force.sqrMagnitude > 0f || drag > 0f || damping > 0f) InField = true;
            }

            // 力 ÷ 质量。质量下限与 RuleAffectedBody 的 MinMass 一致，防除零。
            Acceleration = Force / Mathf.Max(_body.mass, 0.01f);
            return Acceleration;
        }
    }
}
