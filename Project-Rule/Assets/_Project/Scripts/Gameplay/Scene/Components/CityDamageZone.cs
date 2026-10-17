using System.Collections.Generic;
using UnityEngine;
using RuleGame.Infrastructure;
using RuleGame.Gameplay.Combat;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>显式重叠查询兼容 Kinematic 主角；只提交基础伤害，规则结算全部留给 CombatSystem。</summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class CityDamageZone : MonoBehaviour
    {
        public float Damage = 15f;
        public float Knockback = 6f;
        [Min(.05f)] public float RepeatInterval = .6f;
        public bool Armed = true;
        public CombatSystem Combat;
        private Collider2D _shape;
        private readonly Collider2D[] _hits = new Collider2D[24];
        private readonly HashSet<IDamageable> _seen = new HashSet<IDamageable>();
        private readonly Dictionary<IDamageable, float> _cooldowns = new Dictionary<IDamageable, float>();
        private readonly List<IDamageable> _keys = new List<IDamageable>();
        private void Awake() => _shape = GetComponent<Collider2D>();
        private void Start() { if (Combat == null) Combat = FindAnyObjectByType<CombatSystem>(); }
        private void FixedUpdate()
        {
            if (CityMechanism.Paused) return;
            _keys.Clear(); _keys.AddRange(_cooldowns.Keys);
            foreach (var target in _keys)
            {
                float next = _cooldowns[target] - Time.fixedDeltaTime;
                if (next <= 0f) _cooldowns.Remove(target); else _cooldowns[target] = next;
            }
            if (!Armed || !_shape.enabled || Combat == null) return;
            var filter = new ContactFilter2D { useTriggers = false };
            int count = _shape.Overlap(filter, _hits);
            _seen.Clear();
            for (int i = 0; i < count; i++)
            {
                var target = _hits[i].GetComponentInParent<IDamageable>();
                var faction = _hits[i].GetComponentInParent<IFactionMember>();
                if (target == null || faction?.Faction != Faction.Player || !_seen.Add(target) || _cooldowns.ContainsKey(target)) continue;
                if (Combat.ApplyDamage(this, target, Damage, Knockback) > 0) _cooldowns[target] = RepeatInterval;
            }
        }
        private sealed class State { public bool Armed; public Dictionary<IDamageable, float> Cooldowns; }
        public object CaptureCooldowns() => new State { Armed = Armed, Cooldowns = new Dictionary<IDamageable, float>(_cooldowns) };
        public void RestoreCooldowns(object snapshot)
        {
            _cooldowns.Clear();
            if (!(snapshot is State state)) return;
            Armed = state.Armed;
            foreach (var entry in state.Cooldowns) _cooldowns[entry.Key] = entry.Value;
        }
    }
}
