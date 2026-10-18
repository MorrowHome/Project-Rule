using UnityEngine;
using RuleGame.Infrastructure.Time;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 巨石的碎块：**纯表现** —— 炸出去、翻滚、落地，然后停在那儿当瓦砾。
    /// </summary>
    /// <remarks>
    /// <para><b>⚠️ 没有碰撞体、也没有刚体（有意为之）</b>：碎块落地后会堆在剑周围，
    /// 一块一块都是实心方块 —— 玩家走过去会被**卡住 / 顶起来**。瓦砾只需要「看起来崩了一地」，
    /// 不该参与碰撞；顺带也不会挤进取剑的探测圈。</para>
    ///
    /// <para><b>为什么自己积分</b>：没有碰撞体的动态刚体会直接穿过地面掉进虚空。
    /// 所以自己算：重力取 <c>Physics2D.gravity</c>（跟着「重力 × 反转」走 —— 反着重力时
    /// 碎块会往上飘，规则一结束再落回来），落地靠一条**向下射线**找地面。</para>
    ///
    /// <para>⚠️ 父物体缩放必须是 1：尺寸按世界单位算，缩放会破坏落地高度。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BoulderFragment : MonoBehaviour
    {
        [Header("落地")]
        [Tooltip("往下找地面的射线长度（世界单位）。什么都没打到就按这个距离当底面。")]
        [SerializeField, Min(0.1f)] private float _groundProbe = 12f;

        [Tooltip("「地面」算哪些层。默认 Ground（层 8）—— 与石头的落地判定同一个层。")]
        [SerializeField] private LayerMask _groundLayers = 1 << 8;

        [Tooltip("落地后水平速度衰减多快（单位/秒²）。只影响它滑一小段的手感。")]
        [SerializeField, Min(0f)] private float _landingDrag = 6f;

        /// <summary>已经落定了（不再动）。</summary>
        public bool IsSettled { get; private set; }

        private Vector2 _velocity;
        private float _spin;
        private float _groundY;
        private float _halfHeight = 0.25f;

        /// <summary>被 <see cref="GravityBoulder"/> 炸出来时调一次：给初速度与自转。</summary>
        public void Launch(Vector2 velocity, float angularVelocity)
        {
            _velocity = velocity;
            _spin = angularVelocity;
            IsSettled = false;

            var renderer = GetComponentInChildren<SpriteRenderer>();
            if (renderer != null) _halfHeight = renderer.bounds.extents.y;

            _groundY = ProbeGround(transform.position);
        }

        private void FixedUpdate()
        {
            if (IsSettled) return;

            // 暂停时物理不步进，碎块也停（与其它按帧推进的组件同一处理）。
            if (TimeManager.Instance?.IsPaused == true) return;

            float dt = Time.fixedDeltaTime;

            // 重力取全局值：重力规则一翻，碎块也跟着飘 —— 一行特判都不用。
            _velocity += Physics2D.gravity * dt;

            transform.position += (Vector3)(_velocity * dt);

            if (!Mathf.Approximately(_spin, 0f))
            {
                transform.Rotate(0f, 0f, -_spin * dt);
            }

            // 只有「正在往下走」才判落地：重力反着的时候碎块是往上飞的。
            if (_velocity.y >= 0f) return;

            float floorY = _groundY + _halfHeight;
            if (transform.position.y > floorY) return;

            var at = transform.position;
            at.y = floorY;
            transform.position = at;

            // 落地后滑一小段就停（碎石不会一直滑）。
            _velocity.y = 0f;
            _velocity.x = Mathf.MoveTowards(_velocity.x, 0f, _landingDrag * dt);
            _spin *= 0.5f;

            if (_velocity.x == 0f)
            {
                IsSettled = true;
            }
        }

        /// <summary>从 <paramref name="from"/> 往下找地面；找不到就按探针长度兜底。</summary>
        private float ProbeGround(Vector3 from)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(_groundLayers);

            var hits = new RaycastHit2D[1];
            int count = Physics2D.Raycast(from, Vector2.down, filter, hits, _groundProbe);

            return count > 0 ? hits[0].point.y : from.y - _groundProbe;
        }
    }
}
