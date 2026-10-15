using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 接触：每物理帧的接地 / 左墙 / 右墙 / 脚下摩擦系数探测（处理顺序的 ①），
    /// 顺带解析「上」方向。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么「上」方向住在这里</b>：探测得先知道哪边是下 —— 地面扫描朝 <c>-Up</c>、
    /// 墙面扫描朝世界左右。把它拆出去只会让每个调用方多传一个参数进来。
    /// 「上」= 重力反方向；重力失效（零向量）时沿用上一次的方向，避免退化成零向量。</para>
    /// <para><b>只查刚体自身的全部碰撞体</b>，不需要额外的地面检测子物体。</para>
    /// <para>只做判定，不改速度、不读输入、不读规则 —— 规则改的是全局重力，
    /// 本类只是把结果读出来。</para>
    /// </remarks>
    public sealed class ContactSensor
    {
        private readonly Rigidbody2D _body;
        private readonly Collider2D  _bodyCollider;      // 主碰撞体：中段探针的几何来源
        private readonly RaycastHit2D[] _groundHits = new RaycastHit2D[4];
        private readonly RaycastHit2D[] _wallHits   = new RaycastHit2D[4];
        private readonly Collider2D[]   _climbHits  = new Collider2D[4];

        private bool    _grounded;
        private bool    _wallLeft;
        private bool    _wallRight;
        private bool    _climbLeft;
        private bool    _climbRight;
        private bool    _wasGrounded;
        private float   _groundFriction = 1f;
        private Vector2 _up = Vector2.up;

        public ContactSensor(Rigidbody2D body, Collider2D bodyCollider)
        {
            _body         = body;
            _bodyCollider = bodyCollider;
        }

        /// <summary>是否站在支撑面上。</summary>
        public bool Grounded => _grounded;

        /// <summary>
        /// 左侧是否贴着竖墙。<b>全高探测</b>（整个碰撞体的形状往外扫）—— 只用于顶墙分量预夹。
        /// </summary>
        /// <remarks>贴墙**能力**的判据是 <see cref="ClimbLeft"/>，不是这个。</remarks>
        public bool WallLeft => _wallLeft;

        /// <summary>
        /// 右侧是否贴着竖墙。<b>全高探测</b> —— 只用于顶墙分量预夹。
        /// </summary>
        public bool WallRight => _wallRight;

        /// <summary>
        /// 左侧**中段**是否贴着可爬的竖墙 —— <b>贴墙下滑 / 蹬墙跳的判据</b>。
        /// </summary>
        /// <remarks>只认中段的接触，所以矮台 / 台阶 / 头顶横梁都不算墙。</remarks>
        public bool ClimbLeft => _climbLeft;

        /// <summary>右侧**中段**是否贴着可爬的竖墙 —— 贴墙下滑 / 蹬墙跳的判据。</summary>
        public bool ClimbRight => _climbRight;

        /// <summary>脚下这块地的摩擦系数（来自它的 <c>PhysicsMaterial2D</c>）。</summary>
        public float GroundFriction => _groundFriction;

        /// <summary>当前「上」方向 = 重力反方向。</summary>
        public Vector2 Up => _up;

        /// <summary>
        /// 跑一次探测。**每物理帧一次**，且必须在所有消费接触状态的步骤之前。
        /// </summary>
        public State Sense(MotionTuning tuning)
        {
            UpdateUp();

            _wasGrounded = _grounded;
            _grounded  = ProbeGround(tuning);
            _wallLeft  = ProbeWall(Vector2.left, tuning);
            _wallRight = ProbeWall(Vector2.right, tuning);

            // 中段探针：只认「身体中段那一小块」旁边的墙（贴墙能力的判据）。
            Bounds b = _bodyCollider.bounds;
            _climbLeft  = ProbeClimb(new Vector2(b.min.x,  b.center.y), tuning.WallClimbProbeRadius, tuning);
            _climbRight = ProbeClimb(new Vector2(b.max.x,  b.center.y), tuning.WallClimbProbeRadius, tuning);

            return new State(_grounded, _wallLeft, _wallRight, _groundFriction, _up,
                             _grounded && !_wasGrounded, _climbLeft, _climbRight);
        }

        /// <summary>
        /// 中段圆探针：在指定点做一个圆重叠查询，看旁边有没有可爬的竖墙。
        /// </summary>
        /// <remarks>
        /// <para>为什么不复用 <see cref="ProbeWall"/> 的 Cast：那个是**整个碰撞体**往外扫，
        /// 「下半身贴着矮台」也会算成墙 —— 会在平台上误触发贴墙下滑。
        /// 能力判定只认**中段那一小块**，所以矮台 / 台阶 / 头顶横梁都不算墙。</para>
        /// <para>用 <c>SolidLayer</c> 过滤，天然排除玩家自己（玩家在 Default 层，不在 Ground 层）。</para>
        /// <para>⚠️ 它**不读法线**：圆探针只回答「那个点旁边有没有东西」，没有方向可判 ——
        /// 这正是它比 Cast 简单的地方（Cast 那套「法线要顶着扫描方向」的坑没有了）。</para>
        /// </remarks>
        private bool ProbeClimb(Vector2 point, float radius, MotionTuning tuning)
        {
            if (radius <= 0f) return false;

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(tuning.SolidLayer);

            return Physics2D.OverlapCircle(point, radius, filter, _climbHits) > 0;
        }

        /// <summary>沿当前「上」方向的反向做一次碰撞体扫描，看脚下有没有支撑面。</summary>
        /// <remarks>顺带记住脚下这块地的摩擦系数（<see cref="_groundFriction"/>），供减速使用。</remarks>
        private bool ProbeGround(MotionTuning tuning)
        {
            _groundFriction = 1f;      // 没探到面、或那块地没挂材质 → 按普通地面算

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(tuning.SolidLayer);

            int count = _body.Cast(-_up, filter, _groundHits, tuning.GroundProbeDistance);
            for (int i = 0; i < count; i++)
            {
                // 只有法线朝着「上」的接触才算支撑面；墙面法线是水平的，会被排除。
                if (Vector2.Dot(_groundHits[i].normal, _up) <= 0.5f) continue;

                // 地形用 PhysicsMaterial2D 表达材质：冰面给个小的 friction 即可。
                var material = _groundHits[i].collider.sharedMaterial;
                if (material != null) _groundFriction = material.friction;

                return true;
            }
            return false;
        }

        /// <summary>
        /// 朝 <paramref name="direction"/> 做一次碰撞体扫描，看贴不贴着一面**竖着的墙**。
        /// </summary>
        /// <remarks>
        /// <para>只认竖墙：接触面的法线要**基本垂直于「上」**。法线朝上/朝下的是地面或天花板，
        /// 不算墙 —— 否则站在地上也会被判成「贴着墙」。</para>
        /// <para>⚠️ 还要确认这面墙**真的在探测的那一侧**。<c>Rigidbody2D.Cast</c> 在形状已经
        /// 与某个碰撞体接触时，会把它当成 <c>distance = 0</c> 的命中返回，**不管你朝哪个方向扫**。
        /// 于是贴着右墙时「朝左扫」也会命中那面右墙 —— 不判方向的话两侧都会报「有墙」，
        /// 玩家按左就会往右弹、直接弹进墙里。判据：法线要**顶着**扫描方向。</para>
        /// <para>方向用世界坐标的左右，不用 <see cref="_up"/> 推出来的「左右」：
        /// 重力反转翻的是上下，不翻左右，所以墙永远是竖的。</para>
        /// </remarks>
        private bool ProbeWall(Vector2 direction, MotionTuning tuning)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(tuning.SolidLayer);

            int count = _body.Cast(direction, filter, _wallHits, tuning.WallProbeDistance);
            for (int i = 0; i < count; i++)
            {
                Vector2 normal = _wallHits[i].normal;

                if (Mathf.Abs(Vector2.Dot(normal, _up)) > 0.5f) continue;   // 地面 / 天花板
                if (Vector2.Dot(normal, direction) > -0.5f) continue;       // 在另一侧，不是这面

                return true;
            }
            return false;
        }

        /// <summary>「上」方向 = 重力反方向；重力失效（零向量）时沿用上一次的方向。</summary>
        private void UpdateUp()
        {
            if (Physics2D.gravity.sqrMagnitude > 0.0001f)
                _up = -Physics2D.gravity.normalized;
        }

        /// <summary>
        /// 本物理帧的接触快照。
        /// </summary>
        /// <remarks><see cref="JustLanded"/> 是**边沿**（false → true），表现层靠它播一次落地动作 ——
        /// 靠每帧采样接地状态是拿不到的（0.33 秒的落地只有 4 个物理帧）。</remarks>
        public readonly struct State
        {
            public readonly bool    Grounded;
            public readonly bool    WallLeft;
            public readonly bool    WallRight;
            public readonly float   GroundFriction;
            public readonly Vector2 Up;
            public readonly bool    JustLanded;

            /// <summary>左侧**中段**贴着可爬的竖墙 —— 贴墙下滑 / 蹬墙跳用它，不用 <see cref="WallLeft"/>。</summary>
            public readonly bool    ClimbLeft;

            /// <summary>右侧**中段**贴着可爬的竖墙。</summary>
            public readonly bool    ClimbRight;

            public State(bool grounded, bool wallLeft, bool wallRight,
                         float groundFriction, Vector2 up, bool justLanded,
                         bool climbLeft, bool climbRight)
            {
                Grounded       = grounded;
                WallLeft       = wallLeft;
                WallRight      = wallRight;
                GroundFriction = groundFriction;
                Up             = up;
                JustLanded     = justLanded;
                ClimbLeft      = climbLeft;
                ClimbRight     = climbRight;
            }
        }
    }
}
