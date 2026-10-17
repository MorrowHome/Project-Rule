using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        [Tooltip("开启后，活动边界以出生位置为基准。旧 Boss 的世界坐标配置保持不变。")]
        public bool UseSpawnRelativeArena;
        public Vector2 ArenaSize = new Vector2(20f, 20f);
        public Vector2 ArenaOffset;
        public EnemyActor MinionPrefab;
        public EnemyActor FlyingMinionPrefab;
        public EnemyActor Summoner;
        public EnemyWeakPoint WeakPoint;
        [Tooltip("只用于身体移动与生成位置避让；伤害判定统一查询 Hurtbox。")]
        public LayerMask PlayerMask = 1;
        public Sprite ProjectileSprite;
        [Tooltip("光束原点，例如闹钟表盘轴心；未设置时使用身体中心。")]
        public Transform BeamOrigin;
        public bool RuntimeSpawned;
        public Vector2 ArenaX = new Vector2(-12f, 12f);
        [Tooltip("飞行敌人的场地高度范围，身体边缘会留在范围内。")]
        public Vector2 ArenaY = new Vector2(0f, 10f);
        public bool Defeated { get; private set; }
        public float Facing { get; private set; } = -1f;
        public EntityStatus Status { get; private set; }
        public EnemyMotor Motor { get; private set; }
        public EnemyBrain Brain { get; private set; }
        public EnemyPlatformNavigation Navigation { get; private set; }
        public ClockBeamEmitter ClockBeams { get; private set; }
        public bool IsFlying => Brain?.IsFlying ?? (Definition != null && Definition.Flying);
        public bool HasTarget => Target != null && Target.gameObject.activeInHierarchy;
        public Vector2 ArenaAnchor => _arenaInitialized ? _arenaOrigin : (Vector2)transform.position;
        public Faction Faction => Faction.Enemy;
        public string AnimationName { get; private set; } = "Idle";
        public float AnimationDuration { get; private set; } = 1f;
        public bool AnimationLoop { get; private set; }
        public event Action AnimationChanged;
        public event Action Restored;
        public event Action<EnemyActor> OnDefeated;
        public event Action<EnemyFeedback> Feedback;
        private bool _strikeAnnounced;
        private float _invulnerability;
        private float _contactCooldown;
        private BoxCollider2D _bodyCollider;
        private readonly Collider2D[] _contacts = new Collider2D[8];
        private readonly HashSet<IDamageable> _contactTargets = new HashSet<IDamageable>();
        private readonly HashSet<IDamageable> _swingTargets = new HashSet<IDamageable>();
        private readonly List<EnemyActor> _summons = new List<EnemyActor>();
        private RuleReleaseService _release;
        private Vector2 _arenaOrigin;
        private bool _arenaInitialized;
        private bool _automaticTarget;
        private float _nextBindingAttempt;

        private void Awake()
        {
            Status = GetComponent<EntityStatus>();
            Motor = GetComponent<EnemyMotor>();
            _bodyCollider = GetComponent<BoxCollider2D>();
        }
        private void Start()
        {
            if (Definition == null) { Debug.LogError("EnemyActor 缺少 EnemyDefinition。", this); enabled = false; return; }
            _arenaOrigin = transform.position;
            _arenaInitialized = true;
            if (UseSpawnRelativeArena)
            {
                Vector2 half = new Vector2(Mathf.Max(Definition.BodySize.x + .1f, ArenaSize.x),
                    Mathf.Max(Definition.BodySize.y + .1f, ArenaSize.y)) * .5f;
                Vector2 center = _arenaOrigin + ArenaOffset;
                ArenaX = new Vector2(center.x - half.x, center.x + half.x);
                ArenaY = new Vector2(center.y - half.y, center.y + half.y);
            }
            _automaticTarget = Target == null;
            ResolveSceneBindings();
            Status.InitializeVitals(Definition.Health, Definition.Energy);
            _release = new RuleReleaseService(RuleSystem.Instance, Status, AllUnlockedRuleUnlocks.Instance);
            Navigation = new EnemyPlatformNavigation(this);
            Brain = EnemyBrain.Create(this);
            Brain.Initialize();
            if (_bodyCollider != null) _bodyCollider.enabled = Brain.BodyEnabled;
            if (Definition.Kind == EnemyKind.ClockBoss) ClockBeams = new ClockBeamEmitter(this);
        }
        private void FixedUpdate()
        {
            if ((Combat == null || Target == null || (_automaticTarget && !HasTarget))
                && Time.unscaledTime >= _nextBindingAttempt) ResolveSceneBindings();
            if (Brain == null || TimeManager.Instance?.IsPaused == true || Status.IsPaused) return;
            float dt = Time.fixedDeltaTime;
            _invulnerability = Mathf.Max(0f, _invulnerability - dt);
            if (!Defeated) Status.RestoreEnergy(Definition.EnergyRegen * dt);
            Motor.AllowLedgeTraversal = false;
            Brain.Tick(dt);
            ClockBeams?.Tick(dt);
            if (_bodyCollider != null) _bodyCollider.enabled = Brain.BodyEnabled;
            if (Brain.MovementEnabled) Motor.Tick(dt);
            else Motor.Stop();
            TickContactDamage(dt);
        }
        private void TickContactDamage(float dt)
        {
            _contactCooldown = Mathf.Max(0f, _contactCooldown - dt);
            if (Defeated || Brain?.ContactDamageEnabled == false || !Definition.ContactDamage || Combat == null || _contactCooldown > 0f
                || _bodyCollider == null || !_bodyCollider.enabled) return;
            // 用身体盒检查接触，不依赖 Kinematic 刚体是否触发碰撞回调。
            // 身体接触与招式攻击共用战斗结算，持续重叠也不会每帧掉血。
            // 判定的是**对方的受击箱**（挡自伤 / 挡同阵营的规则在 CombatSystem.TryResolveTarget 里）。
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(Hurtbox.Mask);
            Vector3 scale = transform.lossyScale;
            Vector2 size = Vector2.Scale(_bodyCollider.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            size += Vector2.one * (Mathf.Max(0f, Definition.ContactPadding) * 2f);
            Vector2 center = transform.TransformPoint(_bodyCollider.offset);
            int count = Physics2D.OverlapBox(center, size, transform.eulerAngles.z, filter, _contacts);
            _contactTargets.Clear();
            for (int i = 0; i < count; i++)
            {
                if (!CombatSystem.TryResolveTarget(_contacts[i], this, out IDamageable receiver)) continue;
                if (!_contactTargets.Add(receiver)) continue;
                Combat.ApplyDamage(this, receiver, Definition.Damage, 5f);
            }
            if (_contactTargets.Count > 0) _contactCooldown = Mathf.Max(0.05f, Definition.ContactInterval);
        }
        private void OnDisable() { Navigation?.Cancel(); Motor?.Stop(); Brain?.OnDisabled(); }
        private void OnDrawGizmosSelected()
        {
            if (UseSpawnRelativeArena)
            {
                Gizmos.color = new Color(1f, .75f, .15f, .8f);
                Gizmos.DrawWireCube(ArenaAnchor + ArenaOffset, new Vector3(ArenaSize.x, ArenaSize.y, .1f));
            }
            Navigation?.DrawGizmos();
        }

        private void ResolveSceneBindings()
        {
            _nextBindingAttempt = Time.unscaledTime + .5f;
            if (Target == null || (_automaticTarget && !HasTarget))
            {
                var player = FindAnyObjectByType<PlayerController>();
                if (player != null) { Target = player.transform; _automaticTarget = true; }
            }
            if (Combat != null) return;
            foreach (var combat in FindObjectsByType<CombatSystem>(FindObjectsSortMode.None))
            {
                if (combat.gameObject.scene != gameObject.scene) continue;
                Combat = combat;
                return;
            }
            var services = new GameObject("EnemyCombatService");
            SceneManager.MoveGameObjectToScene(services, gameObject.scene);
            Combat = services.AddComponent<CombatSystem>();
        }

        public void Cue(string action, float duration, bool loop = false)
        {
            Navigation?.Cancel();
            AnimationName = action;
            AnimationDuration = Mathf.Max(0.05f, duration);
            AnimationLoop = loop;
            _swingTargets.Clear();
            _strikeAnnounced = false;
            Motor.Intent = Vector2.zero;
            Motor.SpeedMultiplier = 1f;
            AnimationChanged?.Invoke();
        }
        public void FaceTarget()
        {
            if (HasTarget && Mathf.Abs(Target.position.x - transform.position.x) > 0.1f)
                Facing = Mathf.Sign(Target.position.x - transform.position.x);
        }
        public void FaceDirection(Vector2 direction)
        {
            if (Mathf.Abs(direction.x) > 0.1f) Facing = Mathf.Sign(direction.x);
        }
        public float Distance => !HasTarget ? float.PositiveInfinity : Vector2.Distance(transform.position, Target.position);
        public Vector2 TowardTarget => !HasTarget ? Vector2.zero : ((Vector2)(Target.position - transform.position)).normalized;
        public float Duration(string action, float fallback = 0.8f) => Definition.Duration(action, fallback);

        public void ApplyDamage(in DamageInfo damage)
        {
            if (Defeated || Brain == null) return;
            if (damage.Amount < 0f) { Status.ApplyHealthDelta(-damage.Amount); return; }
            if (Brain.DamageLocked || _invulnerability > 0f) return;
            bool wasHurt = Brain.StateName == "Hurt";
            if (damage.Amount > 0f)
            {
                float next = Brain.ClampHealth(Status.Health - damage.Amount);
                float appliedDamage = Mathf.Max(0f, Status.Health - next);
                Status.ApplyHealthDelta(next - Status.Health);
                _invulnerability = 0.12f;
                Brain.OnHit(appliedDamage);
                if (appliedDamage > 0f)
                    EmitHitFeedback(appliedDamage, damage.Attacker, !wasHurt && Brain.StateName == "Hurt", next <= 0f);
            }
            // 小怪仅在本次破韧时完整击退，避免轻击绕过硬直保护持续推退或叠加冲量。
            // 质量削弱与挂账的纯击退仍遵守规则，Boss 原有质量约束不变。
            bool staggered = !wasHurt && Brain.StateName == "Hurt";
            if (Motor.MassReduced || (!Definition.IsBoss && (staggered || damage.Amount == 0f)))
                Motor.AddImpulse(TowardAttacker(damage.Attacker) * -damage.Knockback);
        }
        private Vector2 TowardAttacker(object attacker)
        {
            Component component = attacker as Component;
            if (component == null) return Vector2.left * Facing;
            return ((Vector2)(component.transform.position - transform.position)).normalized;
        }

        /// <summary>仅通知已经发生的扣血；弱点也复用此入口，不改变身体的血量或硬直。</summary>
        public void EmitHitFeedback(float amount, object attacker, bool staggered = false, bool lethal = false,
            Component subject = null)
        {
            Vector2 direction = -TowardAttacker(attacker);
            if (direction.sqrMagnitude < .001f) direction = Vector2.right * -Facing;
            Vector3 position = subject != null ? subject.transform.position : transform.position;
            if (subject == null && _bodyCollider != null)
            {
                // DamageInfo 没有接触点。取身体迎击面的边界，避免所有火花都在腹部爆开。
                Vector2 center = _bodyCollider.bounds.center;
                Vector2 extent = _bodyCollider.bounds.extents;
                float distance = 1f / Mathf.Max(Mathf.Abs(direction.x) / Mathf.Max(.01f, extent.x),
                    Mathf.Abs(direction.y) / Mathf.Max(.01f, extent.y));
                position = center - direction * distance * .85f;
            }
            Feedback?.Invoke(new EnemyFeedback(EnemyFeedbackKind.Hit, position, direction, subject,
                amount, staggered, lethal));
        }
        public void Attack(float reach, bool newPulse = false)
        {
            if (Combat == null || !HasTarget) return;
            if (newPulse) _swingTargets.Clear();
            Vector2 center = (Vector2)transform.position + Vector2.right * Facing * reach * 0.55f;
            // 持续判定窗不逐帧发特效；连斩的 newPulse 则每刀单独通知。
            if (newPulse || !_strikeAnnounced)
            {
                _strikeAnnounced = true;
                EmitFeedback(EnemyFeedbackKind.Strike, center, Vector2.right * Facing);
            }
            Combat.PerformAttack(this, center, new Vector2(reach, Definition.BodySize.y * 0.8f),
                Definition.Damage, 5f, _swingTargets);
        }
        public void Shoot(Vector2 direction, float speed = 8f, float gravity = 0f, float radius = 0.22f, int visualIndex = -1, Vector2? origin = null)
        {
            if (HasTarget) EnemyProjectile.Spawn(this, direction, speed, gravity, radius, visualIndex, origin);
        }
        public void AreaAttack(float radius)
        {
            if (Combat == null) return;
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(Hurtbox.Mask);
            int count = Physics2D.OverlapCircle(transform.position, radius, filter, _contacts);
            _contactTargets.Clear();
            for (int i = 0; i < count; i++)
            {
                if (CombatSystem.TryResolveTarget(_contacts[i], this, out IDamageable target) && _contactTargets.Add(target))
                    Combat.ApplyDamage(this, target, Definition.Damage, 4f);
            }
            EmitFeedback(EnemyFeedbackKind.Strike, transform.position, Motor.Up);
        }
        /// <summary>围绕锁定方向展开等角扇形；每发仍使用共用弹池与战斗结算。</summary>
        public void ShootFan(Vector2 direction, int count, float spread, float speed, float gravity = 0f,
            float radius = .22f, int visualIndex = -1, Vector2? origin = null)
        {
            count = Mathf.Clamp(count, 1, 48);
            float middle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            for (int i = 0; i < count; i++)
            {
                float angle = (middle + (count == 1 ? 0f : Mathf.Lerp(-spread * .5f, spread * .5f, i / (float)(count - 1)))) * Mathf.Deg2Rad;
                Shoot(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), speed, gravity, radius, visualIndex, origin);
            }
        }
        public void ShootRing(int count, float speed, float offset = 0f, float radius = .2f, Vector2? origin = null)
        {
            count = Mathf.Clamp(count, 1, 48);
            for (int i = 0; i < count; i++)
            {
                float angle = (offset + i * 360f / count) * Mathf.Deg2Rad;
                Shoot(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), speed, 0f, radius, origin: origin);
            }
        }
        public void AttackArea(Vector2 center, Vector2 size)
        {
            if (Combat == null || !HasTarget) return;
            _swingTargets.Clear();
            Combat.PerformAttack(this, center, size, Definition.Damage, 7f, _swingTargets);
            EmitFeedback(EnemyFeedbackKind.AreaStrike, center, Motor.Up);
        }
        public void Beam(float angle, float width, float warning = 0.65f)
        {
            if (HasTarget) EnemyBeam.Spawn(this, angle, width, warning);
        }
        public bool CastRule() => CastRule(Definition.RuleAttribute, Definition.RuleRelation);
        public bool CanCastRule => _release != null && RuleSystem.Instance != null
            && _release.CanRelease(Definition.RuleAttribute, Definition.RuleRelation);

        /// <summary>临时选择施法组合，不改团队共享的 EnemyDefinition 资产。</summary>
        public bool CastRule(AttributeId attribute, RelationId relation)
        {
            if (_release == null || RuleSystem.Instance == null) return false;
            bool released = _release.TryRelease(attribute, relation, RuleSource.Enemy).Succeeded;
            if (released) EmitFeedback(EnemyFeedbackKind.RuleCast, transform.position, Motor.Up);
            return released;
        }
        public void EmitFeedback(EnemyFeedbackKind kind, Vector3 position, Vector2 direction, Component subject = null)
            => Feedback?.Invoke(new EnemyFeedback(kind, position, direction, subject));
        public void SpawnMinions(bool flying, int count)
        {
            EnemyActor prefab = flying ? FlyingMinionPrefab : MinionPrefab;
            if (prefab == null) return;
            PruneSummons();
            count = Mathf.Min(count, 8 - _summons.Count);
            int spawned = 0;
            var spawnFilter = new ContactFilter2D { useTriggers = false };
            spawnFilter.SetLayerMask(Motor.SolidMask | Motor.BodyBlockMask | PlayerMask);
            Vector2 size = prefab.Definition.BodySize;
            // 开启敌人身体阻挡后，出生点也必须在 Boss 体外；否则双方会从重叠状态互相卡住。
            for (int candidate = 0; candidate < 16 && spawned < count; candidate++)
            {
                float side = (candidate & 1) == 0 ? -1f : 1f;
                float distance = (Definition.BodySize.x + size.x) * .5f + .25f
                    + (candidate / 2) * (size.x + .25f);
                Vector2 position = (Vector2)transform.position + Vector2.right * (side * distance)
                    + Motor.Up * (flying ? 2f : (size.y - Definition.BodySize.y) * .5f + .05f);
                if (flying && ArenaY.y - ArenaY.x > size.y + .3f)
                    position.y = Mathf.Clamp(position.y, ArenaY.x + size.y * .5f + .15f,
                        ArenaY.y - size.y * .5f - .15f);
                if (position.x - size.x * .5f < ArenaX.x || position.x + size.x * .5f > ArenaX.y) continue;
                if (flying && (position.y - size.y * .5f < ArenaY.x || position.y + size.y * .5f > ArenaY.y)) continue;
                if (Physics2D.OverlapBox(position, size + Vector2.one * .02f, 0f, spawnFilter, _contacts) > 0) continue;
                EnemyActor child = Instantiate(prefab, position, Quaternion.identity);
                child.Target = Target; child.Combat = Combat; child.PlayerMask = PlayerMask;
                child.Motor.SolidMask = Motor.SolidMask; child.Motor.BodyBlockMask = Motor.BodyBlockMask;
                child.ArenaX = ArenaX; child.ArenaY = ArenaY;
                child.Summoner = this; child.RuntimeSpawned = true;
                _summons.Add(child);
                spawned++;
            }
            if (spawned > 0) EmitFeedback(EnemyFeedbackKind.Summon, transform.position, Motor.Up);
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
            Brain?.OnDisabled();
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
            public object ClockBeams;
            public object Navigation;
            public Vector2 Velocity, Impulse;
            public float Facing, Invulnerability, ContactCooldown;
            public bool Defeated;
            public bool StrikeAnnounced;
            public EnemyActor[] Summons;
            public Vector2 GravityDrift;
            public IDamageable[] SwingTargets;
        }
        public object CaptureSnapshotState() => new Data
        {
            Brain = Brain?.Capture(), Velocity = Motor.Velocity, Impulse = Motor.CaptureImpulse(),
            GravityDrift = Motor.GravityDrift,
            ClockBeams = ClockBeams?.Capture(),
            Navigation = Navigation?.Capture(),
            Facing = Facing, Invulnerability = _invulnerability, Defeated = Defeated, Summons = _summons.ToArray(),
            ContactCooldown = _contactCooldown,
            StrikeAnnounced = _strikeAnnounced,
            SwingTargets = new List<IDamageable>(_swingTargets).ToArray()
        };
        public void RestoreSnapshotState(object state)
        {
            if (!(state is Data data)) return;
            Defeated = data.Defeated; Facing = data.Facing; _invulnerability = data.Invulnerability;
            _contactCooldown = data.ContactCooldown;
            _summons.Clear(); _summons.AddRange(data.Summons);
            Brain?.Restore(data.Brain);
            if (_bodyCollider != null) _bodyCollider.enabled = Brain?.BodyEnabled ?? true;
            ClockBeams?.Restore(data.ClockBeams);
            _swingTargets.Clear();
            foreach (IDamageable target in data.SwingTargets) _swingTargets.Add(target);
            Motor.Restore(data.Velocity, data.Impulse, data.GravityDrift);
            Navigation?.Restore(data.Navigation);
            _strikeAnnounced = data.StrikeAnnounced;
            Restored?.Invoke();
        }
    }
}
