using UnityEngine;

namespace RuleGame.Development.P3
{
    /// <summary>
    /// 白盒调试用跟随摄像机：阻尼跟随 + 死区 + 前瞻。
    /// </summary>
    /// <remarks>
    /// <para>⚠️ 这是 <b>P3 开发与手感测试用</b>的临时摄像机，不是正式方案 ——
    /// 正式的场景摄像机归 P4（场景与叙事）。放在 <c>Development/P3/</c> 是为了与生产代码隔离。</para>
    /// <para>为什么必须写在 <see cref="LateUpdate"/>：玩家是在 <c>FixedUpdate</c> 里移动的，
    /// 在 <c>Update</c> 里跟随会读到「上一帧的位置」，画面会抖。</para>
    /// <para>阻尼用 <see cref="Mathf.SmoothDamp"/>（基于速度的平滑），而不是
    /// <c>Lerp(a, b, k * dt)</c> —— 后者的实际阻尼强度随帧率变化，快机器和慢机器手感不一致。</para>
    /// <para><b>纵向对齐 Cinemachine Position Composer 的模型</b>（dead zone / soft zone / lookahead）。
    /// 关键是「前瞻前馈」：<see cref="Mathf.SmoothDamp"/> 对匀速目标有<b>恒定滞后</b>
    /// <c>v × smoothTime</c>，把同样大小的量加到输入上精确抵消它 ——
    /// 目标因此稳定停在 <c>_offset</c> 处（居中），而不是被拖在画面后面。</para>
    /// </remarks>
    public class P3FollowCamera : MonoBehaviour
    {
        // ==================================================================
        //  序列化字段
        // ==================================================================

        [Header("目标")]
        [Tooltip("跟随谁。留空则自动找场景里带 PlayerController 的物体。")]
        [SerializeField] private Transform _target;

        [Tooltip("相对目标的取景偏移（一般把角色放画面偏下一点）。")]
        [SerializeField] private Vector2 _offset = new Vector2(0f, 0.5f);

        [Tooltip("死区大小（世界单位）：目标在这个框内移动时摄像机不动。" +
                 "没有死区的话摄像机会死贴每一次微小移动，画面不断漂 —— 这是「跟随感觉奇怪」的主因。")]
        [SerializeField] private Vector2 _deadZone = new Vector2(1.2f, 0f);

        [Header("阻尼")]
        [Tooltip("横向追上目标所需时间（秒）。越小越跟手。")]
        [SerializeField] private float _smoothTimeX = 0.12f;

        [Tooltip("纵向**低速**时的追赶时间。跳跃这类短促运动走这一档 —— 起跳/落地的速度突变靠它吸掉，画面不上下猛甩。")]
        [SerializeField] private float _smoothTimeY = 0.28f;

        [Tooltip("纵向**高速**时的追赶时间。自由落体与重力反转走这一档 —— 慢档在这个速度下必然掉队，必须切快。")]
        [SerializeField] private float _smoothTimeYFast = 0.06f;

        [Tooltip("纵向速度超过它就开始切向快阻尼档（到它的两倍时完全切完）。")]
        [SerializeField] private float _fastFollowSpeed = 8f;

        [Header("前瞻")]
        [Tooltip("朝移动方向多留出的距离：让你更早看见前进方向上的东西。0 = 关闭。")]
        [SerializeField] private float _lookAhead = 1.2f;

        [Tooltip("前瞻翻转的平滑时间，避免左右横跳时画面来回抽。")]
        [SerializeField] private float _lookAheadSmoothTime = 0.25f;

        [Header("兜底")]
        [Tooltip("纵向最大允许滞后（世界单位）。超出就硬夹回来 —— 参数调错、或被规则把速度拉到极端时，" +
                 "保证目标也不会跑出画面。")]
        [SerializeField] private float _maxLagY = 3f;

        // ==================================================================
        //  运行时状态
        // ==================================================================

        private Vector2 _velocity;
        private float   _leadX;
        private float   _leadVelocity;
        private Vector2 _lastTargetPosition;
        private float   _keepZ;
        private bool    _initialized;

        /// <summary>目标是玩家时能读到物理帧解算出的稳定速度；不是则退回位移差分。</summary>
        private RuleGame.Gameplay.Player.PlayerController _player;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _keepZ = transform.position.z;      // 只动 XY，Z 保持原样（2D 摄像机不会被推远）

            if (_target == null)
            {
                _player = FindAnyObjectByType<RuleGame.Gameplay.Player.PlayerController>();
                if (_player != null) _target = _player.transform;
            }
            else
            {
                _player = _target.GetComponent<RuleGame.Gameplay.Player.PlayerController>();
            }
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector2 targetPosition = _target.position;

