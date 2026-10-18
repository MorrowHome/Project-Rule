using RuleGame.Gameplay.Player;
using RuleGame.Infrastructure;
using UnityEngine;

namespace RuleGame.Gameplay.Common
{
    /// <summary>
    /// 「这个东西算不算玩家」的**唯一判据**。
    /// </summary>
    /// <remarks>
    /// <para>两个条件取或：父链上有 <see cref="PlayerController"/>（玩家的控制器），
    /// 或者有 <see cref="IFactionMember"/> 且阵营是 <see cref="Faction.Player"/>。
    /// 认接口不认具体类型 —— 与 <c>IPushable</c> / <c>IMovingSurface</c> 同一条路子。</para>
    ///
    /// <para>⚠️ 用它的时候一律走**碰撞体查询**（<c>OverlapBox</c> / <c>OverlapCircle</c> /
    /// <c>OverlapCollider</c>），**不要**用触发回调：玩家是 Kinematic + <c>Slide</c>
    /// （<c>useAttachedTriggers = false</c>），触发回调不可靠。</para>
    /// </remarks>
    public static class PlayerLookup
    {
        /// <summary>这个碰撞体（或它的父链）算不算玩家阵营。</summary>
        public static bool IsPlayer(Collider2D collider)
        {
            if (collider == null) return false;

            if (collider.GetComponentInParent<PlayerController>() != null) return true;

            var member = collider.GetComponentInParent<IFactionMember>();
            return member != null && member.Faction == Faction.Player;
        }
    }
}
