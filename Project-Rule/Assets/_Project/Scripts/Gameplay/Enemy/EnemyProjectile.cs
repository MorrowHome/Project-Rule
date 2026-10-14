using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>可复用的方块、冲击波、毛团弹。碰撞只组织命中，仍走 CombatSystem。</summary>
    public sealed class EnemyProjectile : MonoBehaviour, ISnapshotParticipant
    {
        private static readonly Stack<EnemyProjectile> Pool = new Stack<EnemyProjectile>();
        private EnemyActor _source;
        private Rigidbody2D _body;
        private SpriteRenderer _renderer;
        private Vector2 _velocity;
        private float _gravity, _radius, _age;
        private bool _inPool;
        private readonly RaycastHit2D[] _hits = new RaycastHit2D[8];
        public bool DiscardWhenMissing => true;
        public EnemyActor Source => _source;
        public Vector2 Velocity => _velocity;
        public float Radius => _radius;
        public float Gravity => _gravity;

        public static void Spawn(EnemyActor source, Vector2 direction, float speed, float gravity, float radius)
        {
            if (source.Target == null || source.Defeated) return;
            EnemyProjectile shot = null;
            while (Pool.Count > 0 && shot == null)
            {
                EnemyProjectile candidate = Pool.Pop();
                if (candidate != null && !candidate.gameObject.activeSelf && candidate._inPool) shot = candidate;
            }
            if (shot == null)
            {
                var go = new GameObject("EnemyProjectile");
                go.SetActive(false); go.layer = 9;
                var body = go.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
                body.gravityScale = 0f; body.interpolation = RigidbodyInterpolation2D.Interpolate;
                go.AddComponent<CircleCollider2D>().isTrigger = true;
                go.AddComponent<SpriteRenderer>().sortingOrder = 8;
                shot = go.AddComponent<EnemyProjectile>();
                go.AddComponent<RuleAffectedBody>();
            }
            shot._source = source; shot._age = 0f; shot._gravity = gravity; shot._radius = radius;
            shot._velocity = direction.normalized * speed; shot._inPool = false;
            shot.transform.position = source.transform.position;
            if (gravity == 0f && radius >= 0.3f)
                shot.transform.position -= (Vector3)source.Motor.Up * (source.Definition.BodySize.y * 0.5f - radius - 0.05f);
            shot.transform.localScale = Vector3.one * radius * 2f;
            shot.gameObject.SetActive(true);
            shot._renderer.sprite = source.ProjectileSprite;
            // 与身体共用无光照材质，白盒不需要额外全局灯才能看到投射物。
            var visual = source.GetComponentInChildren<SpriteRenderer>();
            if (visual != null) shot._renderer.sharedMaterial = visual.sharedMaterial;
            shot._renderer.color = gravity > 0f ? new Color(0.85f, 0.55f, 0.3f) : new Color(0.7f, 0.32f, 0.38f);
            source.EmitFeedback(EnemyFeedbackKind.ProjectileSpawn, shot.transform.position, direction, shot);
        }
        private void Awake() { _body = GetComponent<Rigidbody2D>(); _renderer = GetComponent<SpriteRenderer>(); }
        private void OnEnable() => _inPool = false;
        private void OnDisable()
        {
            if (_inPool) return;
            _inPool = true;
            Pool.Push(this);
        }
        private void FixedUpdate()
        {
            if (TimeManager.Instance?.IsPaused == true) return;
            if (_source == null || _source.Defeated) { gameObject.SetActive(false); return; }
            float dt = Time.fixedDeltaTime;
            _age += dt;
            if (_age > 7f) { gameObject.SetActive(false); return; }
            _velocity += Physics2D.gravity * _gravity * dt;
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_source.PlayerMask | (1 << 8));
            int count = Physics2D.CircleCast(_body.position, _radius, _velocity.normalized, filter, _hits, _velocity.magnitude * dt);
            if (count > 0)
            {
                _source.EmitFeedback(EnemyFeedbackKind.ProjectileImpact, _hits[0].point, -_velocity.normalized, this);
                for (int i = 0; i < count; i++)
                {
                    IDamageable receiver = _hits[i].collider.GetComponentInParent<IDamageable>();
                    IFactionMember faction = _hits[i].collider.GetComponentInParent<IFactionMember>();
                    if (receiver != null && faction?.Faction == Faction.Player)
                        _source.Combat?.ApplyDamage(_source, receiver, _source.Definition.Damage, 4f);
                }
                gameObject.SetActive(false);
                return;
            }
            _body.MovePosition(_body.position + _velocity * dt);
        }
        private struct Data { public EnemyActor Source; public Vector2 Velocity; public float Age, Gravity, Radius; }
        public object CaptureSnapshotState() => new Data { Source = _source, Velocity = _velocity, Age = _age, Gravity = _gravity, Radius = _radius };
        public void RestoreSnapshotState(object state)
        {
            if (!(state is Data data)) return;
            _source = data.Source; _velocity = data.Velocity; _age = data.Age; _gravity = data.Gravity; _radius = data.Radius;
            transform.localScale = Vector3.one * _radius * 2f;
            if (_source != null) _renderer.sprite = _source.ProjectileSprite;
        }
    }
}
