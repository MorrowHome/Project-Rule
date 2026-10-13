using System;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Rules;
using RuleGame.Core.Rules;

namespace RuleGame.Gameplay.Combat
{
    /// <summary>
    /// 战斗系统：**只做命中判定**。
    /// </summary>
    /// <remarks>
    /// <para>「谁被打到了」是本类的活；「打多少、怎么落地」全交给
    /// <see cref="DamageProcessor"/>（文档 §4.2）。拆开是为了给因果等规则留落点 ——
    /// 那些规则要改的是伤害数值本身，与范围查询无关。</para>
    ///
    /// <para><b>只读规则，不写规则</b>（架构约束）。伤害结算对玩家与敌人使用同一入口（§5.3）。</para>
    ///
    /// <para>本类同时是 <see cref="ICombatSystem"/> 的**事件宿主**：模块
    /// （<see cref="DamageProcessor"/>）自己发的那套事件在这里原样转发出去，
    /// 所以 HUD / VFX 订阅的接口<b>不用改</b>。</para>
    /// </remarks>
    public class CombatSystem : MonoBehaviour, ICombatSystem
    {
        /// <summary>单次命中判定的上限，避免每帧分配。</summary>
        private const int MaxHits = 16;

        private readonly Collider2D[]      _hits       = new Collider2D[MaxHits];
        private readonly List<IDamageable> _hitTargets = new List<IDamageable>();

        /// <summary>伤害处理模块。在 <c>Awake</c> 里建。</summary>
        private DamageProcessor _damage;

        public event Action<DamageInfo> OnDamageApplied;
        public event Action<object>     OnEntityDied;

        /// <summary>挂账窗口内一次伤害被记进账本（没有立即扣血）时触发。供 HUD 显示「欠账」。</summary>
        public event Action<DamageInfo> OnDamageDeferred;

        /// <summary>挂账被一次性结清时触发。<see cref="OnDamageApplied"/> 也会跟着触发一次。</summary>
        public event Action<DamageInfo> OnDamageSettled;

        /// <summary>
        /// 伤害处理模块。**全项目唯一的伤害出口** —— 已知目标要打伤害就走
        /// <c>Damage.Apply(...)</c>，不要另开一条路（否则会绕过伤害规则与挂账账本）。
        /// </summary>
        /// <remarks>首次访问时创建，不依赖 <c>Awake</c> 的调用时机：别的组件可能在它自己的
        /// <c>Awake</c> 里就要用这个模块，而各对象之间的脚本执行顺序并不保证先后。</remarks>
        public DamageProcessor Damage
        {
            get
            {
                EnsureDamageProcessor();
                return _damage;
            }
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void OnEnable()
        {
            // 先强制建起模块与事件转发：即使这一帧没人读 Damage，事件也得是活的。
            EnsureDamageProcessor();

            if (RuleSystem.Instance != null)
            {
                RuleSystem.Instance.OnRuleExpired += OnRuleExpired;
            }
            else
            {
                // RuleSystemDriver 的执行顺序是 -100，正常情况下不会走到这里。
                Debug.LogWarning(
                    "[CombatSystem] RuleSystem 尚未初始化，挂账账本不会自动结算。" +
                    "检查场景里有没有挂 RuleSystemDriver。", this);
            }
        }

        private void OnDisable()
        {
            if (RuleSystem.Instance != null) RuleSystem.Instance.OnRuleExpired -= OnRuleExpired;
        }

        /// <summary>建模块并挂上事件转发。幂等。</summary>
        private void EnsureDamageProcessor()
        {
            if (_damage != null) return;

            // 属性管理器延迟解析：本类不假设 RuleSystem 的初始化时机（它是 -100 的驱动建的）。
            _damage = new DamageProcessor(() => RuleSystem.Instance?.Attributes);

            _damage.OnApplied  += ForwardApplied;
            _damage.OnDeferred += ForwardDeferred;
            _damage.OnSettled  += ForwardSettled;
        }

        /// <summary>模块的事件原样转发出去，保持 <see cref="ICombatSystem"/> 的契约不变。</summary>
        private void ForwardApplied(DamageInfo info)  => OnDamageApplied?.Invoke(info);
        private void ForwardDeferred(DamageInfo info) => OnDamageDeferred?.Invoke(info);
        private void ForwardSettled(DamageInfo info)  => OnDamageSettled?.Invoke(info);

        /// <summary>
        /// 规则结束（到期 / 被覆盖 / <c>ClearAll</c>）时结算挂账账本。
        /// </summary>
        /// <remarks>
        /// <para><b>为什么用事件而不是每帧轮询标志位</b>：轮询会漏进一个竞态 ——
        /// 标志位在 <c>RuleSystemDriver.Update</c>（顺序 −100）里落定，
        /// 而本类的 <c>Update</c> 是默认顺序 0，与敌人的 <c>Update</c> 谁先谁后不定。
        /// 若敌人先动手，那一击会正常结算、给承受方挂上无敌帧，
        /// 紧接着的结算就被无敌帧整笔吞掉 —— 账凭空消失。</para>
        /// <para>订阅事件则在规则结束的**那一刻同步**结算，严格早于所有默认顺序的
        /// <c>Update</c>，竞态不存在。这也是 <c>DamageDeferred</c> 文档指定的结算时机。</para>
        /// <para>过滤到 <see cref="AttributeId.Damage"/>：别的属性结束与本账无关
        /// （真误触时账本也是空的，模块自己会空转退出）。</para>
        /// </remarks>
        private void OnRuleExpired(AttributeId attr)
        {
            if (attr != AttributeId.Damage) return;
            _damage.Settle();
        }

        // ==================================================================
        //  命中判定
        // ==================================================================

        /// <summary>
        /// 在 <see cref="AttackInfo"/> 指定的矩形范围内做一次攻击判定，
        /// 对命中的每个 <see cref="IDamageable"/> 施加伤害。
        /// </summary>
        /// <returns>实际被施加伤害的目标数；<b>0 表示这一击打空</b>（伤害失效规则生效）。</returns>
        /// <remarks>
        /// 先问模块「这一击换算完还剩多少」再决定要不要查询：伤害失效时
        /// 换算结果是 0，那就连范围查询都不做，也不可能广播 ——
        /// 否则 VFX / 音效会在无敌的敌人身上照放。
        /// </remarks>
        public int PerformAttack(AttackInfo attack)
        {
            if (Mathf.Approximately(_damage.ResolveAmount(attack.BaseDamage), 0f)) return 0;

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(attack.Mask);

            int count = Physics2D.OverlapBox(attack.Center, attack.Size, 0f, filter, _hits);

            _hitTargets.Clear();
            for (int i = 0; i < count; i++)
            {
                // 一个目标可能有多个碰撞体：用 GetComponentInParent 找，并按目标去重
                var damageable = _hits[i].GetComponentInParent<IDamageable>();
                if (damageable == null || _hitTargets.Contains(damageable)) continue;

                _hitTargets.Add(damageable);

                // 传的是基础伤害，换算由模块负责 —— 命中判定的长度分辨率不该在这里分叉
                _damage.Apply(attack.Attacker, damageable, attack.BaseDamage, attack.Knockback);
            }
            return _hitTargets.Count;
        }

        /// <summary>
        /// 由实体自身在血量归零时调用（其 <see cref="IDamageable"/> 实现内部判断死亡）。
        /// </summary>
        public void NotifyDied(object entity) => OnEntityDied?.Invoke(entity);
    }
}
