using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Infrastructure.Snapshot;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>机关的物理、规则登记与快照入口。每个刚体只放一个派生机关，避免快照只找到第一个模块。</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(RuleAffectedBody))]
    public class CityMechanism : MonoBehaviour, ISnapshotParticipant
    {
        public Rigidbody2D Body { get; private set; }
        protected Collider2D Shape;
        protected SpriteRenderer[] Visuals;
        private CityDamageZone[] _damageZones;
        public bool DiscardWhenMissing => false;
        protected virtual bool DynamicBody => false;
        public static bool Paused => TimeManager.Instance?.IsPaused == true;

        protected virtual void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            Shape = GetComponent<Collider2D>();
            Visuals = GetComponentsInChildren<SpriteRenderer>(true);
            _damageZones = GetComponentsInChildren<CityDamageZone>(true);
            Body.bodyType = DynamicBody ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
            if (!DynamicBody) Body.gravityScale = 0f;
            Body.constraints = RigidbodyConstraints2D.FreezeRotation;
            Body.interpolation = RigidbodyInterpolation2D.Interpolate;
            Body.useFullKinematicContacts = true;
        }

        protected void SetVisible(bool visible)
        {
            foreach (var visual in Visuals) visual.enabled = visible;
        }
        protected virtual object CaptureMechanismState() => null;
        protected virtual void RestoreMechanismState(object state) { }

        private sealed class State
        {
            public object Mechanism;
            public bool Collision;
            public bool[] Visible;
            public Color[] Colors;
            public object[] Damage;
        }
        public object CaptureSnapshotState()
        {
            var state = new State { Mechanism = CaptureMechanismState(), Collision = Shape != null && Shape.enabled,
                Visible = new bool[Visuals.Length], Colors = new Color[Visuals.Length], Damage = new object[_damageZones.Length] };
            for (int i = 0; i < Visuals.Length; i++) { state.Visible[i] = Visuals[i].enabled; state.Colors[i] = Visuals[i].color; }
            for (int i = 0; i < _damageZones.Length; i++) state.Damage[i] = _damageZones[i].CaptureCooldowns();
            return state;
        }
        public void RestoreSnapshotState(object snapshot)
        {
            if (!(snapshot is State state)) return;
            if (Shape != null) Shape.enabled = state.Collision;
            for (int i = 0; i < Visuals.Length; i++) { Visuals[i].enabled = state.Visible[i]; Visuals[i].color = state.Colors[i]; }
            for (int i = 0; i < _damageZones.Length; i++) _damageZones[i].RestoreCooldowns(state.Damage[i]);
            RestoreMechanismState(state.Mechanism);
        }
    }
}
