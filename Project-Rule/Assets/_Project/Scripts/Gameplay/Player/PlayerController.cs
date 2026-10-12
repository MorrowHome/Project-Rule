using System;
using UnityEngine;
using UnityEngine.InputSystem;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Infrastructure.Interaction;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 玩家移动控制。Kinematic 刚体 + <see cref="Rigidbody2D.Slide"/> 自行解算碰撞。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是 Kinematic</b>（架构决策 D1）：移动完全由代码决定，手感可控。
    /// 代价是引擎不再替我们挡住静态地形 —— <c>MovePosition</c> 自己不处理碰撞 ——
    /// 所以位移必须走 <see cref="Rigidbody2D.Slide"/>：它按 <c>surfaceUp</c> 解析接触面角度、
    /// 在设定斜率内沿面滑行、并多次叠代处理拐角。</para>
    /// <para><b>重力自己积分</b>，并且直接读 <c>Physics2D.gravity</c>：重力规则改的是这个全局值，
    /// 本类无需改一行就会跟着变 —— 「规则作用于世界本身」就是这样落地的。
    /// 重力<b>不能</b>交给 <c>SlideMovement.gravity</c>：那只会得到匀速下落，没有加速度。</para>
    /// <para><c>surfaceUp</c> 取当前重力的反方向。于是「重力反转」之后天花板就是新的地面，
    /// 接地判定与起跳方向会自动跟着翻；「重力失效」时沿用上一次的方向，避免方向退化成零向量。</para>
    /// <para>物理运算写在 <see cref="FixedUpdate"/>（Unity 2D 物理要求）。</para>
    /// </remarks>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class PlayerController : MonoBehaviour, IKnockbackReceiver, IRuleSelectionInputGate
    {
        // ==================================================================
        //  序列化字段
        // ==================================================================

        [Header("输入")]
        [Tooltip("拖入 Assets/_Project/Settings/InputSystem_Actions.inputactions")]
        [SerializeField] private InputActionAsset _inputActions;

        [Header("移动")]
        [Tooltip("水平最大速度。按住方向键时速度会朝这个值加速（不是瞬间到）。")]
        [SerializeField] private float _moveSpeedMax = 8f;

        [Tooltip("地面水平加速度。到全速的时间 ≈ 最大速度 / 本值（200 → 约 0.04 秒，近乎瞬时）。")]
        [SerializeField] private float _groundAccel = 200f;

        [Tooltip("起跳速度。跳跃高度 = 本值² / (2 × 有效上升重力)。")]
        [SerializeField] private float _jumpSpeed    = 13.6f;

        [Tooltip("玩家自身的重力倍率（在规则改过的全局重力之上再乘一次）。" +
                 "它与跳跃速度共同决定「跳跃高度」和「上升用时」：高度 = v²/(2g)，用时 = v/g。" +
                 "想更快就同时加大两者（高度不变、时间变短）。")]
        [SerializeField] private float _gravityScale = 3.83f;

        [Tooltip("下落时的额外重力倍率 —— 落得比升得快，滞空更紧凑。1 = 上下对称。")]
        [SerializeField, Range(1f, 4f)] private float _fallGravityMultiplier = 2f;

        [Tooltip("下落速度上限（终端速度）。防止长距离下落越来越快、难以阅读。")]
        [SerializeField] private float _maxFallSpeed = 30f;

        [Tooltip("地面减速度基准。实际减速度 = 本值 × 脚下地面材质的摩擦系数 × 摩擦规则缩放。" +
                 "所以「冰面」= 给地面挂一个摩擦很小的 PhysicsMaterial2D。")]
        [SerializeField] private float _frictionDecel = 40f;

        [Tooltip("空中水平加速度：能左右改向，但不瞬移。0 = 空中完全锁死横向速度；" +
                 "调到 1000 左右约等于「空中和地面一样跟手」。")]
        [SerializeField] private float _airAccel = 40f;

        [Tooltip("空中无输入时的水平阻力。0 = 空中完整保持动量；调大就是空中能急停。")]
        [SerializeField] private float _airDrag = 3f;

        [Tooltip("松开跳跃键时向上速度保留的比例 —— 可变跳跃高度。1 = 按固定高度跳。")]
        [SerializeField, Range(0.1f, 1f)] private float _jumpCutMultiplier = 0.45f;

        [Tooltip("土狼时间：走出平台边缘后仍可起跳的宽限秒数。")]
        [SerializeField] private float _coyoteTime = 0.1f;

        [Tooltip("跳跃缓冲：落地之前提前按下的跳跃仍然生效的秒数。")]
        [SerializeField] private float _jumpBuffer = 0.12f;

        [Header("碰撞")]
        [Tooltip("阻挡移动、并用于接地判定的层。地形与墙体都放这里。")]
        [SerializeField] private LayerMask _solidLayer;

        [Tooltip("接地探测距离（米）。")]
        [SerializeField] private float _groundProbeDistance = 0.08f;

        [Tooltip("贴地吸附距离：下坡时把角色按回地面，0 = 关闭。")]
        [SerializeField] private float _surfaceAnchorDistance = 0.05f;

        [Tooltip("可沿其滑行的最大坡面角度。竖直墙是 90°，所以必须小于 90 才会被挡住。")]
        [SerializeField] private float _surfaceSlideAngle = 89f;

        [Header("击退")]
        [Tooltip("外部冲量每秒的衰减量。")]
        [SerializeField] private float _knockbackDecay = 30f;

        [Header("状态")]
        [Tooltip("同一物体上的状态组件；暂停状态从这里读。留空则自动取。")]
        [SerializeField] private EntityStatus _status;

        [Header("攻击")]
        [SerializeField] private CombatSystem _combat;
        [SerializeField] private float     _attackDamage    = 10f;
        [SerializeField] private float     _attackKnockback = 6f;
        [SerializeField] private float     _attackCooldown  = 0.25f;
        [SerializeField] private Vector2   _attackBoxSize   = new Vector2(1.2f, 1f);
        [SerializeField] private float     _attackOffset    = 0.8f;
        [SerializeField] private LayerMask _attackMask;

        [Header("能量")]
        [Tooltip("每命中一个目标恢复的能量（策划案 §四：攻击小怪回能）。未命中不给。")]
        [SerializeField] private float _energyPerHit = 4f;

        // ==================================================================
        //  运行时状态
        // ==================================================================

        private readonly RaycastHit2D[] _groundHits = new RaycastHit2D[4];

        private Rigidbody2D _rb;
        private Vector2 _velocity;          // 自己维护的速度：重力 / 输入 / 跳跃
        private Vector2 _externalVelocity;  // 击退等外部冲量，独立衰减，不掺进重力与摩擦
        private Vector2 _moveInput;
        private Vector2 _up = Vector2.up;   // 当前「上」方向 = 重力反方向
        private bool    _jumpQueued;
        private bool    _jumpCutQueued;
        private bool    _grounded;
        private float   _groundFriction = 1f;   // 脚下这块地的摩擦系数（来自它的 PhysicsMaterial2D）
        private float   _coyoteLeft;
        private float   _bufferLeft;
        private float   _facing = 1f;
        private float   _nextAttackTime;

        private InputAction _moveAction;
        private InputAction _jumpAction;
        private InputAction _attackAction;
        private int _inputBlocks;
        private int _inputResumeFrame;

        /// <summary>UI 仅接管输入；慢时间里的重力、碰撞与外部冲量仍走原 FixedUpdate。</summary>
        public IDisposable BlockGameplayInput()
        {
            _inputBlocks++;
            return new InputBlock(this);
        }

        private sealed class InputBlock : IDisposable
        {
            private PlayerController _owner;
            public InputBlock(PlayerController owner) => _owner = owner;
            public void Dispose()
            {
                if (_owner == null) return;
                _owner._inputBlocks = Mathf.Max(0, _owner._inputBlocks - 1);
                // 选择器在玩家 Update 前结束时，这一帧的攻击键仍属于 UI，不能再次挥刀。
                _owner._inputResumeFrame = Time.frameCount + 1;
                _owner = null;
            }
        }

        /// <summary>是否站在支撑面上（供踩踏判定与调试用）。</summary>
        public bool IsGrounded => _grounded;

        /// <summary>暂停中不读输入、不推进物理。</summary>
        /// <remarks>暂停状态由 <c>EntityStatus.SetPaused</c> 驱动 —— 暂停持有者（P1 的时间管理器）负责调用。</remarks>
        private bool IsPaused => _status != null && _status.IsPaused;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();

            // Kinematic + 自己解算（见类注释）。gravityScale 置 0：重力在 FixedUpdate 里自己积分。
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.gravityScale = 0f;
            _rb.linearDamping = 0f;
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;           // 平台角色不翻滚
            // 插值：物理是 50Hz 定步长，渲染帧率更高。不开插值的话 Transform 只在物理帧更新，
            // 摄像机会很顺、角色却在"跳格子"（横向移动时最明显）。
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            _rb.useFullKinematicContacts = true;                              // Kinematic 与静态体 / 推动物之间也要产生接触

            if (_status == null) _status = GetComponent<EntityStatus>();

            UpdateUp();                                                       // 起手就有正确的「上」方向

            if (_inputActions != null)
            {
                _moveAction   = _inputActions.FindAction("Player/Move",   false);
                _jumpAction   = _inputActions.FindAction("Player/Jump",   false);
                _attackAction = _inputActions.FindAction("Player/Attack", false);
            }

            if (_moveAction == null)
                Debug.LogWarning("[PlayerController] 未找到输入动作，请把 InputSystem_Actions 拖到 _inputActions。", this);
        }

        private void OnEnable()
        {
            _moveAction?.Enable();
            _jumpAction?.Enable();
            _attackAction?.Enable();
        }

        private void OnDisable()
        {
            _moveAction?.Disable();
            _jumpAction?.Disable();
            _attackAction?.Disable();
        }

        // ==================================================================
        //  输入（视觉帧）
        // ==================================================================

        private void Update()
        {
            if (IsPaused || _inputBlocks > 0 || Time.frameCount < _inputResumeFrame)
            {
                _moveInput = Vector2.zero;
                _jumpQueued = false;
                _jumpCutQueued = false;
                return;
            }

            _moveInput = _moveAction?.ReadValue<Vector2>() ?? Vector2.zero;

            if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
                _jumpQueued = true;

            if (_jumpAction != null && _jumpAction.WasReleasedThisFrame())
                _jumpCutQueued = true;      // 可变跳跃高度：松键那一刻削速

            if (_attackAction != null && _attackAction.WasPressedThisFrame())
                DoAttack();
        }

        // ==================================================================
        //  物理（固定帧）
        // ==================================================================

        private void FixedUpdate()
        {
            if (IsPaused)
            {
                // 暂停期间清空速度：否则重力会在暂停的每一帧继续堆积，
                // 恢复的瞬间一次性位移出去。
                _velocity         = Vector2.zero;
                _externalVelocity = Vector2.zero;
                return;
            }

            float dt = Time.fixedDeltaTime;
            UpdateUp();

            // ① 接地：查询刚体自身的全部碰撞体，不需要额外的地面检测子物体。
            //    放在移动之前，本帧的水平处理才能用到最新的接地状态。
            _grounded = ProbeGround();

            // ② 重力：自己积分（要的是加速度，所以累加，不交给 SlideMovement.gravity）。
            //    下落时再乘一次 _fallGravityMultiplier —— 落得比升得快，滞空更紧凑。
            bool falling = Vector2.Dot(_velocity, _up) < 0f;
            _velocity += Physics2D.gravity * (_gravityScale * (falling ? _fallGravityMultiplier : 1f)) * dt;

            // 下落速度上限（终端速度）：长距离下落不会一路加速，读起来更稳
            float alongUp = Vector2.Dot(_velocity, _up);
            if (alongUp < -_maxFallSpeed)
                _velocity -= _up * (alongUp + _maxFallSpeed);

            // ③ 水平：按住方向时朝最大速度「加速」（不是瞬间给速度）
            if (_grounded)
            {
                if (Mathf.Abs(_moveInput.x) > 0.01f)
                {
                    _facing = Mathf.Sign(_moveInput.x);
                    _velocity.x = Mathf.MoveTowards(_velocity.x, _moveInput.x * _moveSpeedMax, _groundAccel * dt);
                }
                else
                {
                    // 减速度 = 基准 × 脚下地面材质的摩擦 × 摩擦规则缩放。
                    // 两者相乘：地形可以做成冰面，规则也能把整个世界变成冰面。
                    float friction = _groundFriction * FrictionScale;
                    _velocity.x = Mathf.MoveTowards(_velocity.x, 0f, _frictionDecel * friction * dt);
                }
            }
            else if (Mathf.Abs(_moveInput.x) > 0.01f)
            {
                _facing = Mathf.Sign(_moveInput.x);
                _velocity.x = Mathf.MoveTowards(_velocity.x, _moveInput.x * _moveSpeedMax, _airAccel * dt);
            }
            else
            {
                _velocity.x = Mathf.MoveTowards(_velocity.x, 0f, _airDrag * dt);
            }

            // ④ 贴地时抵消朝地面累积的速度
            if (_grounded && Vector2.Dot(_velocity, _up) < 0f)
                _velocity -= _up * Vector2.Dot(_velocity, _up);

            // ⑤ 跳跃：土狼时间（离地后还有一小段宽限）+ 输入缓冲（落地前提前按也算数）
            if (_grounded) _coyoteLeft = _coyoteTime;
            else           _coyoteLeft -= dt;

            if (_jumpQueued)
            {
                _bufferLeft = _jumpBuffer;
                _jumpQueued = false;
            }
            else if (_bufferLeft > 0f)
            {
                _bufferLeft -= dt;
            }

            if (_bufferLeft > 0f && _coyoteLeft > 0f)
            {
                _velocity = SetAlong(_velocity, _up, _jumpSpeed);
                _bufferLeft = 0f;      // 一次输入只跳一次，也别想二段跳
                _coyoteLeft = 0f;
            }

            // ⑥ 可变跳跃高度：提前松键就把上升速度削掉，跳得矮一些
            if (_jumpCutQueued)
            {
                float along = Vector2.Dot(_velocity, _up);
                if (along > 0f) _velocity = SetAlong(_velocity, _up, along * _jumpCutMultiplier);
                _jumpCutQueued = false;
            }

            // ⑦ 击退：外部速度独立衰减
            _externalVelocity = Vector2.MoveTowards(_externalVelocity, Vector2.zero, _knockbackDecay * dt);

            // ⑧ 位移交给 Slide 解算接触（Kinematic 的 MovePosition 自己不处理碰撞）
            var result = _rb.Slide(_velocity + _externalVelocity, dt, BuildSlideMovement());

            // ⑨ 撞墙 / 撞顶：Slide 把用不掉的速度还回来，清掉对应分量，避免贴着墙持续挤压
            if (Mathf.Abs(result.remainingVelocity.x) > 0.001f) _velocity.x = 0f;
            if (Mathf.Abs(result.remainingVelocity.y) > 0.001f) _velocity.y = 0f;
        }

        /// <summary>每次物理帧的 Slide 配置。</summary>
        private Rigidbody2D.SlideMovement BuildSlideMovement()
        {
            // 上升途中不加吸附，否则起跳会被按回地面。
            bool anchored = _grounded && Vector2.Dot(_velocity, _up) <= 0.01f;

            return new Rigidbody2D.SlideMovement
            {
                surfaceUp         = _up,
                surfaceSlideAngle = _surfaceSlideAngle,
                gravitySlipAngle  = 0f,                  // 不沿斜面自动下滑
                gravity           = Vector2.zero,        // 重力已在 ① 自己积分
                maxIterations     = 4,                   // 拐角要几次叠代才滑得干净
                surfaceAnchor     = anchored ? -_up * _surfaceAnchorDistance : Vector2.zero,
                useLayerMask      = true,
                layerMask         = _solidLayer,
                useAttachedTriggers = false,
                useSimulationMove = true,                // 交给模拟步生效：暂停（不 Simulate）时自然静止
            };
        }

        /// <summary>沿当前「上」方向的反向做一次碰撞体扫描，看脚下有没有支撑面。</summary>
        /// <remarks>顺带记住脚下这块地的摩擦系数（<see cref="_groundFriction"/>），供减速使用。</remarks>
        private bool ProbeGround()
        {
            _groundFriction = 1f;      // 没探到面、或那块地没挂材质 → 按普通地面算

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_solidLayer);

            int count = _rb.Cast(-_up, filter, _groundHits, _groundProbeDistance);
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

        /// <summary>「上」方向 = 重力反方向；重力失效（零向量）时沿用上一次的方向。</summary>
        private void UpdateUp()
        {
            if (Physics2D.gravity.sqrMagnitude > 0.0001f)
                _up = -Physics2D.gravity.normalized;
        }

        /// <summary>把速度沿 <paramref name="axis"/> 的分量设为 <paramref name="value"/>，其余分量不动。</summary>
        private static Vector2 SetAlong(Vector2 velocity, Vector2 axis, float value)
            => velocity - axis * Vector2.Dot(velocity, axis) + axis * value;

        // ==================================================================
        //  击退
        // ==================================================================

        /// <summary>外部冲量入口（击退）。见 <see cref="IKnockbackReceiver"/>。</summary>
        /// <remarks><b>不能</b>直接写 <c>Rigidbody2D.linearVelocity</c>：本类每个物理帧都会整体
        /// 覆盖速度，那样写进去的冲量活不过一帧。</remarks>
        public void AddImpulse(Vector2 impulse) => _externalVelocity += impulse;

        // ==================================================================
        //  攻击
        // ==================================================================

        private void DoAttack()
        {
            if (_combat == null) return;
            if (Time.time < _nextAttackTime) return;      // 攻击冷却：连点不会连打

            _nextAttackTime = Time.time + _attackCooldown;

            Vector2 center = (Vector2)transform.position + new Vector2(_facing * _attackOffset, 0f);
            int hits = _combat.PerformAttack(this, center, _attackBoxSize, _attackMask,
                                             _attackDamage, _attackKnockback);

            // 能量恢复来源之一：命中即回，未命中不给（策划案 §四）。
            if (hits > 0 && _status != null)
                _status.RestoreEnergy(_energyPerHit * hits);
        }

        /// <summary>攻击判定体：调参时直接看得到范围与朝向。</summary>
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.8f);
            Vector2 center = (Vector2)transform.position + new Vector2(_facing * _attackOffset, 0f);
            Gizmos.DrawWireCube(center, _attackBoxSize);
        }

        // ==================================================================
        //  属性读取
        // ==================================================================

        /// <summary>当前摩擦缩放（1.0 = 正常）。规则系统未就绪时退化为 1。</summary>
        private static float FrictionScale
        {
            get
            {
                var attrs = RuleSystem.Instance?.Attributes;
                return attrs?.Values.FrictionScale ?? 1f;
            }
        }
    }
}
