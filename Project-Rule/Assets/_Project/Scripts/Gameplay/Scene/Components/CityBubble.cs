using UnityEngine;
using RuleGame.Gameplay.Player;

namespace RuleGame.Gameplay.Scene.Components
{
    /// <summary>从泡泡上起跳才破裂；擦过侧面/空中二段跳不触发。物体保留，支持快照复原。</summary>
    public sealed class CityBubble : CityMechanism
    {
        [Tooltip("0 表示只通过关卡重置/回溯复原。")]
        public float RespawnSeconds;
        public bool Broken { get; private set; }
        private float _remaining;
        private PlayerController _player;
        private void Start() => BindPlayer(FindAnyObjectByType<PlayerController>());
        public void BindPlayer(PlayerController player)
        {
            if (_player != null) _player.OnJumped -= OnJump;
            _player = player;
            if (_player != null) _player.OnJumped += OnJump;
        }
        private void OnDestroy() { if (_player != null) _player.OnJumped -= OnJump; }
        private void OnJump()
        {
            if (Paused || Broken || _player == null) return;
            var collider = _player.GetComponent<Collider2D>();
            Vector2 up = Physics2D.gravity.sqrMagnitude > .001f ? -Physics2D.gravity.normalized : Vector2.up;
            Bounds p = collider.bounds, b = Shape.bounds;
            float gap = Vector2.Dot((Vector2)p.center - up * p.extents.y - ((Vector2)b.center + up * b.extents.y), up);
            if (gap < -.08f || gap > .2f || p.max.x <= b.min.x || p.min.x >= b.max.x) return;
            Broken = true; _remaining = RespawnSeconds; Shape.enabled = false; SetVisible(false);
        }
        private void FixedUpdate()
        {
            if (Paused || !Broken || RespawnSeconds <= 0f) return;
            _remaining -= Time.fixedDeltaTime;
            if (_remaining <= 0f)
            {
                // 不在玩家身体内重建实心平台。
                if (_player != null && ((Vector2)_player.transform.position - Body.position).sqrMagnitude < 2f) return;
                Broken = false; Shape.enabled = true; SetVisible(true);
            }
        }
        private struct State { public bool Broken; public float Remaining; }
        protected override object CaptureMechanismState() => new State { Broken = Broken, Remaining = _remaining };
        protected override void RestoreMechanismState(object state) { var s = (State)state; Broken = s.Broken; _remaining = s.Remaining; }
    }
}
