using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure;

namespace RuleGame.Gameplay.Scene.DemonCastle
{
    /// <summary>开关点火的箱装烟花；圆形范围只查询受击箱，按承受方去重。</summary>
    public sealed class DemonFirework : CityMechanism, ISwitchTarget
    {
        [Header("烟花")]
        [Min(.1f)] [InspectorName("引信时间（秒）")] public float Fuse = 1.2f;
        [Min(.1f)] [InspectorName("爆炸半径")] public float Radius = 3f;
        [Min(0f)] [InspectorName("基础伤害")] public float Damage = 30f;
        [Min(0f)] [InspectorName("击退强度")] public float Knockback = 10f;
        [InspectorName("共享战斗系统（可空）")]
        public CombatSystem Combat;
        [InspectorName("烟花粒子")]
        public ParticleSystem Burst;
        [InspectorName("爆炸事件")]
        public UnityEvent OnExploded = new UnityEvent();
        public bool Armed { get; private set; }
        public bool Spent { get; private set; }
        public float Remaining { get; private set; }
        private bool _burstPaused;
        private readonly List<Collider2D> _hits = new List<Collider2D>(32);
        private readonly HashSet<IDamageable> _seen = new HashSet<IDamageable>();
        private readonly List<IDamageable> _targets = new List<IDamageable>(16);
        private void Start() { if (Combat == null) Combat = FindAnyObjectByType<CombatSystem>(); }
        public void SetSwitchState(bool on) => SetOn(on);
        public void SetOn(bool on)
        {
            if (!on || Paused || Armed || Spent) return;
            Armed = true; Remaining = Mathf.Max(.1f, Fuse);
        }
        private void FixedUpdate()
        {
            if (Paused || !Armed || Spent) return;
            Remaining = Mathf.Max(0f, Remaining - Time.fixedDeltaTime);
            if (Visuals.Length > 0) Visuals[0].color = Color.Lerp(new Color(.4f, .15f, .3f), new Color(1f, .5f, .2f), .5f + .5f * Mathf.Sin(Remaining * 22f));
            if (Remaining > 0f) return;
            if (Combat == null) Combat = FindAnyObjectByType<CombatSystem>();
            if (Combat == null || Combat.Processor == null) return;
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(Hurtbox.Mask);
            int count = Physics2D.OverlapCircle(Body.position, Radius, filter, _hits);
            _seen.Clear(); _targets.Clear();
            for (int i = 0; i < count; i++)
                if (CombatSystem.TryResolveTarget(_hits[i], this, out var target) && _seen.Add(target)) _targets.Add(target);
            if (_targets.Count > 0) Combat.Processor.Process(new AttackInfo
            { Attacker = this, Origin = Body.position, BaseAmount = Damage, Knockback = Knockback, Targets = _targets });
            Spent = true; Armed = false;
            Shape.enabled = false; SetVisible(false);
            if (Burst != null) Burst.Play(true);
            OnExploded.Invoke();
        }
        private void Update()
        {
            if (Burst == null) return;
            if (Paused && Burst.isPlaying) { Burst.Pause(true); _burstPaused = true; }
            else if (!Paused && _burstPaused) { Burst.Play(true); _burstPaused = false; }
        }
        private struct State { public bool Armed, Spent; public float Remaining; }
        protected override object CaptureMechanismState() => new State { Armed = Armed, Spent = Spent, Remaining = Remaining };
        protected override void RestoreMechanismState(object state)
        {
            var s = (State)state; Armed = s.Armed; Spent = s.Spent; Remaining = s.Remaining;
            _burstPaused = false;
            if (Burst != null) Burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        private void OnDrawGizmosSelected()
        { Gizmos.color = new Color(1f, .35f, .1f, .7f); Gizmos.DrawWireSphere(transform.position, Radius); }
    }
}
