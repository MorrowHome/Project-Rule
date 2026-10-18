using System;
using UnityEngine;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 受击回弹块：**被攻击打得沿自己的轨道退开、随后自己慢慢回正**的场景组件。
    /// </summary>
    /// <remarks>
    /// <para><b>它只在自己的轨道上走</b>：轨道是一条**过锚点的直线**，由
    /// <see cref="_trackFrom"/> / <see cref="_trackTo"/>（相对锚点的世界偏移）定方向与范围。
    /// 位置永远写成 <c>锚点 + 轴 × 偏移</c>，偏移被夹在轨道两端之间 ——
    /// 打不飞、推不歪、也掉不下去。垂直轨道的分量会被**吃掉**，不会把块推离轨道。</para>
    ///
    /// <para><b>它做三件事</b>：① 吃击退（<see cref="IDamageable"/>，只读 <c>Knockback</c>，
    /// 投影到轨道轴上）；② 沿轨道滑出去，被指数衰减停住；③ 惯性散完之后，
    /// 每物理帧朝**锚点**挪一点，直到回到原位。</para>
    ///
    /// <para>⚠️ <b>回正只在惯性散完之后开始</b>（<c>_impulse</c> 归零）。
    /// 这样「被打飞多远」只由击退决定、摩擦只改「回来得多快」，两个效果互不污染 ——
    /// 如果边飞边拉，高摩擦时回正力几乎不刹击退，块反而被推得**更远**（反直觉）。</para>
    ///
    /// <para><b>摩擦就是回正的分母</b>：回正速度 = <c>_returnSpeed ÷ 摩擦倍率</c>
    /// （倍率夹在 <c>_minReturnMultiplier</c> ~ <c>_maxReturnMultiplier</c> 之间）。
    /// 摩擦倍率读的是 <see cref="IAttributeManager.Values"/> 的 <c>FrictionScale</c> ——
    /// 与 <c>RuleAffectedBody</c> 改材质摩擦读的是**同一个源**，不是另起一套。</para>
    ///
    /// <para>⚠️ <b>为什么不直接用引擎摩擦（Dynamic 刚体 + 弹簧力）</b>：</para>
    /// <list type="number">
    /// <item>这类块**悬在竖井里**，脚下没有接触面，引擎摩擦根本不参与 —— 规则对它无效。</item>
    /// <item>就算贴着地，静摩擦上限 ≈ <c>μ·m·g</c> 会随「摩擦 × 增强」涨到 3 倍以上。
    /// 回正力小于它时块会**卡死在偏移位置**、再也回不来 —— 谜题直接锁死。</item>
    /// </list>
    /// <para>所以这里选 **Kinematic + 自己积分 + 轨道约束**（与 <c>MovingPlatform</c> /
    /// <c>SpringPlatform</c> 同一路线），摩擦只作为「世界参数」被读进来。</para>
    ///
    /// <para>⚠️ <b>它不掉血、也不会死</b>：<see cref="IDamageable"/> 只消费 <c>Knockback</c>，
    /// <c>Amount</c> 被直接忽略 —— 它是机关，不是敌人（没有 <c>EntityStatus</c>、没有无敌帧、
    /// 不受伤害挂账影响；挂账期间伤害记 0、击退照样下发）。</para>
    ///
    /// <para>⚠️ <b>要能被打到，必须有受击箱</b>：命中判定只查 <c>Hurtbox</c> 层（10）。
    /// 菜单 ⑭ 生成的预制体自带一个 <c>Hurtbox</c> 子物体；手工搭的话记得补
    /// （<c>Tools/规则游戏/受击箱/①</c>），否则「砍上去毫无反应」。</para>
    ///
    /// <para>⚠️ <b>摩擦 × 反转不响应</b>：反转的语义是「摩擦力的**方向**反过来」，
    /// 只对**有移动意图**的实体成立（玩家 / 敌人控制器把它读成「按右往左走」）。
    /// 回弹块没有意图，把回正方向反过来等于「越推越远」，没有意义 ——
    /// 与 <c>RuleAffectedBody</c> 对动态物体的处理一致，这里只消费倍率。</para>
    ///
    /// <para><b>暂停 / 时间规则</b>：暂停时 <c>TimeManager.IsPaused</c> 早退（位置冻结）；
    /// 积分用 <c>Time.fixedDeltaTime</c>，它随 <c>Time.timeScale</c> 缩放。</para>
    ///
    /// <para>⚠️ <b>不参与时间回溯</b>：与其余地图组件（<c>MovingPlatform</c> / <c>PushableBody</c>）一致。</para>
    ///
    /// <para><b>站在上面的人会被带走</b>：本类实现 <see cref="IMovingSurface"/>，
    /// 承载方（<c>ContactSensor</c> → <c>MotionMover</c>）每个物理帧把 <see cref="Velocity"/>
    /// 加进自己的位移里。</para>
    ///
    /// <para>用法：菜单 <c>Tools/规则游戏/地图组件/⑭ 回弹块（轨道内受击击退 + 缓慢回正）</c> 生成
    /// <c>Prefabs/SpringBackBody.prefab</c>，关卡里拖任意多个。</para>
    /// </remarks>
    [ExecuteAlways]
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public sealed class SpringBackBody : MonoBehaviour, IDamageable, IMovingSurface
    {
        /// <summary>摩擦倍率的下限：低于它一律当它算（避免除以 0，也避免「失效」把速度顶到天上）。</summary>
        private const float MinFrictionScale = 0.25f;

        /// <summary>击退速度低于它就直接归零，免得永远剩一点浮点残速、块一直在抖。</summary>
        private const float KnockbackCutoff = 0.02f;

        /// <summary>轨道白盒的粗细（世界单位）。</summary>
        private const float TrackThickness = 0.08f;

        /// <summary>短于这个长度的轨道算退化（当作不会动）。</summary>
        private const float MinTrackLength = 0.0001f;

        // ==================================================================
        //  配置
        // ==================================================================

        [Header("锚点（回正目标）")]
        [Tooltip("回正的目标位置（世界坐标）。勾了下面的「锚点 = 初始位置」时，运行时会用物体自己的位置覆盖它。")]
        [SerializeField] private Vector2 _anchor = Vector2.zero;

        [Tooltip("勾上：锚点 = 物体在这个场景里的初始位置（Awake 时记一次）—— 预制体拖进场景就能直接用，不用填坐标。\n" +
                 "取消：用上面手填的世界坐标（想让块回到「不是它出生点」的地方时才需要）。")]
        [SerializeField] private bool _anchorAtSpawn = true;

        [Header("轨道（相对锚点的偏移）")]
        [Tooltip("轨道这一端相对锚点的偏移（世界单位）。和下一栏一起定**方向 + 范围**。\n" +
                 "默认 (−1.5, 0) / (1.5, 0)：水平轨道，左右各走 1.5 格。\n" +
                 "竖直轨道填 (0, −1.5) / (0, 1.5)；只往一边走就两端同号（等号上的那一端就是能走到的极限）。")]
        [SerializeField] private Vector2 _trackFrom = new Vector2(-1.5f, 0f);

        [Tooltip("轨道另一端相对锚点的偏移（世界单位）。\n" +
                 "⚠️ 两个端点在**过锚点的那条直线上**才有意义：垂直方向的分量会被忽略（块永远在过锚点的线上）。")]
        [SerializeField] private Vector2 _trackTo = new Vector2(1.5f, 0f);

        [Header("击退（挨打）")]
        [Tooltip("击退冲量的倍率。1 = 原样吃攻击的击退值（玩家轻击 6 / 重击 12，敌人 7）。")]
        [SerializeField, Min(0f)] private float _knockbackScale = 1f;

        [Tooltip("沿轨道击退速度的上限（单位/秒）。防止重击把它一下顶到轨道尽头。")]
        [SerializeField, Min(0f)] private float _maxKnockbackSpeed = 14f;

        [Tooltip("击退速度的衰减（每秒，指数）。越大越「一下就打住」，越小越滑得远。\n" +
                 "默认 6：轻击（6）大约沿轨道推出去 0.9 格，重击（12）推出去 1.9 格。")]
        [SerializeField, Min(0.1f)] private float _knockbackDrag = 6f;

        [Tooltip("击退方向的抬升量：1 = 纯向上，0 = 纯水平（与 EntityDamageable 同一约定）。\n" +
                 "抬升的「上」跟着重力走。**竖直轨道主要靠它推动** —— 横向攻击在竖直轨道上的分量\n" +
                 "只有这一份，想让竖直轨道上的块被顶得明显，把它调大（0.6~1）。")]
        [SerializeField, Range(0f, 1f)] private float _knockbackLift = 0.3f;

        [Header("回正（自己回来）")]
        [Tooltip("摩擦正常时的回正速度（单位/秒）。1.2 ≈ 每秒回一格多一点，读起来是「缓慢」。\n" +
                 "实际速度 = 它 ÷ 摩擦倍率（再夹上下限），见下面「摩擦」一栏。")]
        [SerializeField, Min(0f)] private float _returnSpeed = 1.2f;

        [Tooltip("离锚点这么近时开始减速（单位）：越近越慢，末尾不会「啪」地一下停住。\n" +
                 "0 = 一直匀速走到锚点（会显得很硬）。")]
        [SerializeField, Min(0f)] private float _returnEaseDistance = 0.5f;

        [Tooltip("离锚点这么近、击退惯性也没了，就直接吸到锚点上（单位）。")]
        [SerializeField, Min(0.001f)] private float _settleDistance = 0.02f;

        [Header("摩擦（规则）")]
        [Tooltip("摩擦很大时的回正速度下限倍率。默认 0.25 = 「摩擦 × 增强（×3）」时速度降到 1/3，\n" +
                 "而「× 失效」这类极端值最多把它压到 1/4，不会慢到不动。")]
        [SerializeField, Range(0.05f, 1f)] private float _minReturnMultiplier = 0.25f;

        [Tooltip("摩擦很小时的回正速度上限倍率。默认 3 = 「摩擦 × 削弱（×0.3）」时快 3 倍（封顶），\n" +
                 "不会快到看着像瞬移。")]
        [SerializeField, Range(1f, 8f)] private float _maxReturnMultiplier = 3f;

        [Header("白盒")]
        [Tooltip("轨道白盒条。按当前方向 / 长度自动摆好，**接正式美术时清空它** —— 玩法判定不读它。")]
        [SerializeField] private Transform _trackVisual;

        // ==================================================================
        //  事件
        // ==================================================================

        /// <summary>挨了一下（边沿）。参数是沿轨道实际加进来的冲量（单位/秒）。</summary>
        /// <remarks>垂直命中时轨道上没动，参数是零向量 —— 但它确实挨了一下，表现层仍可以放受击特效。</remarks>
        public event Action<Vector2> OnKnockedBack;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Rigidbody2D _body;

        private IRuleSystem _rules;

        /// <summary>轨道轴（单位向量，世界方向）。</summary>
        private Vector2 _axis = Vector2.right;

        /// <summary>沿轨道能走到的两个极限（相对锚点的有符号距离，<c>_minOffset ≤ 0 ≤ _maxOffset</c>）。</summary>
        private float _minOffset;
        private float _maxOffset;

        /// <summary>轨道可走的长度（= <c>_maxOffset − _minOffset</c>）。</summary>
        private float _trackLength;

        /// <summary>当前位置（世界坐标）。自己记，不读 <c>_body.position</c>：MovePosition 要到物理步进时才生效。</summary>
        private Vector2 _position;

        /// <summary>沿轨道的有符号偏移（相对锚点）。唯一的自由度。</summary>
        private float _offset;

        /// <summary>沿轨道的击退惯性（单位/秒，有符号），按 <see cref="_knockbackDrag"/> 指数衰减。</summary>
        private float _impulse;

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>锚点（世界坐标）。回正的目标，也是轨道的中点参考。</summary>
        public Vector2 Anchor => _anchor;

        /// <summary>轨道这一端的偏移（相对锚点）。</summary>
        public Vector2 TrackFrom => _trackFrom;

        /// <summary>轨道另一端的偏移（相对锚点）。</summary>
        public Vector2 TrackTo => _trackTo;

        /// <summary>轨道轴（世界方向，单位向量）。运行时才有效。</summary>
        public Vector2 TrackAxis => _axis;

        /// <summary>沿轨道可走的长度（单位）。</summary>
        public float TrackLength => _trackLength;

        /// <summary>当前离锚点多远（单位）。调试 / 表现用。</summary>
        public float Displacement => Mathf.Abs(_offset);

        /// <summary>现在是不是被打歪了（还没回到锚点）。</summary>
        public bool IsDisplaced => Displacement > _settleDistance;

        /// <summary>当前的回正速度倍率（摩擦规则换算出来的那个）。</summary>
        public float ReturnMultiplier => ComputeReturnMultiplier();

        /// <summary>本物理帧的速度（单位/秒）。踩在上面的实体读它，见 <see cref="IMovingSurface"/>。</summary>
        public Vector2 Velocity { get; private set; }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            // 编辑模式（[ExecuteAlways]）只跑白盒刷新，不做任何物理初始化。
            if (!Application.isPlaying) return;

            _body = GetComponent<Rigidbody2D>();
            ConfigureBody();

            if (_anchorAtSpawn) CaptureAnchor();

            RebuildTrack();
            ResetToRest();
        }

        private void OnEnable()
        {
            // 编辑模式：只把白盒条摆正（关卡里会把块拖大，条要跟着缩放重算）。
            if (!Application.isPlaying)
            {
                UpdateTrackVisual();
                return;
            }

            // 重开一局 / 对象池取出：回到锚点，不能接着上一局的偏移。
            if (_body == null)
            {
                _body = GetComponent<Rigidbody2D>();
                ConfigureBody();
                if (_anchorAtSpawn) CaptureAnchor();
            }

            // ⚠️ 锚点**只在 Awake 记一次**，别在这里重记：分区卸载后重新启用时，
            //    物体还停在被打歪的位置上（MovePosition 会同步 Transform），
            //    这时再记等于把锚点搬到偏移处 —— 它就永远回不到原位了。
            _rules = RuleSystem.Instance;

            RebuildTrack();
            ResetToRest();
        }

        private void OnDisable()
        {
            _impulse = 0f;
            Velocity = Vector2.zero;
        }

        /// <summary>
        /// 编辑模式的实时预览：块被拖大 / 改轨道时，白盒条立刻跟着重算。
        /// </summary>
        /// <remarks>
        /// 只在编辑模式跑；运行时白盒由 <see cref="FixedUpdate"/> 每步刷新。
        /// <see cref="UpdateTrackVisual"/> 内部对「值没变」做了短路，所以不会每帧把场景标脏。
        /// </remarks>
        private void Update()
        {
            if (Application.isPlaying) return;
            UpdateTrackVisual();
        }

        private void FixedUpdate()
        {
            // 暂停：物理已停步进（Physics2D.simulationMode = Script），位置也冻结。
            if (TimeManager.Instance?.IsPaused == true)
            {
                Velocity = Vector2.zero;
                return;
            }

            // 注意：本文件 using 了 RuleGame.Infrastructure.Time，它会遮蔽 UnityEngine.Time。
            float dt = UnityEngine.Time.fixedDeltaTime;
            if (dt <= 0f) return;

            _offset = StepOffset(
                _offset, ref _impulse, dt,
                _knockbackDrag, _returnSpeed, ComputeReturnMultiplier(),
                _returnEaseDistance, _settleDistance, _minOffset, _maxOffset,
                out float axisSpeed);

            _position = _anchor + _axis * _offset;
            Velocity = _axis * axisSpeed;

            _body.MovePosition(_position);
            UpdateTrackVisual();
        }

        // ==================================================================
        //  运动（纯函数，编辑器自检直接调）
        // ==================================================================

        /// <summary>
        /// 沿轨道走一步：**击退惯性 → 回正 → 吸附 → 轨道限位**。纯函数（只改 <paramref name="impulse"/>）。
        /// </summary>
        /// <remarks>
        /// <para>整条运动是一维的：<c>offset</c> 是相对锚点的有符号距离，最后夹在
        /// [<paramref name="minOffset"/>, <paramref name="maxOffset"/>] 之间 —— 这就是「不能脱离轨道」。</para>
        /// <para>顺序有讲究：回正只在惯性散完之后才开始。这样「被打飞多远」只由击退决定、
        /// 摩擦只改「回来得多快」，两个效果各自干净。</para>
        /// </remarks>
        public static float StepOffset(
            float offset, ref float impulse, float dt,
            float drag, float returnSpeed, float returnMultiplier,
            float easeDistance, float settleDistance, float minOffset, float maxOffset,
            out float speed)
        {
            speed = 0f;
            if (dt <= 0f) return offset;

            // ① 击退惯性：指数衰减。衰减完就彻底归零，不留浮点尾巴。
            if (drag > 0f) impulse *= Mathf.Exp(-drag * dt);
            if (Mathf.Abs(impulse) <= KnockbackCutoff) impulse = 0f;

            // ② 先按惯性滑出去；**惯性没散完之前不拉回**。
            float next = offset + impulse * dt;

            if (impulse == 0f)
            {
                float toAnchor = -next;
                float distance = Mathf.Abs(toAnchor);

                if (distance > 0f)
                {
                    float v = returnSpeed * returnMultiplier;
                    if (easeDistance > 0f) v *= Mathf.Clamp01(distance / easeDistance);

                    if (v > 0f)
                    {
                        float step = Mathf.Min(v * dt, distance);
                        next += Mathf.Sign(toAnchor) * step;
                    }
                }
            }

            // ③ 吸附：惯性没了、又已经很近 —— 直接落到锚点，收尾干净。
            if (impulse == 0f && Mathf.Abs(next) <= settleDistance) next = 0f;

            // ④ 轨道限位：走到头就停住，不会飞出去。
            next = Mathf.Clamp(next, minOffset, maxOffset);

            speed = (next - offset) / dt;
            return next;
        }

        /// <summary>把一次击退投影到轨道轴上：**只有沿轨道的分量能推动块**。</summary>
        /// <param name="direction">击退方向（单位向量）。</param>
        /// <param name="axis">轨道轴（单位向量）。</param>
        /// <param name="strength">击退强度（单位/秒）。</param>
        public static float ProjectKnockback(Vector2 direction, Vector2 axis, float strength)
            => Vector2.Dot(direction, axis) * strength;

        // ==================================================================
        //  IDamageable：只吃击退，不掉血
        // ==================================================================

        /// <summary>
        /// 挨一次攻击：**只读击退**，<c>Amount</c> 直接忽略（机关不会被打死）。
        /// </summary>
        /// <remarks>
        /// <para>方向与 <c>EntityDamageable</c> 完全一致（水平背离施加方 + 沿重力反方向抬升），
        /// 共用同一个静态方法，免得两处手感各调各的。</para>
        /// <para>再把方向**投影到轨道**：垂直分量被吃掉，块不会离开轨道。</para>
        /// <para>同一帧被多次命中（因果扩散 / 连续挥砍）时**累加**冲量，再夹到上限。</para>
        /// </remarks>
        public void ApplyDamage(in DamageInfo damage)
        {
            if (!isActiveAndEnabled) return;

            float strength = damage.Knockback * _knockbackScale;
            if (strength <= 0f) return;

            Vector2 direction = EntityDamageable.ResolveKnockbackDirection(
                transform.position, damage.Attacker, _knockbackLift);

            float along = ProjectKnockback(direction, _axis, strength);
            _impulse = Mathf.Clamp(_impulse + along, -_maxKnockbackSpeed, _maxKnockbackSpeed);

            OnKnockedBack?.Invoke(_axis * along);
        }

        // ==================================================================
        //  内部
        // ==================================================================

        /// <summary>把当前摩擦规则换算成回正速度倍率：<c>1 ÷ 摩擦倍率</c>，夹在上下限之间。</summary>
        private float ComputeReturnMultiplier()
        {
            if (_rules == null) _rules = RuleSystem.Instance;

            IAttributeManager attributes = _rules != null ? _rules.Attributes : null;
            float scale = attributes != null ? attributes.Values.FrictionScale : 1f;

            return ReturnMultiplierFor(scale, _minReturnMultiplier, _maxReturnMultiplier);
        }

        /// <summary>
        /// 摩擦倍率 → 回正速度倍率（纯函数，编辑器验证器直接调它）。
        /// </summary>
        /// <remarks>
        /// <para><c>1 ÷ 摩擦倍率</c>，再夹在上下限之间：摩擦越大，分母越大，回正越慢。</para>
        /// <para>下限 <see cref="MinFrictionScale"/> 是「除以 0」的保险：摩擦失效（0）时
        /// 按 0.25 算，再被 <paramref name="maxMultiplier"/> 封顶，不会顶到无穷大。</para>
        /// </remarks>
        public static float ReturnMultiplierFor(float frictionScale, float minMultiplier, float maxMultiplier)
        {
            float scale = Mathf.Max(frictionScale, MinFrictionScale);
            return Mathf.Clamp(1f / scale, minMultiplier, maxMultiplier);
        }

        /// <summary>从两个端点的偏移算出轨道轴与两个极限。</summary>
        private void RebuildTrack()
        {
            Vector2 delta = _trackTo - _trackFrom;
            float length = delta.magnitude;

            if (length < MinTrackLength)
            {
                // 退化轨道：原地不动（但仍能挨打、仍会发事件）。
                _axis = Vector2.right;
                _minOffset = 0f;
                _maxOffset = 0f;
                _trackLength = 0f;
                return;
            }

            _axis = delta / length;

            float a = Vector2.Dot(_trackFrom, _axis);
            float b = Vector2.Dot(_trackTo, _axis);

            _minOffset = Mathf.Min(a, b);
            _maxOffset = Mathf.Max(a, b);
            _trackLength = _maxOffset - _minOffset;
        }

        private void CaptureAnchor() => _anchor = transform.position;

        /// <summary>回到锚点、清空所有惯性。开局 / 对象池取出 / 重开一局时调用。</summary>
        private void ResetToRest()
        {
            _impulse = 0f;
            _offset = Mathf.Clamp(0f, _minOffset, _maxOffset);
            _position = _anchor + _axis * _offset;
            Velocity = Vector2.zero;

            if (_body != null)
            {
                // 直接落位（不是 MovePosition）：下一步可能就是别人的探测，得「现在」就在锚点上。
                _body.position = _position;
            }

            UpdateTrackVisual();
        }

        private void ConfigureBody()
        {
            // Kinematic：位移完全由本类决定（而且被轨道夹死），重力 / 摩擦都不参与。
            _body.bodyType = RigidbodyType2D.Kinematic;
            _body.gravityScale = 0f;
            _body.linearDamping = 0f;
            _body.constraints |= RigidbodyConstraints2D.FreezeRotation;
            _body.sleepMode = RigidbodySleepMode2D.NeverSleep;

            // 定步长物理 + 高帧率渲染：不开插值回正会一格一格跳。
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // ==================================================================
        //  轨道白盒
        // ==================================================================

        /// <summary>白盒条要围绕的锚点：编辑模式用物体自己的位置（Awake 还没跑），运行时用记下来的锚点。</summary>
        private Vector2 DisplayAnchor =>
            !Application.isPlaying && _anchorAtSpawn ? transform.position : _anchor;

        /// <summary>
        /// 把白盒条摆到轨道上：中点、角度、长度都按当前配置算。
        /// </summary>
        /// <remarks>
        /// <para>条是**根的子物体**，而根自己会动，所以每帧写的是「世界中点 − 根位置」的局部坐标
        /// （与 <c>SpringPlatform</c> 的弹簧柱同一处理）。</para>
        /// <para>⚠️ 不支持带旋转的根物体：局部坐标按世界方向直接算（与其余地图组件一致）。</para>
        /// </remarks>
        private void UpdateTrackVisual()
        {
            if (_trackVisual == null) return;

            Vector2 from = _trackFrom;
            Vector2 to = _trackTo;
            Vector2 delta = to - from;
            float length = delta.magnitude;

            if (length < MinTrackLength)
            {
                // 退化轨道：把条缩成 0 藏起来，而不是关物体（关掉就再也开不回来了）。
                Vector3 hidden = new Vector3(0f, TrackThickness, 1f);
                if (_trackVisual.localScale != hidden) _trackVisual.localScale = hidden;
                return;
            }

            Vector2 axis = delta / length;
            float a = Vector2.Dot(from, axis);
            float b = Vector2.Dot(to, axis);
            float min = Mathf.Min(a, b);
            float max = Mathf.Max(a, b);

            Vector2 anchor = DisplayAnchor;
            Vector2 start = anchor + axis * min;
            Vector2 end = anchor + axis * max;
            Vector2 middle = (start + end) * 0.5f;

            // ⚠️ 运行时用**本帧的目标位置**（_position）而不是 transform.position：
            //    MovePosition 要到物理步进时才生效，用旧位置算会让白盒条每帧跟着块偏一下、再弹回来。
            Vector2 rootPosition = Application.isPlaying ? _position : (Vector2)transform.position;

            // ⚠️ 关卡里会把块**拖大**（根 scale ≠ 1，例如 8×2 的宽板）。子物体继承缩放，
            //    所以局部坐标 / 局部缩放都要先除掉根的缩放，否则条会变成几十个单位长、还偏到旁边去。
            Vector3 scale = transform.lossyScale;
            float scaleX = Mathf.Max(Mathf.Abs(scale.x), 0.0001f);
            float scaleY = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);

            Vector3 worldOffset = (Vector3)(middle - rootPosition);
            Vector3 localOffset = transform.InverseTransformVector(worldOffset);

            var wantedPosition = new Vector3(localOffset.x, localOffset.y, 0f);
            Quaternion wantedRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg);
            var wantedScale = new Vector3((max - min) / scaleX, TrackThickness / scaleY, 1f);

            // 只在真的变了才写 —— 编辑模式的 Update 每帧都会调到这里，
            // 无条件赋值会把场景一直标脏（与各组件「标签只在文字变了才写」同一处理）。
            if (_trackVisual.localPosition != wantedPosition) _trackVisual.localPosition = wantedPosition;
            if (_trackVisual.localRotation != wantedRotation) _trackVisual.localRotation = wantedRotation;
            if (_trackVisual.localScale != wantedScale) _trackVisual.localScale = wantedScale;
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：改轨道并立刻刷新白盒条。</summary>
        public void SetTrack(Vector2 from, Vector2 to)
        {
            _trackFrom = from;
            _trackTo = to;

            if (!Application.isPlaying) UpdateTrackVisual();
        }

        /// <summary>只给编辑器工具用：接上轨道白盒条。</summary>
        public void SetWhitebox(Transform trackVisual)
        {
            _trackVisual = trackVisual;
            UpdateTrackVisual();
        }

        private void OnValidate()
        {
            if (_minReturnMultiplier > _maxReturnMultiplier)
            {
                float swap = _minReturnMultiplier;
                _minReturnMultiplier = _maxReturnMultiplier;
                _maxReturnMultiplier = swap;
            }

            if (_settleDistance < 0.001f) _settleDistance = 0.001f;
            if (_returnEaseDistance < 0f) _returnEaseDistance = 0f;

            if (Application.isPlaying) return;
            UpdateTrackVisual();
        }

        private void OnDrawGizmos()
        {
            Vector2 anchor = DisplayAnchor;
            Vector2 delta = _trackTo - _trackFrom;
            float length = delta.magnitude;

            if (length < MinTrackLength) return;

            Vector2 axis = delta / length;
            float a = Vector2.Dot(_trackFrom, axis);
            float b = Vector2.Dot(_trackTo, axis);
            float min = Mathf.Min(a, b);
            float max = Mathf.Max(a, b);

            Vector2 start = anchor + axis * min;
            Vector2 end = anchor + axis * max;

            // 轨道：块的**全部**活动范围。
            Gizmos.color = new Color(1f, 0.62f, 0.22f, 0.95f);
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireSphere(start, 0.12f);
            Gizmos.DrawWireSphere(end, 0.12f);

            // 锚点：回正的目标。
            Gizmos.color = new Color(0.35f, 1f, 0.7f, 0.95f);
            Gizmos.DrawWireSphere(anchor, 0.18f);

            if (!Application.isPlaying) return;

            // 播放中：当前位置。
            Gizmos.color = new Color(1f, 0.95f, 0.3f, 1f);
            Gizmos.DrawWireSphere(_position, 0.2f);
        }
    }
}
