using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 跳穿平台（单向）：只能从**实心面**落地，从背面可以直接穿过去。
    /// </summary>
    /// <remarks>
    /// <para><b>主动跳穿</b>：站在实心面上朝背面按方向键（正常重力下就是 <b>S / ↓</b>）就放行一小段时间，
    /// 玩家自己钻下去。方向是相对**实心面**算的，所以重力翻转后「往下钻」的键会自然变成 ↑。</para>
    ///
    /// <para><b>可翻转 / 可旋转</b>：实心面默认取「重力反方向」（<c>_followGravity</c>）——
    /// 重力规则一翻，玩家从下方掉上来也能落地。关掉它就用物体的 <c>up</c> 当实心面，
    /// 把物体转 180°（或任意角度）就能改单向。</para>
    ///
    /// <para><b>为什么不用 PlatformEffector2D</b>：玩家是 Kinematic + <c>Rigidbody2D.Slide</c>，
    /// 位移和接地探测都走 shape cast，不交给物理求解器裁决 —— 效应器对它不可靠。
    /// 这里按「玩家在实心面的哪一侧」直接开关碰撞体，判定和移动系统完全一致。</para>
    ///
    /// <para><b>必须放 Ground 层</b>：玩家的 <c>_solidLayer</c> 只扫这一层，放错层就是踩空的空气。</para>
    /// </remarks>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class OneWayPlatform : MonoBehaviour
    {
        [Header("实心面朝向")]
        [Tooltip("开：实心面永远背着重力（重力规则翻转后自动跟着翻）。关：用物体的 up 当实心面，旋转物体即可改单向")]
        [SerializeField] private bool _followGravity = true;

        [Tooltip("把实心面反过来（重力模式 / 旋转模式都生效）")]
        [SerializeField] private bool _invertNormal;

        [Tooltip("要跟着翻面的视觉子物体（留空 = 不翻）。只影响表现，不影响判定")]
        [SerializeField] private Transform _visual;

        [Header("判定")]
        [Tooltip("玩家中心越过实心面多少才恢复实心。太小会贴着平台抖，太大会「陷进去」一点")]
        [SerializeField] private float _reenableMargin = 0.04f;

        [Tooltip("找玩家的重试间隔（玩家可能是运行时生成的）")]
        [SerializeField] private float _searchInterval = 0.5f;

        [Header("主动跳穿")]
        [Tooltip("站在实心面上朝背面按方向键（正常重力下 = S / ↓）就放行")]
        [SerializeField] private bool _dropOnDown = true;

        [Tooltip("放行时长（秒）：这段时间对玩家不实心，够他穿过去")]
        [SerializeField] private float _dropDuration = 0.35f;

        private BoxCollider2D _collider;
        private PlayerController _player;
        private float _nextSearch;
        private float _dropTimer;
        private bool _solid = true;
        private bool _visualFlipped;

        /// <summary>诊断日志的节流时间（静态：所有平台共用一个，避免刷屏）。</summary>
        private static float _nextDiag;

        /// <summary>此刻对玩家是否实心。</summary>
        public bool IsSolid => _solid;

        /// <summary>主动跳穿的放行时长（<see cref="OneWayField"/> 批量生成时会覆盖它）。</summary>
        public float DropDuration
        {
            get => _dropDuration;
            set => _dropDuration = Mathf.Max(0.05f, value);
        }

        /// <summary>实心面朝向：从平台中心指向「能站在上面」的那一侧。</summary>
        public Vector2 SolidNormal
        {
            get
            {
                if (_followGravity && Physics2D.gravity.sqrMagnitude > 0.0001f)
                {
                    Vector2 n = -Physics2D.gravity.normalized;
                    return _invertNormal ? -n : n;
                }

                Vector2 up = transform.up;
                return _invertNormal ? -up : up;
            }
        }

        private void Awake()
        {
            _collider = GetComponent<BoxCollider2D>();
        }

        private void FixedUpdate()
        {
            if (_collider == null) return;

            Vector2 normal = SolidNormal;
            UpdateVisual(normal);

            if (_player == null && !TryFindPlayer())
            {
                SetSolid(true);
                return;
            }

            Vector2 delta = (Vector2)_player.transform.position - (Vector2)transform.position;
            float side = Vector2.Dot(delta, normal);

            // ① 主动跳穿：放行窗口内一律不实心，等玩家掉下去。
            if (_dropTimer > 0f)
            {
                _dropTimer -= UnityEngine.Time.fixedDeltaTime;
                SetSolid(false);
                return;
            }

            // 站在**这一块**上 + 刚按下「朝背面」的方向键 → 开一个放行窗口。
            // 「刚按下」是玩家身上的一次性闩锁：按住不放不会连续放行，
            // 否则从上层掉下来、落在中层的那一帧会把中层也放行（看起来像穿过一层）。
            bool resting = _player.IsGrounded
                           || Mathf.Abs(Vector2.Dot(_player.Velocity, normal)) < 0.5f;

            bool onMe = false;
            if (_dropOnDown && resting && side > 0f)
            {
                onMe = IsStandingOnMe(normal);
                if (onMe && _player.TryConsumeDropPress())
                {
                    _dropTimer = Mathf.Max(0.05f, _dropDuration);
                    SetSolid(false);
                    return;
                }
            }

            // 诊断：按住了方向键却不下钻时，每秒说一次是哪一条没通过（不刷屏）。
            if (_player.DropPressHeld && UnityEngine.Time.time >= _nextDiag)
            {
                _nextDiag = UnityEngine.Time.time + 1f;
                if (!resting || side <= 0f || !onMe)
                {
                    Debug.Log("[跳穿平台] 按住了下方向键但没下钻："
                              + $"resting={resting} side={side:0.00} 踩在我上面={onMe} "
                              + $"玩家={_player.transform.position} 平台={transform.position}");
                }
            }

            // ② 单向：玩家在背面就放行，等他整个越过实心面再恢复，落地才不抖。
            if (_solid)
            {
                if (side < -_reenableMargin) SetSolid(false);
            }
            else if (side > _reenableMargin)
            {
                SetSolid(true);
            }
        }

        /// <summary>
        /// 玩家是不是**踩在这一块**上：从脚下朝背面扫一下，**脚下第一块**必须是我自己。
        /// </summary>
        /// <remarks>
        /// <para>为什么不用「中心距离」算：距离依赖平台的 transform 原点、碰撞体 offset、
        /// 缩放和厚度，任何一项不对就会误判成「没站在上面」（表现就是按键没反应）。
        /// 物理扫描只回答一个问题 —— 脚底下那块是不是我，和几何怎么摆无关。</para>
        ///
        /// <para>扫不到时退回几何判定，避免刚体形状还没同步的那一帧把功能整个卡死。</para>
        /// </remarks>
        private bool IsStandingOnMe(Vector2 normal)
        {
            if (_player == null) return false;

            var body = _player.GetComponent<Collider2D>();
            if (body != null)
            {
                var filter = new ContactFilter2D { useTriggers = false };
                filter.SetLayerMask(Physics2D.AllLayers);

                var hits = new RaycastHit2D[4];
                int count = body.Cast(-normal, filter, hits, 0.25f);
                for (int i = 0; i < count; i++)
                {
                    var col = hits[i].collider;
                    if (col == null || col == body) continue;

                    return col == _collider;        // 只认脚下第一块
                }
            }

            return Reach(normal) >= Vector2.Dot((Vector2)_player.transform.position - (Vector2)transform.position, normal);
        }

        /// <summary>站立时玩家中心到平台中心的最大允许距离（几何兜底用）。</summary>
        private float Reach(Vector2 normal)
        {
            float reach = _reenableMargin + 0.2f + HalfExtent(_collider, normal);

            var body = _player != null ? _player.GetComponent<Collider2D>() : null;
            reach += body != null ? HalfExtent(body, normal) : 0.3f;

            return reach;
        }

        /// <summary>碰撞体沿 <paramref name="normal"/> 方向的半尺寸（对旋转也成立）。</summary>
        private static float HalfExtent(Collider2D collider, Vector2 normal)
        {
            if (collider == null) return 0f;

            Bounds b = collider.bounds;
            if (b.extents.sqrMagnitude < 0.000001f) return 0.1f;     // 还没实例化时给个兜底

            return Mathf.Abs(normal.x) * b.extents.x + Mathf.Abs(normal.y) * b.extents.y;
        }

        private void SetSolid(bool solid)
        {
            if (_solid == solid) return;

            _solid = solid;
            if (_collider != null) _collider.enabled = solid;
        }

        /// <summary>重力翻转时把视觉一起翻过来：否则玩家会看到「平台还是正的，人却挂在下面」。</summary>
        private void UpdateVisual(Vector2 normal)
        {
            if (_visual == null) return;

            bool flip = normal.y < 0f;
            if (flip == _visualFlipped) return;

            _visualFlipped = flip;
            _visual.localRotation = flip ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
        }

        private bool TryFindPlayer()
        {
            if (UnityEngine.Time.time < _nextSearch) return false;
            _nextSearch = UnityEngine.Time.time + Mathf.Max(0.05f, _searchInterval);

            var found = FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (found == null || !found.gameObject.activeInHierarchy) return false;

            _player = found;
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 normal = SolidNormal;
            Gizmos.color = _solid ? Color.cyan : new Color(1f, 1f, 1f, 0.35f);
            Vector3 from = transform.position;
            Gizmos.DrawLine(from, from + (Vector3)(normal * 0.8f));
            Gizmos.DrawSphere(from + (Vector3)(normal * 0.8f), 0.06f);
        }
    }
}
