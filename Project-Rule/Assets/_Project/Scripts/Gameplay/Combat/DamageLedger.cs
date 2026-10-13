using System;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Infrastructure;

namespace RuleGame.Gameplay.Combat
{
    /// <summary>
    /// 账本里一个承受方记下的净额。
    /// </summary>
    public struct DamageLedgerEntry
    {
        /// <summary>净额。正数 = 欠下的伤害，负数 = 多出来的治疗（结算即回血）。</summary>
        public float Net;

        /// <summary>最近一次「进账的伤害」携带的击退强度，结算时那一次性打击用它。</summary>
        public float Knockback;

        /// <summary>最近一次「进账的伤害」的发起方，结算时据它决定击退方向。</summary>
        public object Attacker;
    }

    /// <summary>
    /// 伤害挂账账本（伤害 / 生命 × 恒定）。
    /// </summary>
    /// <remarks>
    /// <para>规则矩阵：「受到的伤害不立即扣血，先记账；规则结束时一次性结算。
    /// 窗口内的治疗可抵消挂账（按净额结算）」。</para>
    ///
    /// <para><b>为什么是一本「净额」账</b>：规则要求窗口内的治疗能抵扣伤害，
    /// 所以不能记一串流水，只需对每个承受方累加一个 <c>Net</c> —— 伤害加、治疗减。
    /// 结算时把这一个数一次交出去，天然就是「净额结算」。</para>
    ///
    /// <para><b>账本为什么逐实体</b>：每个承受方各欠各的，且结算时仍要各走各的
    /// <see cref="IDamageable.ApplyDamage"/>（抗性 / 无敌帧在那边）。所以账本不知道
    /// 「玩家 / 敌人」，只认接口 —— 与规则系统同一口径。</para>
    ///
    /// <para><b>纯 C# 类</b>，不继承 <c>MonoBehaviour</c>：它没有生命周期需求，
    /// 由伤害处理模块持有，这样也能脱离场景单测。结算顺序按入账先后（<c>_order</c>），
    /// 不用字典的枚举顺序 —— 那个顺序不保证稳定。</para>
    /// </remarks>
    public sealed class DamageLedger
    {
        private readonly Dictionary<IDamageable, DamageLedgerEntry> _entries = new();

        /// <summary>入账顺序。结算按它遍历，保证顺序稳定、可复现。</summary>
        private readonly List<IDamageable> _order = new();

        /// <summary>当前有几个承受方挂着账。</summary>
        public int Count => _order.Count;

        /// <summary>账本是空的（没有待结算的账）。</summary>
        public bool IsEmpty => _order.Count == 0;

        // ==================================================================
        //  记账
        // ==================================================================

        /// <summary>
        /// 记一笔。<paramref name="amount"/> 为正记伤害、为负记治疗，两者在同一个数上相抵。
        /// </summary>
        /// <param name="target">承受方。</param>
        /// <param name="amount">净额增量（正 = 伤害，负 = 治疗）。</param>
        /// <param name="knockback">本次打击的击退强度，仅供结算那一次性打击使用。</param>
        /// <param name="attacker">本次打击的发起方，同样只服务结算时算击退方向。</param>
        public void Accrue(IDamageable target, float amount, float knockback, object attacker)
        {
            if (target == null || Mathf.Approximately(amount, 0f)) return;

            if (_entries.TryGetValue(target, out DamageLedgerEntry entry))
            {
                entry.Net += amount;

                // 只有伤害才更新「最近一记打击」：后面的治疗不该把它的方向和力度抹掉。
                if (amount > 0f)
                {
                    entry.Knockback = knockback;
                    entry.Attacker  = attacker;
                }

                _entries[target] = entry;      // 结构体是值类型，改完要写回
                return;
            }

            _entries[target] = amount > 0f
                ? new DamageLedgerEntry { Net = amount, Knockback = knockback, Attacker = attacker }
                : new DamageLedgerEntry { Net = amount };

            _order.Add(target);
        }

        /// <summary>查某个承受方当前挂着的净额；没有账时为 0。供 HUD / 调试面板显示。</summary>
        public float GetNet(IDamageable target)
            => target != null && _entries.TryGetValue(target, out DamageLedgerEntry e) ? e.Net : 0f;

        // ==================================================================
        //  结算
        // ==================================================================

        /// <summary>
        /// 把所有账一次结清，然后清空账本。
        /// </summary>
        /// <param name="apply">
        /// 对每个有账的承受方调用一次，由调用方决定怎么落地（通常是构造
        /// <c>DamageInfo</c> 交给承受方）。净额为 0 或实体已销毁的会被跳过。
        /// </param>
        public void Settle(Action<IDamageable, DamageLedgerEntry> apply)
        {
            if (_order.Count == 0) return;

            if (apply != null)
            {
                for (int i = 0; i < _order.Count; i++)
                {
                    IDamageable target = _order[i];
                    if (!IsAlive(target)) continue;

                    DamageLedgerEntry entry = _entries[target];

                    // 伤害与治疗正好抵消：没有账要清，不产生一记 0 伤害的打击。
                    if (Mathf.Approximately(entry.Net, 0f)) continue;

                    apply(target, entry);
                }
            }

            // 无论结算成不成功都清空：账只结一次，否则会重复扣血。
            Clear();
        }

        /// <summary>丢弃全部账，不结算。重开关卡 / 退出时用。</summary>
        public void Clear()
        {
            _entries.Clear();
            _order.Clear();
        }

        /// <summary>
        /// 承受方是否还活着（可结算）。
        /// </summary>
        /// <remarks>
        /// <see cref="IDamageable"/> 是接口，<c>== null</c> 会走引用比较，
        /// 绕过 <c>UnityEngine.Object</c> 重载的「已销毁对象看着不为 null」那套，
        /// 所以必须按 <c>Object</c> 再判一次。非 Unity 对象（替身实现）按引用判断即可。
        /// </remarks>
        private static bool IsAlive(IDamageable target)
        {
            if (target == null) return false;

            // 非 Unity 对象（替身实现）：引用非空就算活着。
            if (!(target is UnityEngine.Object)) return true;

            // Unity 对象：必须按 Object 的「假 null」判断是否已被销毁。
            return (UnityEngine.Object)target != null;
        }
    }
}
