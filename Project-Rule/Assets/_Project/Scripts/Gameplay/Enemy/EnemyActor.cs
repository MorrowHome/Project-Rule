using System;
using System.Collections.Generic;
using UnityEngine;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Combat;
using RuleGame.Core.Rules;
using RuleGame.Infrastructure;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure.Time;
using RuleGame.Infrastructure.Snapshot;

namespace RuleGame.Gameplay.Enemy
{
    /// <summary>实体接线与伤害入口。AI、移动、表现独立，规则费用和伤害计算复用团队权威。</summary>
    [RequireComponent(typeof(EntityStatus), typeof(EnemyMotor))]
    public sealed class EnemyActor : MonoBehaviour, IDamageable, IFactionMember, ISnapshotParticipant
    {
        public EnemyDefinition Definition;
        public Transform Target;
        public CombatSystem Combat;
        public EnemyActor MinionPrefab;
        public EnemyActor FlyingMinionPrefab;
        public EnemyActor Summoner;
        public EnemyWeakPoint WeakPoint;
        public LayerMask PlayerMask = 1;
        public Sprite ProjectileSprite;
        public bool RuntimeSpawned;
        public Vector2 ArenaX = new Vector2(-12f, 12f);
        public bool Defeated { get; private set; }
        public float Facing { get; private set; } = -1f;
        public EntityStatus Status { get; private set; }
        public EnemyMotor Motor { get; private set; }
        public EnemyBrain Brain { get; private set; }
        public Faction Faction => Faction.Enemy;
        public string AnimationName { get; private set; } = "Idle";
        public float AnimationDuration { get; private set; } = 1f;
        public bool AnimationLoop { get; private set; }
        public event Action AnimationChanged;
        public event Action Restored;
        public event Action<EnemyActor> OnDefeated;
        private float _invulnerability;
        private float _contactCooldown;
        private BoxCollider2D _bodyCollider;
        private readonly Collider2D[] _contacts = new Collider2D[8];
        private readonly HashSet<IDamageable> _contactTargets = new HashSet<IDamageable>();
        private readonly HashSet<IDamageable> _swingTargets = new HashSet<IDamageable>();
        private readonly List<EnemyActor> _summons = new List<EnemyActor>();
        private RuleReleaseService _release;

