using System;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Common;
using RuleGame.Infrastructure.Rules;
using RuleGame.Core.Rules;

namespace RuleGame.Gameplay.Combat
{
    /// <summary>
    /// 战斗系统：**命中判定** + 事件宿主。
    /// </summary>
    /// <remarks>
    /// <para><b>职责已经收窄</b>：伤害算多少归 <see cref="DamageProcessor"/>（唯一伤害出口），
    /// 这里只做「这一击打到了谁」的物理判定，以及把模块的结果广播出去。</para>
    /// <para><b>只读规则，不写规则</b>（架构约束）。</para>
    /// </remarks>
    public class CombatSystem : MonoBehaviour, ICombatSystem
    {
        /// <summary>单次命中判定的上限，避免每帧分配。</summary>
        private const int MaxHits = 16;

        private readonly Collider2D[]      _hits       = new Collider2D[MaxHits];
        private readonly List<IDamageable> _hitTargets = new List<IDamageable>();

        private DamageProcessor _processor;

        public event Action<DamageInfo> OnDamageApplied;
        public event Action<object>     OnEntityDied;

        /// <summary>
        /// 伤害处理模块。**懒建** —— 规则系统没起来之前建不出来，
        /// 而第一次真的打出伤害时它一定已经起来了。
        /// </summary>
        public DamageProcessor Processor
        {
            get
            {
                if (_processor != null) return _processor;

                IAttributeManager attributes = RuleSystem.Instance?.Attributes;
                if (attributes == null) return null;        // 规则系统未就绪，本帧打不出伤害

                _processor = new DamageProcessor(attributes, WorldRegistry.Instance);
                _processor.OnDamageApplied += info => OnDamageApplied?.Invoke(info);
                return _processor;
            }
        }

        private void Update() => _processor?.Tick();

        // ==================================================================
        //  命中判定
        // ==================================================================

        /// <summary>
        /// 在指定矩形范围内做一次攻击判定，把命中的目标交给伤害处理模块。
        /// </summary>
        /// <remarks>
        /// 命中判定归这里（发起方一侧），**伤害算多少归模块** ——
        /// 发起方拿不到「其余可命中实体」，因果扩散的候选集由模块自己查注册表。
        /// </remarks>
        /// <returns>模块实际作用到的目标数；<b>0 表示这一击无效或打空</b>。</returns>
        public int PerformAttack(object attacker, Vector2 center, Vector2 size, LayerMask mask,
                                 float baseDamage, float knockback)
            => PerformAttack(attacker, center, size, mask, baseDamage, knockback, null);

        /// <summary>
        /// 连续攻击窗口：发起方在每次挥刀前清空集合，窗口内按实体去重。
        /// 原有单次判定接口保持不变；伤害、因果传播和挂账仍由 DamageProcessor 处理。
        /// </summary>
        public int PerformAttack(object attacker, Vector2 center, Vector2 size, LayerMask mask,
                                 float baseDamage, float knockback, ISet<IDamageable> swingTargets)
        {
            DamageProcessor processor = Processor;
            if (processor == null) return 0;

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(mask);

            int count = Physics2D.OverlapBox(center, size, 0f, filter, _hits);

            _hitTargets.Clear();
            for (int i = 0; i < count; i++)
            {
                // 一个目标可能有多个碰撞体：用 GetComponentInParent 找，并按目标去重
                var damageable = _hits[i].GetComponentInParent<IDamageable>();
                if (damageable == null || _hitTargets.Contains(damageable)
                    || (swingTargets != null && swingTargets.Contains(damageable))) continue;
                _hitTargets.Add(damageable);
                swingTargets?.Add(damageable);
            }

            if (_hitTargets.Count == 0) return 0;           // 打空：连模块都不进

            var attack = new AttackInfo
            {
                Attacker   = attacker,
                Origin     = center,
                BaseAmount = baseDamage,
                Knockback  = knockback,
                Targets    = _hitTargets,
            };

            return processor.Process(attack);
        }

        /// <summary>
        /// 对单个**已知**目标施加一次攻击（也供调试工具使用）。
        /// </summary>
        /// <remarks>
        /// ⚠️ <paramref name="baseDamage"/> 是<b>基础值</b>，不是最终值 ——
        /// 它同样要过一遍伤害处理模块，所以伤害 / 因果规则对这条路径一样生效。
        /// </remarks>
        /// <returns>模块实际作用到的目标数。</returns>
        public int ApplyDamage(object attacker, IDamageable target, float baseDamage, float knockback)
        {
            if (target == null) return 0;

            DamageProcessor processor = Processor;
            if (processor == null) return 0;

            _hitTargets.Clear();
            _hitTargets.Add(target);

            var attack = new AttackInfo
            {
                Attacker   = attacker,
                Origin     = PositionOf(target),
                BaseAmount = baseDamage,
                Knockback  = knockback,
                Targets    = _hitTargets,
            };

            return processor.Process(attack);
        }

        /// <summary>
        /// 由实体自身在血量归零时调用（其 <see cref="IDamageable"/> 实现内部判断死亡）。
        /// </summary>
        public void NotifyDied(object entity) => OnEntityDied?.Invoke(entity);

        // ==================================================================
        //  辅助
        // ==================================================================

        /// <summary>取一个承受方的世界坐标，用作因果扩散的搜索圆心。</summary>
        private static Vector2 PositionOf(IDamageable target)
        {
            // IDamageable 的静态类型是接口，GameObject 不可能是它的实现
            //（GameObject 是 UnityEngine 的密封类型），所以只判 Component 分支。
            return target is Component component ? component.transform.position : Vector2.zero;
        }
    }
}
