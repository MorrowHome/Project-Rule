using System.Collections.Generic;
using RuleGame.Infrastructure.Common;

namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// 快照名册：**成员资格 = 生命周期**，与「是否启用」解耦。
    /// </summary>
    /// <remarks>
    /// <para>与 <see cref="IWorldRegistry"/> 是两个集合，各自服务不同消费者：</para>
    /// <list type="bullet">
    /// <item><b>快照名册（本接口）</b>：成员从登记到 <c>OnDestroy</c> / 场景卸载，
    /// <b>禁用中的对象也留在名册里</b> —— 否则「采集时禁用」的对象进不了快照，
    /// 「世界回到那一刻」对它们不成立。</item>
    /// <item><b>活动实体注册表</b>：跟随 <c>OnEnable</c> / <c>OnDisable</c>，
    /// 服务伤害扩散与规则施加。</item>
    /// </list>
    /// <para>⚠️ Unity 对<b>初始就禁用</b>的对象不会调 <c>Awake</c> / <c>OnEnable</c>，
    /// 所以名册必须靠场景扫描补上（见 <see cref="SnapshotScope"/>）。</para>
    /// </remarks>
    public interface ISnapshotRoster
    {
        /// <summary>登记。重复登记同一实例会被忽略；<c>null</c> 被忽略。</summary>
        void Register(IRuleAffected target);

        /// <summary>注销。只在对象被销毁 / 场景卸载时调用，**不要跟着 OnDisable 走**。</summary>
        void Unregister(IRuleAffected target);

        /// <summary>当前名册（只读视图，含禁用中的对象；已销毁的会被自动剔除）。</summary>
        IReadOnlyList<IRuleAffected> Targets { get; }

        /// <summary>清空名册。场景扫描 / 关卡加载时先清后扫。</summary>
        void Clear();
    }
}
