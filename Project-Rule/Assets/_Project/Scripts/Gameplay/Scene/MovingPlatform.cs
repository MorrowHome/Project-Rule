using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 平移平台：在**起点 / 终点之间直线往复**的场景组件（滑板车、升降台、巡逻踏板都用它）。
    /// </summary>
    /// <remarks>
    /// <para><b>只走直线、不旋转</b>：路径就是 <see cref="_start"/> → <see cref="_end"/> 一条线段，
    /// 位置按「已走过的距离 / 全长」插值。匀速，没有缓动 —— 缓动属于表现层，不在这里做。</para>
    ///
    /// <para><b>坐标是世界坐标</b>：终点填成世界坐标，肉眼对着场景拖最省事；
    /// 编辑器工具栏里有两个「把起点/终点设为当前位置」按钮，摆好再点一下就行。</para>
    ///
    /// <para><b>刚体是 Kinematic</b>：位移完全由代码决定，物理不会替它解算。
    /// 位置用 <c>Rigidbody2D.MovePosition</c> 提交 —— 它在**物理步进时**生效，
    /// 于是暂停（<c>Physics2D.simulationMode = Script</c>）时平台自动静止，
    /// 不用额外判暂停；<see cref="FixedUpdate"/> 里那句 <c>IsPaused</c> 只是
    /// 让「走了多远」这套账本跟位置保持一致（两边都不动）。</para>
    ///
    /// <para><b>时间规则会改它的速度</b>：每帧用的是 <c>Time.fixedDeltaTime</c>，
    /// 它随 <c>Time.timeScale</c> 缩放（<c>TimeManager.Synthesize</c> 写的那个）。
    /// 时间变慢 → 物理步数变少 → 平台跟着变慢，符合「改变的是世界」。</para>
    ///
    /// <para><b>站在上面的人会被带走</b>：本类实现 <see cref="IMovingSurface"/>，
    /// 承载方（玩家走 <c>ContactSensor</c> / <c>MotionMover</c>）每个物理帧把 <see cref="Velocity"/>
    /// 加进自己的位移里。平台这边**不主动推人** —— 谁踩上来谁自己读。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class MovingPlatform : MonoBehaviour, IMovingSurface
    {
        /// <summary>到达端点之后怎么走。</summary>
        public enum PathMode
        {
            /// <summary>往返：到终点折回起点，循环不停。</summary>
            PingPong = 0,

            /// <summary>单程：走到终点就停住，不再移动。</summary>
            Once = 1,
        }

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("路径（世界坐标）")]
        [Tooltip("起点。进入播放时平台会被搬到这里。")]
        [SerializeField] private Vector2 _start = Vector2.zero;

        [Tooltip("终点。和起点重合 = 不会动的平台。")]
        [SerializeField] private Vector2 _end = Vector2.zero;

        [Tooltip("起始进度：0 = 从起点出发，1 = 从终点出发（单程模式下即终点）。\n" +
                 "放两台同向的平台时错开相位用。")]
        [SerializeField, Range(0f, 1f)] private float _startProgress = 0f;

        [Header("节奏")]
        [Tooltip("移动速度（单位 / 秒）。")]
        [SerializeField, Min(0f)] private float _speed = 3f;

        [Tooltip("走完单程后怎么办。")]
        [SerializeField] private PathMode _mode = PathMode.PingPong;

        [Tooltip("在两端各停多久（秒）。0 = 到端点立刻折返。")]
        [SerializeField, Min(0f)] private float _waitAtEnds = 0f;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;

        /// <summary>沿路径已走过的距离，落在 0 ~ <see cref="_length"/>。</summary>
        private float _travel;

        /// <summary>下一步往哪头走：+1 去终点，−1 回起点。</summary>
        private float _forward = 1f;

        /// <summary>端点停留的剩余秒数。</summary>
        private float _waitRemaining;

        /// <summary>单程模式走到头之后置位，从此不再移动。</summary>
        private bool _finished;

        private float _length;
        private Vector2 _lastTarget;

        /// <summary>本物理帧的平台速度（单位 / 秒）。踩在上面的实体读它，见 <see cref="IMovingSurface"/>。</summary>
        /// <remarks>由「这一步实际走了多远 ÷ dt」算出，与位移严格一致 ——
        /// 承载方加进自己的速度后，走出的距离与平台**逐帧相等**，不会累积漂移。</remarks>
        public Vector2 Velocity { get; private set; }

        /// <summary>路径全长（单位）。0 = 起点终点重合。</summary>
        public float Length => _length;

        /// <summary>起点（世界坐标）。</summary>
        public Vector2 StartPoint => _start;

        /// <summary>终点（世界坐标）。</summary>
        public Vector2 EndPoint => _end;

        /// <summary>移动速度（单位 / 秒）。</summary>
        public float Speed => _speed;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            ConfigureBody();
            Rebuild();
        }

        private void OnEnable()
        {
            // 重开一局 / 对象池取出时也要回到起点，不能接着上次的进度走。
            if (_body == null)
            {
                _body = GetComponent<Rigidbody2D>();
                ConfigureBody();
            }
            Rebuild();
        }

        private void FixedUpdate()
        {
            if (TimeManager.Instance?.IsPaused == true)
            {
                Velocity = Vector2.zero;
                return;
            }

            // 注意：本文件 using 了 RuleGame.Infrastructure.Time，它会遮蔽 UnityEngine.Time，
            // 所以这两个都要全限定。
            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;

            if (!_finished) Advance(dt);

            Vector2 target = Evaluate(_travel);
            Velocity = (target - _lastTarget) / dt;
            _lastTarget = target;

            _body.MovePosition(target);
        }

        // ==================================================================
        //  内部
        // ==================================================================

        /// <summary>把「走了多远」推进 <paramref name="dt"/> 秒。端点停留在这里被消耗掉。</summary>
        private void Advance(float dt)
        {
            if (_waitRemaining > 0f)
            {
                _waitRemaining -= dt;
                if (_waitRemaining > 0f) return;

                // 停留结束：这一帧剩下的时间不再折算成位移（最多丢掉一个物理步，肉眼看不出）。
                _waitRemaining = 0f;
            }

            if (_length <= 0f) return;

            _travel += _speed * dt * _forward;

            // ⚠️ 两个端点都要带上方向判断：速度为 0 时 _travel 恒等于 0，
            //    不带方向就会被判成「抵达起点」，每帧折返一次。
            if (_forward > 0f && _travel >= _length)
            {
                _travel = _length;
                OnReachedEnd();
            }
            else if (_forward < 0f && _travel <= 0f)
            {
                _travel = 0f;
                OnReachedEnd();
            }
        }

        private void OnReachedEnd()
        {
            switch (_mode)
            {
                case PathMode.PingPong:
                    _forward = -_forward;
                    _waitRemaining = _waitAtEnds;
                    break;

                case PathMode.Once:
                    _finished = true;
                    break;
            }
        }

        private Vector2 Evaluate(float travel)
        {
            if (_length <= 0f) return _start;
            return Vector2.LerpUnclamped(_start, _end, travel / _length);
        }

        /// <summary>按配置把内部状态摆回初始位置，并立刻落到位。</summary>
        private void Rebuild()
        {
            _length = Vector2.Distance(_start, _end);
            _forward = 1f;
            _finished = false;
            _waitRemaining = 0f;
            _travel = Mathf.Clamp01(_startProgress) * _length;

            // 单程 + 从终点出发 = 已经没有地方可去。
            if (_mode == PathMode.Once && _travel >= _length && _length > 0f)
            {
                _finished = true;
            }

            // 从终点出发的往返平台，第一步是往回走。
            if (_travel >= _length && _length > 0f) _forward = -1f;

            Vector2 target = Evaluate(_travel);
            _lastTarget = target;
            Velocity = Vector2.zero;
            _body.position = target;
        }

        private void ConfigureBody()
        {
            // Kinematic：位移由代码决定，重力 / 摩擦都不参与。
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.linearDamping = 0f;
            _body.constraints = RigidbodyConstraints2D.FreezeRotation;

            // 定步长物理 + 高帧率渲染：不开插值平台会一格一格跳。
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：不动内部账本，直接改两个端点。</summary>
        public void SetPath(Vector2 start, Vector2 end)
        {
            _start = start;
            _end = end;
            // 立刻跟上，否则编辑模式下 Length 会一直是旧值（Rebuild 只在播放时跑）。
            _length = Vector2.Distance(_start, _end);
        }

        private void OnValidate()
        {
            if (Application.isPlaying) return;

            _length = Vector2.Distance(_start, _end);
        }

        private void OnDrawGizmos()
        {
            Vector2 a = _start;
            Vector2 b = _end;
            float length = Vector2.Distance(a, b);

            Gizmos.color = new Color(0.25f, 0.75f, 1f, 0.9f);
            Gizmos.DrawLine(a, b);

            if (length > 0f)
            {
                // 终点画个箭头，一眼看出往哪头去。
                Vector2 dir = (b - a) / length;
                Vector2 perp = new Vector2(-dir.y, dir.x);
                float head = Mathf.Min(0.35f, length * 0.25f);

                Gizmos.DrawLine(b, b - dir * head + perp * head * 0.5f);
                Gizmos.DrawLine(b, b - dir * head - perp * head * 0.5f);
            }

            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.9f);
            Gizmos.DrawWireSphere(a, 0.18f);

            Gizmos.color = new Color(1f, 0.5f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(b, 0.18f);

            // 播放中额外把「现在走到哪了」标出来。
            if (!Application.isPlaying || length <= 0f) return;

            Gizmos.color = new Color(1f, 0.95f, 0.3f, 1f);
            Gizmos.DrawWireSphere(Evaluate(_travel), 0.22f);
        }
    }
}
