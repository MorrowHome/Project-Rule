using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 拼装重块：一块**很重的几何件**（三角块 / 圆球），要被规则撬动才能挪进凹位。
    /// </summary>
    /// <remarks>
    /// <para><b>它只做四件事</b>：① 把刚体配成「不睡 / 不隧穿」的石头；
    /// ② 收玩家推过来的**力**（<see cref="IPushable"/>）；③ 能站人（<see cref="IMovingSurface"/>）；
    /// ④ 被凹位 <see cref="FitSocket"/> 判定入位之后 <see cref="Seat"/> 成 Kinematic，永久固定。</para>
    ///
    /// <para><b>“推得动 / 推不动”由物理决定，本组件不设门槛</b>：玩家推 60 N，
    /// 静摩擦 = μ·m·g。180 kg、μ0.5 时是 883 N —— 纹丝不动；
    /// 「质量 × 失效」把 mass 压到 9 kg 之后 44 N &lt; 60 N —— 推得动。
    /// 也就是说：**这块件是被规则解锁的**，代码里没有任何“推重物”的特判。</para>
    ///
    /// <para><b>⚠️ 必须是 Dynamic 刚体</b>：只有 Dynamic 吃 <c>Physics2D.gravity</c> 与
    /// <c>Rigidbody2D.mass</c>。「重力 × 反转」「质量 × 失效」全靠这两条 —— 换成 Kinematic / Static，
    /// 这套机制整个失效。</para>
    ///
    /// <para><b>⚠️ 圆球不锁旋转、三角锁旋转</b>：圆滚起来才对；三角滚起来会翻个面，缺口就再也拼不上了。
    /// 三角的平移由玩家推，圆球的滚动由斜坡 + 摩擦规则带出来。</para>
    ///
    /// <para>⚠️ <b>必须放 <c>Ground</c> 层</b>（层 8）：玩家的 <c>Slide</c> 与接地探测只认这一层。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class FitBlock : MonoBehaviour, IPushable, IMovingSurface
    {
        [Header("形状")]
        [Tooltip("凹位只认与自己相同的形状。改这里的同时要换碰撞体（三角 = Polygon，圆 = Circle）。")]
        [SerializeField] private FitShape _shape = FitShape.Triangle;

        [Header("受力")]
        [Tooltip("取消勾选 = 这块件谁也推不动（只靠重力 / 滑落）。")]
        [SerializeField] private bool _canBePushed = true;

        [Header("掉出关卡")]
        [Tooltip("低于**出生点**多少米算掉出关卡（相对值，不是世界坐标）。本工程的关卡在 y≈-28 一带，写死绝对坐标会把整关判成已掉出。")]
        [SerializeField, Min(1f)] private float _killBelowSpawn = 30f;

        [Tooltip("掉出关卡后多久回到出生点（秒）。")]
        [SerializeField, Min(0f)] private float _respawnDelay = 2f;

        private static readonly System.Collections.Generic.List<FitBlock> Registry =
            new System.Collections.Generic.List<FitBlock>();

        private Rigidbody2D _body;
        private Vector3 _spawn;
        private float _killY;
        private bool _seated;
        private float _fellAt = -1f;

        /// <summary>
        /// 场上所有活着的拼装件。
        /// </summary>
        /// <remarks>
        /// 跳穿平台（<see cref="OneWayPlatform"/>）靠它逐个判断「件在实心面哪一侧」，
        /// 再对**这一对**碰撞体开关 <c>Physics2D.IgnoreCollision</c>。
        /// 平台自己不去 Find，件也不认识平台 —— 与 <c>IPushable</c> 同一套「放个接口、各自认领」的分工。
        /// </remarks>
        public static System.Collections.Generic.IReadOnlyList<FitBlock> All => Registry;

        // ==================================================================
        //  只读状态
        // ==================================================================

        /// <summary>形状标签。</summary>
        public FitShape Shape => _shape;

        /// <summary>已经入位并被固定成 Kinematic。</summary>
        public bool IsSeated => _seated;

        /// <summary>本件的刚体。凹位判定读它的位置与速度。</summary>
        public Rigidbody2D Body => _body;

        /// <summary>本物理帧的速度。站在它上面的人靠这个被带走。</summary>
        public Vector2 Velocity => _body != null ? _body.linearVelocity : Vector2.zero;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();

            _body.bodyType = RigidbodyType2D.Dynamic;                             // 受重力、受力
            _body.sleepMode = RigidbodySleepMode2D.NeverSleep;                    // 重力随时会被规则翻
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;   // 极轻时会被推得很快
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;           // 物理 50Hz，渲染更高

            if (_shape == FitShape.Triangle)
            {
                _body.constraints |= RigidbodyConstraints2D.FreezeRotation;       // 三角绝不翻滚
            }
            else
            {
                _body.constraints &= ~RigidbodyConstraints2D.FreezeRotation;      // 圆球要滚
            }

            _spawn = transform.position;
            _killY = _spawn.y - Mathf.Max(1f, _killBelowSpawn);   // 相对出生点，跟关卡在世界的哪个高度无关
        }

        private void OnEnable()
        {
            if (!Registry.Contains(this)) Registry.Add(this);
        }

        private void OnDisable()
        {
            Registry.Remove(this);
        }

        private void FixedUpdate()
        {
            if (_body == null || _seated) return;

            if (transform.position.y >= _killY)
            {
                _fellAt = -1f;
                return;
            }

            if (_fellAt < 0f)
            {
                _fellAt = Time.time;
                return;
            }

            if (Time.time - _fellAt >= _respawnDelay) Respawn();
        }

        // ==================================================================
        //  IPushable
        // ==================================================================

        /// <summary>
        /// 收下一个**力**（牛顿）。加速度 = 力 ÷ 自己的质量 —— 质量由质量规则决定。
        /// </summary>
        public void ReceivePush(Vector2 force)
        {
            if (_body == null || _seated || !_canBePushed) return;

            _body.WakeUp();
            _body.AddForce(force, ForceMode2D.Force);
        }

        // ==================================================================
        //  入位 / 复位
        // ==================================================================

        /// <summary>
        /// 入位：吸到座位锚点、清零速度、改成 Kinematic —— **拼装成果不可逆**，
        /// 之后再怎么翻重力 / 改质量都弄不散它。
        /// </summary>
        /// <param name="position">座位锚点的世界坐标（凹位自己决定）。</param>
        /// <param name="angleDegrees">座位角度。三角固定用它，圆球保留当前角度（没有“朝向”）。</param>
        /// <param name="parent">入位后挂到谁下面（可空）。凹位在会动的闸门上时要传，
        /// 否则闸门升走、件留在原地。</param>
        public void Seat(Vector2 position, float angleDegrees, Transform parent = null)
        {
            if (_seated || _body == null) return;

            _seated = true;

            if (parent != null) transform.SetParent(parent, true);

            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;
            _body.bodyType = RigidbodyType2D.Kinematic;

            if (_shape == FitShape.Round) angleDegrees = transform.eulerAngles.z;

            _body.position = position;
            transform.SetPositionAndRotation(
                new Vector3(position.x, position.y, transform.position.z),
                Quaternion.Euler(0f, 0f, angleDegrees));

            Physics2D.SyncTransforms();
        }

        /// <summary>回到出生点（掉出关卡时自动调用；测试里也可手动调）。</summary>
        public void Respawn()
        {
            if (_body == null) return;

            _fellAt = -1f;
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;
            _body.position = _spawn;
            transform.position = _spawn;
        }

        // ==================================================================
        //  供关卡工具 / 测试配置
        // ==================================================================

        /// <summary>设置形状标签（生成工具用；运行期改它不会换碰撞体）。</summary>
        public void ConfigureShape(FitShape shape) => _shape = shape;
    }
}
