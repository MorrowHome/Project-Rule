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

        [Tooltip("空中还能再跳几次。0 = 只有一段跳；1 = 二段跳。" +
                 "它是**实体自己的能力**（本组件对环境里任何实体都可用），不是玩家特权。")]
        [SerializeField, Range(0, 3)] private int _maxAirJumps = 1;

        [Tooltip("空中段的起跳速度。比第一段略弱 —— 读起来像「再补一下」而不是「再来一次」。" +
                 "两段一样高会让二段跳显得像没落地过。")]
        [SerializeField] private float _airJumpSpeed = 12.2f;

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

        [Header("冲刺")]
        [Tooltip("冲刺速度。远高于 _moveSpeedMax，要的就是爆发感。")]
        [SerializeField] private float _dashSpeed = 26f;

        [Tooltip("冲刺持续秒数。冲刺距离 = 本值 × _dashSpeed（26 × 0.18 ≈ 4.7 米）。")]
        [SerializeField] private float _dashDuration = 0.18f;

        [Tooltip("冲刺结束时保留多少速度。1 = 冲完像被弹射出去，0.35 = 收得住。")]
        [SerializeField, Range(0f, 1f)] private float _dashExitSpeedScale = 0.35f;

        [Tooltip("冲刺次数上限，落地回满。0 = 没有冲刺。")]
        [SerializeField, Range(0, 3)] private int _maxDashes = 1;

        [Tooltip("两次冲刺之间的最小间隔。冲刺进行中再按也无效。")]
        [SerializeField] private float _dashCooldown = 0.25f;

        [Header("蹬墙跳")]
        [Tooltip("横向墙面探测距离（米）。")]
        [SerializeField] private float _wallProbeDistance = 0.08f;

        [Tooltip("贴墙时的下滑速度上限。远低于 _maxFallSpeed —— 贴墙是「缓慢下滑」，不是「挂在墙上」。" +
                 "调大 = 贴墙停留时间短、掉得快；调小 = 有更多时间看准再蹬。")]
        [SerializeField] private float _wallSlideSpeed = 6f;

        [Tooltip("【朝墙外按】时弹离墙壁的水平速度 —— 「跳出去」用。")]
        [SerializeField] private float _wallJumpPushSpeed = 9f;

        [Tooltip("【朝墙按 / 不按】时的推力 —— 「爬升」用。**必须足够小**，" +
                 "小到方向键能在人离开墙之前把它拉回来，否则一面墙爬不上去（会越跳越远）。")]
        [SerializeField] private float _wallClimbPushSpeed = 4f;

        [Tooltip("蹬墙后的向上速度。**调大它是最划算的提升** —— 弧线更高、滞空更久，" +
                 "水平距离会跟着白拿，比单纯加水平推力更接近《空洞骑士》的质感。")]
        [SerializeField] private float _wallJumpUpSpeed = 15f;

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
        [SerializeField] private Vector2   _attackBoxSize   = new Vector2(1.2f, 1f);
        [SerializeField] private float     _attackOffset    = 0.8f;
        [SerializeField] private LayerMask _attackMask;

        [Tooltip("起手：从按键到「判定开窗」的秒数。")]
        [SerializeField] private float _attackWindup = 0.12f;

        [Tooltip("生效：判定窗的时长。**只在开窗那一瞬做一次命中判定**，不做持续判定 —— " +
                 "一刀砍两次会把无敌帧和伤害规则都算乱。")]
        [SerializeField] private float _attackActive = 0.12f;

        [Tooltip("收招：这段时间里**再按一次就接下一刀**，不按就收剑。" +
                 "起手 / 生效段按下会先存着，到收招段再兑现（连段手感靠它）。")]
        [SerializeField] private float _attackRecover = 0.30f;

        [Tooltip("连段最多打几刀。1 = 只有一下，不接。")]
        [SerializeField, Range(1, 4)] private int _maxCombo = 2;

        [Header("能量")]
        [Tooltip("每命中一个目标恢复的能量（策划案 §四：攻击小怪回能）。未命中不给。")]
        [SerializeField] private float _energyPerHit = 4f;

        // ==================================================================
        //  运行时状态
        // ==================================================================

        private readonly RaycastHit2D[] _groundHits = new RaycastHit2D[4];
        private readonly RaycastHit2D[] _wallHits   = new RaycastHit2D[4];

        private Rigidbody2D _rb;
        private Vector2 _velocity;          // 自己维护的速度：重力 / 输入 / 跳跃
        private Vector2 _externalVelocity;  // 击退等外部冲量，独立衰减，不掺进重力与摩擦
        private Vector2 _moveInput;
        private Vector2 _up = Vector2.up;   // 当前「上」方向 = 重力反方向
        private bool    _jumpQueued;
        private bool    _jumpCutQueued;
        private bool    _grounded;
        private bool    _wallLeft;              // 左侧贴着可蹬的竖墙
        private bool    _wallRight;             // 右侧贴着可蹬的竖墙
        private bool    _wallSliding;           // 正在贴墙缓慢下滑
        private float   _groundFriction = 1f;   // 脚下这块地的摩擦系数（来自它的 PhysicsMaterial2D）
        private float   _coyoteLeft;
        private float   _bufferLeft;
        private int     _jumpsUsed;             // 本次落地以来已用掉几段跳
        private float   _facing = 1f;

        /// <summary>连段的三个阶段。见 <c>Doc/美术与动画接入方案.md</c> §十。</summary>
        private enum AttackPhase { None, Windup, Active, Recover }

        private AttackPhase _attackPhase = AttackPhase.None;
        private float   _attackTimer;
        private int     _comboIndex = -1;      // 未出招时为 −1，出招后 0、1、2…
        private bool    _attackBuffered;       // 起手 / 生效段按下 → 留到收招段兑现

        private bool    _dashQueued;            // 本视觉帧按下了冲刺
        private float   _dashLeft;              // 冲刺剩余秒数，> 0 表示正在冲刺
        private Vector2 _dashDirection;
        private int     _dashesLeft;            // 还能冲几次，落地回满
        private float   _dashCooldownLeft;

        private InputAction _moveAction;
        private InputAction _jumpAction;
        private InputAction _attackAction;
        private InputAction _dashAction;
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

        /// <summary>
        /// 当前速度（自己维护的那个，<b>不含</b>击退等外部冲量）。
        /// </summary>
        /// <remarks>给表现层用（动画判上升/下降、跑步快慢）。玩法逻辑不要读它做判断 ——
        /// 那样会把「速度怎么算」泄漏到外面。</remarks>
        public Vector2 Velocity => _velocity;

        /// <summary>
        /// 当前朝向：+1 右，−1 左。
        /// </summary>
        /// <remarks>取的是**换算后**的方向，所以「摩擦 × 反转」生效时按右往左走，
        /// 这里自动是 −1 —— 表现层不需要自己判断方向。</remarks>
        public float Facing => _facing;

        /// <summary>本帧读到的**原始**移动输入（未经摩擦反转换算）。供调试浮层显示。</summary>
        public Vector2 MoveInput => _moveInput;

        /// <summary>左侧是否贴着可蹬的竖墙（供调试浮层显示）。</summary>
        public bool WallLeft => _wallLeft;

        /// <summary>右侧是否贴着可蹬的竖墙（供调试浮层显示）。</summary>
        public bool WallRight => _wallRight;

        /// <summary>是否正在贴墙缓慢下滑（供调试浮层显示）。</summary>
        public bool IsWallSliding => _wallSliding;

        /// <summary>总跳数上限 = 第一段 + 空中段。</summary>
        public int MaxJumps => 1 + _maxAirJumps;

        /// <summary>本次落地以来还剩几段跳（供 HUD / 调试浮层显示）。</summary>
        public int JumpsLeft => Mathf.Max(MaxJumps - _jumpsUsed, 0);

        /// <summary>暂停中不读输入、不推进物理。</summary>
        /// <remarks>暂停状态由 <c>EntityStatus.SetPaused</c> 驱动 —— 暂停持有者（P1 的时间管理器）负责调用。</remarks>
        private bool IsPaused => _status != null && _status.IsPaused;

        // ==================================================================
        //  表现层事件
        // ==================================================================
        //
        // ⚠️ 这些是**边沿事件**（一次性），不是状态 —— 状态用上面那些只读属性读。
        //    分成两种是因为表现层两样都要：「现在是什么」（持续条件，每帧重写就够）
        //    和「刚刚发生了什么」（一次性动作）。后者靠每帧采样很容易漏掉或重复计一次
        //    （比如 0.33 秒的落地只有 4 个物理帧），所以直接从源头发事件。

        /// <summary>**地面跳 / 土狼跳**瞬间。</summary>
        /// <remarks>二段跳走的是 <see cref="OnAirJumped"/>，不在这里发 —— 两者的表现不同
        /// （一个是蹬地起跳、一个是空中再翻一下），合并成一个事件表现层就分不出来了。</remarks>
        public event Action OnJumped;

        /// <summary>**空中段（二段跳）**瞬间：跳数还没用完时在空中再按一次跳。</summary>
        public event Action OnAirJumped;

        /// <summary>蹬墙跳瞬间。与 <see cref="OnJumped"/> 分开：它有两档推力、表现也不同。</summary>
        public event Action OnWallJumped;

        /// <summary>落地瞬间（接地判定 false → true）。</summary>
        public event Action OnLanded;

        /// <summary>冲刺开始瞬间（次数已扣、方向已定）。</summary>
        public event Action OnDashed;

        /// <summary>
        /// 挥出一刀 —— 连段的**第一下和之后每一下**都会发。
        /// </summary>
        /// <remarks>表现层用 <see cref="AttackComboIndex"/> 区分是起手还是接续
        /// （起手播拔剑、接续播下一段）。</remarks>
        public event Action OnAttacked;

        /// <summary>当前连段打到第几刀（0 起数）；没在出招时是 −1。</summary>
        public int AttackComboIndex => _comboIndex;

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
                // ⚠️ 冲刺复用输入资源里**没有任何代码使用**的 Player/Crouch（C 键 /
                //    手柄 buttonEast）—— 这样不必动团队共享的 .inputactions。
                //    等输入方案定稿，把它重命名成 Dash 再改这一行即可（详见 Doc/玩家动作设计.md §3.6）。
                //    （曾用过 Player/Sprint=左 Shift，按你的意见换掉了。）
                _dashAction   = _inputActions.FindAction("Player/Crouch", false);
            }

            _dashesLeft = _maxDashes;           // 起手就有冲刺，不必先落地一次

            if (_moveAction == null)
                Debug.LogWarning("[PlayerController] 未找到输入动作，请把 InputSystem_Actions 拖到 _inputActions。", this);
        }

        private void OnEnable()
        {
            _moveAction?.Enable();
            _jumpAction?.Enable();
            _attackAction?.Enable();
            _dashAction?.Enable();
        }

        private void OnDisable()
        {
            _moveAction?.Disable();
            _jumpAction?.Disable();
            _attackAction?.Disable();
            _dashAction?.Disable();
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
                _dashQueued = false;
                return;
            }

            _moveInput = _moveAction?.ReadValue<Vector2>() ?? Vector2.zero;

            if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
                _jumpQueued = true;

            if (_jumpAction != null && _jumpAction.WasReleasedThisFrame())
                _jumpCutQueued = true;      // 可变跳跃高度：松键那一刻削速

            if (_dashAction != null && _dashAction.WasPressedThisFrame())
                _dashQueued = true;

            if (_attackAction != null && _attackAction.WasPressedThisFrame())
                QueueAttack();
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
            bool wasGrounded = _grounded;
            _grounded  = ProbeGround();
            _wallLeft  = ProbeWall(Vector2.left);
            _wallRight = ProbeWall(Vector2.right);

            // 落地边沿：属性给的是「有没有踩着」，这里补一个「这一帧刚踩上」——
            // 表现层要播一次落地动作，需要精确的起点，靠每帧采样接地状态是拿不到的。
            if (_grounded && !wasGrounded) OnLanded?.Invoke();

            // ② 跳数与土狼时间：与冲刺无关，一直推进 —— 冲刺结束的下一帧就要拿到正确的跳数
            if (_grounded)
            {
                _coyoteLeft = _coyoteTime;
                _jumpsUsed  = 0;                      // 落地回满跳数
            }
            else
            {
                _coyoteLeft -= dt;

                // 土狼窗口一关，第一段跳就当作已用掉 —— 从平台走下来之后只剩空中段。
                // ⚠️ 窗口**内**不受这条影响：那时第一段 + 空中段都还在，
                //    所以「走下来再跳」和「站着跳」拿到的是同样的跳数。
                if (_coyoteLeft <= 0f) _jumpsUsed = Mathf.Max(_jumpsUsed, 1);
            }

            // ③ 冲刺：次数 / 计时 / 冷却。
            UpdateDash(dt);

            // ③′ 攻击连段：起手 → 生效（在这里开一次判定）→ 收招。
            UpdateAttack(dt);

            // ④ 重力：自己积分（要的是加速度，所以累加，不交给 SlideMovement.gravity）。
            //    下落时再乘一次 _fallGravityMultiplier —— 落得比升得快，滞空更紧凑。
            //    ⚠️ 冲刺期间**不积分**：再叠一份下落加速度，空中冲刺会变成一条又陡又快的斜线，
            //       非常突兀。停掉积分后竖直速度**原样保留**（不归零、也不加速）——
            //       这正是「冲刺不动竖直方向的大小」：它不给你加竖直速度，也不拿走你的。
            if (!IsDashing)
            {
                bool falling = Vector2.Dot(_velocity, _up) < 0f;
                _velocity += Physics2D.gravity * (_gravityScale * (falling ? _fallGravityMultiplier : 1f)) * dt;
            }

            // 下落速度上限（终端速度）：长距离下落不会一路加速，读起来更稳
            float alongUp = Vector2.Dot(_velocity, _up);
            if (alongUp < -_maxFallSpeed)
                _velocity -= _up * (alongUp + _maxFallSpeed);

            // ④′ 贴墙下滑：空中贴着竖墙时，把下落速度压到 _wallSlideSpeed。
            //     ⚠️ 这不是「吸附」—— 人**照样在往下掉**，只是掉得慢；松开墙立刻恢复正常下落。
            //        所以它给出时间在竖井里看准了再往反方向蹬，而不是把人挂住不动。
            _wallSliding = !_grounded && !IsDashing && (_wallLeft || _wallRight);
            if (_wallSliding)
            {
                float alongWall = Vector2.Dot(_velocity, _up);
                if (alongWall < -_wallSlideSpeed)
                    _velocity -= _up * (alongWall + _wallSlideSpeed);
            }

            // ⑤ 水平：冲刺接管，否则按住方向时朝最大速度「加速」（不是瞬间给速度）
            if (IsDashing)
            {
                // 冲刺**只改水平速度**：竖直方向的大小不动（那是重力与跳跃的领地）。
                _velocity.x = _dashDirection.x * _dashSpeed;
            }
            else if (_grounded)
            {
                if (Mathf.Abs(_moveInput.x) > 0.01f)
                {
                    // 摩擦反转（摩擦 × 反转）：摩擦力的方向翻转 → 地面把你往「按键的反方向」送，
                    // 于是按右就往左走。**只作用于接地时** —— 语义是摩擦力的方向，空中没有摩擦。
                    // 所以跳跃可以「逃出」反转区。这也是它与被砍掉的「方向」属性的区别：
                    // 改的是世界给你的力，不是你的按键映射（策划案 D3）。
                    float direction = ResolveMoveDirection(_moveInput.x, FrictionInverted);
                    _facing = Mathf.Sign(direction);
                    _velocity.x = Mathf.MoveTowards(_velocity.x, direction * _moveSpeedMax, _groundAccel * dt);
                }
                else
                {
                    // 减速度 = 基准 × 脚下地面材质的摩擦 × 摩擦规则缩放。
                    // 两者相乘：地形可以做成冰面，规则也能把整个世界变成冰面。
                    // ⚠️ 反转**不翻这里的符号** —— 减速力反向会变成"越滑越快"，直接失控。
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

            // ⑥ 贴地时抵消朝地面累积的速度
            if (_grounded && Vector2.Dot(_velocity, _up) < 0f)
                _velocity -= _up * Vector2.Dot(_velocity, _up);

            // ⑦ 跳跃：输入缓冲（落地前提前按也算数）+ 多段跳
            //    冲刺期间整块不跑：冲刺中按下的跳跃会被**留到冲刺结束那一刻**兑现
            //    （跳跃缓冲通常短于冲刺剩余时间，衔接不上就是正常放弃）。
            if (!IsDashing)
            {
                if (_jumpQueued)
                {
                    _bufferLeft = _jumpBuffer;
                    _jumpQueued = false;
                }
                else if (_bufferLeft > 0f)
                {
                    _bufferLeft -= dt;
                }

                if (_bufferLeft > 0f)
                {
                    // 优先级：地面 / 土狼跳  >  蹬墙跳  >  空中跳
                    // ⚠️ 蹬墙必须排在**空中跳之前**：否则贴着墙、跳数还没用完时，
                    //    空中跳会抢先把这一下吃掉，玩家得先把二段跳耗完才蹬得上墙 —— 反直觉。
                    bool  groundJump = _grounded || _coyoteLeft > 0f;
                    float wallPush   = ResolveWallJumpPush(_wallLeft, _wallRight, _moveInput.x, _grounded,
                                                           _wallJumpPushSpeed, _wallClimbPushSpeed);

                    if (groundJump)
                    {
                        _velocity   = SetAlong(_velocity, _up, _jumpSpeed);
                        _bufferLeft = 0f;                 // 一次输入只跳一段
                        _coyoteLeft = 0f;                 // 起跳后土狼窗口作废，否则按住方向就能连跳
                        _jumpsUsed  = Mathf.Min(_jumpsUsed + 1, MaxJumps);
                        OnJumped?.Invoke();
                    }
                    else if (!Mathf.Approximately(wallPush, 0f))
                    {
                        // 蹬墙跳：朝墙外弹开 + 向上。竖直方向用 SetAlong 沿当前「上」写，
                        // 所以重力反转时它照样是「离开墙、朝世界的上方去」。
                        _velocity   = new Vector2(wallPush, 0f);
                        _velocity   = SetAlong(_velocity, _up, _wallJumpUpSpeed);
                        _facing     = Mathf.Sign(wallPush);   // 朝向跟着弹开方向转，攻击判定才对得上前方
                        _jumpsUsed  = 0;                  // 蹬墙刷新跳数 —— 一面墙就能一路爬上去
                        _coyoteLeft = 0f;
                        _bufferLeft = 0f;
                        OnWallJumped?.Invoke();
                    }
                    else
                    {
                        // 走到这里必然离地且土狼窗口已过，所以这个判据只可能给出空中段的速度。
                        float airSpeed = ResolveJumpSpeed(_grounded, _coyoteLeft > 0f,
                                                          _jumpsUsed, MaxJumps, _jumpSpeed, _airJumpSpeed);

                        // 跳数为 0 时**不清掉缓冲** —— 留到落地那一刻兑现，这就是跳跃缓冲。
                        if (airSpeed > 0f)
                        {
                            _velocity   = SetAlong(_velocity, _up, airSpeed);
                            _bufferLeft = 0f;             // 一次输入只跳一段
                            _coyoteLeft = 0f;
                            _jumpsUsed  = Mathf.Min(_jumpsUsed + 1, MaxJumps);
                            OnAirJumped?.Invoke();
                        }
                    }
                }

                // ⑧ 可变跳跃高度：提前松键就把上升速度削掉，跳得矮一些
                if (_jumpCutQueued)
                {
                    float along = Vector2.Dot(_velocity, _up);
                    if (along > 0f) _velocity = SetAlong(_velocity, _up, along * _jumpCutMultiplier);
                    _jumpCutQueued = false;
                }
            }

            // ⑨ 击退：外部速度独立衰减
            _externalVelocity = Vector2.MoveTowards(_externalVelocity, Vector2.zero, _knockbackDecay * dt);

            // ⑩ 位移交给 Slide 解算接触（Kinematic 的 MovePosition 自己不处理碰撞）
            var result = _rb.Slide(_velocity + _externalVelocity, dt, BuildSlideMovement());

            // ⑪ 撞墙 / 撞顶：Slide 把用不掉的速度还回来，清掉对应分量，避免贴着墙持续挤压
            if (Mathf.Abs(result.remainingVelocity.x) > 0.001f) _velocity.x = 0f;
            if (Mathf.Abs(result.remainingVelocity.y) > 0.001f) _velocity.y = 0f;
        }

        // ==================================================================
        //  冲刺
        // ==================================================================

        /// <summary>正在冲刺。</summary>
        public bool IsDashing => _dashLeft > 0f;

        /// <summary>还能冲几次（供 HUD / 调试浮层显示）。</summary>
        public int DashesLeft => _dashesLeft;

        /// <summary>冲刺次数上限。</summary>
        public int MaxDashes => _maxDashes;

        /// <summary>
        /// 冲刺的次数 / 计时 / 冷却。速度的接管在 <see cref="FixedUpdate"/> 里。
        /// </summary>
        /// <remarks>
        /// 冲刺**不做输入缓冲**：冲不了就是冲不了（次数用尽 / 正在冲刺 / 冷却中），
        /// 请求直接丢掉。它是主动选择，不像跳跃那样需要迁就落地时机。
        /// </remarks>
        private void UpdateDash(float dt)
        {
            if (_dashCooldownLeft > 0f) _dashCooldownLeft -= dt;

            // 落地回满。冲刺**贴地滑行**时也满足这条 —— 所以同一次冲刺期间会被反复回满，
            // 靠「正在冲刺」这个条件挡住重复触发（见 CanDash）。
            if (_grounded) _dashesLeft = _maxDashes;

            if (_dashLeft > 0f)
            {
                _dashLeft -= dt;
                if (_dashLeft <= 0f) EndDash();
            }

            if (!_dashQueued) return;
            _dashQueued = false;

            if (!CanDash(_dashesLeft, _dashLeft, _dashCooldownLeft)) return;

            _dashDirection = ResolveDashDirection(_moveInput, _facing);
            if (!Mathf.Approximately(_dashDirection.x, 0f)) _facing = Mathf.Sign(_dashDirection.x);
            _dashLeft  = _dashDuration;
            _dashesLeft = Mathf.Max(_dashesLeft - 1, 0);
            OnDashed?.Invoke();
        }

        /// <summary>冲刺结束：收住水平速度、开始冷却。</summary>
        private void EndDash()
        {
            _dashLeft         = 0f;
            _velocity.x      *= _dashExitSpeedScale;   // **只收水平** —— 竖直速度不归冲刺管
            _dashCooldownLeft = _dashCooldown;
        }

        /// <summary>
        /// 能不能起冲。<b>三个条件缺一不可</b>：还有次数、没在冲、冷却已过。
        /// </summary>
        /// <remarks>抽成纯函数是为了可测，与 <see cref="ResolveJumpSpeed"/> 同一个理由。</remarks>
        public static bool CanDash(int dashesLeft, float dashTimeLeft, float cooldownLeft)
            => dashesLeft > 0 && dashTimeLeft <= 0f && cooldownLeft <= 0f;

        /// <summary>
        /// 冲刺方向：**只有左右两向**。有横向输入就按它，没有就朝当前朝向。
        /// </summary>
        /// <remarks>
        /// <para>竖直方向恒为 0 —— 冲刺**不动**垂直速度，那是重力与跳跃的领地。
        /// 所以冲刺期间角色照样会被重力拉下去 / 被跳跃顶上去，冲刺只负责「横着推一把」。</para>
        /// <para>⚠️ 这里读的是**原始输入**，不经过 <see cref="ResolveMoveDirection"/> ——
        /// 「摩擦 × 反转」翻的是**地面给的力**，冲刺是自己推自己，不该被翻。
        /// 详见 <c>Doc/玩家动作设计.md</c> §3.4。</para>
        /// </remarks>
        public static Vector2 ResolveDashDirection(Vector2 moveInput, float facing)
        {
            bool pressing = Mathf.Abs(moveInput.x) > 0.01f;
            float sign = pressing ? Mathf.Sign(moveInput.x) : (facing >= 0f ? 1f : -1f);
            return new Vector2(sign, 0f);
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
        private bool ProbeWall(Vector2 direction)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_solidLayer);

            int count = _rb.Cast(direction, filter, _wallHits, _wallProbeDistance);
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

        /// <summary>按下攻击键。三种去向：起手 / 接下一刀 / 先存着。</summary>
        private void QueueAttack()
        {
            // 空着 → 起手
            if (_attackPhase == AttackPhase.None)
            {
                BeginSwing(0);
                return;
            }

            // 收招段 → 接下一刀（打满了就丢掉这次输入）
            if (_attackPhase == AttackPhase.Recover)
            {
                TryAdvanceCombo();
                return;
            }

            // 起手 / 生效段 → 先存着，等进收招段再兑现。
            // 不存的话连段就只能「卡在收招那一下」按，手感很差。
            _attackBuffered = true;
        }

        /// <summary>能不能接下一刀。打满了返回 false。</summary>
        private bool TryAdvanceCombo()
        {
            if (_comboIndex + 1 >= _maxCombo) return false;
            BeginSwing(_comboIndex + 1);
            return true;
        }

        private void BeginSwing(int index)
        {
            _comboIndex     = index;
            _attackPhase    = AttackPhase.Windup;
            _attackTimer    = _attackWindup;
            _attackBuffered = false;

            OnAttacked?.Invoke();      // 表现层据此播「拔剑」或「下一段」
        }

        /// <summary>
        /// 推连段计时。<b>判定只在开窗那一瞬做一次</b>，之后是收招 —— 不做持续判定。
        /// </summary>
        /// <remarks>计时用 <c>fixedDeltaTime</c>，而暂停时 <see cref="FixedUpdate"/> 提前返回，
        /// 所以连段天然冻结，不需要额外的暂停分支（与冲刺同理）。</remarks>
        private void UpdateAttack(float dt)
        {
            if (_attackPhase == AttackPhase.None) return;

            _attackTimer -= dt;
            if (_attackTimer > 0f) return;

            switch (_attackPhase)
            {
                case AttackPhase.Windup:
                    PerformAttackHit();
                    _attackPhase = AttackPhase.Active;
                    _attackTimer = _attackActive;
                    break;

                case AttackPhase.Active:
                    _attackPhase = AttackPhase.Recover;
                    _attackTimer = _attackRecover;
                    // 起手 / 生效段存下的那一下，现在兑现
                    if (_attackBuffered)
                    {
                        _attackBuffered = false;
                        TryAdvanceCombo();
                    }
                    break;

                case AttackPhase.Recover:
                    _comboIndex  = -1;
                    _attackPhase = AttackPhase.None;
                    break;
            }
        }

        /// <summary>生效段的唯一命中判定。走正式伤害链路（传**基础值**，由伤害处理模块按规则算）。</summary>
        private void PerformAttackHit()
        {
            if (_combat == null) return;

            // 发起方只组织「打哪儿、打多大」，最终伤害由伤害处理模块按当前规则算。
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

        /// <summary>
        /// 把原始横向输入换算成「本帧该往哪个方向走」。
        /// </summary>
        /// <remarks>
        /// <para>摩擦反转时取反 —— 这是「摩擦 × 反转」在玩家身上的全部实现。</para>
        /// <para>抽成纯函数是为了**可测**：真机验证要靠真按键，而注入按键在编辑器失焦时会被
        /// Input System 重置；手动调 <c>FixedUpdate</c> 又会因为里面的 <c>Rigidbody2D.Slide</c>
        /// 在物理步之外调用而卡死。所以把决策留在这里单独断言。</para>
        /// </remarks>
        public static float ResolveMoveDirection(float rawInput, bool frictionInverted)
            => frictionInverted ? -rawInput : rawInput;

        /// <summary>
        /// 本帧起跳用哪一档速度。<b>返回 0 表示这一段跳用不了。</b>
        /// </summary>
        /// <param name="grounded">此刻是否踩在支撑面上。</param>
        /// <param name="inCoyoteWindow">是否还在土狼时间内。</param>
        /// <param name="jumpsUsed">本次落地以来已用掉几段跳。</param>
        /// <param name="maxJumps">总跳数上限。</param>
        /// <param name="groundSpeed">第一段的速度。</param>
        /// <param name="airSpeed">空中段的速度。</param>
        /// <remarks>
        /// <para><b>只用跳数决定不了能不能跳</b>：踩在地上时跳数当然是够的，
        /// 而离开地面却还在土狼窗口内的那一下，本来就该和站着跳等价。
        /// 所以判据是「<b>在地面 / 窗口内 → 算第一段</b>，否则 → 扣空中段」。</para>
        /// <para>调用方负责在窗口关闭时把 <c>jumpsUsed</c> 抬到至少 1
        /// （「走下来就等于用掉了第一段」），本函数不管那件事，只做判断。</para>
        /// <para>抽成纯函数是为了可测：真机验证要靠真按键，而失焦的编辑器会重置输入设备；
        /// 手动调 <c>FixedUpdate</c> 又会因为 <c>Rigidbody2D.Slide</c> 在物理步之外调用而卡死。</para>
        /// </remarks>
        public static float ResolveJumpSpeed(bool grounded, bool inCoyoteWindow, int jumpsUsed, int maxJumps,
                                             float groundSpeed, float airSpeed)
        {
            if (grounded || inCoyoteWindow) return groundSpeed;   // 第一段：地面 / 土狼
            return jumpsUsed < maxJumps ? airSpeed : 0f;          // 空中段
        }

        /// <summary>
        /// 能不能蹬墙跳，以及弹开的**水平速度**（带符号）。
        /// </summary>
        /// <param name="wallLeft">左侧贴着竖墙。</param>
        /// <param name="wallRight">右侧贴着竖墙。</param>
        /// <param name="moveInputX">横向输入。</param>
        /// <param name="grounded">是否踩在支撑面上。</param>
        /// <param name="pushSpeed">朝墙外按时的推力 —— 「跳出去」用，要够远。</param>
        /// <param name="climbSpeed">朝墙按（或不按）时的推力 —— 「爬升」用，**要小到方向键能立刻把人拉回墙**。</param>
        /// <returns>水平速度：正 = 往右弹，负 = 往左弹，<b>0 = 不能蹬</b>。</returns>
        /// <remarks>
        /// <para>两档推力是这套动作的核心：</para>
        /// <list type="bullet">
        /// <item><b>朝墙外按</b> → <c>pushSpeed</c>：明确地想离开这面墙（去对面、跳出去）。</item>
        /// <item><b>朝墙按 / 不按</b> → <c>climbSpeed</c>：**仍然是往墙外弹**，但只弹一点点。
        /// 因为你还按着朝墙的方向，空中加速度会立刻把你拉回去 —— 于是「贴墙 → 跳 → 贴回去 → 跳」
        /// 能一路爬上去，<b>一面墙就够</b>。若这里用大推力，人会被推飞、接不上。</item>
        /// </list>
        /// <para>踩在地上时返回 0：那是普通起跳的场合，不该被蹬墙截胡。</para>
        /// <para>抽成纯函数是为了可测，与 <see cref="ResolveJumpSpeed"/> 同一个理由。</para>
        /// </remarks>
        public static float ResolveWallJumpPush(bool wallLeft, bool wallRight, float moveInputX,
                                                bool grounded, float pushSpeed, float climbSpeed)
        {
            if (grounded) return 0f;

            // 先看「明确想离开哪面墙」
            if (wallRight && moveInputX < -0.01f) return -pushSpeed;   // 墙在右、按左 → 往左蹬出去
            if (wallLeft  && moveInputX >  0.01f) return  pushSpeed;   // 墙在左、按右 → 往右蹬出去

            // 否则只是在贴墙爬升：仍然往外弹，但只弹一点点
            if (wallRight) return -climbSpeed;
            if (wallLeft)  return  climbSpeed;

            return 0f;
        }

        /// <summary>当前摩擦缩放（1.0 = 正常）。规则系统未就绪时退化为 1。</summary>
        private static float FrictionScale
        {
            get
            {
                var attrs = RuleSystem.Instance?.Attributes;
                return attrs?.Values.FrictionScale ?? 1f;
            }
        }

        /// <summary>摩擦方向是否被规则反转（摩擦 × 反转）。规则系统未就绪时视为未反转。</summary>
        /// <remarks>
        /// 敌人侧的控制器要读同一个标志才能一起反转 —— 这条规则的语义是「地面给你的力的方向」，
        /// 对所有「按意图移动」的实体都成立（石头这类没有意图的动态物体不响应，见 <c>RuleAffectedBody</c>）。
        /// </remarks>
        private static bool FrictionInverted
        {
            get
            {
                var attrs = RuleSystem.Instance?.Attributes;
                return attrs != null && attrs.Values.FrictionInverted;
            }
        }
    }
}
