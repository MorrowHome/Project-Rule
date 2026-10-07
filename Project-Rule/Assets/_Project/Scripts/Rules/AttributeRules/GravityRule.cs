using UnityEngine;

namespace RuleGame.Core.Rules
{
    /// <summary>
    /// 重力属性规则。
    /// </summary>
    /// <remarks>
    /// 重力**直接写引擎**（<c>Physics2D.gravity</c>），不进属性管理器——
    /// 因为引擎已经有全局载体。
    /// </remarks>
    public class GravityRule : AttributeRuleBase
    {
        /// <summary>
        /// 工程里的默认重力。
        /// </summary>
        /// <remarks>
        /// 对应 Project Settings → Physics 2D → Gravity。
        /// ⚠️ **改了工程设置就要同步改这里**（当前值是 <c>(0, -9.81)</c>）。
        /// </remarks>
        private static readonly Vector2 Normal = new Vector2(0f, -9.81f);

        /// <summary>反转后的重力。</summary>
        private static readonly Vector2 Reverted = new Vector2(0f, 9.81f);

        /// <summary>反转：重力方向翻转。</summary>
        protected override bool OnRevert()
        {
            Physics2D.gravity = Reverted;
            return true;
        }

        /// <summary>失效：重力归零。</summary>
        protected override bool OnNullify()
        {
            Physics2D.gravity = Vector2.zero;
            return true;
        }

        /// <summary>恒定：锁在默认重力。</summary>
        protected override bool OnConstant()
        {
            Physics2D.gravity = Normal;
            return true;
        }

        /// <summary>增强：默认重力的 2 倍。</summary>
        protected override bool OnEnhance()
        {
            Physics2D.gravity = Normal * 2f;
            return true;
        }

        /// <summary>削弱：默认重力的 0.3 倍。</summary>
        protected override bool OnWeaken()
        {
            Physics2D.gravity = Normal * 0.3f;
            return true;
        }

        /// <summary>恢复默认重力。</summary>
        public override void Clear() => Physics2D.gravity = Normal;
    }
}
