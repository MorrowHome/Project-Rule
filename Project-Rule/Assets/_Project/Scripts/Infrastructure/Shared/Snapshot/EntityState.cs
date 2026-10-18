using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;

namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// 一个动态实体在快照时刻的状态。
    /// </summary>
    /// <remarks>
    /// <para><see cref="Entity"/> 持的是接口引用，恢复时用它找回刚体写回状态。
    /// 实体若已被销毁（没走「禁用」路线的漏网之鱼），恢复时跳过 —— 快照不负责复活已被
    /// <c>Destroy</c> 的对象，只负责恢复「禁用」的对象。</para>
    /// <para>动画状态用「状态 hash + 归一化时间」表达：恢复时 <c>Animator.Play(hash, 0, t)</c>，
    /// 不试图倒放动画帧 —— 够用且成本为零。</para>
    /// <para>⚠️ <b>激活状态就在本结构里</b>（<see cref="ActiveSelf"/>）——
    /// 名册的成员资格是生命周期，采集时禁用的对象也在名册里，所以每条记录都要带「当时是开是关」。
    /// 旧版单独的 <c>ObjectActiveState</c> 列表已并入这里（两者本来就 1:1）。</para>
    /// </remarks>
    public struct EntityState
    {
        /// <summary>实体引用（来自快照名册）。</summary>
        public IRuleAffected Entity;

        /// <summary>刚体位置（<c>Rigidbody2D.position</c>，非 Transform）。</summary>
        public Vector2 Position;

        /// <summary>刚体速度（<c>Rigidbody2D.linearVelocity</c>，Unity 6 API）。</summary>
        public Vector2 Velocity;

        /// <summary>刚体旋转角（度）。</summary>
        public float Rotation;

        /// <summary>
        /// 采集时刻的 <c>GameObject.activeSelf</c>。
        /// </summary>
        /// <remarks>恢复的是 <c>activeSelf</c>（自身开关），不是 <c>activeInHierarchy</c>。</remarks>
        public bool ActiveSelf;

        /// <summary>动画状态 fullPathHash。实体没有可用的 Animator 时为 0（恢复时跳过）。</summary>
        public int AnimatorStateHash;

        /// <summary>动画状态的归一化时间。</summary>
        public float AnimatorNormalizedTime;

        /// <summary>实体状态数据（能量 / 生命 / 持有物）。实体没有 <see cref="IEntityStatus"/> 时为 null。</summary>
        public EntityStatusData Status;

        /// <summary>
        /// 是否采集到了模块状态。
        /// </summary>
        /// <remarks>
        /// ⚠️ 不能靠 <see cref="ModuleState"/> 是否为 null 判断「有没有模块」——
        /// 参与者合法返回 null 时会被误判成「没有模块」而静默跳过恢复。
        /// 所以用一个显式标志区分「没有参与者」与「参与者状态是 null」。
        /// </remarks>
        public bool HasModuleState;

        /// <summary>可选的模块状态；内存快照使用，不作为磁盘存档格式。</summary>
        public object ModuleState;
    }
}
