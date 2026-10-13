using UnityEngine;

namespace RuleGame.Gameplay.Combat
{
    /// <summary>
    /// 一次攻击的原始信息。由**发起方**（玩家 / 敌人）组织，交给伤害处理模块。
    /// </summary>
    /// <remarks>
    /// <para>发起方只填「打哪儿、打多大」，<b>不算最终伤害</b> ——
    /// 缩放 / 反转 / 失效由 <see cref="DamageProcessor"/> 按当前生效规则算（文档 §4.2）。</para>
    ///
    /// <para>⚠️ <b>字段集是暂定的</b>：文档 §4.2 把「<c>AttackInfo</c> 需要哪些字段」列为未决问题，
    /// 这里只放当前攻击路径真正用得到的。未来的「因果」要补两类：
    /// 「攻击扩散到周围」需要范围查询、「治疗扩散到全队」需要阵营概念
    /// —— 而阵营原本由「关系」属性承载，该属性已删除，落点待定。</para>
    /// </remarks>
    public struct AttackInfo
    {
        /// <summary>发起方。承受方据它算击退方向。</summary>
        public object Attacker;

        /// <summary>判定区域的中心（世界坐标）。</summary>
        public Vector2 Center;

        /// <summary>判定区域的尺寸。</summary>
        public Vector2 Size;

        /// <summary>这次攻击能命中的层。</summary>
        public LayerMask Mask;

        /// <summary>基础伤害，<b>尚未</b>含全局伤害参数。</summary>
        public float BaseDamage;

        /// <summary>基础击退强度。</summary>
        public float Knockback;
    }
}
