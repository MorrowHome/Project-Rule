using System;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 三棍支架陷阱：三根棍子搭起来的平台 —— <b>右腿固定</b>，<b>左腿</b>被质量够大的实体顶倒，
    /// <b>顶梁</b>随即绕右柱顶转下来，形成三角形斜坡，并把推动者关在笼内。
    /// </summary>
    /// <remarks>
    /// <para><b>形态</b>：左腿竖直、顶梁水平，顶梁是一条可以站人走人的实心平台面。
    /// 左腿是这个结构<b>唯一</b>的活动支点。</para>
    ///
    /// <para><b>五个阶段</b>（见 <see cref="Phase"/>）：
    /// ① <see cref="Phase.Standing"/> 三棍搭好 →
    /// ② <see cref="Phase.LegFalling"/> 左腿绕<b>底铰链</b>倒向内侧（＝实体前进方向），
    /// 顺路把挡在笼口的东西往里扫 →
    /// ③ <see cref="Phase.PlankFalling"/> 顶梁失去左支点，绕<b>右柱顶铰链</b>下摆，
    /// 梢端扫过笼子、把推动者往里推并封死左侧出口 →
    /// ④ <see cref="Phase.Sealed"/> 梢端落到倒下的左腿（＝门槛）上，三角形闭合。
    /// 于是「地面 + 右腿 + 顶梁」围出一个直角三角形：<b>顶梁就是斜坡，笼内关着刚才那个推动者</b>。</para>
    ///
    /// <para><b>触发判据是「质量」，不是「动量」</b>：每物理帧扫一次左腿上段的探测盒，
    /// 找 <c>Rigidbody2D.mass</c> ≥ <see cref="_toppleMass"/> 的刚体，持续
    /// <see cref="_contactHold"/> 秒就倒。于是<b>质量规则就是这一关的钥匙</b>——
    /// 「质量 × 削弱」能把本来顶得动的铁箱变轻、顶不动；「质量 × 增强」能把人压重、顶得动。
    /// 本类一行规则代码都不写，读的就是 <c>Rigidbody2D.mass</c> 那个字段
    /// （质量规则经 <c>RuleAffectedBody</c> 写的就是它）。</para>
    ///
    /// <para>⚠️ <b>探测盒只罩「笼内」那一侧</b>（<see cref="_probeInnerSideOnly"/>）：三角区
    /// 永远落在两腿之间 —— 顶梁梢端的落点 <c>x ≥ 右柱 − 顶梁长 = 左腿底端</c>，
    /// 所以<b>从外面推倒的实体一定落在三角区外面，关不住</b>。要让「推倒它的人被关住」
    /// 这条设计成立，只有站在笼里的实体才该算数。副作用是站在外面的实体怎么撞都不倒 ——
    /// 这不是漏判，是上面那条几何约束的直接推论。</para>
    ///
    /// <para>⚠️ <b>玩家现在读不到质量规则</b>：玩家 <c>mass = 1</c> 且没挂 <c>RuleAffectedBody</c>，
    /// 所以「玩家自己变重去顶倒」暂时不成立 —— 要它成立，给玩家挂上那个组件即可（摩擦那栏可以关）。</para>
    ///
    /// <para><b>为什么每物理帧自己扫探测盒、不用碰撞回调</b>：玩家是 Kinematic 刚体、位移走
    /// <c>Rigidbody2D.Slide</c>（<c>useAttachedTriggers = false</c>），接触 / 触发回调不可靠。
    /// 与 <c>ContactSensor</c> / <c>PushSensor</c> / <c>ImpactButton</c> 同一个做法。</para>
    ///
    /// <para><b>为什么用脚本转角度、不用 HingeJoint2D</b>：关节要让左腿「保持直立」得靠电机或
    /// 可断关节，既不好调、结果又不确定 —— 平台解谜不该掷骰子。三根棍子都是 Kinematic 刚体，
    /// 角度由状态机按时间表提交（<c>Rigidbody2D.MoveRotation</c>），和 <c>MovingPlatform</c>
    /// 用 <c>MovePosition</c> 走直线是同一套做法。</para>
    ///
    /// <para><b>顶梁的静止角是算出来的、不是手调的</b>：<c>asin((柱高 − 门槛厚) / 顶梁长)</c>，
    /// 保证梢端下缘正好落在倒下的左腿上、把左侧缝隙封死。改三根棍子的长度 / 位置，
    /// 角度会自己跟上（见 <see cref="ComputeGeometry"/>）。</para>
    ///
    /// <para>⚠️ <b>几何前提</b>：顶梁长（＝两腿间距）必须<b>大于</b>柱高，否则顶梁转到底会贴着
    /// 右柱变成竖直、根本不成三角形。<see cref="OnValidate"/> 会就此报警告。</para>
    ///
    /// <para>⚠️ <b>不支持带旋转的根物体</b>：角度按轴对齐算，方向直接当世界方向用
    /// （与 <c>ImpactButton</c> / <c>BreakableGate</c> 同一约定）。</para>
    ///
    /// <para>⚠️ <b>不参与时间回溯</b>：与其余地图组件（<c>MovingPlatform</c> / <c>PushableBody</c>）
    /// 一致，没挂 <c>ISnapshotParticipant</c>。要让时间规则能把它倒回去，另说。</para>
    ///
    /// <para>白盒阶段三根棍子都是纯色方块 + 汉字标签，接正式美术时清空 <see cref="_label"/> 即可
    /// —— 玩法判定不读它。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TrestleTrap : MonoBehaviour
    {
        /// <summary>现在走到哪一步了。</summary>
        public enum Phase
        {
            /// <summary>三棍搭好、顶梁水平，可以站人。</summary>
            Standing = 0,

            /// <summary>左腿正在绕底铰链倒向内侧。</summary>
            LegFalling = 1,

            /// <summary>顶梁正在绕右柱顶铰链下摆。</summary>
            PlankFalling = 2,

            /// <summary>三角斜坡成型、笼已封死（顶梁可能已被锁成 Static）。</summary>
            Sealed = 3,
        }

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("触发（质量闸门）")]
        [Tooltip("顶倒左腿所需的质量（kg）。参考：玩家 1 · 铁箱 8；\n" +
                 "「质量 × 削弱」把铁箱压到 2.4 · 「质量 × 增强」把玩家（挂了 RuleAffectedBody 时）压到 3。\n" +
                 "默认 2.5 正好卡在「削弱铁箱」与「增强玩家」之间。")]
        [SerializeField, Min(0f)] private float _toppleMass = 2.5f;

        [Tooltip("要持续顶住多久（秒）才算数。防止单帧擦碰误触发。")]
        [SerializeField, Min(0f)] private float _contactHold = 0.08f;

        [Tooltip("谁顶得动它。默认勾了 Default（玩家）+ Ground（铁箱等动态物）+ Enemy（敌人）。\n" +
                 "⚠️ 实体在哪个层就要勾哪个层 —— 漏了它就在探测范围外，撞上去什么都不会发生。\n" +
                 "静态地形没有刚体，勾了也不会触发。")]
        [SerializeField] private LayerMask _probeLayer = 1 | (1 << 8) | (1 << 9);

        [Tooltip("探测盒在左腿表面之外还要往外扩多少（世界单位）。\n" +
                 "它决定「离多远就算顶上了」：留太大，实体还没碰到腿机关就倒了；\n" +
                 "留太小，贴上去的碰撞体可能探不到。0.15~0.2 刚好是「碰到才倒」。")]
        [SerializeField, Min(0f)] private float _probeReach = 0.18f;

        [Tooltip("探测盒罩住左腿从下往上多大比例（1 = 整根腿）。\n" +
                 "只认上段，免得地上的碎块 / 台阶把腿顶倒。")]
        [SerializeField, Range(0.05f, 1f)] private float _probeTopFraction = 0.75f;

        [Tooltip("只认**笼内**那一侧。默认取消（两侧都算）。\n" +
                 "标准玩法是「实体从左侧走过来顶倒左腿，然后自己走进笼里」—— 那要两侧都认。\n" +
                 "勾上 = 只有站在笼里的实体顶得倒它（留给「把怪引进来再顶」的变体）。")]
        [SerializeField] private bool _probeInnerSideOnly = false;

        [Tooltip("左腿被顶倒之后**不再挡路**（默认勾上，强烈建议保持）。\n" +
                 "⚠️ 实测：敌人的 Rigidbody2D.Slide **爬不过 0.28 的台阶**，\n" +
                 "倒下的左腿会像一堵墙一样把顶倒它的实体挡在笼外，进不去。\n" +
                 "勾上 = 倒下的左腿只是「视觉门槛」，碰撞体在开始倒的那一帧就关掉，实体能走进笼里；\n" +
                 "取消 = 保留碰撞体（只有本来就在笼里的实体才关得住）。")]
        [SerializeField] private bool _legBecomesPassable = true;

        [Header("倒塌")]
        [Tooltip("左腿倒下去用多久（秒）。")]
        [SerializeField, Min(0.01f)] private float _legFallDuration = 0.28f;

        [Tooltip("左腿倒到多少度（从竖直算起，倒向内侧）。\n" +
                 "90 = 平躺在地上当门槛；小于 90 会斜撑在笼里、把门槛抬高。")]
        [SerializeField, Range(1f, 100f)] private float _legFallAngle = 90f;

        [Tooltip("顶梁下摆用多久（秒）。比左腿慢一点，让「先倒腿、再落梁」读得出来。")]
        [SerializeField, Min(0.01f)] private float _plankFallDuration = 0.42f;

        [Tooltip("成型后把顶梁锁成 Static —— 否则笼里的东西能把斜坡顶起来跑掉。\n" +
                 "取消勾选 = 顶梁保持 Kinematic，留一个「理论上能被顶开」的口子。")]
        [SerializeField] private bool _lockWhenSealed = true;

        [Header("三根棍子（各自的轴心就是铰链）")]
        [Tooltip("左腿：轴心 = 底铰链，绕它倒向内侧。必须挂 Rigidbody2D + BoxCollider2D。")]
        [SerializeField] private Transform _leftLeg;

        [Tooltip("右腿：固定不动，只做静态碰撞体（不需要刚体）。")]
        [SerializeField] private Transform _rightPost;

        [Tooltip("顶梁：轴心 = 右柱顶铰链，绕它下摆。必须挂 Rigidbody2D + BoxCollider2D。")]
        [SerializeField] private Transform _plank;

        [Header("白盒")]
        [Tooltip("白盒标签。**接正式美术时把这个引用清空** —— 玩法判定不读它。")]
        [SerializeField] private TextMesh _label;

        [Header("调试")]
        [Tooltip("顶倒时在 Console 打一条：是谁、多少 kg、在哪。\n" +
                 "排查「没人碰它自己倒了」时打开 —— 它会直接告诉你推动者是谁（没有推动者时不会打）。")]
        [SerializeField] private bool _logTopple = true;

        [Header("成型时（可选接线）")]
        [Tooltip("笼封死那一刻要接的事情。把接收方拖进来即可，不需要写代码。")]
        [SerializeField] private UnityEvent _onSealed = new UnityEvent();

        // ==================================================================
        //  事件
        // ==================================================================

        /// <summary>左腿开始倒的瞬间（边沿）。参数是把它顶倒的那个实体，可能是 <c>null</c>。</summary>
        public event Action<GameObject> OnToppled;

        /// <summary>三角斜坡成型、笼封死的瞬间（边沿）。</summary>
        public event Action OnSealed;

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>现在走到哪一步了。</summary>
        public Phase CurrentPhase { get; private set; } = Phase.Standing;

        /// <summary>把左腿顶倒的那个实体（调试 / 表现用，可能是 <c>null</c>）。</summary>
        public GameObject LastPusher { get; private set; }

        /// <summary>它被判定时读到的质量（kg）。调试用：拿它对着阈值看差多少。</summary>
        public float LastPusherMass { get; private set; }

        /// <summary>顶倒所需的门槛质量（kg）。</summary>
        public float ToppleMass => _toppleMass;

        /// <summary>顶梁下摆到静止时的角度（度，正数）。由几何算出来，见 <see cref="ComputeGeometry"/>。</summary>
        public float PlankRestAngle => _plankRestAngle;

        /// <summary>两腿之间的跨距（＝顶梁长度，世界单位）。</summary>
        public float Span => _span;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _legBody;
        private Rigidbody2D _plankBody;
        private BoxCollider2D _legBox;
        private BoxCollider2D _plankBox;

        /// <summary>探测命中缓存。同时挤进左腿前两个以上活动物体的机会很小。</summary>
        private readonly Collider2D[] _hits = new Collider2D[8];

        /// <summary>本阶段已经走了多少秒。</summary>
        private float _elapsed;

        /// <summary>当前这个够重的实体已经顶了多久（秒）。</summary>
        private float _hold;

        private float _plankLength;
        private float _plankThickness;
        private float _sillHeight;
        private float _plankRestAngle;
        private float _span;

        /// <summary>笼在左腿的哪一侧：+1 = 右（右柱在右边），−1 = 左。</summary>
        private float _inward = 1f;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            Resolve();

            // 一开始一定是搭好的形态。
            ResetToStanding();
        }

        /// <summary>重开一局 / 对象池取出时也要回到搭好的形态，不能接着上一局的残骸。</summary>
        private void OnEnable() => ResetToStanding();

        private void FixedUpdate()
        {
            // 暂停：物理已停步进（Physics2D.simulationMode = Script），判定也跟着停 —— 保持当前形态。
            if (TimeManager.Instance?.IsPaused == true) return;

            // 本文件 using 了 RuleGame.Infrastructure.Time，它会遮蔽 UnityEngine.Time，所以这里全限定。
            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;

            switch (CurrentPhase)
            {
                case Phase.Standing:    TickStanding(dt);    break;
                case Phase.LegFalling:  TickLegFalling(dt);  break;
                case Phase.PlankFalling: TickPlankFalling(dt); break;
                case Phase.Sealed:      break;
            }
        }

        // ==================================================================
        //  ① 搭建：等一个够重的实体
        // ==================================================================

        private void TickStanding(float dt)
        {
            float mass = MeasureMass(out GameObject source);

            if (mass < _toppleMass)
            {
                _hold = 0f;
                return;
            }

            _hold += dt;
            if (_hold < _contactHold) return;

            Topple(source, mass);
        }

        /// <summary>把左腿顶倒。</summary>
        private void Topple(GameObject pusher, float mass)
        {
            LastPusher = pusher;
            LastPusherMass = mass;
            CurrentPhase = Phase.LegFalling;
            _elapsed = 0f;
            _hold = 0f;

            // 把左腿的碰撞体**当场**关掉：它倒下去就是躺在地上的一根 0.28 粗的棍子，
            // 而实体的 Slide 爬不过台阶（实测）—— 留着它就是一道把推动者挡在笼外的墙。
            // 关掉之后它只剩视觉（倒下的「门槛」），实体能走进笼里被顶梁关住。
            if (_legBecomesPassable && _legBox != null) _legBox.enabled = false;

            if (_logTopple)
            {
                Debug.Log("[TrestleTrap] 顶倒它的是 " + (pusher != null ? pusher.name : "<空>")
                    + "（" + mass.ToString("0.#") + " kg）@ " + transform.position, this);
            }

            OnToppled?.Invoke(pusher);
        }

        // ==================================================================
        //  ② 左腿倒 → ③ 顶梁下摆 → ④ 成型
        // ==================================================================

        private void TickLegFalling(float dt)
        {
            _elapsed += dt;
            float k = Mathf.Clamp01(_elapsed / _legFallDuration);

            // 二次曲线：越倒越快，读起来像被重力拽下去。
            // 左腿朝上伸：倒向内侧是**顺时针**，所以是负角。
            SetRotation(_legBody, -_legFallAngle * k * k, -_legFallAngle);

            if (k < 1f) return;

            SetRotation(_legBody, -_legFallAngle, -_legFallAngle);
            CurrentPhase = Phase.PlankFalling;
            _elapsed = 0f;
        }

        private void TickPlankFalling(float dt)
        {
            _elapsed += dt;
            float k = Mathf.Clamp01(_elapsed / _plankFallDuration);

            // 顶梁朝**左**伸：往下摆是**逆时针**，所以是正角（负角会把它抬上去）。
            SetRotation(_plankBody, _plankRestAngle * k * k, _plankRestAngle);

            if (k < 1f) return;

            Seal();
        }

        /// <summary>三角斜坡成型：把顶梁摆到静止角，并按配置锁死。</summary>
        private void Seal()
        {
            SetRotation(_plankBody, _plankRestAngle, _plankRestAngle);
            CurrentPhase = Phase.Sealed;

            if (_lockWhenSealed && _plankBody != null)
            {
                // 先停掉运动再换类型，免得切 Static 那一刻把残留速度当成一次位移。
                _plankBody.linearVelocity = Vector2.zero;
                _plankBody.angularVelocity = 0f;
                _plankBody.bodyType = RigidbodyType2D.Static;
            }

            OnSealed?.Invoke();
            _onSealed.Invoke();
        }

        /// <summary>把整套状态摆回「三棍搭好」。幂等 —— 重复调用没有副作用。</summary>
        private void ResetToStanding()
        {
            CurrentPhase = Phase.Standing;
            _elapsed = 0f;
            _hold = 0f;
            LastPusher = null;
            LastPusherMass = 0f;

            // 锁过的那一根要放回 Kinematic，否则下一局它转不动。
            RestoreBody(_legBody);
            RestoreBody(_plankBody);

            // 被顶倒时关掉的碰撞体要还回来（否则下一局左腿是根穿得过去的空气）。
            if (_legBox != null) _legBox.enabled = true;

            // ⚠️ 用 rotation 直接落位、不用 MoveRotation：MoveRotation 要等下一个物理步才生效，
            //    而这里需要「立刻」是搭好的样子（OnEnable 里下一步可能就是别人的探测）。
            ResetRotation(_legBody);
            ResetRotation(_plankBody);
        }

        private static void RestoreBody(Rigidbody2D body)
        {
            if (body == null) return;

            // ⚠️ 顺序：先把类型改回 Kinematic 再写速度。封锁之后顶梁是 **Static**，
            //    而 Static 刚体上读/写 linearVelocity 会报「Cannot use 'linearVelocity' on a static body」。
            body.bodyType = RigidbodyType2D.Kinematic;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.gravityScale = 0f;
        }

        private static void ResetRotation(Rigidbody2D body)
        {
            if (body == null) return;

            body.rotation = 0f;
        }

        /// <summary>
        /// 把某一根棍子摆到指定角度，并**夹在 [−limit, 0]** 之间。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ <b>不用 <c>MoveRotation</c></b>：它是「算一个角速度、让下一个物理步转过去」，
        /// 会**过冲** —— 最后一帧落到哪个角度不可控。这里每帧只有一个目标角，直接写
        /// <c>rotation</c> 落位最稳，也顺带把角度夹住。</para>
        /// <para>⚠️ <b><paramref name="limit"/> 与目标角同号</b>，两者都把「逆时针为正」当基准：
        /// 左腿朝上伸，倒向内侧是**顺时针 → 负**；顶梁朝左伸，落下来是**逆时针 → 正**。
        /// 给左腿传负数、顶梁传正数。</para>
        /// </remarks>
        private static void SetRotation(Rigidbody2D body, float degrees, float limit)
        {
            if (body == null) return;

            float lo = Mathf.Min(0f, limit);
            float hi = Mathf.Max(0f, limit);
            body.rotation = Mathf.Clamp(degrees, lo, hi);
        }

        // ==================================================================
        //  探测
        // ==================================================================

        /// <summary>
        /// 扫一次左腿上段的探测盒，返回里面**最重**的那个刚体的质量。
        /// </summary>
        /// <param name="source">最重的那个实体（可能是 <c>null</c>）。</param>
        private float MeasureMass(out GameObject source)
        {
            source = null;

            if (_legBox == null) return 0f;

            ProbeGeometry(out Vector2 center, out Vector2 size);

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_probeLayer);

            int count = Physics2D.OverlapBox(center, size, 0f, filter, _hits);
            float best = 0f;

            for (int i = 0; i < count; i++)
            {
                Collider2D col = _hits[i];
                if (col == null) continue;

                // 别把三根棍子自己算进来：探测盒贴着它们，浮点误差可能蹭到。
                if (col.transform.IsChildOf(transform)) continue;

                Rigidbody2D body = col.attachedRigidbody;
                if (body == null) continue;                                   // 静态地形：没有刚体
                if (body.bodyType == RigidbodyType2D.Static) continue;        // 静态地形：没有质量可言

                if (body.mass <= best) continue;

                best = body.mass;
                source = body.gameObject;
            }

            return best;
        }

        /// <summary>
        /// 探测盒：罩住左腿<b>上段</b>，并往两侧各扩 <see cref="_probeReach"/>。
        /// </summary>
        /// <remarks>
        /// 只认上段有两个原因：① 倒下的腿、地上的碎块不该把腿顶倒；
        /// ② 贴着腿站的人，身体中上段一定在探测盒里，而下段可能被台阶挡住。
        /// 两侧都扩是因为「从哪一侧顶」不影响结果 —— 左腿一律倒向内侧。
        /// </remarks>
        private void ProbeGeometry(out Vector2 center, out Vector2 size)
        {
            Bounds bounds = LegBounds();
            float height = bounds.size.y * _probeTopFraction;
            float halfWidth = bounds.size.x * 0.5f;
            float y = bounds.max.y - height * 0.5f;

            if (!_probeInnerSideOnly)
            {
                center = new Vector2(bounds.center.x, y);
                size = new Vector2(bounds.size.x + _probeReach * 2f, height);
                return;
            }

            // 只罩住笼内那一侧：从腿的**内侧表面**再往里扩 _probeReach。
            // 站在外面的实体整个探不到 —— 这正是「推倒它的人一定在笼里」的保证。
            float near = bounds.center.x + _inward * halfWidth;
            float far = near + _inward * Mathf.Max(0.05f, _probeReach);
            center = new Vector2((near + far) * 0.5f, y);
            size = new Vector2(Mathf.Abs(far - near), height);
        }

        /// <summary>
        /// 左腿的世界包围盒。<b>从碰撞体自己的 size / offset 算</b>，不读 <c>Collider2D.bounds</c> ——
        /// 编辑模式的 gizmo 在 Awake 之前就会跑（与 <c>ImpactButton</c> 同一处理）。
        /// </summary>
        private Bounds LegBounds()
        {
            if (_legBox == null) return new Bounds(transform.position, Vector3.zero);

            Vector3 scale = _leftLeg != null ? _leftLeg.lossyScale : Vector3.one;
            var size = new Vector2(Mathf.Abs(_legBox.size.x * scale.x), Mathf.Abs(_legBox.size.y * scale.y));
            var center = (Vector2)_legBox.transform.TransformPoint(_legBox.offset);
            return new Bounds(center, size);
        }

        // ==================================================================
        //  几何
        // ==================================================================

        /// <summary>
        /// 从三根棍子的实际尺寸 / 位置把「顶梁该转到多少度」算出来。
        /// </summary>
        /// <remarks>
        /// <para>约束是「顶梁梢端的下缘正好落到倒下的左腿上」：设顶梁长 <c>A</c>、半厚 <c>B</c>、
        /// 铰链到门槛顶面的高差 <c>C</c>，则 <c>A·sinθ + B·cosθ = C</c>。
        /// 解出来 <c>θ = asin(C / √(A²+B²)) − atan2(B, A)</c>。</para>
        /// <para>门槛高 = 左腿平躺后的<b>半厚</b>（轴心在底端，倒 90° 后有一半埋在地面线以下）。</para>
        /// </remarks>
        private void ComputeGeometry()
        {
            if (_leftLeg == null || _plank == null || _legBox == null || _plankBox == null) return;

            Vector3 legPivot = _leftLeg.localPosition;
            Vector3 plankPivot = _plank.localPosition;

            _span = plankPivot.x - legPivot.x;
            _inward = _span >= 0f ? 1f : -1f;

            Vector3 plankScale = _plank.lossyScale;
            Vector3 legScale = _leftLeg.lossyScale;

            _plankLength = Mathf.Abs(_plankBox.size.x * plankScale.x);
            _plankThickness = Mathf.Abs(_plankBox.size.y * plankScale.y);
            _sillHeight = Mathf.Abs(_legBox.size.x * legScale.x) * 0.5f;

            if (_plankLength <= 0.0001f)
            {
                _plankRestAngle = 0f;
                return;
            }

            float c = (plankPivot.y - legPivot.y) - _sillHeight;
            float half = _plankThickness * 0.5f;
            float r = Mathf.Sqrt(_plankLength * _plankLength + half * half);

            float ratio = Mathf.Clamp(c / r, -1f, 1f);
            float phi = Mathf.Atan2(half, _plankLength);

            // ⚠️ Asin / Atan2 给的是**弧度**，而 Rigidbody2D.MoveRotation 与 Gizmos 要的是**角度**。
            //    这里必须换算 —— 漏掉的话顶梁只会转 0.43°（＝24.5° 的弧度值当角度用），
            //    表现就是「腿倒了、顶梁纹丝不动」，斜坡根本不成形。
            _plankRestAngle = (Mathf.Asin(ratio) - phi) * Mathf.Rad2Deg;
        }

        // ==================================================================
        //  装配 / 校验
        // ==================================================================

        /// <summary>缓存三根棍子的刚体与碰撞体，并算一次几何。</summary>
        private void Resolve()
        {
            _legBox = _leftLeg != null ? _leftLeg.GetComponent<BoxCollider2D>() : null;
            _plankBox = _plank != null ? _plank.GetComponent<BoxCollider2D>() : null;

            _legBody = _leftLeg != null ? _leftLeg.GetComponent<Rigidbody2D>() : null;
            _plankBody = _plank != null ? _plank.GetComponent<Rigidbody2D>() : null;

            if (_leftLeg == null || _rightPost == null || _plank == null)
            {
                Debug.LogError("[TrestleTrap] 三根棍子的引用没接全 —— 组件不会动。" +
                               "把 LeftLeg / RightPost / Plank 三个子物体分别拖进对应栏位。", this);
                return;
            }

            if (_legBody == null || _plankBody == null)
            {
                Debug.LogError("[TrestleTrap] 左腿与顶梁都必须挂 Rigidbody2D（Kinematic），" +
                               "否则它们转不起来。", this);
            }

            if (_legBox == null || _plankBox == null)
            {
                Debug.LogError("[TrestleTrap] 左腿与顶梁都必须挂 BoxCollider2D —— " +
                               "探测盒与静止角都是按它们的尺寸算的。", this);
            }

            ComputeGeometry();
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>白盒标签上该写什么。生成工具与 Inspector 共用它，免得两处写歪。</summary>
        public static string LabelText(float toppleMass)
        {
            return "三棍陷阱 ≥" + toppleMass.ToString("0.#") + "kg";
        }

        private void OnValidate()
        {
            if (_leftLeg != null && _plank != null)
            {
                _legBox = _leftLeg.GetComponent<BoxCollider2D>();
                _plankBox = _plank.GetComponent<BoxCollider2D>();
                ComputeGeometry();

                // 顶梁长必须大于柱高，否则转到底会贴着右柱变成竖直、根本不成三角形。
                if (_plankRestAngle <= 0.5f)
                {
                    Debug.LogWarning(
                        "[TrestleTrap] 顶梁长（＝两腿间距）不大于柱高 —— 顶梁转到底会贴着右柱，" +
                        "形不成三角形斜坡。把右腿往外挪、或把柱子降低。", this);
                }
            }

            RefreshLabel();
        }

        /// <summary>把阈值写进白盒标签。**只在文字真的变了才写** ——
        /// 否则每次脚本重编译都会把场景标脏，提交里全是噪声。</summary>
        private void RefreshLabel()
        {
            if (_label == null) return;

            string wanted = LabelText(_toppleMass);
            if (_label.text != wanted) _label.text = wanted;
        }

        private void OnDrawGizmos()
        {
            if (_legBox == null) return;

            // 实心的那一块：左腿本体。
            Bounds leg = LegBounds();
            Gizmos.color = new Color(0.85f, 0.55f, 0.25f, 0.9f);
            Gizmos.DrawWireCube(leg.center, leg.size);

            // 探测区：罩住上段、两侧各扩一截。
            ProbeGeometry(out Vector2 center, out Vector2 size);
            Gizmos.color = new Color(1f, 0.75f, 0.28f, 0.45f);
            Gizmos.DrawWireCube(center, size);

            if (_plank == null || _plankRestAngle <= 0.01f) return;

            // 顶梁下摆的终点：梢端会落在哪。
            Vector3 pivot = _plank.position;
            float a = _plankRestAngle * Mathf.Deg2Rad;
            var tip = new Vector2(pivot.x - _plankLength * Mathf.Cos(a), pivot.y - _plankLength * Mathf.Sin(a));

            Gizmos.color = new Color(0.4f, 0.75f, 1f, 0.9f);
            Gizmos.DrawLine(pivot, tip);

            // 三角形：地面 + 右腿 + 顶梁。
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.5f);
            Gizmos.DrawLine(tip, new Vector2(tip.x, transform.position.y));
            Gizmos.DrawLine(new Vector2(tip.x, transform.position.y), new Vector2(pivot.x, transform.position.y));
        }
    }
}
