using System;
using UnityEngine;
using RuleGame.Infrastructure.Rules;

namespace RuleGame.Gameplay.Enemy
{
    public enum EnemyKind { BrickMinion, BrickBoss, ClockBoss, RabbitBoss, BatBoss, BatMinion }

    [Serializable]
    public struct EnemyActionTiming
    {
        public string Action;
        [Min(0.05f)] public float Duration;
    }

    /// <summary>共用配置资产；生命、阶段、冷却等运行状态必须放在实例上。</summary>
    [CreateAssetMenu(menuName = "RuleGame/Enemy/Enemy Definition")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        public EnemyKind Kind;
        public string DisplayName;
        public Vector2 BodySize = new Vector2(1.8f, 3f);
        public float Health = 100f;
        public float Damage = 10f;
        public bool ContactDamage = true;
        [Min(0.05f)] public float ContactInterval = 0.6f;
        public float Energy = 100f;
        public float EnergyRegen = 2f;
        public AttributeId RuleAttribute = AttributeId.Gravity;
        public RelationId RuleRelation = RelationId.Weaken;
        public float MoveSpeed = 2.5f;
        public float AttackRange = 3.5f;
        public float AttackCooldown = 1.8f;
        public float HurtCooldown = 2f;
        public float GravityScale = 1f;
        public bool Flying;
        public float ImpactFraction = 0.6f;
        public float RabbitCycle = 20f;
        public float RabbitThrowInterval = 2f;
        public float ClockHourInterval = 12f;
        public float ClockCoreWindow = 3f;
        public float ClockCoreHealth = 15f;
        public EnemyActionTiming[] Timings = Array.Empty<EnemyActionTiming>();

        public float Duration(string action, float fallback = 0.8f)
        {
            foreach (EnemyActionTiming entry in Timings)
                if (entry.Action == action) return Mathf.Max(0.05f, entry.Duration);
            return fallback;
        }
    }
}
