using UnityEngine;

namespace RuleGame.Gameplay.Common
{
    /// <summary>
    /// 会动的实体：**自己报动量**（kg·单位/秒），给「按动量判定」的机关读。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它</b>：Kinematic 刚体（玩家 / 敌人）的
    /// <c>Rigidbody2D.linearVelocity</c> <b>不可信</b> —— 位移是代码用
    /// <c>MovePosition</c> 走出来的，那个字段不会跟着更新。所以「这个实体此刻动量多少」
    /// 只有实体自己知道，让它自己报。</para>
    ///
    /// <para><b>为什么报动量、不报速度</b>：机关要判的是「撞得够不够狠」，
    /// 也就是质量 × 速度。质量住在 <c>Rigidbody2D.mass</c>（质量规则写的就是它），
    /// 由实体自己乘进去 —— 机关不必知道质量规则存在，也不必认识任何实体类型。
    /// 「质量 × 增强」把实体压重三倍之后，同一个 <see cref="Momentum"/> 读出来就是三倍，
    /// 机关一行都不用改。</para>
    ///
    /// <para><b>谁实现</b>：Kinematic 且会动的实体（玩家现在实现了）。
    /// <b>Dynamic 刚体不用实现</b> —— 它的 <c>mass × linearVelocity</c> 本来就是动量，
    /// 机关直接读刚体（铁箱等）。</para>
    ///
    /// <para>与 <see cref="IMovingSurface"/> / <see cref="IPushable"/> 同一套做法：
    /// 接口是机关与实体之间**唯一**的耦合点 —— 机关不认识玩家，玩家也不认识机关。</para>
    /// </remarks>
    public interface IMomentumSource
    {
        /// <summary>本物理帧的动量（kg·单位/秒），方向 = 运动方向。</summary>
        Vector2 Momentum { get; }
    }
}
