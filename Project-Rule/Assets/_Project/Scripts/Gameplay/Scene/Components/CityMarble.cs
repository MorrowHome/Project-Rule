using UnityEngine;
using RuleGame.Infrastructure;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>真正的动态弹珠：质量、重力和摩擦由已有规则组件/物理引擎作用；受击只应用已处理的击退。</summary>
    [RequireComponent(typeof(CircleCollider2D), typeof(CityDamageZone))]
    public sealed class CityMarble : CityMechanism, IDamageable, IFactionMember
    {
        public Vector2 InitialVelocity = new Vector2(5f, 7f);
        protected override bool DynamicBody => true;
        public Faction Faction => Faction.Enemy;
        private void Start() => Body.linearVelocity = InitialVelocity;
        public void ApplyDamage(in DamageInfo damage)
        {
            if (Paused || damage.Knockback <= 0f) return;
            var attacker = damage.Attacker as Component;
            Vector2 away = attacker != null ? (Vector2)(transform.position - attacker.transform.position) : Vector2.right;
            if (away.sqrMagnitude < .001f) away = Vector2.up;
            Body.AddForce(away.normalized * damage.Knockback, ForceMode2D.Impulse);
        }
    }
}
