using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 可推重物（铁箱 / 货箱 / 石块）：**Dynamic 刚体**，受重力与质量影响，被顶住就能推走。
    /// </summary>
    /// <remarks>
    /// <para><b>本类只做三件事</b>：① 把刚体配成「不睡觉 / 不翻滚 / 不隧穿」的箱子；
    /// ② 实现 <see cref="IPushable"/>，把收到的力转交物理引擎；
    /// ③ 实现 <see cref="IMovingSurface"/>，站在箱子上的人跟着箱子一起走。</para>
    ///
    /// <para><b>质量与重力都不用本类管</b>：重力规则直接写全局 <c>Physics2D.gravity</c>，
    /// Dynamic 刚体天然吃；质量规则由 <c>RuleAffectedBody</c> 写进 <c>Rigidbody2D.mass</c>。
    /// 两个组件一起挂，「质量 × 削弱 → 一推就飞」「重力 × 反转 → 箱子往天上掉」
    /// 这些效果**一行额外代码都不用写**。</para>
    ///
    /// <para>⚠️ <b>必须放在 <c>Ground</c> 层</b>（层 8）：玩家的 <c>Rigidbody2D.Slide</c>、
    /// 接地探测、墙面探测都只认这一层 —— 放错层箱子就是一块穿得过去的空气。
    /// 代价是玩家也能在箱子侧面贴墙下滑 / 蹬墙跳（它就是一块会动的墙），这是预期行为。
    /// 接线工具见菜单 <c>Tools/规则游戏/地图组件/② 铁箱（可推重物）</c>。</para>
    ///
    /// <para>⚠️ <b>为什么不睡觉</b>（<see cref="RigidbodySleepMode2D.NeverSleep"/>）：
    /// Unity 2D 的刚体静止 0.5 秒后休眠，而**休眠的刚体不响应重力变化**。
    /// 本作的重力由规则随时翻转，一个睡着的箱子会在重力反转后原地悬空 —— 看起来就是 bug。
    /// 场上箱子数量很少，不休眠的开销可以忽略。</para>
    ///
    /// <para>⚠️ <b>为什么不翻滚</b>（<see cref="RigidbodyConstraints2D.FreezeRotation"/>）：
    /// 滚动会让解谜变成掷骰子（同一个操作两次结果不同），而且白盒 / 精灵是方的，
    /// 转起来立刻穿帮。要恢复翻滚把这个约束去掉即可。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PushableBody : MonoBehaviour, IPushable, IMovingSurface
    {
        private Rigidbody2D _body;

        // ==================================================================
        //  IMovingSurface
        // ==================================================================

        /// <summary>
        /// 本物理帧箱子的速度（单位 / 秒）。站在箱子上的实体靠它被带走。
        /// </summary>
        /// <remarks>
        /// 箱子是**被动承载**：它不主动推谁，唯一的效果是「踩在上面的人跟着一起走」。
        /// 动态刚体的速度就是 <c>linearVelocity</c>，不需要自己积分。
        /// </remarks>
        public Vector2 Velocity => _body != null ? _body.linearVelocity : Vector2.zero;

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();

            _body.bodyType = RigidbodyType2D.Dynamic;                            // 受重力、受力
            _body.constraints |= RigidbodyConstraints2D.FreezeRotation;          // 平台解谜不翻滚
            _body.sleepMode = RigidbodySleepMode2D.NeverSleep;                   // 重力随时会被规则翻
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;  // 极轻时会被推得很快
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;          // 物理 50Hz，渲染更高
        }

        // ==================================================================
        //  IPushable
        // ==================================================================

        /// <summary>
        /// 收下一个力（牛顿）。加速度 = 力 ÷ 本刚体的质量 —— 质量由质量规则决定。
        /// </summary>
        /// <remarks>
        /// <c>WakeUp</c> 是兜底：本组件把刚体设成不休眠，但运行期别处（或 Inspector 里改回去）
        /// 让它睡了的话，睡着时施加的力会被丢掉。醒来是幂等的，代价可以忽略。
        /// </remarks>
        public void ReceivePush(Vector2 force)
        {
            if (_body == null) return;

            _body.WakeUp();
            _body.AddForce(force, ForceMode2D.Force);
        }
    }
}