            // 本帧位移（横向用来推朝向；纵向只在拿不到玩家速度时当差分之用）
            float dx = _initialized ? targetPosition.x - _lastTargetPosition.x : 0f;
            float dy = _initialized ? targetPosition.y - _lastTargetPosition.y : 0f;
            _lastTargetPosition = targetPosition;

            // ---- 横向前瞻：用「目标这一帧移动了多少」推断朝向 ----
            // 停下来时前瞻平滑回中；不然会一直保持「朝上次方向偏一段」的姿态。
            float desiredLead = Mathf.Abs(dx) > 0.0001f ? Mathf.Sign(dx) * _lookAhead : 0f;
            _leadX = Mathf.SmoothDamp(_leadX, desiredLead, ref _leadVelocity, _lookAheadSmoothTime, Mathf.Infinity, dt);

            // ---- 纵向：自适应阻尼 + 前瞻前馈 ----
            float vy = ReadVerticalVelocity(dy, dt);

            // 阻尼档看**摄像机自己**跑多快（也兼顾目标速度）：谁快谁用硬阻尼。
            // ⚠️ 不能只看目标速度 —— 落地那一刻目标速度已经归零，档位会立刻退回慢档（0.28），
            //    而摄像机身上还带着 −30 的动量，于是刹不住、冲过头（实测过冲约 1.5 单位、1.7 秒才回正）。
            //    取两者较大值：落地时用摄像机的速度刹住，起步时用目标的速度跟紧。
            float speedNow = Mathf.Max(Mathf.Abs(_velocity.y), Mathf.Abs(vy));
            float speedT   = Mathf.InverseLerp(_fastFollowSpeed, _fastFollowSpeed * 2f, speedNow);
            float smoothY  = Mathf.Lerp(_smoothTimeY, _smoothTimeYFast, speedT);

            // 前馈量 = 速度 × 当前的阻尼时间。这正好是 SmoothDamp 在匀速下的稳态滞后量，
            // 加到输入上便精确抵消 → 目标稳定停在取景位（居中），而不是被拖在后面。
            float yLead = vy * smoothY;

            Vector2 desired = targetPosition + _offset + new Vector2(_leadX, yLead);

            if (!_initialized)
            {
                // 开场直接归位，否则摄像机会从初始位置一路飘过去
                _initialized = true;
                transform.position = new Vector3(desired.x, desired.y, _keepZ);
                return;
            }

            // 死区：目标没走出这个框时摄像机原地不动；走出去了才追到「框边」而不是追目标本身。
            // 纵向默认留 0 —— 目标是「玩家尽量待在视野中心」，只剩前馈在管纵向取景。
            Vector2 delta = desired - (Vector2)transform.position;
            float halfX = _deadZone.x * 0.5f;
            float halfY = _deadZone.y * 0.5f;
            delta.x = Mathf.Abs(delta.x) < halfX ? 0f : delta.x - Mathf.Sign(delta.x) * halfX;
            delta.y = Mathf.Abs(delta.y) < halfY ? 0f : delta.y - Mathf.Sign(delta.y) * halfY;

            Vector2 goal = (Vector2)transform.position + delta;
            Vector2 current = transform.position;
            current.x = Mathf.SmoothDamp(current.x, goal.x, ref _velocity.x, _smoothTimeX, Mathf.Infinity, dt);
            current.y = Mathf.SmoothDamp(current.y, goal.y, ref _velocity.y, smoothY,       Mathf.Infinity, dt);

            // 硬兜底：纵向滞后一旦超限就夹回来（对齐官方 Position Composer 的 Hard Limits）
            float lagY = desired.y - current.y;
            if (Mathf.Abs(lagY) > _maxLagY)
                current.y = desired.y - Mathf.Sign(lagY) * _maxLagY;

            transform.position = new Vector3(current.x, current.y, _keepZ);
        }

        /// <summary>
        /// 取目标的纵向速度。
        /// </summary>
        /// <remarks>
        /// 玩家走 <see cref="RuleGame.Gameplay.Player.PlayerController.Velocity"/>：
        /// 那是**物理帧解算出来的**值，一个物理帧内恒定，在 <see cref="LateUpdate"/> 里读天然不抖。
        /// 用位移差分会撞上「物理 50Hz / 渲染帧更高」的拍频 —— 有的帧位移为 0、有的帧跳两格，
        /// 前馈量跟着锯齿，画面就抖。
        /// （对方不是玩家时只能退回差分：抖动是已知代价，本摄像机目前只服务玩家。）
        /// </remarks>
        private float ReadVerticalVelocity(float deltaY, float dt)
        {
            if (_player != null) return _player.Velocity.y;
            return _initialized ? deltaY / dt : 0f;
        }
    }
}
