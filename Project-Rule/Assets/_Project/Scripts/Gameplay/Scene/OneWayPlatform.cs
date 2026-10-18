using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 跳穿平台（单向）：只能从**实心面**落地，从背面可以直接穿过去。
    /// </summary>
    /// <remarks>
    /// <para><b>主动跳穿</b>：站在实心面上**同时按住下 + 按跳**（正常重力下 = S + 空格）就放行一小段时间，
    /// 玩家自己钻下去。跳键这一帧被玩家吃掉，不会先起跳。重力翻转后「下」跟着翻，组合键不变。</para>
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
        [Tooltip("站在实心面上「按住下 + 按跳」（正常重力下 = S + 空格）就放行")]
        [SerializeField] private bool _dropOnDown = true;

        [Tooltip("放行时长（秒）：这段时间对玩家不实心，够他穿过去")]
        [SerializeField] private float _dropDuration = 0.35f;

        [Header("重物（拼装件）")]
        [Tooltip("勾上后：Dynamic 的拼装件从背面过来会被放行（能穿上来），从实心面过来照常落地。取消勾选 = 对重物是实心墙。")]
        [SerializeField] private bool _passFitBlocks = true;

        private BoxCollider2D _collider;
        private PlayerController _player;
        private float _nextSearch;
        private float _dropTimer;
        private bool _solid = true;
        private bool _visualFlipped;

        /// <summary>此刻被本平台放行的件（已经关掉碰撞的那几个）。</summary>
        private readonly System.Collections.Generic.HashSet<Collider2D> _passingBlocks =
            new System.Collections.Generic.HashSet<Collider2D>();

        /// <summary>实心面上方有件压着 —— 这时平台对谁都必须是实心的，否则件会掉下去。</summary>
        private bool _blockAbove;

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

        /// <summary>场上所有跳穿平台（运行时注册；玩家按「下+跳」时用来问「有没有平台认定我在上面」）。</summary>
        private static readonly System.Collections.Generic.List<OneWayPlatform> s_all
            = new System.Collections.Generic.List<OneWayPlatform>();

        private void OnEnable()
        {
            if (!s_all.Contains(this)) s_all.Add(this);
        }

        private void OnDisable()
        {
            s_all.Remove(this);
        }

        /// <summary>有没有任意一块跳穿平台认定「玩家站在我上面」。给玩家判断要不要吃掉跳键用。</summary>
        public static bool AnyClaims(PlayerController player)
        {
            for (int i = 0; i < s_all.Count; i++)
            {
                var p = s_all[i];
                if (p != null && p.isActiveAndEnabled && p.Claims(player)) return true;
            }
            return false;
        }

        /// <summary>
        /// 玩家是不是站在我上面（**纯几何**，不做物理查询）。
        /// </summary>
        /// <remarks>
        /// <para>为什么不用射线 / 形状扫描：人站在平台上时两者互相压着，
        /// 形状扫描对「起始就重叠」的碰撞体不一定报命中 —— 那正是「狂按空格偶尔才生效」的原因。</para>
        /// <para>判据 = 玩家中心在实心面这一侧，且距离不超过「平台半厚 + 玩家半身高 + 一点余量」。
        /// 余量不能太大：上下叠放的平台，下面那块必须被排除。</para>
        /// </remarks>
        public bool Claims(PlayerController player)
        {
            if (player == null || _collider == null) return false;

            Vector2 normal = SolidNormal;

            // 碰撞体刚建好、还没被物理同步过时 bounds 可能是 0 —— 退回 transform 原点，别拿世界原点算距离。
            Vector2 center = _collider.bounds.extents.sqrMagnitude > 0.000001f
                ? (Vector2)_collider.bounds.center
                : (Vector2)transform.position;

            float side = Vector2.Dot((Vector2)player.transform.position - center, normal);

            return side > 0f && side <= Reach(normal);
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

            bool solid = EvaluatePlayer(normal);
            UpdateBlocks(normal);

            // 有件压在实心面上时必须是实心的，否则件会连人带板一起掉下去。
            SetSolid(solid || _blockAbove);
        }

        /// <summary>玩家这一侧要不要实心（单向 + 主动跳穿）。</summary>
        private bool EvaluatePlayer(Vector2 normal)
        {
            if ((_player == null || !_player.isActiveAndEnabled || !_player.IsInputReady) && !TryFindPlayer())
                return true;

            // 用**碰撞体中心**而不是 transform 原点：平台被摆歪 / 碰撞体带 offset 时原点不可靠。
            Vector2 center = _collider != null ? (Vector2)_collider.bounds.center : (Vector2)transform.position;
            Vector2 delta = (Vector2)_player.transform.position - center;
            float side = Vector2.Dot(delta, normal);

            // ① 主动跳穿：放行窗口内一律不实心，等玩家掉下去。
            if (_dropTimer > 0f)
            {
                _dropTimer -= UnityEngine.Time.fixedDeltaTime;
                return false;
            }

            // 站在**这一块**上 + 这一帧有「下钻」请求（按住下 + 按跳）→ 开一个放行窗口。
            // 请求在玩家身上生成、取走即清零：从上层掉到中层时那个组合已经用过了，
            // 中层不会跟着放行（否则会穿过一层、停在第三层）。
            // 「站在上面」用纯几何的 Claims（不做物理查询 —— 扫描对「贴着」的碰撞体不可靠）。
            if (_dropOnDown && Claims(_player) && _player.ConsumeDropRequest())
            {
                _dropTimer = Mathf.Max(0.05f, _dropDuration);
                return false;
            }

            // ② 单向：玩家在背面就放行，等他整个越过实心面再恢复，落地才不抖。
            return _solid ? side >= -_reenableMargin : side > _reenableMargin;
        }

        /// <summary>
        /// 拼装件：**只对那一对碰撞体**开关碰撞 —— 件在背面就放行，能穿上来；越过实心面就恢复，能落住。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么不用 <c>collider.enabled</c></b>：那是整块平台的开关，件一穿就把玩家一起关了。
        /// <c>Physics2D.IgnoreCollision</c> 按「碰撞体对」生效，件和玩家各管各的。</para>
        ///
        /// <para><b>放行到哪为止</b>：件心越过「实心面 + 半个件厚」才算过来 ——
        /// 提早恢复会在平面上卡一下（件被顶回去），延后恢复件会直接穿下去。</para>
        /// </remarks>
        private void UpdateBlocks(Vector2 normal)
        {
            _blockAbove = false;

            System.Collections.Generic.IReadOnlyList<FitBlock> blocks = FitBlock.All;
            for (int i = 0; i < blocks.Count; i++)
            {
                FitBlock block = blocks[i];
                if (block == null || block.Body == null) continue;

                Collider2D body = block.Body.GetComponent<Collider2D>();
                if (body == null || body.isTrigger) continue;

                // ⚠️ 件用**平台自己的 up**，不跟重力翻 —— 跟重力翻的话，
                // 重力一反转「实心面」就跑到下面去了，从井底升上来的球反而正撞实心面。
                // 跳穿平台对件的语义很简单：**从下面顶上来就放行，落到上面就接住**。
                Vector2 up = transform.up;

                Vector2 delta = (Vector2)block.transform.position - (Vector2)transform.position;
                float side = Vector2.Dot(delta, up);

                // 放行条件 = **正在朝上过来**（v·up > 0）且还没整个越过。
                // 只看位置不行：接触点恰好等于「件心越过平面」那一刻，恢复碰撞会把件卡在面里。
                // 加一段余量，等件真正离开再恢复 —— 停住（v≈0）的件立刻恢复，所以能落住。
                float clearance = HalfExtent(body, up) + HalfExtent(_collider, up);
                bool rising = Vector2.Dot(block.Body.linearVelocity, up) > 0.05f;
                bool pass = _passFitBlocks && rising && side < clearance + 0.30f;

                if (side >= clearance) _blockAbove = true;

                if (pass)
                {
                    if (_passingBlocks.Add(body)) Physics2D.IgnoreCollision(body, _collider, true);
                }
                else if (_passingBlocks.Remove(body))
                {
                    Physics2D.IgnoreCollision(body, _collider, false);
                }
            }
        }

        /// <summary>
        /// <summary>站立时玩家中心到平台中心的最大允许距离。</summary>
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

        /// <summary>
        /// 找玩家：取**最近的、已经初始化好的**那个。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ 不能只取「随便一个」：运行时可能同时存在**空壳实例**（<c>_input</c> 还是 null、
        /// 位置停在预制体原点、每帧抛 NRE），随机取到它就会表现成「按了完全没反应 / 偶尔才成功」。</para>
        /// <para>取最近的那个同时解决「多玩家」和「空壳在远处」两种情况。</para>
        /// </remarks>
        private bool TryFindPlayer()
        {
            if (UnityEngine.Time.time < _nextSearch) return false;
            _nextSearch = UnityEngine.Time.time + Mathf.Max(0.05f, _searchInterval);

            PlayerController best = null;
            float bestSqr = float.MaxValue;
            Vector2 self = transform.position;

            foreach (var p in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
            {
                if (p == null || !p.isActiveAndEnabled || !p.IsInputReady) continue;

                float d = ((Vector2)p.transform.position - self).sqrMagnitude;
                if (d >= bestSqr) continue;

                bestSqr = d;
                best = p;
            }

            if (best == null) return false;

            _player = best;
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
