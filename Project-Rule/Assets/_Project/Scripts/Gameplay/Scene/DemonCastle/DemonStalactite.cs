using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure;

namespace RuleGame.Gameplay.Scene.DemonCastle
{
    /// <summary>玩家进入下方范围后预警并下落；扫掠地面与受击箱，落地保留碎裂对象供回溯。</summary>
    public sealed class DemonStalactite : CityMechanism
    {
        [Min(.1f)] [InspectorName("下方探测深度")] public float DetectDepth = 9f;
        [Min(.1f)] [InspectorName("下方探测宽度")] public float DetectWidth = 2f;
        [Min(0f)] [InspectorName("落下预警（秒）")] public float WarningSeconds = .55f;
        [Min(.1f)] [InspectorName("下落加速度")] public float Acceleration = 25f;
        [Min(0f)] [InspectorName("基础伤害")] public float Damage = 25f;
        [Min(0f)] [InspectorName("击退强度")] public float Knockback = 6f;
        [Min(1f)] [InspectorName("最大下落距离")] public float MaxFallDistance = 30f;
        [InspectorName("共享战斗系统（可空）")]
        public CombatSystem Combat;
        public int Phase { get; private set; }
        private float _timer, _speed, _distance;
        private readonly List<Collider2D> _overlaps = new List<Collider2D>(16);
        private readonly List<RaycastHit2D> _casts = new List<RaycastHit2D>(16);
        private readonly HashSet<IDamageable> _hit = new HashSet<IDamageable>();
        private void Start() { if (Combat == null) Combat = FindAnyObjectByType<CombatSystem>(); }
        private void FixedUpdate()
        {
            if (Paused || Phase == 3) return;
            if (Phase == 0)
            {
                var filter = new ContactFilter2D { useTriggers = false };
                Bounds b = Shape.bounds;
                int count = Physics2D.OverlapBox(new Vector2(b.center.x, b.min.y - DetectDepth * .5f), new Vector2(DetectWidth, DetectDepth), 0f, filter, _overlaps);
                for (int i = 0; i < count; i++)
                    if (_overlaps[i].GetComponentInParent<PlayerController>() != null) { Phase = 1; _timer = WarningSeconds; break; }
                return;
            }
            if (Phase == 1)
            {
                _timer -= Time.fixedDeltaTime;
                if (Visuals.Length > 0) Visuals[0].color = Color.Lerp(new Color(.45f, .36f, .55f), new Color(.95f, .3f, .3f), .5f + .5f * Mathf.Sin(_timer * 30f));
                if (_timer <= 0f) Phase = 2;
                return;
            }
            _speed += Acceleration * Time.fixedDeltaTime;
            float distance = _speed * Time.fixedDeltaTime;
            var ground = new ContactFilter2D { useTriggers = false }; ground.SetLayerMask(1 << 8);
            int hits = Body.Cast(Vector2.down, ground, _casts, distance + .01f);
            bool landed = false;
            for (int i = 0; i < hits; i++)
            {
                if (_casts[i].collider.attachedRigidbody == Body || _casts[i].normal.y < .2f) continue;
                if (_casts[i].distance <= distance + .01f) { distance = Mathf.Min(distance, Mathf.Max(0f, _casts[i].distance - .01f)); landed = true; }
            }
            if (Combat == null) Combat = FindAnyObjectByType<CombatSystem>();
            if (Combat != null && Combat.Processor != null)
                Combat.PerformAttack(this, (Vector2)Shape.bounds.center + Vector2.down * distance * .5f,
                    (Vector2)Shape.bounds.size + Vector2.up * distance, Damage, Knockback, _hit);
            Body.MovePosition(Body.position + Vector2.down * distance); _distance += distance;
            if (landed || _distance >= MaxFallDistance) { Phase = 3; Shape.enabled = false; SetVisible(false); }
        }
        private sealed class State
        { public int Phase; public float Timer, Speed, Distance; public HashSet<IDamageable> Hit; }
        protected override object CaptureMechanismState() => new State
        { Phase = Phase, Timer = _timer, Speed = _speed, Distance = _distance, Hit = new HashSet<IDamageable>(_hit) };
        protected override void RestoreMechanismState(object state)
        {
            var s = (State)state; Phase = s.Phase; _timer = s.Timer; _speed = s.Speed; _distance = s.Distance;
            _hit.Clear(); foreach (var target in s.Hit) _hit.Add(target);
        }
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            var shape = GetComponent<Collider2D>();
            float y = shape != null ? shape.bounds.min.y : transform.position.y;
            Gizmos.DrawWireCube(new Vector3(transform.position.x, y - DetectDepth * .5f), new Vector3(DetectWidth, DetectDepth));
        }
    }
}