        private void Awake()
        {
            Status = GetComponent<EntityStatus>();
            Motor = GetComponent<EnemyMotor>();
            _bodyCollider = GetComponent<BoxCollider2D>();
        }
        private void Start()
        {
            if (Definition == null) { Debug.LogError("EnemyActor 缺少 EnemyDefinition。", this); enabled = false; return; }
            Status.InitializeVitals(Definition.Health, Definition.Energy);
            _release = new RuleReleaseService(RuleSystem.Instance, Status, AllUnlockedRuleUnlocks.Instance);
            Brain = EnemyBrain.Create(this);
            Brain.Initialize();
        }
        private void FixedUpdate()
        {
            if (Brain == null || TimeManager.Instance?.IsPaused == true || Status.IsPaused) return;
            float dt = Time.fixedDeltaTime;
            _invulnerability = Mathf.Max(0f, _invulnerability - dt);
            if (!Defeated) Status.RestoreEnergy(Definition.EnergyRegen * dt);
            Brain.Tick(dt);
            Motor.Tick(dt);
            TickContactDamage(dt);
        }
        private void TickContactDamage(float dt)
        {
            _contactCooldown = Mathf.Max(0f, _contactCooldown - dt);
            if (Defeated || !Definition.ContactDamage || Combat == null || _contactCooldown > 0f
                || _bodyCollider == null || !_bodyCollider.enabled) return;
            // 用身体盒检查接触，不依赖 Kinematic 刚体是否触发碰撞回调。
            // 身体接触与招式攻击共用战斗结算，持续重叠也不会每帧掉血。
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(PlayerMask);
            Vector3 scale = transform.lossyScale;
            Vector2 size = Vector2.Scale(_bodyCollider.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            Vector2 center = transform.TransformPoint(_bodyCollider.offset);
            int count = Physics2D.OverlapBox(center, size, transform.eulerAngles.z, filter, _contacts);
            _contactTargets.Clear();
            for (int i = 0; i < count; i++)
            {
                var receiver = _contacts[i].GetComponentInParent<IDamageable>();
                var faction = _contacts[i].GetComponentInParent<IFactionMember>();
                if (receiver == null || faction?.Faction != Faction.Player || !_contactTargets.Add(receiver)) continue;
                Combat.ApplyDamage(this, receiver, Definition.Damage, 5f);
            }
            if (_contactTargets.Count > 0) _contactCooldown = Mathf.Max(0.05f, Definition.ContactInterval);
        }
        private void OnDisable() => Motor?.Stop();

        public void Cue(string action, float duration, bool loop = false)
        {
            AnimationName = action;
            AnimationDuration = Mathf.Max(0.05f, duration);
            AnimationLoop = loop;
            _swingTargets.Clear();
            Motor.Intent = Vector2.zero;
            Motor.SpeedMultiplier = 1f;
            AnimationChanged?.Invoke();
        }
        public void FaceTarget()
        {
            if (Target != null && Mathf.Abs(Target.position.x - transform.position.x) > 0.1f)
                Facing = Mathf.Sign(Target.position.x - transform.position.x);
        }
        public float Distance => Target == null ? float.PositiveInfinity : Vector2.Distance(transform.position, Target.position);
        public Vector2 TowardTarget => Target == null ? Vector2.zero : ((Vector2)(Target.position - transform.position)).normalized;
        public float Duration(string action, float fallback = 0.8f) => Definition.Duration(action, fallback);

        public void ApplyDamage(in DamageInfo damage)
        {
            if (Defeated || Brain == null) return;
            if (damage.Amount < 0f) { Status.ApplyHealthDelta(-damage.Amount); return; }
            if (Brain.DamageLocked || _invulnerability > 0f) return;
            if (damage.Amount > 0f)
            {
                float next = Brain.ClampHealth(Status.Health - damage.Amount);
                Status.ApplyHealthDelta(next - Status.Health);
                _invulnerability = 0.12f;
                Brain.OnHit();
            }
            // 挂账产生的 0 伤害仍可击退；Boss 的免疫是实体属性，不在规则类里特判。
            if (Definition.Kind == EnemyKind.BrickMinion || Definition.Kind == EnemyKind.BatMinion)
                Motor.AddImpulse(TowardAttacker(damage.Attacker) * -damage.Knockback);
        }
        private Vector2 TowardAttacker(object attacker)
        {
            Component component = attacker as Component;
            if (component == null) return Vector2.left * Facing;
            return ((Vector2)(component.transform.position - transform.position)).normalized;
        }
        public void Attack(float reach, bool newPulse = false)
        {
            if (Combat == null || Target == null) return;
            if (newPulse) _swingTargets.Clear();
            Vector2 center = (Vector2)transform.position + Vector2.right * Facing * reach * 0.55f;
            Combat.PerformAttack(this, center, new Vector2(reach, Definition.BodySize.y * 0.8f),
                PlayerMask, Definition.Damage, 5f, _swingTargets);
        }
        public void Shoot(Vector2 direction, float speed = 8f, float gravity = 0f, float radius = 0.22f)
            => EnemyProjectile.Spawn(this, direction, speed, gravity, radius);
        public void Beam(float angle, float width, float warning = 0.65f)
            => EnemyBeam.Spawn(this, angle, width, warning);
        public bool CastRule()
        {
            if (_release == null || RuleSystem.Instance == null) return false;
            return _release.TryRelease(Definition.RuleAttribute, Definition.RuleRelation, RuleSource.Enemy).Succeeded;
        }
        public void SpawnMinions(bool flying, int count)
        {
            EnemyActor prefab = flying ? FlyingMinionPrefab : MinionPrefab;
            if (prefab == null) return;
            PruneSummons();
            count = Mathf.Min(count, 8 - _summons.Count);
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = flying ? new Vector3((i - (count - 1) * 0.5f) * 1.5f, 2f, 0f)
                    : new Vector3((i - (count - 1) * 0.5f) * 1.3f, -Definition.BodySize.y * 0.5f + 0.8f, 0f);
                EnemyActor child = Instantiate(prefab, transform.position + offset, Quaternion.identity);
                child.Target = Target; child.Combat = Combat; child.Summoner = this; child.RuntimeSpawned = true;
                _summons.Add(child);
            }
        }
        private void PruneSummons() => _summons.RemoveAll(item => item == null || !item.gameObject.activeInHierarchy || item.Defeated);
        public int SummonCount { get { PruneSummons(); return _summons.Count; } }
        public void CommandSummons()
        {
            PruneSummons();
            foreach (EnemyActor summon in _summons) summon.Brain?.CommandAttack();
        }
        public void MarkDefeated()
        {
            if (Defeated) return;
            Defeated = true;
            Motor.Stop();
            foreach (EnemyActor summon in _summons) if (summon != null) summon.gameObject.SetActive(false);
            OnDefeated?.Invoke(this);
            Combat?.NotifyDied(this);
        }
        public void RestoreLifeFlag(bool defeated) { Defeated = defeated; _invulnerability = 0f; Restored?.Invoke(); }

        public bool DiscardWhenMissing => RuntimeSpawned;
        private sealed class Data
        {
            public object Brain;
            public Vector2 Velocity, Impulse;
            public float Facing, Invulnerability, ContactCooldown;
            public bool Defeated;
            public EnemyActor[] Summons;
            public IDamageable[] SwingTargets;
        }
        public object CaptureSnapshotState() => new Data
        {
            Brain = Brain?.Capture(), Velocity = Motor.Velocity, Impulse = Motor.CaptureImpulse(),
            Facing = Facing, Invulnerability = _invulnerability, Defeated = Defeated, Summons = _summons.ToArray(),
            ContactCooldown = _contactCooldown,
            SwingTargets = new List<IDamageable>(_swingTargets).ToArray()
        };
        public void RestoreSnapshotState(object state)
        {
            if (!(state is Data data)) return;
            Defeated = data.Defeated; Facing = data.Facing; _invulnerability = data.Invulnerability;
            _contactCooldown = data.ContactCooldown;
            _summons.Clear(); _summons.AddRange(data.Summons);
            Brain?.Restore(data.Brain);
            _swingTargets.Clear();
            foreach (IDamageable target in data.SwingTargets) _swingTargets.Add(target);
            Motor.Restore(data.Velocity, data.Impulse);
            Restored?.Invoke();
        }
    }
}
