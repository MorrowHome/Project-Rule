using System;

namespace RuleGame.Infrastructure
{
    /// <summary>
    /// 实体状态组件的数据快照。
    /// </summary>
    /// <remarks>
    /// <para>同时用于两处：<b>存档</b>（<c>SaveData.PlayerStatus</c>）与<b>快照</b>（<c>EntityState.Status</c>）。</para>
    /// <para>⚠️ <b>绝不包含 Transform / Rigidbody 等物理状态。</b>（架构约束 C5）</para>
    /// </remarks>
    [Serializable]
    public class EntityStatusData
    {
        public float  Energy;
        public float  Health;
        public object HeldItem;
        public int    ThrowableCount;

        /// <summary>
        /// 返回一份独立副本。
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>这是 class 不是 struct。</b> `var d = es.Status` 拿到的是**引用**，
        /// 改它会污染快照本身 —— 而「时间恒定」恰恰是**同一张快照反复回溯**的场景，
        /// 第一次回溯就把快照改坏，后面每一次都用错的值。
        /// 需要「改一份但不影响快照」时必须先 <see cref="Clone"/>。
        /// </remarks>
        public EntityStatusData Clone() => new EntityStatusData
        {
            Energy         = Energy,
            Health         = Health,
            HeldItem       = HeldItem,
            ThrowableCount = ThrowableCount,
        };
    }
}
