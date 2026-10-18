using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>两站式电梯、门和站间列车共用路径运动；关卡里只需设置终点偏移和联动开关。</summary>
    [DefaultExecutionOrder(-40)] // 承载位移先提交，随后由 PlayerController 在自己的 Slide 内统一解算。
    public sealed class CityPathMotor : CityMechanism, ISwitchTarget
    {
        public Vector2 EndOffset = new Vector2(0f, 5f);
        [Min(.1f)] public float Speed = 2f;
        public bool CarryRiders = true;
        public bool AutoReturn;
        public bool TargetEnd;
        public bool BlockActors;
        public float StationWait = 1f;
        public LayerMask SolidMask = 1 << 8;
        public UnityEvent<bool> OnArrived = new UnityEvent<bool>();
        private Vector2 _origin;
        private float _progress, _wait;
        private bool _arrived;
        private readonly Collider2D[] _overlaps = new Collider2D[24];
        private readonly RaycastHit2D[] _hits = new RaycastHit2D[16];
        private readonly HashSet<PlayerController> _riders = new HashSet<PlayerController>();

        protected override void Awake() { base.Awake(); _origin = Body.position; }
        public void SetOn(bool on) { if (TargetEnd != on) { TargetEnd = on; _arrived = false; _wait = 0f; } }

        /// <summary>开关驱动入口（<see cref="ISwitchTarget"/>）：与 <see cref="SetOn"/> 是同一件事。</summary>
        public void SetSwitchState(bool on) => SetOn(on);
        private void FixedUpdate()
        {
            if (Paused || EndOffset.sqrMagnitude < .001f) return;
            if (_wait > 0f) { _wait -= Time.fixedDeltaTime; return; }
            float next = Mathf.MoveTowards(_progress, TargetEnd ? 1f : 0f, Speed * Time.fixedDeltaTime / EndOffset.magnitude);
            Vector2 delta = _origin + EndOffset * next - Body.position;
            if (delta.sqrMagnitude > .000001f)
            {
                float fraction = SafeFraction(Body, delta, null, SolidMask | (BlockActors ? (1 << 9) | 1 : 0));
                FindRiders();
                foreach (var rider in _riders)
                    fraction = Mathf.Min(fraction, SafeFraction(rider.GetComponent<Rigidbody2D>(), delta, Shape, SolidMask));
                delta *= fraction;
                _progress = Mathf.Clamp01(_progress + Vector2.Dot(delta, EndOffset) / EndOffset.sqrMagnitude);
                foreach (var rider in _riders) rider.CarryOnPlatform(delta, Shape);
                Body.MovePosition(Body.position + delta);
            }
            if (!_arrived && Mathf.Abs(_progress - (TargetEnd ? 1f : 0f)) < .001f)
            {
                _arrived = true; OnArrived.Invoke(TargetEnd);
                if (AutoReturn) { TargetEnd = !TargetEnd; _arrived = false; _wait = Mathf.Max(.05f, StationWait); }
            }
        }
        private void FindRiders()
        {
            _riders.Clear();
            if (!CarryRiders || Shape == null) return;
            Bounds b = Shape.bounds;
            var filter = new ContactFilter2D { useTriggers = false };
            int count = Physics2D.OverlapBox(b.center, b.size + new Vector3(.1f, .35f), 0f, filter, _overlaps);
            Vector2 up = Physics2D.gravity.sqrMagnitude > .001f ? -Physics2D.gravity.normalized : Vector2.up;
            for (int i = 0; i < count; i++)
            {
                var player = _overlaps[i].GetComponentInParent<PlayerController>();
                if (player == null || !player.IsGrounded) continue;
                Bounds p = _overlaps[i].bounds;
                float height = Vector2.Dot((Vector2)p.center - up * p.extents.y - ((Vector2)b.center + up * b.extents.y), up);
                if (height >= -.08f && height <= .18f && p.max.x > b.min.x + .02f && p.min.x < b.max.x - .02f) _riders.Add(player);
            }
        }
        private float SafeFraction(Rigidbody2D body, Vector2 delta, Collider2D ignored, int mask)
        {
            float distance = delta.magnitude;
            if (distance < .0001f) return 1f;
            var filter = new ContactFilter2D { useTriggers = false }; filter.SetLayerMask(mask);
            int count = body.Cast(delta / distance, filter, _hits, distance + .02f);
            float fraction = 1f;
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == ignored || Vector2.Dot(hit.normal, delta) >= -.00001f) continue;
                fraction = Mathf.Min(fraction, Mathf.Max(0f, hit.distance - .015f) / distance);
            }
            return Mathf.Clamp01(fraction);
        }
        private struct State { public float Progress, Wait; public bool Target, Arrived; }
        protected override object CaptureMechanismState() => new State { Progress = _progress, Wait = _wait, Target = TargetEnd, Arrived = _arrived };
        protected override void RestoreMechanismState(object state)
        {
            var s = (State)state; _progress = s.Progress; _wait = s.Wait; TargetEnd = s.Target; _arrived = s.Arrived;
            _riders.Clear();
        }
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Vector3 start = Application.isPlaying ? (Vector3)_origin : transform.position;
            Gizmos.DrawLine(start, start + (Vector3)EndOffset); Gizmos.DrawWireSphere(start + (Vector3)EndOffset, .2f);
        }
    }
}
