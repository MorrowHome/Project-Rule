using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 固定轨道巡逻：沿一串**相对出生点的偏移航点**匀速前进，支持往返 / 闭环 / 单程。
    /// </summary>
    /// <remarks>
    /// <para><b>只管「沿轨道走」这一件事</b>：不认玩家、不认敌人、不认机关类型。
    /// 巡逻炸弹、移动踏板、来回的电车都能挂它，挂上就是「会沿轨道动的物体」。</para>
    ///
    /// <para><b>航点是局部偏移</b>：<c>Points</c> 填相对**出生点**的偏移（世界坐标差值），
    /// 路径 = 出生点 → 出生点 + Points[0] → 出生点 + Points[1] → ……
    /// 于是同一个 prefab 摆在哪都从自己脚下长出一条轨道，不用按关卡改绝对坐标。
    /// 出生点在 <c>Awake</c> 记一次。</para>
    ///
    /// <para><b>起始位置可配</b>：出生点只是轨道的**原点**，平台开局停在哪由
    /// <c>_startProgress</c> 决定（0 = 就停在出生点，1 = 末点，比例按**全长**算）。
    /// 放多台同轨平台错开相位、或者让平台「开局已经在半路」时用；
    /// <see cref="ResetToStart"/> 也是回到这个位置，不是回到出生点。</para>
    ///
    /// <para><b>刚体可选</b>：身上有 <c>Rigidbody2D</c> 就走 <c>MovePosition</c>
    /// （位移在物理步进里提交，暂停时自动停住）；没有就直接写 <c>transform.position</c>。</para>
    ///
    /// <para><b>速度报的是「实际走出来的」</b>：本类实现 <see cref="IMovingSurface"/>，
    /// <see cref="Velocity"/> 由「这一步真的走了多远 ÷ dt」算出，与位移严格一致 ——
    /// 踩在上面的实体读它就能被带走，不累积漂移（与 <c>MovingPlatform</c> 同一套分工）。</para>
    ///
    /// <para><b>不会穿墙</b>：<c>_stopAtObstacles</c> 打开时先沿位移方向 Cast 一次，
    /// 撞到 <c>_solidMask</c> 里的东西就停在它前面，不会把站在轨道上的实体挤进地形。</para>
    ///
    /// <para><b>快照由宿主转发</b>：<c>SnapshotSystem</c> 每个刚体只认**一个**
    /// <c>ISnapshotParticipant</c>，所以本类**不自己实现**那个接口，而是把状态暴露成
    /// <see cref="CaptureTrackState"/> / <see cref="RestoreTrackState"/>，
    /// 由宿主（如 <c>CityTrackBomb</c>）在自己那份机关快照里带上一程。</para>
    /// </remarks>
    [DefaultExecutionOrder(-40)] // 承载位移先提交，随后由承载方（玩家）在自己的 Slide 内统一解算。
    [DisallowMultipleComponent]
    public sealed class TrackMotor : MonoBehaviour, IMovingSurface
    {
        /// <summary>走到轨道尽头之后怎么走。</summary>
        public enum PathMode
        {
            /// <summary>往返：末点折回起点，循环不停。</summary>
            PingPong = 0,

            /// <summary>闭环：末点直接回到起点，绕圈跑。</summary>
            Loop = 1,

            /// <summary>单程：走到末点停住（<see cref="SetOn"/> 再开也不动）。</summary>
            Once = 2,
        }

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("轨道（相对出生点的偏移）")]
        [Tooltip("航点偏移，按顺序连接；起点自动是出生点。留空 = 不会动的物体。")]
        [SerializeField] private Vector2[] _points = { new Vector2(0f, 3f) };

        [Tooltip("走到尽头怎么办。")]
        [SerializeField] private PathMode _mode = PathMode.PingPong;

        [Header("起始位置")]
        [Tooltip("开局时平台已经沿轨道走到哪：0 = 出生点，1 = 末点（按**全长**的比例）。\n" +
                 "往返模式下 1 = 从末点往回开；单程模式下 1 = 已经到站、不再动；闭环模式下 1 与 0 是同一点。\n" +
                 "放多台同轨平台错开相位、或者让平台「开局就在半路」时用。")]
        [SerializeField, Range(0f, 1f)] private float _startProgress = 0f;

        [Header("节奏")]
        [Tooltip("移动速度（单位 / 秒）。")]
        [SerializeField, Min(0.05f)] private float _speed = 2f;

        [Tooltip("每到一个航点停多久（秒）。0 = 到点立刻继续。")]
        [SerializeField, Min(0f)] private float _waitAtPoint = 0f;

        [Tooltip("关掉就原地不动（可被开关联动）。")]
        [SerializeField] private bool _running = true;

        [Header("碰撞")]
        [Tooltip("撞到这一层就停在它前面。默认只认 Ground（层 8）。")]
        [SerializeField] private LayerMask _solidMask = 1 << 8;

        [Tooltip("关掉 = 直接穿过去（触发器式机关用得上）。")]
        [SerializeField] private bool _stopAtObstacles = true;

        [Tooltip("到达第 N 个航点时触发，N 是 _points 的下标 + 1（0 = 出生点）。")]
        public UnityEvent<int> OnReached = new UnityEvent<int>();

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;
        private Collider2D _shape;
        private readonly RaycastHit2D[] _hits = new RaycastHit2D[16];

        /// <summary>出生点：轨道的原点，也是 <c>_points</c> 的参照系。</summary>
        private Vector2 _origin;

        /// <summary>完整路径（含出生点），长度 = 航点数 + 1。</summary>
        private Vector2[] _path;

        /// <summary>当前线段的两头在 <see cref="_path"/> 里的下标。</summary>
        private int _from, _to;

        /// <summary>当前线段上的进度，0 ~ 1。</summary>
        private float _t;

        /// <summary>往返模式的下一段朝哪头：+1 去末点，−1 回起点。</summary>
        private int _direction = 1;

        /// <summary>航点停留的剩余秒数。</summary>
        private float _waitRemaining;

        /// <summary>单程模式走到头后置位。</summary>
        private bool _finished;

        /// <summary>本物理帧的实际速度（单位 / 秒）。踩在上面的实体读它，见 <see cref="IMovingSurface"/>。</summary>
        public Vector2 Velocity { get; private set; }

        /// <summary>当前线段的朝向（单位向量）。给「按朝向翻图」之类用。</summary>
        public Vector2 Direction
        {
            get
            {
                if (_path == null || _path.Length < 2) return Vector2.right;
                Vector2 segment = _path[_to] - _path[_from];
                return segment.sqrMagnitude > 0.000001f ? segment.normalized : Vector2.right;
            }
        }

        /// <summary>出生点（世界坐标）。</summary>
        public Vector2 StartPoint => _origin;

        /// <summary>当前线段的目标航点下标（<see cref="_path"/> 下标，0 = 出生点）。</summary>
        public int TargetIndex => _to;

        /// <summary>路径全长（单位）。0 = 没有轨道。</summary>
        public float Length
        {
            get
            {
                if (_path == null || _path.Length < 2) return 0f;
                float total = 0f;
                for (int i = 1; i < _path.Length; i++) total += Vector2.Distance(_path[i - 1], _path[i]);
                if (_mode == PathMode.Loop) total += Vector2.Distance(_path[_path.Length - 1], _path[0]);
                return total;
            }
        }

        /// <summary>是否在动（关掉 = 停住，但位置与进度都留着）。</summary>
        public bool Running => _running;

        /// <summary>配置的起始位置（0 = 出生点，1 = 末点，按全长比例）。</summary>
        public float StartProgress => _startProgress;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _shape = GetComponent<Collider2D>();
            _origin = _body != null ? _body.position : (Vector2)transform.position;
            Rebuild();
            // 出生点只是轨道的原点；平台**开局停在哪**由 _startProgress 决定（0 = 就停在出生点）。
            PlaceAtProgress(_startProgress);
        }

        private void FixedUpdate()
        {
            if (!_running || TimeManager.Instance?.IsPaused == true)
            {
                Velocity = Vector2.zero;
                return;
            }

            // 注意：本文件 using 了 RuleGame.Infrastructure.Time，它会遮蔽 UnityEngine.Time，
            // 所以这两个都要全限定。
            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f || _path == null || _path.Length < 2)
            {
                Velocity = Vector2.zero;
                return;
            }

            if (_waitRemaining > 0f)
            {
                _waitRemaining = Mathf.Max(0f, _waitRemaining - dt);
                Velocity = Vector2.zero;
                return;
            }

            Vector2 from = _path[_from];
            Vector2 to = _path[_to];
            Vector2 segment = to - from;
            float length = segment.magnitude;
            if (length <= 0.0001f)
            {
                Velocity = Vector2.zero;
                Arrive();
                return;
            }

            // 先按名义进度算出想去哪，再让碰撞决定实际能走多远。
            float nominal = Mathf.Min(1f, _t + _speed * dt / length);
            Vector2 current = _body != null ? _body.position : (Vector2)transform.position;
            Vector2 delta = Vector2.LerpUnclamped(from, to, nominal) - current;
            if (_stopAtObstacles) delta *= SafeFraction(delta);

            Vector2 next = current + delta;
            if (_body != null) _body.MovePosition(next); else transform.position = next;

            // 进度按**实际**位置回算：被墙挡住时进度不许虚增，否则解除阻挡会瞬移补上。
            _t = Mathf.Clamp01(Vector2.Dot(next - from, segment) / (length * length));
            Velocity = delta / dt;

            // 到达判据看「位置到没到航点」，不看 _t 是不是正好 1：
            // 除法加 Clamp 之后 _t 常停在 0.9999999，用 _t 判会让往返卡在端点永不折返。
            // 被墙挡住的 next 到不了 to，所以「卡在半路」依然不算到达。
            if (nominal >= 1f && (next - to).sqrMagnitude <= 0.000001f) Arrive();
        }

        // ==================================================================
        //  外部
        // ==================================================================

        /// <summary>开关联动（按钮 / 摇杆 / 电梯那样接）。单程模式走完后再开无效。</summary>
        public void SetOn(bool on)
        {
            if (on && _finished) return;
            _running = on;
        }

        /// <summary>只给编辑器工具用：直接换一套航点（播放中会立刻重建路径）。</summary>
        public void SetPoints(Vector2[] points)
        {
            _points = points;
            if (Application.isPlaying && _path != null) Rebuild();
        }

        /// <summary>把进度拉回**配置的起始位置**（<see cref="_startProgress"/>），并清掉停留与单程标记。</summary>
        public void ResetToStart() => PlaceAtProgress(_startProgress);

        // ==================================================================
        //  内部
        // ==================================================================

        private void Arrive()
        {
            _t = 0f;
            _waitRemaining = Mathf.Max(0f, _waitAtPoint);
            OnReached.Invoke(_to);

            switch (_mode)
            {
                case PathMode.Once:
                    if (_to >= _path.Length - 1) { _finished = true; _running = false; return; }
                    _from = _to;
                    _to = _to + 1;
                    break;

                case PathMode.Loop:
                    _from = _to;
                    _to = (_from + 1) % _path.Length;
                    break;

                default: // PingPong
                    _from = _to;
                    if (_from >= _path.Length - 1) _direction = -1;
                    else if (_from <= 0) _direction = 1;
                    _to = _from + _direction;
                    break;
            }
        }

        /// <summary>把配置铺成路径。出生点永远在 [0]，后面是各航点的世界坐标。</summary>
        private void Rebuild()
        {
            int count = _points == null ? 0 : _points.Length;
            _path = new Vector2[count + 1];
            _path[0] = _origin;
            for (int i = 0; i < count; i++) _path[i + 1] = _origin + _points[i];

            _from = 0;
            _to = _path.Length > 1 ? 1 : 0;
            _t = 0f;
            _direction = 1;
            _waitRemaining = 0f;
            _finished = false;
            Velocity = Vector2.zero;
        }

        /// <summary>
        /// 沿轨道走到 <paramref name="progress"/>（0 ~ 1 的**全长比例**）时是哪个点，
        /// 顺带报出它落在哪一段（<paramref name="from"/> / <paramref name="to"/> / <paramref name="t"/>）。
        /// </summary>
        /// <remarks>
        /// <para>比例按**全长**算，不是按段数：两段长短不一时，「走一半」应该落在长段的中腰，
        /// 而不是第一段的末尾。</para>
        /// <para>闭环模式把「末点 → 出生点」那一段也算进全长，所以 1 和 0 是同一个点。</para>
        /// <para>静态方法：编辑模式的 gizmo 也要用（那时还没有 <see cref="_path"/>，是现铺的）。</para>
        /// </remarks>
        private static Vector2 SamplePath(Vector2[] path, PathMode mode, float progress,
                                          out int from, out int to, out float t)
        {
            from = 0;
            to = 1;
            t = 0f;
            if (path == null || path.Length < 2) return path != null && path.Length > 0 ? path[0] : Vector2.zero;

            int last = path.Length - 1;
            int segments = mode == PathMode.Loop ? path.Length : last;

            float total = 0f;
            for (int i = 0; i < segments; i++) total += Vector2.Distance(path[i], path[(i + 1) % path.Length]);
            if (total <= 0.0001f) return path[0];

            float travel = Mathf.Clamp01(progress) * total;
            float walked = 0f;
            int segment = segments - 1;

            for (int i = 0; i < segments; i++)
            {
                float span = Vector2.Distance(path[i], path[(i + 1) % path.Length]);
                if (walked + span >= travel || i == segments - 1)
                {
                    segment = i;
                    t = span > 0.0001f ? Mathf.Clamp01((travel - walked) / span) : 0f;
                    break;
                }
                walked += span;
            }

            from = segment;
            to = (segment + 1) % path.Length;
            return Vector2.LerpUnclamped(path[from], path[to], t);
        }

        /// <summary>
        /// 把平台摆到 <paramref name="progress"/> 处，并把「现在走在哪一段」那套账本一起对好。
        /// </summary>
        /// <remarks>
        /// 只改位置、不改账本的话，下一帧会按**旧线段**算位移 —— 平台会先往错误的方向弹一下再走。
        /// </remarks>
        private void PlaceAtProgress(float progress)
        {
            if (_path == null || _path.Length < 2) return;

            int last = _path.Length - 1;
            Vector2 point = SamplePath(_path, _mode, progress, out int from, out int to, out float t);

            _from = from;
            _to = to;
            _t = t;
            _direction = 1;
            _waitRemaining = 0f;
            _finished = false;

            // 正好停在末点：往返模式下一步只能往回走；单程模式已经没有地方可去。
            // 不在这儿对好，第一帧就会先「到达」一次（白等一个物理步，还会多触发一次 OnReached）。
            bool atLastPoint = to >= last && t >= 0.9999f;

            if (atLastPoint && _mode == PathMode.PingPong)
            {
                _from = last;
                _to = last - 1;
                _t = 0f;
                _direction = -1;
                point = _path[last];
            }
            else if (atLastPoint && _mode == PathMode.Once)
            {
                _from = last;
                _to = last;
                _t = 0f;
                _finished = true;
                _running = false;
                point = _path[last];
            }

            if (_body != null) _body.position = point; else transform.position = point;
            Velocity = Vector2.zero;
        }

        /// <summary>沿位移方向 Cast，返回「最多能走原位移的几分之几」。撞不到就返回 1。</summary>
        private float SafeFraction(Vector2 delta)
        {
            float distance = delta.magnitude;
            if (distance < 0.0001f) return 1f;

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_solidMask);
            Vector2 direction = delta / distance;
            int count;
            if (_body != null) count = _body.Cast(direction, filter, _hits, distance + 0.02f);
            else if (_shape != null) count = _shape.Cast(direction, filter, _hits, distance + 0.02f);
            else return 1f;

            float fraction = 1f;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = _hits[i];
                // 只认「迎面挡住」的面：擦着走和背面的接触不算阻挡。
                if (hit.collider == _shape || Vector2.Dot(hit.normal, delta) >= -0.00001f) continue;
                fraction = Mathf.Min(fraction, Mathf.Max(0f, hit.distance - 0.015f) / distance);
            }
            return Mathf.Clamp01(fraction);
        }

        // ==================================================================
        //  快照（由宿主机关转发）
        // ==================================================================

        private struct TrackState
        {
            public int From, To, Direction;
            public float T, Wait;
            public bool Running, Finished;
        }

        /// <summary>给宿主机关的快照用：记下走到哪一段、哪一点、停多久。</summary>
        public object CaptureTrackState() => new TrackState
        {
            From = _from, To = _to, Direction = _direction, T = _t, Wait = _waitRemaining,
            Running = _running, Finished = _finished,
        };

        /// <summary>恢复 <see cref="CaptureTrackState"/> 记下的进度（位置本身由快照系统写回刚体）。</summary>
        public void RestoreTrackState(object state)
        {
            if (!(state is TrackState track)) return;
            _from = Mathf.Clamp(track.From, 0, Mathf.Max(0, _path.Length - 1));
            _to = Mathf.Clamp(track.To, 0, Mathf.Max(0, _path.Length - 1));
            _direction = track.Direction >= 0 ? 1 : -1;
            _t = Mathf.Clamp01(track.T);
            _waitRemaining = Mathf.Max(0f, track.Wait);
            _running = track.Running;
            _finished = track.Finished;
            Velocity = Vector2.zero;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        private void OnDrawGizmos()
        {
            Vector3 start = Application.isPlaying ? (Vector3)_origin : transform.position;
            Vector3 previous = start;
            int count = _points == null ? 0 : _points.Length;

            Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.9f);
            for (int i = 0; i < count; i++)
            {
                Vector3 point = start + (Vector3)_points[i];
                Gizmos.DrawLine(previous, point);
                Gizmos.DrawWireSphere(point, 0.16f);
                previous = point;
            }
            if (_mode == PathMode.Loop && count > 0) Gizmos.DrawLine(previous, start);

            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.9f);
            Gizmos.DrawWireSphere(start, 0.2f);

            // 开局停在哪（_startProgress）：编辑模式就能看见，不用进 Play 猜。
            if (_startProgress > 0.0001f && count > 0)
            {
                var preview = new Vector2[count + 1];
                preview[0] = start;
                for (int i = 0; i < count; i++) preview[i + 1] = (Vector2)start + _points[i];

                Vector2 parked = SamplePath(preview, _mode, _startProgress, out _, out _, out _);
                Gizmos.color = new Color(1f, 0.55f, 0.15f, 1f);
                Gizmos.DrawWireSphere(parked, 0.26f);
                Gizmos.DrawLine(start, parked);
            }

            if (!Application.isPlaying || _path == null || _path.Length < 2) return;
            Gizmos.color = new Color(1f, 0.95f, 0.3f, 1f);
            Gizmos.DrawWireSphere(Vector2.Lerp(_path[_from], _path[_to], _t), 0.22f);
        }
    }
}
