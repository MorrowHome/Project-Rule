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
    }
}
