using System;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>
    /// **多站电梯**：车在 N 个站点之间升降，选哪一站由拉杆（<see cref="CityFloorLever"/>）决定。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不是 <c>CityPathMotor</c></b>：那个只有两站（一个 <c>TargetEnd</c> 布尔）。
    /// 这里要 N 站，而**旧的两站电梯一行都不能动** —— 所以是独立组件 + 独立预制体，
    /// <c>CityPathMotor</c> / <c>CityLift</c> / <c>CityLever</c> 都不碰。</para>
    ///
    /// <para><b>站点 = 相对初始位置的世界偏移</b>（与 <c>CityPathMotor.EndOffset</c> 同一个口径）：
    /// 整台电梯拖到别的房间，站点跟着走。不要缩放根物体（子物体会继承缩放）。</para>
    ///
    /// <para><b>为什么拉杆不是车的子物体</b>：每个 <c>CityMechanism</c> 都要求自己的刚体，
    /// 而 Kinematic 子刚体挂在父级 <c>MovePosition</c> 下面会**晚一个物理步**（2 m/s 一帧 4 cm，
    /// 高速电梯更明显）。所以拉杆放在**兄弟节点**，由本组件把这一步的位移**显式**交给
    /// <see cref="_carriedBodies"/>，两边走的是同一个 delta。</para>
    ///
    /// <para>承载走 <c>PlayerController.CarryOnPlatform</c>（与 <c>CityPathMotor</c> / <c>SpringBackBody</c>
    /// 同一条路）；遇阻用同一套 <c>SafeFraction</c> 把位移夹短 —— 车停住，不把人挤进墙里。</para>
    /// </remarks>
    [DefaultExecutionOrder(-40)] // 承载位移先提交，玩家再在自己的 Slide 里解算。
    public sealed class CityMultiStopLift : CityMechanism
    {
        [Header("站点（相对初始位置的世界偏移，从低到高）")]
        [SerializeField] private Vector2[] _stops =
        {
            Vector2.zero,
            new Vector2(0f, 4f),
            new Vector2(0f, 8f),
        };

        [Header("运动")]
        [Tooltip("升降速度（米 / 秒）。")]
        [SerializeField, Min(0.05f)] private float _speed = 2f;

        [Tooltip("到站后停多久（秒）。0 = 不停。")]
        [SerializeField, Min(0f)] private float _waitAtStop = 0.4f;

        [Tooltip("站上去的人会被带走。")]
        [SerializeField] private bool _carryRiders = true;

        [Tooltip("什么层挡得住它（默认 Ground）。被挡住时车停住，不硬挤。")]
        [SerializeField] private LayerMask _solidMask = 1 << 8;

        [Header("随车刚体（拉杆等）")]
        [Tooltip("这些刚体每物理步跟着车走**同一个位移**。拉杆拖这里，**不要**挂成车的子物体。")]
        [SerializeField] private Rigidbody2D[] _carriedBodies = Array.Empty<Rigidbody2D>();

        /// <summary>到站（参数是站点下标）。开局落到起始站也会发一次。</summary>
        public event Action<int> OnArrived;

        private Vector2 _origin;
        private Vector2[] _carriedOffsets = Array.Empty<Vector2>();
        private int _targetStop;
        private int _arrivedStop;
        private float _wait;

        private readonly Collider2D[] _overlaps = new Collider2D[24];
        private readonly RaycastHit2D[] _hits = new RaycastHit2D[16];
        private readonly HashSet<PlayerController> _riders = new HashSet<PlayerController>();

        /// <summary>站点数。</summary>
        public int StopCount => _stops != null ? _stops.Length : 0;

        /// <summary>目标站（0 = 最低）。</summary>
        public int TargetStop => _targetStop;

        /// <summary>车现在停在哪一站；移动途中是 -1。</summary>
        public int ArrivedStop => _arrivedStop;

        /// <summary>正停在某一站上（没在走）。</summary>
        public bool IsStopped => _arrivedStop >= 0;

        protected override void Awake()
        {
            base.Awake();

            _origin = Body.position;
            int last = Mathf.Max(0, StopCount - 1);
            _targetStop = Mathf.Clamp(_targetStop, 0, last);
            _arrivedStop = _targetStop;
            CaptureCarriedOffsets();
        }

        /// <summary>
        /// 选站。**已经在那一站就什么都不做** —— 否则每拨一下都会重发到站、重开停表。
        /// </summary>
        public void SelectStop(int index)
        {
            if (StopCount == 0) return;

            index = Mathf.Clamp(index, 0, StopCount - 1);
            if (index == _targetStop && _arrivedStop == index) return;

            _targetStop = index;
            _arrivedStop = -1;
            _wait = 0f;
        }

        private void CaptureCarriedOffsets()
        {
            _carriedOffsets = new Vector2[_carriedBodies != null ? _carriedBodies.Length : 0];
            for (int i = 0; i < _carriedOffsets.Length; i++)
            {
                Rigidbody2D body = _carriedBodies[i];
                if (body == null) continue;

                // 编辑器里（生成工具）Awake 还没跑过，Body 是 null —— 退回 transform。
                Vector2 self = Body != null ? Body.position : (Vector2)transform.position;
                _carriedOffsets[i] = body.position - self;
            }
        }

        private void FixedUpdate()
        {
            if (Paused || StopCount == 0) return;

            if (_wait > 0f)
            {
                _wait -= Time.fixedDeltaTime;
                return;
            }

            Vector2 target = _origin + _stops[_targetStop];
            Vector2 remaining = target - Body.position;
            float distance = Mathf.Min(_speed * Time.fixedDeltaTime, remaining.magnitude);

            if (distance <= 0.0001f)
            {
                Arrive();
                return;
            }

            Vector2 step = remaining.normalized * distance;
            float fraction = SafeFraction(Body, step, null, _solidMask);

            if (_carryRiders)
            {
                FindRiders();
                foreach (PlayerController rider in _riders)
                {
                    fraction = Mathf.Min(fraction, SafeFraction(rider.GetComponent<Rigidbody2D>(), step, Shape, _solidMask));
                }
            }

            step *= fraction;

            if (_carryRiders)
                foreach (PlayerController rider in _riders) rider.CarryOnPlatform(step, Shape);

            // 随车刚体：**同一个 step**，不是靠父级变换（那会晚一个物理步）。
            for (int i = 0; i < _carriedBodies.Length; i++)
            {
                Rigidbody2D body = _carriedBodies[i];
                if (body == null) continue;
                body.MovePosition(body.position + step);
            }

            Body.MovePosition(Body.position + step);

            if (fraction >= 0.999f && step.magnitude >= remaining.magnitude - 0.001f) Arrive();
        }

        private void Arrive()
        {
            if (_arrivedStop == _targetStop) return;

            _arrivedStop = _targetStop;
            _wait = _waitAtStop;
            OnArrived?.Invoke(_arrivedStop);
        }

        // ==================================================================
        //  承载与碰撞（与 CityPathMotor 同一套做法，**复制**而不是改它 —— 旧电梯不能动）
        // ==================================================================

        private void FindRiders()
        {
            _riders.Clear();
            if (!_carryRiders || Shape == null) return;

            Bounds b = Shape.bounds;
            var filter = new ContactFilter2D { useTriggers = false };
            int count = Physics2D.OverlapBox(b.center, b.size + new Vector3(.1f, .35f), 0f, filter, _overlaps);

            Vector2 up = Physics2D.gravity.sqrMagnitude > .001f ? -Physics2D.gravity.normalized : Vector2.up;
            for (int i = 0; i < count; i++)
            {
                var player = _overlaps[i].GetComponentInParent<PlayerController>();
                if (player == null || !player.IsGrounded) continue;

                Bounds p = _overlaps[i].bounds;
                float height = Vector2.Dot((Vector2)p.center - up * p.extents.y - ((Vector2)b.center + up * b.extents.y), up);
                if (height >= -.08f && height <= .18f && p.max.x > b.min.x + .02f && p.min.x < b.max.x - .02f) _riders.Add(player);
            }
        }

        private float SafeFraction(Rigidbody2D body, Vector2 delta, Collider2D ignored, int mask)
        {
            float distance = delta.magnitude;
            if (distance < .0001f) return 1f;

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(mask);

            int count = body.Cast(delta / distance, filter, _hits, distance + .02f);
            float fraction = 1f;
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == ignored || Vector2.Dot(hit.normal, delta) >= -.00001f) continue;
                fraction = Mathf.Min(fraction, Mathf.Max(0f, hit.distance - .015f) / distance);
            }
            return Mathf.Clamp01(fraction);
        }

        // ==================================================================
        //  快照
        // ==================================================================

        private struct State
        {
            public int Target;
            public int Arrived;
            public float Wait;
        }

        protected override object CaptureMechanismState()
            => new State { Target = _targetStop, Arrived = _arrivedStop, Wait = _wait };

        protected override void RestoreMechanismState(object state)
        {
            if (!(state is State snapshot)) return;

            int last = Mathf.Max(0, StopCount - 1);
            _targetStop = Mathf.Clamp(snapshot.Target, 0, last);
            _arrivedStop = snapshot.Arrived;
            _wait = snapshot.Wait;
            _riders.Clear();
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：一次写三个站点。</summary>
        public void SetStops(Vector2[] stops)
        {
            _stops = stops != null && stops.Length > 0
                ? stops
                : new[] { Vector2.zero };
            _targetStop = Mathf.Clamp(_targetStop, 0, StopCount - 1);
        }

        /// <summary>只给编辑器工具用：接上随车刚体（拉杆）。</summary>
        public void SetCarriedBodies(Rigidbody2D[] bodies)
        {
            _carriedBodies = bodies ?? Array.Empty<Rigidbody2D>();
            CaptureCarriedOffsets();
        }

        private void OnDrawGizmosSelected()
        {
            if (_stops == null) return;

            Vector3 origin = Application.isPlaying ? (Vector3)_origin : transform.position;
            Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.95f);
            for (int i = 0; i < _stops.Length; i++)
            {
                Vector3 p = origin + (Vector3)_stops[i];
                Gizmos.DrawWireSphere(p, 0.18f);
                if (i > 0) Gizmos.DrawLine(origin + (Vector3)_stops[i - 1], p);
            }

            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.95f);
            Gizmos.DrawWireCube(origin + (Vector3)_stops[Mathf.Clamp(_targetStop, 0, _stops.Length - 1)],
                                new Vector3(3f, 0.5f));
        }
    }
}
