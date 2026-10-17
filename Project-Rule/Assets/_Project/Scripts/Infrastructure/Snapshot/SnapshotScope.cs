using UnityEngine;
using RuleGame.Infrastructure.Common;

namespace RuleGame.Infrastructure.Snapshot
{
    /// <summary>
    /// 场景扫描：把场景里所有 <see cref="IRuleAffected"/> 登记进快照名册。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它</b>：Unity 对<b>初始就禁用</b>的对象不会调 <c>Awake</c> / <c>OnEnable</c>，
    /// 所以「禁用中的预置对象」不会自己登记 —— 而它们恰恰是快照必须记住「当时是关的」的那批。</para>
    /// <para>扫描用 <c>FindObjectsByType(FindObjectsInactive.Include)</c>，**一次性**在关卡加载时做，
    /// 不是每帧。运行时生成的对象由 <c>RuleAffectedBody.Awake</c> 自己登记。</para>
    /// <para>由 <c>RuleSystemDriver</c> 在初始化名册之后调用（执行顺序 -100，早于所有实体 Awake）。</para>
    /// </remarks>
    public static class SnapshotScope
    {
        /// <summary>清空名册并重新扫描整个已加载场景。返回登记后的数量。</summary>
        public static int Rebuild()
        {
            SnapshotRoster roster = SnapshotRoster.Instance;
            if (roster == null) return 0;

            roster.Clear();

            // 不用 FindObjectsSortMode 重载：Unity 6 已把它标为 obsolete。
            MonoBehaviour[] all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is IRuleAffected target) roster.Register(target);
            }

            return roster.Count;
        }
    }
}
