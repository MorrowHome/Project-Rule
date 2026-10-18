using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 一块**单纯的石头**：Dynamic 刚体、吃重力，碰到「接触式开关」（CityButton / 摇杆）就按下去。
    /// </summary>
    /// <remarks>
    /// <para><b>它只做三件事</b>：① 把刚体配成「不睡 / 不翻滚 / 不隧穿」的石头；
    /// ② 碰到按钮就把「我被压到了」报给按钮（<see cref="IContactSwitch"/>）；
    /// ③ 收玩家推过来的**力**（<see cref="IPushable"/>）。</para>
    ///
    /// <para><b>“推得动 / 推不动”由摩擦决定，本组件不设任何门槛</b>：玩家推的是 60 N，
    /// 石头要动就得先克服静摩擦 <c>μ·m·g</c>。30 kg、μ = 0.5 时是 147 N &gt; 60 N ——
    /// **纹丝不动**；「摩擦 × 削弱」（μ = 0.15）把它压到 44 N &lt; 60 N —— **推得动**；
    /// 「摩擦 × 失效」（μ = 0）一推就滑。也就是说：**这块石头是被摩擦规则解锁的**，
    /// 代码里没有任何“推重物”的特判（与铁箱 <c>PushableBody</c> 同一条路线）。</para>
    ///
    /// <para><b>⚠️ 没有碎、没有剑、没有锁</b> —— 那些是 <c>GravityBoulder</c>（巨石断剑机关）的事。
    /// 想要会碎的巨石用菜单 ⑫；只想要一块吃重力、能撞开关的石头用 ⑬（菜单
    /// <c>Tools/规则游戏/地图组件/⑬ 巨石（纯重力石头，撞开关）</c>）。</para>
    ///
    /// <para><b>重力规则天然生效</b>：规则直接写 <c>Physics2D.gravity</c>，Dynamic 刚体自动吃 ——
    /// 一行特判都没有（与铁箱 <c>PushableBody</c> 同一条路线）。质量规则也照样生效，
    /// 因为 <c>RuleAffectedBody</c> 会把它写进 <c>Rigidbody2D.mass</c>。</para>
    ///
    /// <para><b>为什么用碰撞 / 触发回调</b>：它是 Dynamic 刚体，回调可靠。
    /// （玩家是 Kinematic + <c>Slide</c>，才需要自己扫探测盒。）</para>
    ///
    /// <para>⚠️ <b>必须放 <c>Ground</c> 层</b>（层 8）：玩家的 <c>Slide</c> 与接地探测只认这一层，
    /// 放错层就是一块穿得过去的空气。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class StoneBody : MonoBehaviour, IMovingSurface, IPushable
    {
        [Header("接触式开关")]
        [Tooltip("碰到 CityButton / 摇杆时按下去。取消勾选 = 它只是一块石头，不碰开关。")]
        [SerializeField] private bool _pressSwitches = true;

        private Rigidbody2D _body;

        // ==================================================================
        //  IMovingSurface：站在石头上的人跟着一起走
        // ==================================================================

        /// <summary>
        /// 本物理帧石头自己的速度。踩在它上面的人靠这个被带走。
        /// </summary>
        /// <remarks>它不是「新机制」，只是别让「石头被重力举起来、站在上面的人却留在原地」—— 那看着像 bug。</remarks>
        public Vector2 Velocity => _body != null ? _body.linearVelocity : Vector2.zero;

        // ==================================================================
        //  IPushable：玩家推过来的力
        // ==================================================================

        /// <summary>
        /// 收下一个**力**（牛顿）。加速度 = 力 ÷ 自己的质量 —— 质量由质量规则决定。
        /// </summary>
        /// <remarks>
        /// <para>⚠️ <b>这里只交力，不设门槛</b>：推不推得动是**物理**在回答
        /// （静摩擦 <c>μ·m·g</c> 挡住它），不是这里判「石头太重」。
        /// 于是「摩擦 × 削弱 / 失效」自动让它变轻推动 —— 本组件一行特判都没有。</para>
        /// <para><c>WakeUp</c> 是兜底：本组件把刚体设成不休眠，但别处若改回去，
        /// 睡着时施加的力会被丢掉。</para>
        /// </remarks>
        public void ReceivePush(Vector2 force)
        {
            if (_body == null) return;

            _body.WakeUp();
            _body.AddForce(force, ForceMode2D.Force);
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();

            // 与 PushableBody 同一套：受重力、不翻滚、不休眠、不隧穿。
            _body.bodyType = RigidbodyType2D.Dynamic;
            _body.constraints |= RigidbodyConstraints2D.FreezeRotation;
            _body.sleepMode = RigidbodySleepMode2D.NeverSleep;                   // 重力随时会被规则翻
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        // ==================================================================
        //  开关
        // ==================================================================

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision != null) Press(collision.collider);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            if (collision != null) Release(collision.collider);
        }

        private void OnTriggerEnter2D(Collider2D other) => Press(other);

        private void OnTriggerExit2D(Collider2D other) => Release(other);

        /// <summary>碰到接触式开关就按下去（按钮是 Trigger 也能中）。</summary>
        private void Press(Collider2D other)
        {
            if (!_pressSwitches || other == null) return;

            var target = other.GetComponentInParent<IContactSwitch>();
            if (target != null) target.PressByContact();
        }

        /// <summary>离开时通知一声；单次锁定的按钮会忽略它（按下去就锁住）。</summary>
        private void Release(Collider2D other)
        {
            if (!_pressSwitches || other == null) return;

            var target = other.GetComponentInParent<IContactSwitch>();
            if (target != null) target.ReleaseByContact();
        }
    }
}
