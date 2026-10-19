using System;
using UnityEngine;

namespace RuleGame.Gameplay.Player
{
    /// <summary>
    /// 玩家的四项可解锁能力。位标记，方便一次给多个、也方便 prefab 上直接配。
    /// </summary>
    /// <remarks>
    /// <para>能力本身仍然是**实体自己的序列化字段**（<c>_maxAirJumps</c> / <c>_wallClimbEnabled</c> ...），
    /// 这里只是「这个实体有没有被解锁」的闸门 —— 敌人配了对应字段就有，不受本枚举限制。</para>
    /// </remarks>
    [Flags]
    public enum PlayerAbility
    {
        [InspectorName("无")] None = 0,

        /// <summary>贴墙下滑 + 蹬墙跳。两项共用一个开关（<c>WallClimbEnabled</c>）。</summary>
        [InspectorName("爬墙跳")] WallClimb = 1 << 0,

        /// <summary>二段跳：只锁空中段，地面跳 / 土狼跳不受影响。</summary>
        [InspectorName("二段跳")] AirJump = 1 << 1,

        /// <summary>轻击 / 重击。锁住时攻击盒也不会扳开关。</summary>
        [InspectorName("攻击")] Attack = 1 << 2,

        /// <summary>纸飞机飞刀。</summary>
        [InspectorName("飞刀")] Throw = 1 << 3,

        [InspectorName("全部")] All = WallClimb | AirJump | Attack | Throw,
    }
}
