using UnityEngine;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>等待、预警、出拳、停留、收回；只有实际出拳与停留阶段伤人。</summary>
    [RequireComponent(typeof(CityDamageZone))]
    public sealed class CityHammer : CityMechanism
    {
        public Vector2 Stroke = new Vector2(0f, -4f);
        public float RestSeconds = 1.2f, WarningSeconds = .7f, StrikeSpeed = 12f, ReturnSpeed = 3f, HoldSeconds = .3f;
        public bool Running = true;
        private enum Phase { Rest, Warning, Strike, Hold, Return }
        private Phase _phase;
        private float _elapsed, _progress;
        private Vector2 _origin;
        private CityDamageZone _damage;
        protected override void Awake() { base.Awake(); _origin = Body.position; _damage = GetComponent<CityDamageZone>(); _damage.Armed = false; }
        public void SetOn(bool on) => Running = on;
        private void FixedUpdate()
        {
            if (Paused || !Running) return;
            _elapsed += Time.fixedDeltaTime;
            switch (_phase)
            {
                case Phase.Rest: if (_elapsed >= RestSeconds) Enter(Phase.Warning); break;
                case Phase.Warning: if (_elapsed >= WarningSeconds) Enter(Phase.Strike); break;
                case Phase.Strike:
                    _progress = Mathf.MoveTowards(_progress, 1f, Mathf.Max(.1f, StrikeSpeed) * Time.fixedDeltaTime / Mathf.Max(.1f, Stroke.magnitude));
                    if (_progress >= 1f) Enter(Phase.Hold);
                    break;
                case Phase.Hold: if (_elapsed >= HoldSeconds) Enter(Phase.Return); break;
                case Phase.Return:
                    _progress = Mathf.MoveTowards(_progress, 0f, Mathf.Max(.1f, ReturnSpeed) * Time.fixedDeltaTime / Mathf.Max(.1f, Stroke.magnitude));
                    if (_progress <= 0f) Enter(Phase.Rest);
                    break;
            }
            Body.MovePosition(_origin + Stroke * _progress);
        }
        private void Enter(Phase phase)
        {
            _phase = phase; _elapsed = 0f;
            _damage.Armed = phase == Phase.Strike || phase == Phase.Hold;
            if (Visuals.Length > 0) Visuals[0].color = phase == Phase.Warning ? new Color(1f, .7f, .15f)
                : _damage.Armed ? new Color(.9f, .25f, .2f) : new Color(.55f, .35f, .3f);
        }
        private struct State { public Phase Phase; public float Elapsed, Progress; public bool Running; }
        protected override object CaptureMechanismState() => new State { Phase = _phase, Elapsed = _elapsed, Progress = _progress, Running = Running };
        protected override void RestoreMechanismState(object state)
        {
            var s = (State)state; Enter(s.Phase); _elapsed = s.Elapsed; _progress = s.Progress; Running = s.Running;
        }
        private void OnDrawGizmosSelected() { Gizmos.color = Color.red; Gizmos.DrawLine(transform.position, transform.position + (Vector3)Stroke); }
    }
}
