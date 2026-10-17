using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 弹簧平台：**底端锚定、顶端站人**的竖直弹簧 —— 有人站上来会被压下去，释放时把站在上面的实体弹飞。
    /// </summary>
    /// <remarks>
    /// <para><b>它是什么</b>：一个简谐振子。压缩量 <c>c</c> 是唯一的自由度（0 = 自然长度，
    /// 1 = 压到底），弹簧力 <c>k × (目标 − c)</c> 与阻尼力 <c>−阻尼 × c′</c> 一起决定它怎么走。
    /// 所以「压下去」和「弹回来」都不是插值，是**真的在积分**：会加速、会冲过头（阻尼调得小的时候）。</para>
    ///
    /// <para><b>四个阶段</b>：
    /// <list type="bullet">
    /// <item><b>Idle</b> —— 空载，停在自然长度；</item>
    /// <item><b>Compressing</b> —— 有人压着，往压缩长度走；</item>
    /// <item><b>Loaded</b> —— 压到底，倒计时 <see cref="_holdSeconds"/>；</item>
    /// <item><b>Releasing</b> —— 回弹。这一阶段本组件会给出 <see cref="LaunchVelocity"/>，
    /// 站在上面的实体被弹飞（<see cref="_launchSpeed"/> 为 0 时不给速度，人只是被浮回原位）。</item>
    /// </list>
    /// 中途走人（人在 Compressing 里跳走）会直接进 Releasing —— 弹性势能总得放掉。
    /// ⚠️ 压载只认「上表面从无到有」的**边沿**：否则释放完人还站着，会立刻又被压下去，弹簧就永远在上下循环。</para>
    ///
    /// <para><b>「弹飞」是怎么发生的</b>：平台自己推不动任何人（玩家是 Kinematic 刚体，
    /// 引擎的力对它无效）。它是**报出一个速度**（<see cref="ISpringSurface"/>），
    /// 由实体每物理帧自己查一次、写进自己的速度里。所以「谁被弹」= 「谁正踩在上面」，
    /// 平台不需要认识玩家。带人（压下去时一起落）走的是另一条路
    /// （<see cref="IMovingSurface"/>，位置跟随）—— 两个接口管两件事，都在本类上。</para>
    ///
    /// <para><b>承重靠包围盒判定，不靠接触回调</b>：与 <c>Tube</c> / <c>OneWayGate</c> 同一约定 ——
    /// 玩家是 Kinematic 刚体且 <c>useAttachedTriggers = false</c>，接触 / 触发回调不可靠。
    /// 这里每物理帧在平台上表面扫一个扁盒子（<see cref="_riderProbe"/> 那么高），看承重层里有没有东西。</para>
    ///
    /// <para><b>方向恒为世界竖直</b>：弹簧只做上下伸缩，锚点的 <c>x</c> 就是平台的 <c>x</c>。
    /// 不要旋转根物体 —— 位姿由代码按锚点解算，旋转只会被下一帧覆盖掉。</para>
    ///
    /// <para><b>暂停 / 时间规则</b>：暂停时物理已停步进（<c>Physics2D.simulationMode = Script</c>），
    /// 弹簧也必须停 —— 读 <c>TimeManager.IsPaused</c> 早退（单一真相是 TimeManager）；
    /// 积分用 <c>Time.fixedDeltaTime</c>，随 <c>Time.timeScale</c> 缩放 —— 世界变慢，弹簧也跟着变慢。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑥ 铁丝弹簧（可伸缩弹簧平台）。</para>
    ///
    /// <para><b>为什么要抢在玩家之前执行</b>：承接方（<c>ContactSensor</c> ①）读到的是
    /// <see cref="Velocity"/>，而它按 <see cref="IMovingSurface"/> 的契约必须是**本物理帧**的速度。
    /// 脚本执行顺序不定，若本组件排在玩家之后，玩家读到的就是**上一帧**的速度 ——
    /// 匀速的平移平台无所谓（速度不变，等价），但**弹簧是加速运动的**，加速段会逐帧落后一点点。
    /// 现在这点落后被玩家的贴地吸附盖住了（实测不可见），但高速平台（弹射器 / 快速电梯）会放大它。
    /// 排到 <c>RuleSystemDriver</c>（−100）之后、默认（0）之前。</para>
    /// </remarks>
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public sealed class SpringPlatform : MonoBehaviour, IMovingSurface, ISpringSurface
    {
        /// <summary>弹簧当前处在哪一段。</summary>
        private enum Phase
        {
            /// <summary>空载，停在自然长度。</summary>
            Idle = 0,

            /// <summary>有人压着，正在往压缩长度走。</summary>
            Compressing = 1,

            /// <summary>压到底。有发射速度时在倒计时，倒完自动弹。</summary>
            Loaded = 2,

            /// <summary>回弹中 —— 这一阶段接触者会被弹飞。</summary>
            Releasing = 3,
        }

        /// <summary>每隔多少「弧度」拆一个积分子步：<c>ω·h</c> 超过它就把这一步拆开。</summary>
        /// <remarks>半隐式欧拉在 <c>ω·dt &gt; 2</c> 时会发散。弹簧刚度可以调得很大（k=5000 → ω≈70，
        /// 50Hz 下 ω·dt≈1.4），不拆的话平台会**炸开**而不是压缩。0.2 远低于发散门槛，代价只是几个浮点乘。</remarks>
        private const float MaxSubstepAngle = 0.2f;

        /// <summary>子步上限：k 调到离谱时也不至于一帧算上千次。</summary>
        private const int MaxSubsteps = 8;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("弹簧（世界坐标）")]
        [Tooltip("底端固定点。弹簧从这里垂直立起，平台只沿这条线上下动。\n" +
                 "摆位用 Inspector 上的「锚点 = 平台正下方」按钮。")]
        [SerializeField] private Vector2 _anchor = Vector2.zero;

        [Tooltip("自然长度：空载时平台**中心**在锚点上方多高。")]
        [SerializeField, Min(0.05f)] private float _restLength = 2f;

        [Tooltip("压缩长度：被压到底时平台中心在锚点上方多高。\n" +
                 "它和自然长度之差就是弹簧的全部行程 —— 这个差越大，压下去越明显。")]
        [SerializeField, Min(0f)] private float _compressedLength = 0.8f;

        [Header("劲度")]
        [Tooltip("劲度系数 k。越大越硬：压得更浅、回弹更急。\n" +
                 "等效角频率 ω = √k（rad/s），压缩到底大约要半个周期 ≈ π/ω 秒。k=90 约 0.33 秒。")]
        [SerializeField, Min(1f)] private float _springConstant = 90f;

        [Tooltip("阻尼。越小越会来回振荡（本组件在两端有硬限位，所以过冲会被吃掉，表现为「弹到位就停」）。\n" +
                 "临界阻尼 ≈ 2√k：k=90 时约 19。取一半左右读起来最有弹性，取临界值则是平滑下落。")]
        [SerializeField, Min(0f)] private float _damping = 12f;

        [Header("弹射")]
        [Tooltip("压到底之后，给站在上面的实体的**向上速度**（单位/秒）。\n" +
                 "大于 0 = 弹跳板，把人弹飞；0 = 只沉不弹 —— 压到底停 _holdSeconds 秒后自己浮回来，\n" +
                 "读起来是一块软垫（人还站着的话，浮回来就停在自然长度，不会反复上下动）。")]
        [SerializeField, Min(0f)] private float _launchSpeed = 18f;

        [Tooltip("压到底之后停多久再释放（秒）。0 = 一到底就弹。\n" +
                 "调得很大（比如 99）就是「压住不动」的承重台 —— 只有人走开才回弹。")]
        [SerializeField, Min(0f)] private float _holdSeconds = 0.12f;

        [Header("承重")]
        [Tooltip("**谁压得动它**。默认只勾了 Default 层 —— 玩家在这一层（敌人在 Enemy 层，要一起勾上）。\n" +
                 "一个都不勾 = 这个弹簧不会被压动。")]
        [SerializeField] private LayerMask _riderLayer = 1;   // 位 0 = Default 层

        [Tooltip("承重探测条的高度（世界单位）：在平台上表面往上扫这么高的一条扁盒子。\n" +
                 "太矮会漏掉刚踩上来的一瞬，太高会把从上方掠过的人一起算进来。")]
        [SerializeField, Min(0.05f)] private float _riderProbe = 0.35f;

        [Header("摩擦")]
        [Tooltip("平台表面的摩擦系数。1 = 普通地面；比 1 大更「涩」—— 站上去起步 / 刹车更抓地，\n" +
                 "小于 1 像冰面。运行时改这个值会立刻生效。\n" +
                 "⚠️ 摩擦规则仍然照常叠加：它在外层再乘一个倍率（增强 ×3 / 削弱 ×0.3 / 失效 ×0）。")]
        [SerializeField, Min(0f)] private float _friction = 1.5f;

        [Header("白盒")]
        [Tooltip("弹簧柱的美术。**会被沿 Y 轴拉伸**：缩放 y = 锚点到平台底面的距离。\n" +
                 "所以这张图要按「1 世界单位高」做（与白盒方块一致）。**接正式美术时清空玩法不读它。**")]
        [SerializeField] private Transform _springVisual;

        [Tooltip("白盒标签。**接正式美术时把这个引用清空** —— 玩法判定不读它。")]
        [SerializeField] private TextMesh _label;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;
        private Collider2D  _plankCollider;
        private readonly Collider2D[] _riderHits = new Collider2D[4];

        private Phase _phase = Phase.Idle;

        /// <summary>压缩量：0 = 自然长度，1 = 压到底。唯一的自由度。</summary>
        private float _compression;

        /// <summary>压缩量的变化率（每秒）。弹簧系统的另一半状态。</summary>
        private float _compressionSpeed;

        /// <summary>压到底之后还剩多少秒释放。</summary>
        private float _holdRemaining;

        /// <summary>上一物理帧上表面有没有负载。压载只认「无 → 有」的边沿。</summary>
        private bool _wasLoaded;

        /// <summary>本实例**独占**的物理材质 —— 平台表面的摩擦就写在它上面。</summary>
        /// <remarks>⚠️ 不能直接改 <c>Collider2D.sharedMaterial</c>：那是共享的，
        /// 场景里放三个弹簧、各想调不同摩擦时会互相覆盖。运行时 new 一份自己用。</remarks>
        private PhysicsMaterial2D _ownedMaterial;

        /// <summary>已经写进材质的摩擦值。与 <see cref="_friction"/> 不同才重新写。</summary>
        private float _appliedFriction;

        /// <summary>本物理帧的平台速度（单位 / 秒）。踩在上面的实体读它，见 <see cref="IMovingSurface"/>。</summary>
        /// <remarks>由「这一步实际走了多远 ÷ dt」算出，与位移严格一致 ——
        /// 承载方加进自己的速度后，走出的距离与平台**逐帧相等**，不会累积漂移。</remarks>
        public Vector2 Velocity { get; private set; }

        /// <summary>本物理帧要弹给接触者的速度，见 <see cref="ISpringSurface"/>。不在释放阶段时为 <see cref="Vector2.zero"/>。</summary>
        public Vector2 LaunchVelocity { get; private set; }

        /// <summary>当前长度（锚点到平台中心的竖直距离）。</summary>
        public float CurrentLength => Mathf.LerpUnclamped(_restLength, _compressedLength, _compression);

        /// <summary>压缩量 0 ~ 1（表现层要抖屏幕 / 播音效可以读它）。</summary>
        public float Compression => _compression;

        /// <summary>现在是不是在回弹（这一阶段接触者会被弹飞）。</summary>
        public bool IsReleasing => _phase == Phase.Releasing;

        /// <summary>底端锚点（世界坐标）。</summary>
        public Vector2 AnchorPoint => _anchor;

        /// <summary>自然长度：空载时平台中心在锚点上方多高。</summary>
        public float RestLength => _restLength;

        /// <summary>压缩长度：压到底时平台中心在锚点上方多高。</summary>
        public float CompressedLength => _compressedLength;

        /// <summary>行程 = 自然长度 − 压缩长度。0 = 这个弹簧不会动。</summary>
        public float Travel => Mathf.Max(0f, _restLength - _compressedLength);

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _plankCollider = GetComponent<Collider2D>();
            ConfigureBody();
            ApplyFriction();
            Rebuild();
        }

        private void OnEnable()
        {
            // 重开一局 / 对象池取出时也要回到自然长度，不能接着上次的压缩量走。
            if (_body == null)
            {
                _body = GetComponent<Rigidbody2D>();
                _plankCollider = GetComponent<Collider2D>();
                ConfigureBody();
            }
            ApplyFriction();
            Rebuild();
        }

        private void OnDestroy()
        {
            // 运行时 new 出来的材质不归任何资产管，跟着组件一起清掉。
            if (_ownedMaterial == null) return;

            if (_plankCollider != null && _plankCollider.sharedMaterial == _ownedMaterial)
            {
                _plankCollider.sharedMaterial = null;
            }

            Destroy(_ownedMaterial);
            _ownedMaterial = null;
        }

        private void FixedUpdate()
        {
            // 运行期改摩擦立刻生效（调试时常这么干）。值没变时只是一次浮点比较。
            ApplyFriction();

            if (TimeManager.Instance?.IsPaused == true)
            {
                Velocity = Vector2.zero;
                LaunchVelocity = Vector2.zero;
                return;
            }

            // 注意：本文件 using 了 RuleGame.Infrastructure.Time，它会遮蔽 UnityEngine.Time，
            // 所以这两个都要全限定（与 MovingPlatform / Tube 同一处理）。
            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;

            Step(dt, ProbeRider());
            ApplyPosition(dt);
            UpdateSpringVisual();
        }

        // ==================================================================
        //  状态机 + 积分
        // ==================================================================

        /// <summary>推进状态机，再按当前阶段的目标积分压缩量。</summary>
        private void Step(float dt, bool loaded)
        {
            AdvancePhase(dt, loaded);

            // 目标：往压缩走（0）还是往自然长度走（1）。
            // Releasing 期间目标恒为 0 —— 哪怕人还踩着，也要先把弹性势能放掉，
            // 不然「压到底 → 弹」这条路径会被人一直踩着卡在 Loaded。
            bool compressing = _phase == Phase.Compressing || _phase == Phase.Loaded;
            Integrate(dt, compressing ? 1f : 0f);
        }

        private void AdvancePhase(float dt, bool loaded)
        {
            switch (_phase)
            {
                case Phase.Idle:
                    // ⚠️ 只认「从无到有」的**边沿**。用持续判定的话，释放完人还站在上面，
                    //    弹簧会立刻又被压下去 —— 于是它永远在「压下去 → 弹回来 → 压下去」之间循环。
                    if (loaded && !_wasLoaded) _phase = Phase.Compressing;
                    break;

                case Phase.Compressing:
                    if (!loaded)
                    {
                        // 人中途走了（跳走 / 走出去了）：弹性势能总得放掉。
                        _phase = Phase.Releasing;
                        break;
                    }
                    // ⚠️ 判定只看压缩量，**不要**再带「速度朝里」的条件：撞到硬限位时速度被清零，
                    //    但阻尼不足时它可能在限位附近来回，带速度条件会让状态机卡在 Compressing 里反复。
                    if (_compression >= 1f)
                    {
                        _phase = Phase.Loaded;
                        _holdRemaining = Mathf.Max(0f, _holdSeconds);
                    }
                    break;

                case Phase.Loaded:
                    if (!loaded)
                    {
                        _phase = Phase.Releasing;
                        break;
                    }
                    _holdRemaining -= dt;
                    if (_holdRemaining <= 0f) _phase = Phase.Releasing;
                    break;

                case Phase.Releasing:
                    if (_compression <= 0f && _compressionSpeed <= 0f) _phase = Phase.Idle;
                    break;
            }

            _wasLoaded = loaded;
        }

        /// <summary>
        /// 半隐式欧拉积分一步：<c>c″ = k(target − c) − 阻尼 × c′</c>。
        /// </summary>
        /// <remarks>
        /// <para>两端都有**硬限位**（压缩量夹在 0 ~ 1）：平台不会缩进锚点里，也不会弹过自然长度
        /// —— 站人的面必须可预测。撞到限位时把朝外的速度归零，就是「能量被限位吃掉」。</para>
        /// <para>刚度调得很大时按 <see cref="MaxSubstepAngle"/> 拆子步，否则会数值发散。</para>
        /// </remarks>
        private void Integrate(float dt, float target)
        {
            float omega = Mathf.Sqrt(_springConstant);
            int substeps = Mathf.Clamp(
                Mathf.CeilToInt(omega * dt / MaxSubstepAngle), 1, MaxSubsteps);
            float h = dt / substeps;

            for (int i = 0; i < substeps; i++)
            {
                float accel = _springConstant * (target - _compression)
                            - _damping * _compressionSpeed;

                _compressionSpeed += accel * h;
                _compression      += _compressionSpeed * h;

                if (_compression <= 0f)
                {
                    _compression = 0f;
                    if (_compressionSpeed < 0f) _compressionSpeed = 0f;
                }
                else if (_compression >= 1f)
                {
                    _compression = 1f;
                    if (_compressionSpeed > 0f) _compressionSpeed = 0f;
                }
            }
        }

        /// <summary>把当前长度写成位置，并顺带算出本物理帧的速度。</summary>
        private void ApplyPosition(float dt)
        {
            Vector2 previous = _body.position;
            Vector2 target   = new Vector2(_anchor.x, _anchor.y + CurrentLength);

            Velocity = (target - previous) / dt;
            _body.MovePosition(target);

            // ⚠️ 发射速度在这里定，不在 Step 里：它只跟阶段有关，且**必须**与位移同一帧报出去，
            //    否则承载方读到的是上一帧的发射状态。
            LaunchVelocity = _phase == Phase.Releasing && _launchSpeed > 0f
                           ? Vector2.up * _launchSpeed
                           : Vector2.zero;
        }

        // ==================================================================
        //  承重探测
        // ==================================================================

        /// <summary>平台上表面有没有东西压着。</summary>
        /// <remarks>
        /// <para>用**包围盒重叠**而不是接触 / 触发回调：玩家是 Kinematic 刚体且
        /// <c>useAttachedTriggers = false</c>，回调不可靠（与 <c>Tube</c> / <c>OneWayGate</c> 同一约定）。</para>
        /// <para>⚠️ <c>Collider2D.bounds</c> 是缓存值，物理步解算前读到的是**上一步**的包围盒
        /// —— 差一个物理步，对「脚下有没有人」没有影响。</para>
        /// <para>宽度取 0.9 倍：站在边缘上的人只搭了一点边也算压着，但**完全在平台外**的人不算。</para>
        /// </remarks>
        private bool ProbeRider()
        {
            if (_riderLayer.value == 0 || _plankCollider == null) return false;

            Bounds b = _plankCollider.bounds;
            var center = new Vector2(b.center.x, b.max.y + _riderProbe * 0.5f);
            var size   = new Vector2(Mathf.Max(b.size.x * 0.9f, 0.1f), _riderProbe);

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_riderLayer);

            int count = Physics2D.OverlapBox(center, size, 0f, filter, _riderHits);
            for (int i = 0; i < count; i++)
            {
                // 万一有人把平台自己的层也勾进了承重层，别自己压自己。
                if (_riderHits[i] == null) continue;
                if (_riderHits[i].transform.IsChildOf(transform)) continue;
                return true;
            }
            return false;
        }

        // ==================================================================
        //  内部
        // ==================================================================

        /// <summary>回到自然长度、清空状态。开局 / 对象池取出时调用。</summary>
        private void Rebuild()
        {
            _phase            = Phase.Idle;
            _compression      = 0f;
            _compressionSpeed = 0f;
            _holdRemaining    = 0f;

            Velocity       = Vector2.zero;
            LaunchVelocity = Vector2.zero;

            if (_body != null)
            {
                _body.position = new Vector2(_anchor.x, _anchor.y + _restLength);
            }

            UpdateSpringVisual();
        }

        /// <summary>把弹簧柱按当前长度拉伸。**锚点不动、柱顶跟着平台底面走。**</summary>
        /// <remarks>
        /// <para>用**根的局部坐标**而不是世界坐标：根（平台）每个物理帧都在动，
        /// 写成世界坐标的话柱子会留在原地、和平台分家。换算成局部坐标，它才会跟着走。</para>
        /// <para>柱子是 1 世界单位高的图（与白盒方块一致），所以缩放 y 直接就是高度。</para>
        /// </remarks>
        private void UpdateSpringVisual()
        {
            if (_springVisual == null) return;

            // ⚠️ 编辑模式下 `transform` 可能刚被搬过（生成器摆位 / Inspector 按钮 / 改参数），
            //    而 `Collider2D.bounds` 是**缓存值**，不 SyncTransforms 就会读到搬家前的包围盒 ——
            //    算出来的柱高会莫名其妙多出一段（实测多出整整一个位移量）。
            //    运行时不用同步：物理步刚跑过，bounds 本来就是新的。
            if (!Application.isPlaying) Physics2D.SyncTransforms();

            // 编辑模式下 Awake 没跑过，_plankCollider 还是 null —— 就地补一次，
            // 免得摆位时柱子高度按「平台中心」算、和平台面糊在一起。
            Collider2D plank = _plankCollider != null ? _plankCollider : GetComponent<Collider2D>();
            float plankBottomY = plank != null ? plank.bounds.min.y : transform.position.y;
            float height = Mathf.Max(plankBottomY - _anchor.y, 0.05f);

            _springVisual.localPosition = new Vector3(_anchor.x - transform.position.x,
                                                      _anchor.y - transform.position.y + height * 0.5f,
                                                      0f);

            Vector3 scale = _springVisual.localScale;
            scale.y = height;
            _springVisual.localScale = scale;
        }

        /// <summary>把 <see cref="_friction"/> 写进一份**本实例独占**的物理材质。</summary>
        /// <remarks>
        /// <para><b>为什么必须独占</b>：<c>Collider2D.sharedMaterial</c> 是共享的 —— 直接改它，
        /// 所有引用同一材质的物体会一起变。场景里放三个弹簧、各想调不同摩擦时就互相覆盖了。</para>
        /// <para><b>为什么非得挂在物理材质上</b>：玩家侧读地面摩擦走的是
        /// <c>Collider2D.sharedMaterial.friction</c>（见 <c>ContactSensor.ProbeGround</c>），
        /// 没有材质就一律按 1（普通地面）算 —— 这个参数没有别的落点。</para>
        /// <para><b>与摩擦规则不冲突</b>：规则只在外面乘一个倍率（<c>VelocitySolver</c> 里的
        /// <c>GroundFriction × FrictionScale</c>），不碰这里的材质。所以「弹簧 × 摩擦增强」
        /// 是 1.5 × 3 = 4.5，仍然成立。</para>
        /// </remarks>
        private void ApplyFriction()
        {
            if (_plankCollider == null) return;

            if (_ownedMaterial == null)
            {
                _ownedMaterial = new PhysicsMaterial2D(name + " (Friction)");
                _ownedMaterial.bounciness = 0f;     // 平台本身不弹，弹射归弹簧管
                _plankCollider.sharedMaterial = _ownedMaterial;
                _appliedFriction = float.NaN;       // NaN 与任何值都不相等：强制写一次
            }

            if (Mathf.Approximately(_appliedFriction, _friction)) return;

            _ownedMaterial.friction = Mathf.Max(0f, _friction);
            _appliedFriction = _friction;
        }

        private void ConfigureBody()
        {
            // Kinematic：位移完全由代码决定，重力 / 摩擦都不参与。
            _body.bodyType      = RigidbodyType2D.Kinematic;
            _body.gravityScale  = 0f;
            _body.linearDamping = 0f;
            _body.constraints   = RigidbodyConstraints2D.FreezeRotation;

            // 定步长物理 + 高帧率渲染：不开插值弹簧会一格一格跳（压下去的动画最明显）。
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：不跑动力学，直接把锚点与长度摆到位。</summary>
        public void SetAnchor(Vector2 anchor)
        {
            _anchor = anchor;
            if (Application.isPlaying) return;

            _compression = 0f;
            _compressionSpeed = 0f;
            _phase = Phase.Idle;
        }

        /// <summary>只给编辑器工具用：把根物体搬到「自然长度」该在的地方。</summary>
        public void SnapToRest()
        {
            transform.position = new Vector3(_anchor.x, _anchor.y + _restLength, transform.position.z);
            UpdateSpringVisual();
        }

        /// <summary>只给编辑器工具用：接上白盒标签的引用。</summary>
        public void SetWhitebox(Transform springVisual, TextMesh label)
        {
            _springVisual = springVisual;
            _label = label;
            RefreshLabel();
            UpdateSpringVisual();
        }

        /// <summary>白盒标签上该写什么。生成工具和本类共用它，免得两处写歪。</summary>
        public static string LabelText(float springConstant, float launchSpeed)
        {
            string core = "弹簧 k" + springConstant.ToString("0.#");
            return launchSpeed > 0f ? core + " · 弹" + launchSpeed.ToString("0.#") : core + " · 承重";
        }

        private void OnValidate()
        {
            // 压缩长度超过自然长度没有意义（弹簧变成往上顶的杆），就地夹住。
            if (_compressedLength > _restLength) _compressedLength = _restLength;
            if (_restLength < 0.05f) _restLength = 0.05f;
            if (_riderProbe < 0.05f) _riderProbe = 0.05f;

            RefreshLabel();

            if (Application.isPlaying) return;
            UpdateSpringVisual();
        }

        /// <summary>把当前的 k / 发射速度写进白盒标签。**只在文字真的变了才写** ——
        /// 否则每次脚本重编译都会把场景标脏，提交里全是噪声。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_springConstant, _launchSpeed);
            if (_label.text != wanted) _label.text = wanted;
        }

        private void OnDrawGizmos()
        {
            var plank = GetComponent<Collider2D>();

            // 弹簧轴：从锚点到自然长度处。
            Gizmos.color = new Color(0.85f, 0.75f, 0.95f, 0.9f);
            Gizmos.DrawLine(_anchor, new Vector3(_anchor.x, _anchor.y + _restLength, 0f));

            // 行程范围：压缩长度那一端的标记。
            Gizmos.color = new Color(1f, 0.45f, 0.45f, 0.9f);
            Gizmos.DrawWireSphere(new Vector3(_anchor.x, _anchor.y + _compressedLength, 0f), 0.12f);

            // 锚点。
            Gizmos.color = new Color(0.55f, 1f, 0.75f, 0.95f);
            Gizmos.DrawWireSphere(_anchor, 0.18f);

            if (Application.isPlaying)
            {
                // 播放中：当前位置 + 承重探测条。
                Gizmos.color = new Color(1f, 0.95f, 0.3f, 1f);
                Gizmos.DrawWireSphere(transform.position, 0.2f);
            }

            if (plank == null) return;

            Bounds b = plank.bounds;
            var center = new Vector3(b.center.x, b.max.y + _riderProbe * 0.5f, 0f);
            var size   = new Vector3(Mathf.Max(b.size.x * 0.9f, 0.1f), _riderProbe, 0f);
            Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.5f);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
