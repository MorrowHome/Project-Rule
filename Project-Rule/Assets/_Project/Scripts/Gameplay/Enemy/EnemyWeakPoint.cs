using UnityEngine;
using RuleGame.Gameplay.Combat;
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
        private Hurtbox _hurtbox;
        public Faction Faction => Faction.Enemy;
        public bool DiscardWhenMissing => false;
        public void Open(float health) { _maximum = Health = health; Broken = false; Opened = true; SetVisible(true); }
        public void Close() { Opened = false; SetVisible(false); }
        private void SetVisible(bool visible)
        {
            GetComponent<Collider2D>().enabled = visible;
            GetComponent<SpriteRenderer>().enabled = visible;

            // ⚠️ 命中判定看的是**受击箱**，所以它必须跟着开合 ——
            //    只关自己的身体碰撞体的话，收起来的弱点照样挨打。
            if (_hurtbox == null) _hurtbox = GetComponentInChildren<Hurtbox>(true);
            if (_hurtbox != null) _hurtbox.SetHittable(visible);
        }
        private void Start() { if (!Opened) Close(); }
        public void ApplyDamage(in DamageInfo damage)
        {
            if (!Opened || Broken) return;
            Health = Mathf.Clamp(Health - damage.Amount, 0f, _maximum);
            Broken = Health <= 0f;
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
