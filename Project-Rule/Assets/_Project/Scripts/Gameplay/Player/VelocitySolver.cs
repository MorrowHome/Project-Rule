using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 速度解算：重力 + 终端速度（④）、贴墙下滑（④′）、水平（⑤）、贴地抵消（⑥）、
    /// 跳跃落地（⑦⑧）。
    /// </summary>
    /// <remarks>
    /// <para><b>无状态，且不认识任何对象</b>：速度从参数进、从返回值出。
    /// 不持有 <c>Rigidbody2D</c>、不读 <c>RuleSystem</c>、不读 <c>Physics2D.gravity</c>
    /// （重力由 <see cref="MotionFrame.Gravity"/> 传进来）—— 所以它能在 EditMode 里
    /// 直接断言，不需要场景、不需要物理步。</para>
    ///
    /// <para><b>两条轴，最后只合一次</b>：进来时先把速度拆成
    /// <c>alongUp</c>（沿「上」轴的分量）与 <c>lateral</c>（横向，即 <c>x</c>）。
    /// 之后每个逻辑<b>只碰自己那条轴</b>，两条轴各自演化，<see cref="Solve"/> 的最后两行
    /// 才把它们拼回一个向量。所以「一个速度被十几处轮流改写」这件事不存在了。</para>
    ///
    /// <para><b>竖直链是「变化量」的折叠，横向链是「决策」</b>：</para>
    /// <list type="bullet">
    /// <item>竖直方向的每一步（重力 / 终端速度 / 贴墙 / 贴地抵消 / 起跳 / 削速）
    /// 都是<b>叠加性</b>的，各自算一个 Δ 交给调用方 <c>+=</c>。
    /// 它们的 Δ <b>必须按顺序算</b> —— 终端速度要读重力之后的值、削速要读起跳之后的值。
    /// 各算各的再用同一个基线相加会改掉物理行为。</item>
    /// <item>唯一一处「谁覆盖谁」的判定是<b>竖直轴的所有权</b>：墙真的在托着时竖直轴归墙独占，
    /// 重力与终端速度被屏蔽（见 <see cref="VerticalDelta"/>）。</item>
    /// <item>横向是三档<b>互斥</b>的（冲刺接管 / 接地 / 空中），不存在「叠加」，
    /// 所以它返回<b>新的横向速度</b>而不是 Δ —— 硬做成 Δ 反而会掩盖互斥这件事。</item>
    /// </list>
    ///
    /// <para><b>唯一写速度的地方是 <see cref="MotionMover"/></b>：本类只算，不算完就落地。</para>
    ///
    /// <para>⚠️ <b>下面各步的顺序即语义</b>（<c>Doc/玩家动作设计.md</c> §6），
    /// 它决定了各种状态谁覆盖谁。改动前先读那张表。</para>
    ///
    /// <para>⚠️ <b>隐含前提：竖直走 <c>up</c> 轴、横向走 <c>x</c></b>。两条轴要正交，
    /// 得靠 <c>up</c> 是轴对齐的。现在成立 —— <see cref="ContactSensor"/> 里
    /// <c>up = -gravity.normalized</c>，而 <c>GravityRule</c> 只赋 <c>(0,±9.81)</c> 及其倍数、
    /// 从不旋转。将来真要做斜向重力，横向那一档得跟着改走「右」轴。</para>
    ///
    /// <para>按值传参（不用 <c>in</c>）：MCP 的 <c>execute_code</c> 只到 C# 5，
    /// 会把 <c>in</c> 误读成 <c>ref</c> 而编译不过。</para>
    /// </remarks>
    public static class VelocitySolver
    {
        /// <summary>接地反向输入时的最低制动加速度。低于它的抓地力会让转向拖沓到读不出来。</summary>
        private const float MinimumTurnBrakeDecel = 12f;

        /// <summary>
        /// 算一帧的速度。
        /// </summary>
        /// <param name="velocity">上一帧末的速度（<b>不含</b>击退等外部冲量）。</param>
        public static Result Solve(Vector2 velocity, MotionFrame frame, float dt)
        {
            MotionTuning t   = frame.Tuning;
            Vector2      up  = frame.Contact.Up;
            bool         dashing = frame.Dash.Dashing;
            float        facing  = frame.Facing;

            // ── 拆轴：进来就分成两条独立的量，全程各自演化，最后只合一次。
            float alongUp = Vector2.Dot(velocity, up);   // 竖直分量（沿「上」轴）
            float lateral = velocity.x;                  // 横向分量

            // ③ 冲刺收尾：结束那一帧收横向速度。放在重力之前 —— 重力只作用于竖直轴，
            //    两者互不干扰，与原实现的先后一致（原来 EndDash 跑在冲刺计时那一步里）。
            if (frame.Dash.JustEnded) lateral *= t.DashExitSpeedScale;

            // ③ 起冲那一刻定朝向。只改符号来源，不碰速度本身。
            if (frame.Dash.Started && !Mathf.Approximately(frame.Dash.Direction.x, 0f))
                facing = Mathf.Sign(frame.Dash.Direction.x);

            // ── 竖直链：④④′ 先一次定完「谁拥有竖直轴」，再各算自己的变化量。
            bool wallSliding;
            alongUp += VerticalDelta(t, frame, alongUp, dt, dashing, out wallSliding);  // ④④′ 重力 / 终端 / 贴墙

            // ── 横向链：三档互斥，返回新的横向速度（不是变化量）。
            lateral = Horizontal(t, frame, lateral, dt, ref facing);                    // ⑤ 水平

            // ── 竖直链续。必须排在横向之后：蹬墙跳会整体接管横向。
            alongUp += SurfaceCancelDelta(frame.Contact, alongUp);                      // ⑥ 贴地抵消
            alongUp += JumpDelta(frame, alongUp, ref lateral, ref facing);              // ⑦ 起跳
            alongUp += JumpCutDelta(t, frame, alongUp);                                 // ⑧ 松键削速

            // ── 合成：本类唯一一次组装成向量。
            Vector2 result = SetAlong(velocity, up, alongUp);   // 竖直沿「上」轴写
            result.x       = lateral;                           // 横向直接写 x

            return new Result(result, facing, wallSliding);
        }

        // ==================================================================
        //  竖直链：各算各的变化量
        // ==================================================================

        /// <summary>
        /// ④ 重力这一帧贡献的变化量。冲刺期间<b>不积分</b>（竖直速度原样保留，既不归零也不加速）。
        /// </summary>
        /// <remarks>沿「上」轴的分量取 <c>Dot(gravity, up)</c>。因为 <c>up = -gravity.normalized</c>，
        /// 重力与「上」轴恒平行、垂直分量恒为 0，所以这个投影是无损的 —— 与原实现
        /// 「整个 gravity 向量加到 velocity 上」逐位等价。</remarks>
        private static float GravityDelta(MotionTuning t, Vector2 gravity, Vector2 up,
                                          float alongUp, float dt, bool dashing)
        {
            if (dashing) return 0f;

            // 落得比升得快，滞空更紧凑
            float scale = alongUp < 0f ? t.FallGravityMultiplier : 1f;
            return Vector2.Dot(gravity, up) * t.GravityScale * scale * dt;
        }

        /// <summary>④ 终端速度：下落超过上限时把差值补回来。<b>返回 0 表示没触发。</b></summary>
        private static float TerminalFallDelta(MotionTuning t, float alongUp)
            => alongUp < -t.MaxFallSpeed ? -t.MaxFallSpeed - alongUp : 0f;

        /// <summary>
        /// ④④′ 竖直轴这一帧的变化量：重力、终端速度、墙面摩擦。
        /// </summary>
        /// <remarks>
        /// <para><b>谁拥有竖直轴在这里一次定完</b>：墙真的在托着时，竖直轴归墙<b>独占</b> ——
        /// 重力与终端速度被<b>屏蔽</b>，只剩墙面摩擦把速度收向 <c>WallSlideSpeed</c>。
        /// 这就是文档那句「贴到墙……收到 <c>_wallSlideSpeed</c>，之后等速缓降」的实现。</para>
        ///
        /// <para>⚠️ <b>为什么屏蔽而不是事后扣掉</b>：重力加速度是
        /// <c>9.81 × GravityScale × FallGravityMultiplier</c>（默认 75.1 m/s²），
        /// 比 <c>WallSlideImpactDecel</c>（默认 60）还大。不屏蔽的话两者对拉，净值仍是
        /// <b>向下 15 m/s²</b>，实测竖直速度稳定在 <b>−28.8</b>（96% 终端速度）——
        /// 贴墙等于没减速。屏蔽之后 0.4 秒收到 −6 并保持。</para>
        ///
        /// <para>⚠️ <b>屏蔽必须带「墙真的在托着」这个条件</b>：<see cref="wallSliding"/> 只表示
        /// 「贴墙且在往下」，速度在 <c>−WallSlideSpeed</c> 与 0 之间时墙<b>并不吸收</b>。
        /// 只判 <c>wallSliding</c> 就屏蔽重力，人会<b>浮在空中</b>。</para>
        ///
        /// <para>⚠️ <b>判定要在算重力之前做</b>：否则墙面收速会从「已经被重力拉过」的值起算，
        /// 屏蔽就只是事后扣一个数，等于没屏蔽。</para>
        /// </remarks>
        private static float VerticalDelta(MotionTuning t, MotionFrame frame, float alongUp, float dt,
                                           bool dashing, out bool wallSliding)
        {
            Vector2 move = frame.Input.Move;
            bool pressingIntoWall = (frame.Contact.WallLeft && move.x < -0.01f)
                                 || (frame.Contact.WallRight && move.x > 0.01f);

            // 贴墙能力关掉时，本帧根本不算「在贴墙」—— 竖直轴照常归重力。
            wallSliding = t.WallClimbEnabled
                       && !frame.Contact.Grounded && !dashing && alongUp <= 0f && pressingIntoWall;

            // 墙在托着：竖直轴归它独占，重力与终端速度不参与。
            // ⚠️ 边界用 `<=` 而不是 `<`：速度正好落在 WallSlideSpeed 上时也算「在托着」。
            //    用 `<` 的话那一帧判定为「没托着」→ 重力把它推下去 → 墙面只按 ImpactDecel 拉回一点，
            //    于是速度在 WallSlideSpeed 与 WallSlideSpeed+重力步 之间来回抖（实测 −6.0 ↔ −7.5）。
            if (wallSliding && alongUp <= -t.WallSlideSpeed)
                return Mathf.MoveTowards(alongUp, -t.WallSlideSpeed, t.WallSlideImpactDecel * dt) - alongUp;

            // 否则重力照常积分，再压终端速度（终端速度要读重力之后的值）。
            float delta = GravityDelta(t, frame.Gravity, frame.Contact.Up, alongUp, dt, dashing);
            return delta + TerminalFallDelta(t, alongUp + delta);
        }

        /// <summary>⑥ 贴地时抵消朝地面累积的速度。<b>返回 0 表示不在接地或本来就在上升。</b></summary>
        /// <remarks>不抵消的话，站在地上每一帧都会往地里攒一点速度，离开地面的那一瞬间才一次性弹出来。</remarks>
        private static float SurfaceCancelDelta(ContactSensor.State contact, float alongUp)
            => contact.Grounded && alongUp < 0f ? -alongUp : 0f;

        /// <summary>
        /// ⑦ 起跳贡献的变化量。蹬墙跳额外接管横向与朝向。
        /// </summary>
        /// <remarks>速度大小由 <see cref="JumpSolver"/> 定好放在结论里，本函数只负责把它落到分量上。</remarks>
        private static float JumpDelta(MotionFrame frame, float alongUp,
                                       ref float lateral, ref float facing)
        {
            switch (frame.Jump.Kind)
            {
                case JumpSolver.JumpKind.Wall:
                    // 蹬墙跳：朝墙外弹开 + 向上。**横向整体重写**（旧的水平速度作废），
                    // 竖直沿「上」轴写，所以重力反转时它照样是「离开墙、朝世界的上方去」。
                    lateral = frame.Jump.PushSpeed;
                    facing  = Mathf.Sign(frame.Jump.PushSpeed);   // 朝向跟着弹开方向转，攻击判定才对得上前方
                    return frame.Jump.AlongUpSpeed - alongUp;

                case JumpSolver.JumpKind.Ground:
                case JumpSolver.JumpKind.Air:
                    return frame.Jump.AlongUpSpeed - alongUp;

                default:
                    return 0f;
            }
        }

        /// <summary>⑧ 可变跳跃高度：提前松键就把上升速度削掉，跳得矮一些。</summary>
        /// <remarks>只有真在上升时才削 —— 下落中松键不该把它往下拽。</remarks>
        private static float JumpCutDelta(MotionTuning t, MotionFrame frame, float alongUp)
            => frame.Jump.JustCut && alongUp > 0f ? alongUp * (t.JumpCutMultiplier - 1f) : 0f;

        // ==================================================================
        //  横向链：一次决策
        // ==================================================================

        /// <summary>
        /// ⑤ 横向速度：这一帧谁拥有横向 —— 冲刺接管，否则接地逻辑，否则空中逻辑。
        /// </summary>
        /// <remarks>三档<b>互斥</b>，所以返回的是「该是多少」而不是「变了多少」。</remarks>
        private static float Horizontal(MotionTuning t, MotionFrame frame, float lateral, float dt,
                                        ref float facing)
        {
            Vector2 move = frame.Input.Move;

            if (frame.Dash.Dashing)
            {
                // 冲刺**只改水平速度**：竖直方向的大小不动（那是重力与跳跃的领地）。
                return frame.Dash.Direction.x * t.DashSpeed;
            }

            if (frame.Contact.Grounded)
            {
                float frictionScale = frame.FrictionScale;    // 摩擦规则缩放：加速与减速都要乘

                if (Mathf.Abs(move.x) > 0.01f)
                {
                    // 摩擦反转（摩擦 × 反转）：摩擦力的方向翻转 → 地面把你往「按键的反方向」送，
                    // 于是按右就往左走。**只作用于接地时** —— 语义是摩擦力的方向，空中没有摩擦。
                    // 所以跳跃可以「逃出」反转区。这也是它与被砍掉的「方向」属性的区别：
                    // 改的是世界给你的力，不是你的按键映射（策划案 D3）。
                    float direction = ResolveMoveDirection(move.x, frame.FrictionInverted);
                    facing = Mathf.Sign(direction);

                    // 反向输入先按抓地力刹到零，再加速反向；低摩擦面因此保留惯性。
                    if (lateral * direction < -0.01f)
                    {
                        float turnBrake = Mathf.Max(t.FrictionDecel * frame.Contact.GroundFriction,
                                                    MinimumTurnBrakeDecel) * frictionScale;
                        return Mathf.MoveTowards(lateral, 0f, turnBrake * dt);
                    }

                    return Mathf.MoveTowards(lateral, direction * t.MoveSpeedMax, t.GroundAccel * dt);
                }

                // 减速度 = 基准 × 脚下地面材质的摩擦 × 摩擦规则缩放。
                // 两者相乘：地形可以做成冰面，规则也能把整个世界变成冰面。
                // ⚠️ 反转**不翻这里的符号** —— 减速力反向会变成"越滑越快"，直接失控。
                float friction = frame.Contact.GroundFriction * frictionScale;
                return Mathf.MoveTowards(lateral, 0f, t.FrictionDecel * friction * dt);
            }

            if (Mathf.Abs(move.x) > 0.01f)
            {
                facing = Mathf.Sign(move.x);
                return Mathf.MoveTowards(lateral, move.x * t.MoveSpeedMax, t.AirAccel * dt);
            }

            return Mathf.MoveTowards(lateral, 0f, t.AirDrag * dt);
        }

        // ==================================================================
        //  辅助
        // ==================================================================

        /// <summary>把速度沿 <paramref name="axis"/> 的分量设为 <paramref name="value"/>，其余分量不动。</summary>
        private static Vector2 SetAlong(Vector2 velocity, Vector2 axis, float value)
            => velocity - axis * Vector2.Dot(velocity, axis) + axis * value;

        /// <summary>
        /// 把原始横向输入换算成「本帧该往哪个方向走」。
        /// </summary>
        /// <remarks>
        /// <para>摩擦反转时取反 —— 这是「摩擦 × 反转」在移动上的全部实现。</para>
        /// <para>抽成纯函数是为了**可测**：真机验证要靠真按键，而注入按键在编辑器失焦时会被
        /// Input System 重置；手动调 <c>FixedUpdate</c> 又会因为里面的 <c>Rigidbody2D.Slide</c>
        /// 在物理步之外调用而卡死。所以把决策留在这里单独断言。</para>
        /// </remarks>
        public static float ResolveMoveDirection(float rawInput, bool frictionInverted)
            => frictionInverted ? -rawInput : rawInput;

        /// <summary>
        /// 解算结果。
        /// </summary>
        public readonly struct Result
        {
            /// <summary>解算后的速度。**这是唯一一份被算出来的速度。**</summary>
            public readonly Vector2 Velocity;

            /// <summary>朝向：+1 右，−1 左。取的是**换算后**的方向，所以「摩擦 × 反转」
            /// 生效时按右往左走，这里自动是 −1。</summary>
            public readonly float Facing;

            /// <summary>本帧是否在贴墙缓降。</summary>
            public readonly bool WallSliding;

            public Result(Vector2 velocity, float facing, bool wallSliding)
            {
                Velocity    = velocity;
                Facing      = facing;
                WallSliding = wallSliding;
            }
        }
    }
}
