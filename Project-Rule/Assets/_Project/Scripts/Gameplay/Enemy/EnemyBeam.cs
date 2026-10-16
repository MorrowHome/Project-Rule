using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>有限时长的光束；预警提前显示起射方向，发射期间跟随指针并限制最大扫射角。</summary>
    [DefaultExecutionOrder(30)] // 物理判定晚于 Actor 推进；显示晚于 ClockHandView 更新表盘原点。
    public sealed class EnemyBeam : MonoBehaviour, ISnapshotParticipant
    {
        private static Material _material;
        private static readonly Stack<EnemyBeam> Pool = new Stack<EnemyBeam>();
        private EnemyActor _source;
        private LineRenderer _line;
        private Vector2 _direction;
        private float _age, _warning, _width;
        private float _duration = .35f;
        private float _nextHit;
        private int _clockHand = -1;
        private bool _hit;
        private bool _inPool;
        private readonly Collider2D[] _hits = new Collider2D[8];
        public bool DiscardWhenMissing => true;
        public EnemyActor Source => _source;
        public Vector2 Direction => FollowsClockHand && _source != null && _source.ClockBeams != null
            ? _source.ClockBeams.HandDirection(_clockHand, Mathf.Max(0f, _warning - _age)) : _direction;
        public Vector2 SweepEndDirection => FollowsClockHand && _source != null && _source.ClockBeams != null
            ? _source.ClockBeams.HandDirection(_clockHand, Mathf.Max(0f, _warning + _duration - _age)) : _direction;
        public float SweepAngle => FollowsClockHand && _source != null && _source.ClockBeams != null
            ? _duration * 360f / _source.ClockBeams.RotationPeriod(_clockHand) : 0f;
        public Vector2 SweepEndPoint => Origin + SweepEndDirection * Length(Origin, SweepEndDirection);
        public bool FollowsClockHand => _clockHand >= 0;
        public int ClockHand => _clockHand;
        public float Age => _age;
        public float WarningDuration => _warning;
        public float FireDuration => _duration;
        public float Width => _width;
        public Vector2 Origin => _source.BeamOrigin != null ? (Vector2)_source.BeamOrigin.position : (Vector2)_source.transform.position;
        public static void Spawn(EnemyActor source, float angle, float width, float warning)
            => Create(source, angle, width, warning, -1);
        public static EnemyBeam SpawnClockHand(EnemyActor source, int hand, float width, float warning)
            => Create(source, 90f, width, warning, hand);
        private static EnemyBeam Create(EnemyActor source, float angle, float width, float warning, int hand)
        {
            if (source.Defeated) return null;
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
            beam._clockHand = hand; beam._nextHit = 0f;
            beam._duration = hand >= 0 ? source.ClockBeams.FireDuration(hand) : .35f;
            beam._direction = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            beam.gameObject.SetActive(true);
            source.EmitFeedback(EnemyFeedbackKind.BeamSpawn, source.transform.position, beam.Direction, beam);
            return beam;
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
            if (_source == null || _source.Defeated
                || (FollowsClockHand && (_source.ClockBeams == null || !_source.ClockBeams.IsHandActive(_clockHand))))
                { gameObject.SetActive(false); return; }
            if (FollowsClockHand && _source.Status.IsPaused) return;
            _age += Time.fixedDeltaTime;
            if (_age >= _warning + _duration) { gameObject.SetActive(false); return; }
            if (_age < _warning || (FollowsClockHand ? _age < _nextHit : _hit)) return;
            Vector2 origin = Origin;
            Vector2 direction = Direction;
            float length = Length(origin, direction);
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_source.PlayerMask);
            int count = Physics2D.OverlapBox(origin + direction * length * 0.5f, new Vector2(length, _width),
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, filter, _hits);
            for (int i = 0; i < count; i++)
            {
                IDamageable receiver = _hits[i].GetComponentInParent<IDamageable>();
                IFactionMember faction = _hits[i].GetComponentInParent<IFactionMember>();
                if (receiver == null || faction?.Faction != Faction.Player) continue;
                _source.Combat?.ApplyDamage(_source, receiver, _source.Definition.Damage, 4f);
                _hit = true;
                _nextHit = _age + Mathf.Max(.05f, _source.Definition.ContactInterval);
                break;
            }
        }
        private float Length(Vector2 origin, Vector2 direction)
        {
            RaycastHit2D wall = Physics2D.Raycast(origin, direction, 32f, _source.Motor.SolidMask);
            return wall.collider != null ? wall.distance : 32f;
        }
        private void LateUpdate()
        {
            if (_source == null) return;
            Vector2 origin = Origin;
            _line.SetPosition(0, new Vector3(origin.x, origin.y, -0.5f));
            Vector2 direction = Direction;
            Vector2 end = origin + direction * Length(origin, direction);
            _line.SetPosition(1, new Vector3(end.x, end.y, -0.5f));
            bool firing = _age >= _warning;
            _line.startWidth = _line.endWidth = firing ? _width : 0.035f;
            _line.startColor = _line.endColor = firing ? new Color(1f, 0.4f, 0.2f, 0.95f) : new Color(0.95f, 0.75f, 0.3f, 0.4f);
        }
        private struct Data { public EnemyActor Source; public Vector2 Direction; public float Age, Warning, Width, NextHit, Duration; public bool Hit; public int ClockHand; }
        public object CaptureSnapshotState() => new Data
        {
            Source = _source, Direction = _direction, Age = _age, Warning = _warning, Width = _width,
            Hit = _hit, NextHit = _nextHit, ClockHand = _clockHand, Duration = _duration
        };
        public void RestoreSnapshotState(object state)
        {
            if (!(state is Data data)) return;
            _source = data.Source; _direction = data.Direction; _age = data.Age; _warning = data.Warning; _width = data.Width; _hit = data.Hit;
            _nextHit = data.NextHit; _clockHand = data.ClockHand;
            _duration = data.Duration;
        }
    }
}
