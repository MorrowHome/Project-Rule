using UnityEngine;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Components;

namespace RuleGame.Gameplay.Scene.DemonCastle
{
    /// <summary>圆盘弹簧，锚点相对出生位置；通过既有承载/弹射接口交给玩家解算碰撞。</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class DemonSpringPlatform : CityMechanism, IMovingSurface, ISpringSurface
    {
        [Min(.1f)] [InspectorName("压缩行程")] public float Compression = .55f;
        [Min(.05f)] [InspectorName("压缩／回弹时长")] public float CompressSeconds = .2f;
        [Min(.05f)] [InspectorName("压到底停留时长")] public float HoldSeconds = .12f;
        [Min(.1f)] [InspectorName("弹射速度")] public float LaunchSpeed = 18f;
        [InspectorName("弹簧外观")]
        public Transform Coil;
        public Vector2 Velocity { get; private set; }
        public Vector2 LaunchVelocity => !Paused && _phase == 2 ? Vector2.up * LaunchSpeed : Vector2.zero;
        private Vector2 _origin;
        private float _offset, _timer;
        private int _phase;
        private bool _occupied;
        private PlayerController _player;
        private float _retry;
        protected override void Awake() { base.Awake(); _origin = Body.position; }
        private void FixedUpdate()
        {
            Velocity = Vector2.zero;
            if (Paused) return;
            if (_player == null || !_player.isActiveAndEnabled)
            {
                _retry -= Time.fixedDeltaTime;
                if (_retry <= 0f) { _retry = .5f; _player = FindAnyObjectByType<PlayerController>(); }
            }
            bool occupied = _player != null && _player.IsGrounded && DemonSurfaceTrigger.IsOnSurface(_player, Shape);
            if (occupied && !_occupied && _phase == 0) { _phase = 1; _timer = 0f; }
            _occupied = occupied;
            float previous = _offset;
            if (_phase == 1)
            {
                _offset = Mathf.MoveTowards(_offset, Compression, Compression * Time.fixedDeltaTime / Mathf.Max(.05f, CompressSeconds));
                if (_offset >= Compression) { _timer += Time.fixedDeltaTime; if (_timer >= HoldSeconds) _phase = 2; }
                else if (!occupied) _phase = 2;
            }
            else if (_phase == 2)
            {
                _offset = Mathf.MoveTowards(_offset, 0f, Compression * Time.fixedDeltaTime / Mathf.Max(.05f, CompressSeconds));
                if (_offset <= 0f) _phase = 0;
            }
            Velocity = Vector2.down * ((_offset - previous) / Time.fixedDeltaTime);
            Body.MovePosition(_origin + Vector2.down * _offset);
            RefreshCoil();
        }
        private void RefreshCoil()
        {
            if (Coil == null) return;
            float height = 1f - _offset;
            Coil.localPosition = new Vector3(0f, -height * .5f - .15f, 0f);
            Coil.localScale = new Vector3(.35f, Mathf.Max(.1f, height), 1f);
        }
        private struct State { public float Offset, Timer; public int Phase; public bool Occupied; }
        protected override object CaptureMechanismState() => new State { Offset = _offset, Timer = _timer, Phase = _phase, Occupied = _occupied };
        protected override void RestoreMechanismState(object state)
        { var s = (State)state; _offset = s.Offset; _timer = s.Timer; _phase = s.Phase; _occupied = s.Occupied; Velocity = Vector2.zero; RefreshCoil(); }
    }
}
