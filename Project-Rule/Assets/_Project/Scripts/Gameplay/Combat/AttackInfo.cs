using System.Collections.Generic;
using UnityEngine;
using RuleGame.Infrastructure;

namespace RuleGame.Gameplay.Combat
{
    /// <summary>
    /// 一次攻击的输入：发起方交给**伤害处理模块**的东西。
    /// </summary>
    /// <remarks>
    /// <para><b>与 <see cref="DamageInfo"/> 的分工</b>：这个是「进去」的（发起方的意图），
    /// 那个是「出来」的（结算结果，已含全局规则影响、尚未扣目标抗性）。</para>
    /// <para><b>发起方只填精确命中的结果</b> —— 它拿不到「场景里还有哪些可命中实体」，
    /// 那个候选集由伤害处理模块自己查对象注册表（因果扩散 / 随机作用对象要用）。</para>
    /// </remarks>
    public struct AttackInfo
    {
        /// <summary>发起方。可以是 Component / GameObject，模块会自己找它的阵营。</summary>
        public object Attacker;

        /// <summary>
        /// 这次攻击的作用中心。
        /// </summary>
        /// <remarks>扩散与随机作用对象都以它为圆心搜索。发起方本来就持有它
        /// （命中判定的那个矩形中心）。</remarks>
        public Vector2 Origin;

        /// <summary>
        /// 基础数值，<b>带符号</b>：正数 = 伤害，负数 = 回血。
        /// </summary>
        /// <remarks>符号约定与 <see cref="DamageInfo.Amount"/> 一致，省一次转换。</remarks>
        public float BaseAmount;

        /// <summary>基础击退。</summary>
        public float Knockback;

        /// <summary>命中判定给出的精确结果。可以为空（那就只靠扩散/随机产生目标）。</summary>
        public IReadOnlyList<IDamageable> Targets;
    }
}
