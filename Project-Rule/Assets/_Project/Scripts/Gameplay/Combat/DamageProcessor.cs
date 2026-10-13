using System;
using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Gameplay.Combat
{
    /// <summary>
    /// 伤害处理模块：**全局唯一的伤害出口**。
    /// </summary>
    /// <remarks>
    /// <para><b>它负责什么</b>：把基础伤害按当前生效规则算成实际伤害（缩放 → 反转 → 失效，
    /// 以后还有因果），然后交给承受方。命中判定不在这里 —— 那是 <see cref="CombatSystem"/> 的活。</para>
    ///
    /// <para><b>依赖方向（关键，别搞反）</b>：本模块读 <see cref="IAttributeManager"/>，
    /// <b>不读</b>规则维护表。<see cref="IAttributeManager.Values"/> 就是「规则生效后的事实来源」
    /// —— <c>DamageRule</c> 已经把结果写进 <c>DamageScale</c> / <c>DamageInverted</c> /
    /// <c>DamageDisabled</c>。若本模块自己去解释「现在有哪些规则生效」，等于把算法从属性规则类
    /// 搬回战斗侧，违反 <b>D10</b>（算法收归属性规则类）与「规则系统不做特例判断」。</para>
    ///
    /// <para><b>边界（两条硬约束）</b>：抗性 / 无敌帧<b>不进</b>本模块
    /// （约束 C4：免疫写在实体自己的物理属性里，由 <c>EntityDamageable</c> 承担）——
    /// 本模块负责「算多少」，承受方负责「扣不扣」。本模块也<b>不认识</b>「玩家 / 敌人」，
    /// 只认 <see cref="IDamageable"/>，与规则系统同一口径。</para>
    ///
    /// <para><b>为什么是纯 C# 类</b>：它没有生命周期需求，由 <see cref="CombatSystem"/> 持有；
    /// 这样也能脱离场景单测 —— 属性管理器用 <see cref="Func{TResult}"/> 延迟解析，
    /// 测试里传个 lambda 造替身即可，不必起 <c>RuleSystem</c> 单例。</para>
    ///
    /// <para>伤害挂账（伤害 × 恒定）的账本住在 <see cref="Ledger"/>，
    /// 因为只有这一个咽喉能拦住伤害。</para>
    /// </remarks>
    public sealed class DamageProcessor
    {
        /// <summary>属性管理器的解析器。延迟解析，不假设 <c>RuleSystem</c> 的初始化时机。</summary>
        private readonly Func<IAttributeManager> _attributes;

        private readonly DamageLedger _ledger = new();

        /// <param name="attributes">
        /// 属性管理器解析器。生产环境传 <c>() =&gt; RuleSystem.Instance?.Attributes</c>；
        /// 单测传替身。传 null 视为配置错误。
        /// </param>
        public DamageProcessor(Func<IAttributeManager> attributes)
        {
            _attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        }

        // ==================================================================
        //  状态
        // ==================================================================

        /// <summary>挂账账本。只读用途（HUD / 调试面板查「某个实体欠了多少」）。</summary>
        public DamageLedger Ledger => _ledger;

        /// <summary>挂账窗口是否开着（伤害 × 恒定）。</summary>
        public bool IsDeferred
        {
            get
            {
                IAttributeManager attrs = _attributes();
                return attrs != null && attrs.Values.DamageDeferred;
            }
        }

        // ==================================================================
        //  事件
        // ==================================================================

        /// <summary>一次伤害真的落到承受方身上（正常施加，或挂账结算）。</summary>
        public event Action<DamageInfo> OnApplied;

        /// <summary>挂账窗口内一次伤害被记进账本（没有立即扣血）。</summary>
        public event Action<DamageInfo> OnDeferred;

        /// <summary>挂账被一次性结清（<see cref="OnApplied"/> 也会跟着触发一次）。</summary>
        public event Action<DamageInfo> OnSettled;

        // ==================================================================
        //  算多少
        // ==================================================================

        /// <summary>
        /// 把基础伤害换算为实际伤害：<b>缩放 → 反转 → 失效</b>。
        /// </summary>
        /// <remarks>返回值已含全局伤害参数，<b>尚未扣目标抗性</b>（那是承受方的事）。</remarks>
        public float ResolveAmount(float baseDamage)
        {
            IAttributeManager attrs = _attributes();
            if (attrs == null) return baseDamage;        // 规则系统未就绪时退化为原始值

            AttributeValues v = attrs.Values;
            if (v.DamageDisabled) return 0f;             // 失效：攻击与回血都无效

            float amount = baseDamage * v.DamageScale;
            return v.DamageInverted ? -amount : amount;  // 反转：正伤害转为回血
        }

        // ==================================================================
        //  落地
        // ==================================================================

        /// <summary>
        /// 对单个已知目标施加伤害。<b>传入的是基础伤害</b>，本模块负责按规则换算。
        /// </summary>
        /// <remarks>
        /// <para>调用方一律传基础伤害，不要自己先算 —— 算法只能有一处，
        /// 否则「伤害失效」这类规则会被绕过（漏算等于规则失效）。</para>
        /// <para>换算后为 0（伤害失效）时什么都不做：不扣血、不记帐、不广播 ——
        /// 否则 VFX / 音效会在一个毫发无损的实体身上照放。</para>
        /// </remarks>
        public void Apply(object attacker, IDamageable target, float baseDamage, float knockback)
        {
            if (target == null) return;

            float amount = ResolveAmount(baseDamage);
            if (Mathf.Approximately(amount, 0f)) return;

            var info = new DamageInfo
            {
                Attacker  = attacker,
                Target    = target,
                Amount    = amount,
                Knockback = knockback,
            };

            // 挂账窗口（伤害 × 恒定）：先记账，不扣血、不击退。
            if (IsDeferred)
            {
                _ledger.Accrue(target, amount, knockback, attacker);
                OnDeferred?.Invoke(info);
                return;
            }

            target.ApplyDamage(info);
            OnApplied?.Invoke(info);
        }

        // ==================================================================
        //  挂账结算
        // ==================================================================

        /// <summary>
        /// 把账本一次结清：每个有账的承受方收到一记「净额打击」。
        /// </summary>
        /// <remarks>
        /// <para><b>调用时机由外部决定</b>（与规则系统解耦的代价）。生产环境里
        /// <see cref="CombatSystem"/> 订阅 <c>IRuleSystem.OnRuleExpired</c> 后转调这里 ——
        /// 那边有「为什么必须是事件而不是每帧轮询」的详细理由。</para>
        /// <para>账本为空时直接返回，所以「别的属性结束了」这类误触是免费的。</para>
        /// </remarks>
        public void Settle()
        {
            if (_ledger.IsEmpty) return;

            _ledger.Settle(SettleOne);
        }

        /// <summary>结算单个承受方。走的是与平时完全相同的承受路径，所以抗性 / 无敌帧照旧生效。</summary>
        private void SettleOne(IDamageable target, DamageLedgerEntry entry)
        {
            var info = new DamageInfo
            {
                Attacker  = entry.Attacker,
                Target    = target,
                Amount    = entry.Net,
                Knockback = entry.Knockback,
            };

            target.ApplyDamage(info);
            OnApplied?.Invoke(info);
            OnSettled?.Invoke(info);
        }
    }
}
