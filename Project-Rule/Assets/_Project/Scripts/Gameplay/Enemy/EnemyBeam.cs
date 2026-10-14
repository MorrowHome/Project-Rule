using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>先锁定方向预告，再开启短判定窗；手针计时与 Boss 身体动作互不阻塞。</summary>
    public sealed class EnemyBeam : MonoBehaviour, ISnapshotParticipant
    {
        private static Material _material;
        private static readonly Stack<EnemyBeam> Pool = new Stack<EnemyBeam>();
        private EnemyActor _source;
        private LineRenderer _line;
        private Vector2 _direction;
        private float _age, _warning, _width;
        private bool _hit;
        private bool _inPool;
        private readonly Collider2D[] _hits = new Collider2D[8];
        public bool DiscardWhenMissing => true;
        public EnemyActor Source => _source;
        public Vector2 Direction => _direction;
        public float Age => _age;
        public float WarningDuration => _warning;
        public float Width => _width;
        public Vector2 Origin => _source.BeamOrigin != null ? (Vector2)_source.BeamOrigin.position : (Vector2)_source.transform.position;
        public static void Spawn(EnemyActor source, float angle, float width, float warning)
        {
            if (source.Defeated) return;
            EnemyBeam beam = null;
            while (Pool.Count > 0 && beam == null)
            {
                EnemyBeam candidate = Pool.Pop();
                if (candidate != null && !candidate.gameObject.activeSelf && candidate._inPool) beam = candidate;
            }
            if (beam == null)
            {
                var go = new GameObject("ClockBeam"); go.SetActive(false);
                var body = go.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0f;
                var line = go.AddComponent<LineRenderer>();
                line.positionCount = 2; line.useWorldSpace = true; line.sortingOrder = 12;
                if (_material == null) _material = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.DontSave };
                line.sharedMaterial = _material;
                beam = go.AddComponent<EnemyBeam>();
                go.AddComponent<RuleAffectedBody>();
            }
            beam._source = source; beam._width = width; beam._warning = warning;
            beam._age = 0f; beam._hit = false; beam._inPool = false;
            beam._direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            beam.gameObject.SetActive(true);
            source.EmitFeedback(EnemyFeedbackKind.BeamSpawn, source.transform.position, beam._direction, beam);
        }
        private void Awake() => _line = GetComponent<LineRenderer>();
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
            if (_source == null || _source.Defeated || _age > _warning + 0.35f) { gameObject.SetActive(false); return; }
            _age += Time.fixedDeltaTime;
            if (_age < _warning || _hit) return;
            Vector2 origin = Origin;
            float length = Length(origin);
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_source.PlayerMask);
            int count = Physics2D.OverlapBox(origin + _direction * length * 0.5f, new Vector2(length, _width),
                Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg, filter, _hits);
            for (int i = 0; i < count; i++)
            {
                IDamageable receiver = _hits[i].GetComponentInParent<IDamageable>();
                IFactionMember faction = _hits[i].GetComponentInParent<IFactionMember>();
                if (receiver == null || faction?.Faction != Faction.Player) continue;
                _source.Combat?.ApplyDamage(_source, receiver, _source.Definition.Damage, 4f);
                _hit = true;
                break;
            }
        }
        private float Length(Vector2 origin)
        {
            RaycastHit2D wall = Physics2D.Raycast(origin, _direction, 32f, 1 << 8);
            return wall.collider != null ? wall.distance : 32f;
        }
        private void LateUpdate()
        {
            if (_source == null) return;
            Vector2 origin = Origin;
            _line.SetPosition(0, new Vector3(origin.x, origin.y, -0.5f));
            Vector2 end = origin + _direction * Length(origin);
            _line.SetPosition(1, new Vector3(end.x, end.y, -0.5f));
            bool firing = _age >= _warning;
            _line.startWidth = _line.endWidth = firing ? _width : 0.035f;
            _line.startColor = _line.endColor = firing ? new Color(1f, 0.4f, 0.2f, 0.95f) : new Color(0.95f, 0.75f, 0.3f, 0.4f);
        }
        private struct Data { public EnemyActor Source; public Vector2 Direction; public float Age, Warning, Width; public bool Hit; }
        public object CaptureSnapshotState() => new Data { Source = _source, Direction = _direction, Age = _age, Warning = _warning, Width = _width, Hit = _hit };
        public void RestoreSnapshotState(object state)
        {
            if (!(state is Data data)) return;
            _source = data.Source; _direction = data.Direction; _age = data.Age; _warning = data.Warning; _width = data.Width; _hit = data.Hit;
        }
    }
}
