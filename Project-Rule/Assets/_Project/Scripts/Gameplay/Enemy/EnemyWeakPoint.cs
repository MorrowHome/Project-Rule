using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Snapshot;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>发条是独立可攻击实体；眩晕时打开，关闭时不参与攻击查询。</summary>
    public sealed class EnemyWeakPoint : MonoBehaviour, IDamageable, IFactionMember, ISnapshotParticipant
    {
        public EnemyActor Owner;
        public bool Broken { get; private set; }
        public bool Opened { get; private set; }
        public float Health { get; private set; }
        private float _maximum;
        private Vector3 _normalOffset;
        public Faction Faction => Faction.Enemy;
        public bool DiscardWhenMissing => false;
        private void Awake() => _normalOffset = transform.localPosition;
        private void LateUpdate()
        {
            if (Owner == null || Owner.Motor == null) return;
            // 身体翻到天花板时，发条也应露在可攻击的一侧，不能继续藏在身体上方的墙内。
            transform.localPosition = Owner.Motor.Up.y < 0f
                ? new Vector3(_normalOffset.x, -_normalOffset.y, _normalOffset.z) : _normalOffset;
        }
        public void Open(float health) { _maximum = Health = health; Broken = false; Opened = true; SetVisible(true); }
        public void Close() { Opened = false; SetVisible(false); }
        private void SetVisible(bool visible)
        {
            GetComponent<Collider2D>().enabled = visible;
            GetComponent<SpriteRenderer>().enabled = visible;
        }
        private void Start() { if (!Opened) Close(); }
        public void ApplyDamage(in DamageInfo damage)
        {
            if (!Opened || Broken) return;
            float previous = Health;
            Health = Mathf.Clamp(Health - damage.Amount, 0f, _maximum);
            Broken = Health <= 0f;
            if (Health < previous)
                Owner?.EmitHitFeedback(previous - Health, damage.Attacker, Broken, false, this);
        }
        private struct Data { public bool Open, Broken; public float Health, Maximum; }
        public object CaptureSnapshotState() => new Data { Open = Opened, Broken = Broken, Health = Health, Maximum = _maximum };
        public void RestoreSnapshotState(object state)
        {
            if (!(state is Data data)) return;
            Opened = data.Open; Broken = data.Broken; Health = data.Health; _maximum = data.Maximum;
            SetVisible(Opened);
        }
    }
}
