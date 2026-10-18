using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 一个物理帧的全部输入：参数 + 意图 + 接触 + 各解算器的结论 + 规则读值。
    /// </summary>
    /// <remarks>
    /// <para>打包它是为了让 <see cref="VelocitySolver"/> <b>不依赖任何对象</b> ——
    /// 速度从参数进、从返回值出，纯函数要的就是这个。</para>
    /// <para>⚠️ <see cref="FrictionScale"/> 与 <see cref="FrictionInverted"/> 由
    /// <see cref="PlayerController"/> 读好放进来，所以 <see cref="VelocitySolver"/>
    /// <b>不认识规则系统</b>：规则怎么算、什么时候变，都不必知道。</para>
    /// </remarks>
    public readonly struct MotionFrame
    {
        /// <summary>本实体的机动参数。</summary>
        public readonly MotionTuning Tuning;

        /// <summary>本物理帧的意图。</summary>
        public readonly PlayerInput.State Input;

        /// <summary>本物理帧的接触状态。</summary>
        public readonly ContactSensor.State Contact;

        /// <summary>跳跃解算的结论。</summary>
        public readonly JumpSolver.Decision Jump;

        /// <summary>冲刺解算的结论。</summary>
        public readonly DashSolver.Decision Dash;

        /// <summary>全局重力（<c>Physics2D.gravity</c>，已被规则改过）。自己积分要的是原始向量。</summary>
        public readonly Vector2 Gravity;

        /// <summary>
        /// 本物理帧的风加速度（风场合力 ÷ 质量）。没风时为 <see cref="Vector2.zero"/>。
        /// </summary>
        /// <remarks>它是**加速度**不是力 —— 除法由 <see cref="WindSensor"/> 做好，
        /// 解算器不必认识 <c>Rigidbody2D.mass</c>。与 <see cref="Gravity"/> 同为「环境给的运动学量」。</remarks>
        public readonly Vector2 WindAccel;

        /// <summary>上一帧末的朝向。冲刺方向、蹬墙方向都要用它。</summary>
        public readonly float Facing;

        /// <summary>
        /// 本物理帧的空气阻力系数（风场给的速度衰减率，每秒）。没风时为 0。
        /// </summary>
        /// <remarks>风力的另一半：没有它，风场里的物体会在平衡点附近永远弹射（实测出场速度 18+）。
        /// 与 <see cref="WindAccel"/> 同命运 —— 冲刺时两者都不生效。</remarks>
        public readonly float WindDrag;

        /// <summary>
        /// 本物理帧的竖直阻尼系数（风场给的速度衰减率，每秒）。没风时为 0。
        /// </summary>
        /// <remarks>**只作用于竖直轴**（与 <see cref="WindDrag"/> 叠加），横向不读它。
        /// 用途是把平衡高度附近的上下弹跳收掉 —— 只有各向同性阻力时竖直方向欠阻尼。</remarks>
        public readonly float WindVerticalDamping;

        /// <summary>本物理帧是否在**生效中**的风场里。在风场里重力不分上升 / 下落。</summary>
        /// <remarks>见 <see cref="VelocitySolver"/>：风场内统一用上升重力，否则「上升轻、下落重」
        /// 这个方向依赖（非保守）的重力会持续给竖直振荡泵能量，形成消不掉的极限环。</remarks>
        public readonly bool WindInField;

        /// <summary>摩擦规则缩放（1.0 = 正常）。</summary>
        public readonly float FrictionScale;

        /// <summary>摩擦方向是否被规则反转（摩擦 × 反转）。</summary>
        public readonly bool FrictionInverted;

        public MotionFrame(MotionTuning tuning, PlayerInput.State input, ContactSensor.State contact,
                           JumpSolver.Decision jump, DashSolver.Decision dash, Vector2 gravity,
                           Vector2 windAccel, float windDrag, float windVerticalDamping,
                           bool windInField, float facing,
                           float frictionScale, bool frictionInverted)
        {
            Tuning               = tuning;
            Input                = input;
            Contact              = contact;
            Jump                 = jump;
            Dash                 = dash;
            Gravity              = gravity;
            WindAccel            = windAccel;
            WindDrag             = windDrag;
            WindVerticalDamping  = windVerticalDamping;
            WindInField          = windInField;
            Facing               = facing;
            FrictionScale        = frictionScale;
            FrictionInverted     = frictionInverted;
        }
    }
}
