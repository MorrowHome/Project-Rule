using UnityEngine;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 塌陷地板的碎块：**纯表现** —— 掉下去、翻滚、落地，停一会儿淡出。
    /// </summary>
    /// <remarks>
    /// <para><b>⚠️ 没有碰撞体、也没有刚体（有意为之）</b>，与 <see cref="BoulderFragment"/> 同一套理由：
    /// 带碰撞的碎块会砸到 Boss、把玩家顶起来、卡在墙缝里。Boss 战的场地必须干净。</para>
    ///
    /// <para><b>为什么自己积分</b>：没有碰撞体的动态刚体会直接穿过地面掉进虚空。
    /// 重力取 <c>Physics2D.gravity</c>（跟着「重力 × 反转」走），落地靠一条<b>向下射线</b>找地面。</para>
    ///
    /// <para>⚠️ <b>射线在第一次物理步进时才打</b>，不在 <see cref="Launch"/> 里打：
    /// 地板的瓦片刚被清掉，<c>TilemapCollider2D</c> 要到帧末才重建，
    /// 立刻探测会打到自己刚离开的那块地面，碎块就当場「落地」卡在半空。</para>
    ///
    /// <para>⚠️ 父物体缩放必须是 1：尺寸按世界单位算，缩放会破坏落地高度。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TrapFloorDebris : MonoBehaviour
    {
        [Header("落地")]
        [Tooltip("往下找地面的射线长度（世界单位）。Boss 房在下层时要比房间高度长。")]
        [SerializeField, Min(1f)] private float _groundProbe = 20f;

        [Tooltip("「地面」算哪些层。默认 Ground（层 8）。")]
        [SerializeField] private LayerMask _groundLayers = 1 << 8;

        [Tooltip("落地后水平速度衰减多快（单位/秒²）。")]
        [SerializeField, Min(0f)] private float _landingDrag = 6f;

        [Header("寿命")]
        [Tooltip("落地后停多久开始淡出。")]
        [SerializeField, Min(0f)] private float _lingerSeconds = 1.5f;

        [Tooltip("淡出用时。走完就销毁。")]
        [SerializeField, Min(0.05f)] private float _fadeSeconds = 0.8f;

        private Vector2 _velocity;
        private float _spin;
        private float _groundY = float.NaN;
        private float _halfHeight = 0.25f;
        private float _settledAge = -1f;
        private SpriteRenderer _renderer;
        private Color _color = Color.white;

        /// <summary>已经落定（不再动）。</summary>
        public bool IsSettled { get; private set; }

        /// <summary>被 <see cref="TrapFloor"/> 生成时调一次：给初速度与自转。</summary>
        public void Launch(Vector2 velocity, float angularVelocity)
        {
            _velocity = velocity;
            _spin = angularVelocity;
            IsSettled = false;
            _settledAge = -1f;
            _groundY = float.NaN;

            if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>();
            if (_renderer != null)
            {
                _halfHeight = _renderer.bounds.extents.y;
                _color = _renderer.color;
            }
        }

        private void FixedUpdate()
        {
            // 暂停时物理不步进，碎块也停（与其它按帧推进的组件同一处理）。
            if (TimeManager.Instance?.IsPaused == true) return;

            float dt = Time.fixedDeltaTime;

            // 第一次步进才探测：这时瓦片碰撞体已经重建，不会打到自己刚离开的地板。
            if (float.IsNaN(_groundY))
            {
                _groundY = ProbeGround();
                if (float.IsNaN(_groundY)) _groundY = transform.position.y - _groundProbe;
            }

            if (!IsSettled)
            {
                // 重力取全局值：重力规则一翻，碎块也跟着飘 —— 一行特判都不用。
                _velocity += Physics2D.gravity * dt;

                transform.position += (Vector3)(_velocity * dt);

                if (!Mathf.Approximately(_spin, 0f)) transform.Rotate(0f, 0f, -_spin * dt);

                // 只有「正在往下走」才判落地：重力反着的时候碎块是往上飞的。
                if (_velocity.y < 0f)
                {
                    float floorY = _groundY + _halfHeight;
                    if (transform.position.y <= floorY)
                    {
                        Vector3 at = transform.position;
                        at.y = floorY;
                        transform.position = at;

                        _velocity.y = 0f;
                        _velocity.x = Mathf.MoveTowards(_velocity.x, 0f, _landingDrag * dt);
                        _spin *= 0.5f;

                        if (Mathf.Approximately(_velocity.x, 0f))
                        {
                            IsSettled = true;
                            _settledAge = 0f;
                        }
                    }
                }

                return;
            }

            _settledAge += dt;
            if (_settledAge < _lingerSeconds) return;

            float fade = Mathf.Clamp01((_settledAge - _lingerSeconds) / _fadeSeconds);
            if (_renderer != null)
            {
                Color color = _color;
                color.a = _color.a * (1f - fade);
                _renderer.color = color;
            }

            if (fade >= 1f) Destroy(gameObject);
        }

        /// <summary>从当前位置往下找地面；找不到返回 NaN。</summary>
        private float ProbeGround()
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_groundLayers);

            var hits = new RaycastHit2D[1];
            int count = Physics2D.Raycast(transform.position, Vector2.down, filter, hits, _groundProbe);

            return count > 0 ? hits[0].point.y : float.NaN;
        }
    }
}
