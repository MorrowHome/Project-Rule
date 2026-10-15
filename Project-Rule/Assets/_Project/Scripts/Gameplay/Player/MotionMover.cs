using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 身体：**唯一持有速度的地方**。负责位移与碰撞响应（⑧ 击退衰减 · ⑨ Slide · ⑩ 撞墙撞顶），
    /// 以及暂停清零。
    /// </summary>
    /// <remarks>
    /// <para><b>它不决定速度是多少</b> —— 那是 <see cref="VelocitySolver"/> 的返回值，
    /// 经 <see cref="Apply"/> 落到身上。于是「谁写速度」只剩这一个类，
    /// 冲刺结束那条例外也消失了（系数由 <see cref="DashSolver"/> 给、由解算器应用）。</para>
    /// <para><b>Kinematic 刚体的配置也住在这里</b>：位移完全由代码决定，手感可控。
    /// 代价是引擎不再替我们挡住静态地形 —— <c>MovePosition</c> 自己不处理碰撞 ——
    /// 所以位移必须走 <see cref="Rigidbody2D.Slide"/>：它按 <c>surfaceUp</c> 解析接触面角度、
    /// 在设定斜率内沿面滑行、并多次叠代处理拐角。</para>
    /// <para>换个实体复用本类时，这套刚体配置会自动跟着走 —— 这正是把它放在这里的原因。</para>
    /// </remarks>
    public sealed class MotionMover
    {
        private readonly Rigidbody2D _body;

        private Vector2 _velocity;          // 自己维护的速度：重力 / 输入 / 跳跃
        private Vector2 _externalVelocity;  // 击退等外部冲量，独立衰减，不掺进重力与摩擦
        private float   _facing = 1f;
        private bool    _wallSliding;

        public MotionMover(Rigidbody2D body)
        {
            _body = body;
            ConfigureBody();
        }

        /// <summary>当前速度（自己维护的那个，**不含**击退等外部冲量）。</summary>
        /// <remarks>给表现层用（动画判上升/下降、跑步快慢）。玩法逻辑不要读它做判断 ——
        /// 那样会把「速度怎么算」泄漏到外面。</remarks>
        public Vector2 Velocity => _velocity;

        /// <summary>当前朝向：+1 右，−1 左。</summary>
        public float Facing => _facing;

        /// <summary>是否正在贴墙缓慢下滑。</summary>
        public bool WallSliding => _wallSliding;

        /// <summary>把解算结果落到身上。**每物理帧唯一一次写速度。**</summary>
        public void Apply(VelocitySolver.Result solved)
        {
            _velocity    = solved.Velocity;
            _facing      = solved.Facing;
            _wallSliding = solved.WallSliding;
        }

        /// <summary>⑧⑨⑩ 击退衰减 → 位移 → 撞墙撞顶清分量。</summary>
        public void Move(MotionTuning tuning, ContactSensor.State contact, float dt)
        {
            // ⑧ 击退：外部速度独立衰减
            _externalVelocity = Vector2.MoveTowards(_externalVelocity, Vector2.zero,
                                                    tuning.KnockbackDecay * dt);

            Vector2 slideVelocity = _velocity + _externalVelocity;

            // ⑨′ 顶墙分量预夹：已知哪边贴着墙，就把「顶进去」的那一份先夹掉。
            //
            //    ⚠️ 这一条不是优化，是**必须的兜底**。`Rigidbody2D.Slide` 一旦在移动过程中
            //       碰到一个「不能沿其滑动」的面（surfaceSlideAngle 挡不住的竖墙、或**墙的下沿拐角**
            //       这种斜法线接触），它会**把整笔位移取消** —— 不只是顶墙那一份，连沿墙下滑的
            //       竖直分量一起丢掉；**而且照样报 `remainingVelocity = (0,0)`**，于是 ⑩ 以为
            //       「移动成功了」，不清零速度 → 表现就是「速度满速、位置不动」，代码里没人发现。
            //       实测（2026-10-15）：真实左墙下沿 y=1.72、玩家 y=1.4473 时必然冻结；
            //       同一位置把 x 夹成 0 就恢复正常。加叠代次数（8/16/32）无效。
            //
            //    墙的法向分量本来就过不去，留着只会让 Slide 走那条取消路径，所以夹掉它没有副作用。
            if (contact.WallLeft  && slideVelocity.x < 0f) slideVelocity.x = 0f;
            if (contact.WallRight && slideVelocity.x > 0f) slideVelocity.x = 0f;

            var result = _body.Slide(slideVelocity, dt, BuildSlideMovement(tuning, contact));

            // ⑩ 撞墙 / 撞顶：Slide 把用不掉的速度还回来，清掉对应分量，避免贴着墙持续挤压
            //    ⚠️ 这条判据只在「Slide 真的动了」时才可信 —— 它取消整笔位移时也返回 0，
            //       那种情况由 ⑨′ 提前挡掉。
            if (Mathf.Abs(result.remainingVelocity.x) > 0.001f) _velocity.x = 0f;
            if (Mathf.Abs(result.remainingVelocity.y) > 0.001f) _velocity.y = 0f;
        }

        /// <summary>
        /// 外部冲量入口（击退）。见 <c>IKnockbackReceiver</c>。
        /// </summary>
        /// <remarks><b>不能</b>直接写 <c>Rigidbody2D.linearVelocity</c>：本类每个物理帧都会整体
        /// 覆盖速度，那样写进去的冲量活不过一帧。</remarks>
        public void AddImpulse(Vector2 impulse) => _externalVelocity += impulse;

        /// <summary>
        /// 暂停：速度清零。
        /// </summary>
        /// <remarks>不清的话重力会在暂停的每一帧继续堆积，恢复的瞬间一次性位移出去。</remarks>
        public void Halt()
        {
            _velocity         = Vector2.zero;
            _externalVelocity = Vector2.zero;
        }

        /// <summary>Kinematic + 自己解算。gravityScale 置 0：重力在 <see cref="VelocitySolver"/> 里自己积分。</summary>
        private void ConfigureBody()
        {
            _body.bodyType   = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.linearDamping = 0f;
            _body.constraints = RigidbodyConstraints2D.FreezeRotation;   // 平台角色不翻滚

            // 插值：物理是 50Hz 定步长，渲染帧率更高。不开插值的话 Transform 只在物理帧更新，
            // 摄像机会很顺、角色却在"跳格子"（横向移动时最明显）。
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;

            // Kinematic 与静态体 / 推动物之间也要产生接触
            _body.useFullKinematicContacts = true;
        }

        /// <summary>每次物理帧的 Slide 配置。</summary>
        private Rigidbody2D.SlideMovement BuildSlideMovement(MotionTuning tuning, ContactSensor.State contact)
        {
            // 上升途中不加吸附，否则起跳会被按回地面。
            bool anchored = contact.Grounded && Vector2.Dot(_velocity, contact.Up) <= 0.01f;

            return new Rigidbody2D.SlideMovement
            {
                surfaceUp         = contact.Up,
                surfaceSlideAngle = tuning.SurfaceSlideAngle,
                gravitySlipAngle  = 0f,                  // 不沿斜面自动下滑
                gravity           = Vector2.zero,        // 重力已在 VelocitySolver 里自己积分
                maxIterations     = 4,                   // 拐角要几次叠代才滑得干净
                surfaceAnchor     = anchored ? -contact.Up * tuning.SurfaceAnchorDistance : Vector2.zero,
                useLayerMask      = true,
                layerMask         = tuning.SolidLayer,
                useAttachedTriggers = false,
                useSimulationMove = true,                // 交给模拟步生效：暂停（不 Simulate）时自然静止
            };
        }
    }
}
