using System;
using UnityEngine;
using UnityEngine.InputSystem;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Infrastructure.Interaction;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 玩家移动控制。<b>只做编排</b>：接线、按顺序调用七个职能类、把它们的结论翻译成事件。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么是 Kinematic</b>（架构决策 D1）：移动完全由代码决定，手感可控。
    /// 代价是引擎不再替我们挡住静态地形 —— <c>MovePosition</c> 自己不处理碰撞 ——
    /// 所以位移必须走 <c>Rigidbody2D.Slide</c>。刚体配置与 Slide 都归
    /// <see cref="MotionMover"/>。</para>
    /// <para><b>重力自己积分</b>，并且直接读 <c>Physics2D.gravity</c>：重力规则改的是这个全局值，
    /// 本类无需改一行就会跟着变 —— 「规则作用于世界本身」就是这样落地的。</para>
    /// <para><b>七个职能类</b>（都住在本目录，都是普通 C# 类，构造函数注入）：</para>
    /// <list type="bullet">
    /// <item><see cref="PlayerInput"/> —— 输入与门禁，产出本帧意图</item>
    /// <item><see cref="ContactSensor"/> —— 接地 / 左右墙 / 脚下摩擦 / 「上」方向</item>
    /// <item><see cref="JumpSolver"/> —— 土狼时间、跳跃缓冲、三档起跳优先级</item>
    /// <item><see cref="DashSolver"/> —— 冲刺计时、次数、冷却、方向</item>
    /// <item><see cref="AttackSolver"/> —— 攻击连段三阶段（判定本身留在本类）</item>
    /// <item><see cref="VelocitySolver"/> —— 速度解算，**无状态纯函数**</item>
    /// <item><see cref="MotionMover"/> —— **唯一持有速度的地方**，位移与碰撞响应</item>
    /// </list>
    /// <para>依赖是单向的：本类认识它们，它们互相不认识，只通过「传进去的参数、返回来的结果」交接。
    /// 所以规则读值（<see cref="FrictionScale"/> / <see cref="FrictionInverted"/>）留在本类 ——
    /// <see cref="VelocitySolver"/> 因此**不认识规则系统**。</para>
    /// <para><b>边沿走返回，状态走属性</b>：一次性动作（起跳、起冲、挥刀、落地）靠返回值带出来，
    /// 持续条件（接地、剩几段跳、连段打到第几刀）走只读属性。</para>
    /// <para>⚠️ 类名与公开面**冻结**：三个资产（<c>LevelRuntime.prefab</c> /
    /// <c>Test_RuleSystem.unity</c> / <c>Test_RuleWheel.unity</c>）序列化了本组件，
    /// <c>PlayerAnimationDriver</c> / <c>P3DebugOverlay</c> / <c>RuleWheelPresenter</c> 等
    /// 六个文件引用它。重构只搬内脏，不换壳。</para>
    /// </remarks>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class PlayerController : MonoBehaviour, IKnockbackReceiver, IRuleSelectionInputGate
    {
        // ==================================================================
        //  序列化字段
        // ==================================================================
        //
        // ⚠️ 字段名与顺序**不要动**：三个资产里存着这些值的现值，
        //    改名等于让它们全部回落到代码默认值。

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
        [Tooltip("**贴墙能力总开关**（贴墙下滑 + 蹬墙跳）。这是**这个实体自己的能力**，" +
                 "不是玩家特权 —— 敌人配一下就有、关掉就没有。")]
        [SerializeField] private bool _wallClimbEnabled = true;

        [Tooltip("横向墙面探测距离（米）。**全高探测**，用于顶墙分量预夹（防止 Slide 把整笔位移取消）。")]
        [SerializeField] private float _wallProbeDistance = 0.08f;

        [Tooltip("贴墙能力的**中段圆探针**半径（米）。探针位置 = 主碰撞体的中段高度、左右两条边缘。\n" +
                 "只认中段的接触 —— 矮台 / 台阶 / 头顶横梁都不算墙，所以不会在平台上误触发贴墙。\n" +
                 "与上面那条是两回事：那条是全高探测，管预夹。")]
        [SerializeField] private float _wallClimbProbeRadius = 0.06f;

        [Tooltip("贴墙时的下滑速度上限。远低于 _maxFallSpeed —— 贴墙是「缓慢下滑」，不是「挂在墙上」。" +
                 "调大 = 贴墙停留时间短、掉得快；调小 = 有更多时间看准再蹬。")]
        [SerializeField] private float _wallSlideSpeed = 6f;

        [Tooltip("贴到墙那一瞬间，下落速度被「墙面摩擦」吃掉的快慢（单位/秒²）。" +
                 "调大 = 一碰墙就被截停（越接近瞬间夹住）；调小 = 先冲下去一点再被拉住。")]
        [SerializeField] private float _wallSlideImpactDecel = 60f;

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

        [Tooltip("可沿其滑行的最大坡面角度（引擎限制 0~90）。**必须给 90**。" +
                 "给小于 90 的值时，竖直墙（90°）会把整个滑动取消 —— 不是只挡住顶墙那一份，" +
                 "而是把沿墙下滑的竖直分量一起丢掉，表现为「速度满速但位置不动」。")]
        [SerializeField] private float _surfaceSlideAngle = 90f;

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

        // ------------------------------------------------------------------
        //  攻击节奏：判定三段管玩法节奏，动画速度是另一件事
        // ------------------------------------------------------------------

        /// <summary>
        /// 起手：从按键到「判定开窗」的基准秒数。**它要对齐刀刃的视觉接触时刻**。
        /// </summary>
        /// <remarks>
        /// <para>第一刀在图上要走 <c>DrawSword</c>（拔剑）再进 <c>Slash1</c>，刀刃扫到目标
        /// 差不多是 <c>Slash1</c> 的第二、三帧 —— 那是 0.3 秒出头，不是 0.1 秒。
        /// 起手太短 = **判定比视觉早**，看着像"没打到就掉血"。</para>
        /// <para>⚠️ 它和 <see cref="AttackSwingSeconds"/> 是**两件事**：动画提速 → 接触时刻提前
        /// → 这个数要跟着缩短。改一个记得看另一个。</para>
        /// </remarks>
        public const float AttackWindupBase = 0.30f;

        /// <summary>生效：判定窗的基准时长。</summary>
        public const float AttackActiveBase = 0.12f;

        /// <summary>收招：连段窗口的基准时长。</summary>
        public const float AttackRecoverBase = 0.30f;

        /// <summary>
        /// 一刀动画的目标时长（秒）—— <c>HeroAnimatorBuilder</c> 按它算 <c>AnimatorState.speed</c>。
        /// </summary>
        /// <remarks>
        /// <para>「一刀」按图上的**连续段**算：第一刀 = <c>DrawSword + Slash1</c>，
        /// 第二刀 = <c>Slash2</c>（回旋）。每一段各自的剪辑时长除以本值 = 该段的播放倍率。</para>
        /// <para>⚠️ 它**不是**判定一个循环的长度，别把两边绑一起：判定循环 = 起手 + 生效 + 收招
        /// = <see cref="AttackWindupBase"/> + <see cref="AttackActiveBase"/>
        /// + <see cref="AttackRecoverBase"/>。判定管「多久能再打」，本值管「动画多快播完」。</para>
        /// </remarks>
        public const float AttackSwingSeconds = 0.54f;

        [Tooltip("起手：从按键到「判定开窗」的秒数。")]
        [SerializeField] private float _attackWindup = AttackWindupBase;

        [Tooltip("生效：判定窗的时长。**只在开窗那一瞬做一次命中判定**，不做持续判定 —— " +
                 "一刀砍两次会把无敌帧和伤害规则都算乱。")]
        [SerializeField] private float _attackActive = AttackActiveBase;

        [Tooltip("收招：这段时间里**再按一次就接下一刀**，不按就收剑。" +
                 "起手 / 生效段按下会先存着，到收招段再兑现（连段手感靠它）。")]
        [SerializeField] private float _attackRecover = AttackRecoverBase;

        [Tooltip("连段最多打几刀。1 = 只有一下，不接。")]
        [SerializeField, Range(1, 4)] private int _maxCombo = 2;

        [Header("能量")]
        [Tooltip("每命中一个目标恢复的能量（策划案 §四：攻击小怪回能）。未命中不给。")]
        [SerializeField] private float _energyPerHit = 4f;

        // ==================================================================
        //  职能类
        // ==================================================================

        private PlayerInput   _input;
        private ContactSensor _contacts;
        private JumpSolver    _jump;
        private DashSolver    _dash;
        private AttackSolver  _attack;
        private MotionMover   _mover;

        // ==================================================================
        //  表现层事件
        // ==================================================================
        //
        // ⚠️ 这些是**边沿事件**（一次性），不是状态 —— 状态用下面那些只读属性读。
        //    分成两种是因为表现层两样都要：「现在是什么」（持续条件，每帧重写就够）
        //    和「刚刚发生了什么」（一次性动作）。后者靠每帧采样很容易漏掉或重复计一次
        //    （比如 0.33 秒的落地只有 4 个物理帧），所以直接从源头发事件。
        //
        //    事件名与签名**冻结**：PlayerAnimationDriver 订阅着它们。

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

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            if (_status == null) _status = GetComponent<EntityStatus>();

            var body = GetComponent<Rigidbody2D>();

            _input    = new PlayerInput(_inputActions);
            _contacts = new ContactSensor(body, GetComponent<Collider2D>());
            _jump     = new JumpSolver();
            _dash     = new DashSolver();
            _attack   = new AttackSolver();
            _mover    = new MotionMover(body);          // 刚体的 Kinematic 配置在这里面

            _dash.Reset(BuildTuning());                 // 起手就有冲刺，不必先落地一次

            if (!_input.IsReady)
                Debug.LogWarning("[PlayerController] 未找到输入动作，请把 InputSystem_Actions 拖到 _inputActions。", this);
        }

        private void OnEnable()  => _input?.Enable();

        private void OnDisable() => _input?.Disable();

        // ==================================================================
        //  输入（视觉帧）
        // ==================================================================

        private void Update()
        {
            // 暂停 / 被 UI 接管 / 门禁刚释放的恢复帧 —— 三种都不读键，并清空粘滞标记。
            _input.Tick(IsPaused || _input.IsSuppressed);
        }

        // ==================================================================
        //  物理（固定帧）
        // ==================================================================

        private void FixedUpdate()
        {
            // 暂停：速度清零、整帧不跑。冲刺计时与连段计时因此天然冻结
            //（文档 §5）—— 不需要额外的暂停分支。
            if (IsPaused)
            {
                _mover.Halt();
                return;
            }

            float dt = Time.fixedDeltaTime;

            // 参数每物理帧重打包一次（运行期改字段立刻生效），这一帧内共用同一份。
            MotionTuning tuning = BuildTuning();

            // ① 接触：Sense 内部先解出「上」方向（探测得先知道哪边是下），再扫地面与左右墙。
            PlayerInput.State   input   = _input.Sample();
            ContactSensor.State contact = _contacts.Sense(tuning);
            if (contact.JustLanded) OnLanded?.Invoke();

            // ③ 冲刺：计时 / 次数 / 冷却 / 方向。Dashing 是后面几步的总开关。
            DashSolver.Decision dash = _dash.Advance(tuning, input, contact, _mover.Facing, dt);
            if (dash.Started) OnDashed?.Invoke();

            // ③′ 攻击连段：起手 → 生效 → 收招。判定本身留在本类（要用 CombatSystem 与位置）。
            AttackSolver.Decision attack = _attack.Advance(tuning, input.AttackPressed, dt);
            if (attack.Swing)  OnAttacked?.Invoke();
            if (attack.Strike) PerformAttackHit();

            // ②⑦⑧ 跳跃：跳数与土狼时间一直推进；冲刺期间只推进计数、不消费输入。
            JumpSolver.Decision jump = _jump.Solve(tuning, input, contact, dash.Dashing, dt);
            switch (jump.Kind)
            {
                case JumpSolver.JumpKind.Ground: OnJumped?.Invoke();     break;
                case JumpSolver.JumpKind.Wall:   OnWallJumped?.Invoke(); break;
                case JumpSolver.JumpKind.Air:    OnAirJumped?.Invoke();  break;
            }

            // ④④′⑤⑥⑦⑧ 速度解算：纯计算。速度从参数进、从返回值出。
            var frame = new MotionFrame(tuning, input, contact, jump, dash,
                                        Physics2D.gravity, _mover.Facing,
                                        FrictionScale, FrictionInverted);

            _mover.Apply(VelocitySolver.Solve(_mover.Velocity, frame, dt));   // 唯一一次写速度

            // ⑧⑨⑩ 击退衰减 → Slide 解算接触 → 撞墙撞顶清分量
            _mover.Move(tuning, contact, dt);
        }

        // ==================================================================
        //  对外只读状态
        // ==================================================================
        //
        // ⚠️ 全部对「协作对象还不存在」做了保护。
        //    这些属性在 **Awake 之前**就会被读到：编辑模式的 gizmo、P3DebugOverlay 的 OnGUI、
        //    以及 play 模式切换的那一瞬间（域刚重载完、Awake 还没跑）。
        //    重构前它们读的是普通字段，默认值天然安全；现在解引用协作对象，不加保护就是 NRE。
        //    ⚠️ 保护方式是**返回安全默认值，不是惰性创建** —— `new MotionMover(...)` 会去配置
        //       刚体（bodyType / interpolation），在编辑模式里读一下属性就改场景，那是脏场景。

        /// <summary>是否站在支撑面上（供踩踏判定与调试用）。</summary>
        public bool IsGrounded => _contacts != null && _contacts.Grounded;

        /// <summary>
        /// 当前速度（自己维护的那个，<b>不含</b>击退等外部冲量）。
        /// </summary>
        /// <remarks>给表现层用（动画判上升/下降、跑步快慢）。玩法逻辑不要读它做判断 ——
        /// 那样会把「速度怎么算」泄漏到外面。</remarks>
        public Vector2 Velocity => _mover != null ? _mover.Velocity : Vector2.zero;

        /// <summary>
        /// 当前朝向：+1 右，−1 左。
        /// </summary>
        /// <remarks>取的是**换算后**的方向，所以「摩擦 × 反转」生效时按右往左走，
        /// 这里自动是 −1 —— 表现层不需要自己判断方向。</remarks>
        public float Facing => _mover != null ? _mover.Facing : 1f;

        /// <summary>本帧读到的**原始**移动输入（未经摩擦反转换算）。供调试浮层显示。</summary>
        public Vector2 MoveInput => _input != null ? _input.Move : Vector2.zero;

        /// <summary>
        /// 左侧是否贴着竖墙（**全高**探测；供调试浮层显示）。只用于顶墙分量预夹。
        /// </summary>
        public bool WallLeft => _contacts != null && _contacts.WallLeft;

        /// <summary>右侧是否贴着竖墙（**全高**探测）。只用于顶墙分量预夹。</summary>
        public bool WallRight => _contacts != null && _contacts.WallRight;

        /// <summary>左侧**中段**是否贴着可爬的竖墙 —— 贴墙下滑 / 蹬墙跳的判据。</summary>
        public bool ClimbLeft => _contacts != null && _contacts.ClimbLeft;

        /// <summary>右侧**中段**是否贴着可爬的竖墙 —— 贴墙下滑 / 蹬墙跳的判据。</summary>
        public bool ClimbRight => _contacts != null && _contacts.ClimbRight;

        /// <summary>是否正在贴墙缓慢下滑（供调试浮层显示）。</summary>
        public bool IsWallSliding => _mover != null && _mover.WallSliding;

        /// <summary>总跳数上限 = 第一段 + 空中段。</summary>
        public int MaxJumps => JumpSolver.MaxJumps(_maxAirJumps);

        /// <summary>本次落地以来还剩几段跳（供 HUD / 调试浮层显示）。</summary>
        public int JumpsLeft => Mathf.Max(MaxJumps - (_jump != null ? _jump.JumpsUsed : 0), 0);

        /// <summary>正在冲刺。</summary>
        public bool IsDashing => _dash != null && _dash.IsDashing;

        /// <summary>还能冲几次（供 HUD / 调试浮层显示）。</summary>
        public int DashesLeft => _dash != null ? _dash.DashesLeft : 0;

        /// <summary>冲刺次数上限。</summary>
        public int MaxDashes => _maxDashes;

        /// <summary>当前连段打到第几刀（0 起数）；没在出招时是 −1。</summary>
        public int AttackComboIndex => _attack != null ? _attack.ComboIndex : -1;

        /// <summary>暂停中不读输入、不推进物理。</summary>
        /// <remarks>暂停状态由 <c>EntityStatus.SetPaused</c> 驱动 —— 暂停持有者（P1 的时间管理器）负责调用。</remarks>
        private bool IsPaused => _status != null && _status.IsPaused;

        // ==================================================================
        //  输入门禁
        // ==================================================================

        /// <summary>UI 仅接管输入；慢时间里的重力、碰撞与外部冲量仍走原 FixedUpdate。</summary>
        public IDisposable BlockGameplayInput() => _input.Block();

        // ==================================================================
        //  击退
        // ==================================================================

        /// <summary>外部冲量入口（击退）。见 <see cref="IKnockbackReceiver"/>。</summary>
        public void AddImpulse(Vector2 impulse) => _mover.AddImpulse(impulse);

        // ==================================================================
        //  攻击判定
        // ==================================================================

        /// <summary>生效段的唯一命中判定。走正式伤害链路（传**基础值**，由伤害处理模块按规则算）。</summary>
        /// <remarks>放在速度解算**之前**调用，所以读到的朝向是上一帧末的 ——
        /// 与重构前一致（原来 ③′ 也在 ④ 之前）。</remarks>
        private void PerformAttackHit()
        {
            if (_combat == null) return;

            // 发起方只组织「打哪儿、打多大」，最终伤害由伤害处理模块按当前规则算。
            Vector2 center = (Vector2)transform.position + new Vector2(_mover.Facing * _attackOffset, 0f);

            int hits = _combat.PerformAttack(this, center, _attackBoxSize, _attackMask,
                                             _attackDamage, _attackKnockback);

            // 能量恢复来源之一：命中即回，未命中不给（策划案 §四）。
            if (hits > 0 && _status != null)
                _status.RestoreEnergy(_energyPerHit * hits);
        }

        /// <summary>攻击判定体 + 贴墙探针：调参时直接看得到范围与朝向。</summary>
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.8f);
            Vector2 center = (Vector2)transform.position + new Vector2(Facing * _attackOffset, 0f);
            Gizmos.DrawWireCube(center, _attackBoxSize);

            // 贴墙能力的**中段圆探针**。它的位置是从主碰撞体 bounds 推出来的（没有实体碰撞体），
            // 所以必须画出来才看得见 —— 这也是判断「探针高度合不合适」的唯一直观手段。
            var collider = GetComponent<Collider2D>();
            if (collider == null || _wallClimbProbeRadius <= 0f) return;

            Bounds b = collider.bounds;
            Gizmos.color = new Color(0.25f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(new Vector3(b.min.x, b.center.y, transform.position.z), _wallClimbProbeRadius);
            Gizmos.DrawWireSphere(new Vector3(b.max.x, b.center.y, transform.position.z), _wallClimbProbeRadius);
        }

        // ==================================================================
        //  参数打包 / 规则读值
        // ==================================================================

        /// <summary>
        /// 把序列化字段打成 <see cref="MotionTuning"/>。
        /// </summary>
        /// <remarks><b>每物理帧重打包</b>：这样运行期改字段立刻生效，包括调试时用反射改值那条流程。
        /// 几十个浮点的拷贝，50Hz 下可以忽略。</remarks>
        private MotionTuning BuildTuning() => new MotionTuning
        {
            MoveSpeedMax         = _moveSpeedMax,
            GroundAccel          = _groundAccel,
            AirAccel             = _airAccel,
            AirDrag              = _airDrag,
            FrictionDecel        = _frictionDecel,

            JumpSpeed            = _jumpSpeed,
            AirJumpSpeed         = _airJumpSpeed,
            MaxAirJumps          = _maxAirJumps,
            GravityScale         = _gravityScale,
            FallGravityMultiplier = _fallGravityMultiplier,
            MaxFallSpeed         = _maxFallSpeed,
            JumpCutMultiplier    = _jumpCutMultiplier,
            CoyoteTime           = _coyoteTime,
            JumpBuffer           = _jumpBuffer,

            DashSpeed            = _dashSpeed,
            DashDuration         = _dashDuration,
            DashExitSpeedScale   = _dashExitSpeedScale,
            DashCooldown         = _dashCooldown,
            MaxDashes            = _maxDashes,

            WallClimbEnabled     = _wallClimbEnabled,
            WallProbeDistance    = _wallProbeDistance,
            WallClimbProbeRadius = _wallClimbProbeRadius,
            WallSlideSpeed       = _wallSlideSpeed,
            WallSlideImpactDecel = _wallSlideImpactDecel,
            WallJumpPushSpeed    = _wallJumpPushSpeed,
            WallClimbPushSpeed   = _wallClimbPushSpeed,
            WallJumpUpSpeed      = _wallJumpUpSpeed,

            SolidLayer           = _solidLayer,
            GroundProbeDistance  = _groundProbeDistance,
            SurfaceAnchorDistance = _surfaceAnchorDistance,
            SurfaceSlideAngle    = _surfaceSlideAngle,

            KnockbackDecay       = _knockbackDecay,

            AttackWindup         = _attackWindup,
            AttackActive         = _attackActive,
            AttackRecover        = _attackRecover,
            MaxCombo             = _maxCombo,
        };

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
