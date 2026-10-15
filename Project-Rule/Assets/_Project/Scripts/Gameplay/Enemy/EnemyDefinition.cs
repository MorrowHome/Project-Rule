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
        [Tooltip("身体接触伤害向外检测的余量，覆盖移动解算保留的微小间隙。")]
        [Min(0f)] public float ContactPadding = 0.05f;
        public float Energy = 100f;
        public float EnergyRegen = 2f;
        public AttributeId RuleAttribute = AttributeId.Gravity;
        public RelationId RuleRelation = RelationId.Weaken;
        public float MoveSpeed = 2.5f;
        public float AttackRange = 3.5f;
        public float AttackCooldown = 1.8f;
        [Header("Boss 进攻后的喘息")]
        [Tooltip("一套攻击结束后不再出招的时间；地面 Boss 原地休息，蝙蝠高空巡飞。不随阶段加速缩短。")]
        [Min(0.1f)] public float BossRestDuration = 1.8f;
        [Tooltip("发条连续发射这么多轮指针后休息；同一物理帧多根指针算一轮。")]
        [Min(1)] public int ClockBeamVolleysBeforeRest = 2;
        [Header("Boss 距离控制")]
        [Min(0.5f)] public float PreferredDistance = 5f;
        [Min(0.1f)] public float SpacingTolerance = 0.6f;
        [Min(0.5f)] public float RangedRange = 9f;
        [Range(0.1f, 1f)] public float RetreatSpeedMultiplier = 0.8f;
        [Header("受击硬直")]
        [Tooltip("Boss 短时间累计受到这么多实际伤害才进入受击状态。小怪仍每次命中受击。")]
        [Min(1f)] public float StaggerDamageThreshold = 30f;
        [Tooltip("连续这么久没有有效命中，清空累计硬直伤害。冷却期间不累计。")]
        [Min(0.1f)] public float StaggerResetDelay = 1.5f;
        [Tooltip("受击动作结束后，允许再次进入硬直前的冷却。期间仍正常扣血。")]
        [Min(0f)]
        public float HurtCooldown = 2f;
        [Tooltip("Boss 普通受击硬直时长上限；不影响阶段虚弱、发条眩晕或小怪。")]
        [Min(0.05f)] public float BossHurtDuration = 0.45f;
        public float GravityScale = 1f;
        public bool Flying;
        [Min(1f)] public float FlightAcceleration = 24f;
        [Header("蝙蝠空战")]
        [Min(1f)] public float BatOrbitDistance = 4.5f;
        [Min(0.5f)] public float BatHoverHeight = 3f;
        [Min(1f)] public float BatDashCooldown = 4f;
        [Min(0.2f)] public float BatDashWindup = 0.5f;
        [Min(0.1f)] public float BatDashDuration = 0.65f;
        [Min(0.5f)] public float BatEvadeCooldown = 2.5f;
        [Min(0.1f)] public float BatEvadeDuration = 0.65f;
        [Min(0.1f)] public float BatRecoverDuration = 0.45f;
        [Header("蝙蝠休息巡飞")]
        [Tooltip("休息目标高度占场地高度的比例，仍受身体与场地边界限制。")]
        [Range(0.5f, 0.95f)] public float BatRestHeightRatio = 0.8f;
        [Tooltip("以进入休息的位置为中心，向两侧来回飞行的距离。")]
        [Min(0.5f)] public float BatRestPatrolDistance = 2.5f;
        [Tooltip("休息巡飞相对基础飞行速度的倍率，不随阶段加速。")]
        [Range(0.1f, 1f)] public float BatRestSpeedMultiplier = 0.85f;
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
