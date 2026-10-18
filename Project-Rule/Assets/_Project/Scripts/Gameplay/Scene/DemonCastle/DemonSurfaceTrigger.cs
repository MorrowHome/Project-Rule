using UnityEngine;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Gameplay.Scene.DemonCastle
{
    /// <summary>仅上表面起跳或踩踏触发；绑定真实主角，不依赖 Kinematic 触发回调。</summary>
    public abstract class DemonSurfaceTrigger : CityMechanism
    {
        [Header("触发方式")]
        [InspectorName("踩上触发（关闭则起跳触发）")]
        public bool OnStep;
        [Min(.01f)] [InspectorName("触发冷却")] public float Cooldown = .2f;
        protected PlayerController Player;
        private bool _occupied;
        private float _cooldown, _retry;

        protected virtual void OnEnable() { BindPlayer(); }
        protected virtual void OnDisable()
        {
            if (Player != null) Player.OnJumped -= HandleJump;
            Player = null;
            _occupied = false;
        }
        private void BindPlayer()
        {
            if (Player != null) Player.OnJumped -= HandleJump;
            Player = FindAnyObjectByType<PlayerController>();
            if (Player != null) Player.OnJumped += HandleJump;
        }
        public static bool IsOnSurface(PlayerController player, Collider2D surface)
        {
            if (player == null || !player.isActiveAndEnabled || surface == null || !surface.enabled) return false;
            var shape = player.GetComponent<Collider2D>();
            if (shape == null) return false;
            Bounds p = shape.bounds, b = surface.bounds;
            Vector2 up = Physics2D.gravity.y > 0f ? Vector2.down : Vector2.up;
            float gap = Vector2.Dot((Vector2)p.center - up * p.extents.y - ((Vector2)b.center + up * b.extents.y), up);
            return gap >= -.08f && gap <= .22f && p.max.x > b.min.x + .02f && p.min.x < b.max.x - .02f;
        }
        private void HandleJump()
        {
            if (!OnStep && IsOnSurface(Player, Shape)) TryActivate();
        }
        public bool TryActivate()
        {
            if (!isActiveAndEnabled || Paused || _cooldown > 0f) return false;
            if (!Activate()) return false;
            _cooldown = Mathf.Max(.01f, Cooldown);
            return true;
        }
        protected abstract bool Activate();
        protected virtual void FixedUpdate()
        {
            if (Paused) return;
            _cooldown = Mathf.Max(0f, _cooldown - Time.fixedDeltaTime);
            if (Player == null || !Player.isActiveAndEnabled)
            {
                _retry -= Time.fixedDeltaTime;
                if (_retry <= 0f) { _retry = .5f; BindPlayer(); }
            }
            bool occupied = Player != null && Player.IsGrounded && IsOnSurface(Player, Shape);
            if (OnStep && occupied && !_occupied) TryActivate();
            _occupied = occupied;
            TickSurface(Time.fixedDeltaTime);
        }
        protected virtual void TickSurface(float dt) { }
        protected struct SurfaceState { public bool Occupied; public float Cooldown; public object Payload; }
        protected virtual object CaptureSurfaceState() => null;
        protected virtual void RestoreSurfaceState(object state) { }
        protected override object CaptureMechanismState() => new SurfaceState
        { Occupied = _occupied, Cooldown = _cooldown, Payload = CaptureSurfaceState() };
        protected override void RestoreMechanismState(object state)
        {
            var s = (SurfaceState)state;
            _occupied = s.Occupied; _cooldown = s.Cooldown;
            RestoreSurfaceState(s.Payload);
        }
    }
}
