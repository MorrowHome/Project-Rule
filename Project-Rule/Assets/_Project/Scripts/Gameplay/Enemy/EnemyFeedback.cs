using UnityEngine;

namespace RuleGame.Gameplay.Enemy
{
    public enum EnemyFeedbackKind { Strike, ProjectileSpawn, ProjectileImpact, BeamSpawn, Hit, RuleCast, Summon }

    /// <summary>
    /// 战斗已发生的表现通知。只携带位置和事件源，不携带特效资源，也不反过来驱动伤害。
    /// 监听者可缺省；正式关卡只要使用敌人 Prefab 就会带上同一套表现。
    /// </summary>
    public readonly struct EnemyFeedback
    {
        public readonly EnemyFeedbackKind Kind;
        public readonly Vector3 Position;
        public readonly Vector2 Direction;
        public readonly Component Subject;

        public EnemyFeedback(EnemyFeedbackKind kind, Vector3 position, Vector2 direction, Component subject = null)
        { Kind = kind; Position = position; Direction = direction; Subject = subject; }
    }
}
