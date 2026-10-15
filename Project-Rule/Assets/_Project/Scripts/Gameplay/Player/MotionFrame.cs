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

        /// <summary>上一帧末的朝向。冲刺方向、蹬墙方向都要用它。</summary>
        public readonly float Facing;

        /// <summary>摩擦规则缩放（1.0 = 正常）。</summary>
        public readonly float FrictionScale;

        /// <summary>摩擦方向是否被规则反转（摩擦 × 反转）。</summary>
        public readonly bool FrictionInverted;

        public MotionFrame(MotionTuning tuning, PlayerInput.State input, ContactSensor.State contact,
                           JumpSolver.Decision jump, DashSolver.Decision dash, Vector2 gravity,
                           float facing, float frictionScale, bool frictionInverted)
        {
            Tuning           = tuning;
            Input            = input;
            Contact          = contact;
            Jump             = jump;
            Dash             = dash;
            Gravity          = gravity;
            Facing           = facing;
            FrictionScale    = frictionScale;
            FrictionInverted = frictionInverted;
        }
    }
}
