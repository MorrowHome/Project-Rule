using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 风场：区域内给实体一个**随高度线性变化**的力（风扇 / 上升气流 / 风道都用它）。
    /// </summary>
    /// <remarks>
    /// <para><b>区域与力</b>：区域就是自己身上的 <see cref="Collider2D"/>（用 trigger，不挡人）；
    /// 力在世界 y 轴上从<b>底部值</b>线性插值到<b>顶部值</b>。</para>
    ///
    /// <para><b>为什么要有「顶部值」这个字段</b>：恒定力**做不出「停留位置」**。玩家要么一路加速
    /// 飞出风场，要么减速后掉回来 —— 只有阈值，没有平衡点。让力随高度衰减，玩家才会停在
    /// <c>F(y) = m × g</c> 那个高度上，而「质量 / 重力」改变的就是这个高度 —— 也就是这条玩法的主轴。
    /// 两个值填一样即退化成恒定力。</para>
    ///
    /// <para><b>为什么还要有阻力</b>：只有「力的平衡」还不够 —— 从低处进来的玩家带着一路攒下的动能，
    /// 会直接冲过平衡点。实测：无阻力时出场速度 18 m/s，飞到场顶再掉回来、周而复始，永远不停。
    /// 阻力（<see cref="_drag"/>）负责把这笔动能收掉，「停留位置」才真的停得住。</para>
    ///
    /// <para><b>报力、不报加速度</b>（<see cref="IWindField"/> 的关键约定）：由承受方自己除以质量。
    /// 于是「质量 × 增强 / 削弱 / 反转」直接改变人在风里浮多高，而重力（恒定加速度）不受质量影响 ——
    /// 两者互补，正好把「力」和「加速度」这两类作用区分开。</para>
    ///
    /// <para><b>它是被动探测的</b>：本组件**不主动推任何人**，实体每个物理帧自己来读
    /// （玩家走 <c>WindSensor</c>）。玩家是 Kinematic 刚体，引擎的力对它们无效，
    /// 只能走「读力 → 除以质量 → 并进速度解算」这条通道。本组件因此只负责「哪里有力」，
    /// 不认识玩家、敌人，也不认识规则系统。</para>
    ///
    /// <para>⚠️ <b>假定风场是静止的</b>：判定与插值都读 <c>Collider2D.bounds</c>（缓存值）。
    /// 想移动风场，移动后再调一次 <c>Physics2D.SyncTransforms()</c>，否则读到的区域滞后一步。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider2D))]
    public sealed class WindZone : MonoBehaviour, IWindField
    {
        // ==================================================================
        //  配置
        // ==================================================================

        [Header("风（牛顿，世界坐标）")]
        [Tooltip("区域**底部**的风力。正值 = 朝该轴正方向吹（(0, 150) = 向上吹）。")]
        [SerializeField] private Vector2 _forceAtBottom = new Vector2(0f, 150f);

        [Tooltip("区域**顶部**的风力。与底部相同 = 恒定力；顶部更小 = 越往上越弱 —— " +
                 "玩家会浮在「风力 = 自身重力」的高度，改质量 / 重力就能改这个高度。")]
        [SerializeField] private Vector2 _forceAtTop = new Vector2(0f, 0f);

        [Header("空气阻力")]
        [Tooltip("场内空气阻力（每秒速度衰减率）。0 = 无阻力 —— 实测那样玩家会被一路加速弹出场顶" +
                 "（出场速度 18+），永远停不下来；给个 2 左右就会收敛到「风力 = 重力」的高度。")]
        [SerializeField, Min(0f)] private float _drag = 2f;

        // ==================================================================
        //  运行时
        // ==================================================================

        private Collider2D _area;

        private void Awake() => _area = GetComponent<Collider2D>();

        /// <inheritdoc />
        /// <remarks>点不在场内、或本组件被关掉时返回 <see cref="Vector2.zero"/>。
        /// 关掉组件即可让风场停用 —— 调用方不需要额外判断。</remarks>
        public Vector2 ForceAt(Vector2 point)
        {
            if (!Contains(point)) return Vector2.zero;

            Bounds b = _area.bounds;
            float t = b.size.y > 0.0001f
                ? Mathf.InverseLerp(b.min.y, b.max.y, point.y)
                : 0f;

            return Vector2.Lerp(_forceAtBottom, _forceAtTop, t);
        }

        /// <inheritdoc />
        public float DragAt(Vector2 point) => Contains(point) ? _drag : 0f;

        /// <summary>点是否落在场内（真实形状）。组件关掉 = 整个风场停用。</summary>
        private bool Contains(Vector2 point)
        {
            if (!isActiveAndEnabled) return false;

            if (_area == null) _area = GetComponent<Collider2D>();
            // OverlapPoint 判**真实形状**，不是外接矩形 —— 圆 / 胶囊风场也认。
            return _area != null && _area.OverlapPoint(point);
        }

        // ==================================================================
        //  编辑器
        // ==================================================================

        /// <summary>只给编辑器工具用：按实际区域尺寸摆好之后直接改两个风力。</summary>
        public void SetForce(Vector2 bottom, Vector2 top)
        {
            _forceAtBottom = bottom;
            _forceAtTop    = top;
        }

        private void OnDrawGizmos()
        {
            var area = _area != null ? _area : GetComponent<Collider2D>();
            if (area == null) return;

            Bounds b = area.bounds;

            // 区域轮廓
            Gizmos.color = new Color(0.45f, 0.85f, 1f, 0.85f);
            Gizmos.DrawWireCube(b.center, b.size);

            // 风：底部与顶部各画一排箭头，长度按力大小、方向即风向。
            DrawForceRow(new Vector2(b.min.x, b.min.y), b.size.x, _forceAtBottom);
            DrawForceRow(new Vector2(b.min.x, b.max.y), b.size.x, _forceAtTop);
        }

        /// <summary>沿一条横线画若干箭头示意风力（10 牛顿 ≈ 1 个单位长度）。</summary>
        private static void DrawForceRow(Vector2 left, float width, Vector2 force)
        {
            const float NewtonsPerUnit = 100f;

            Vector2 dir = force.normalized;
            float length = Mathf.Clamp(force.magnitude / NewtonsPerUnit, 0f, 2f);
            if (length <= 0.01f) return;

            Gizmos.color = new Color(0.9f, 0.95f, 0.4f, 0.9f);

            const int arrows = 5;
            for (int i = 0; i < arrows; i++)
            {
                float u = arrows == 1 ? 0.5f : i / (float)(arrows - 1);
                Vector2 basePoint = left + new Vector2(width * u, 0f);
                Vector2 tip = basePoint + dir * length;

                Gizmos.DrawLine(basePoint, tip);

                // 箭头头部
                Vector2 back = -dir * (length * 0.28f);
                Vector2 perp = new Vector2(-dir.y, dir.x) * (length * 0.12f);
                Gizmos.DrawLine(tip, tip + back + perp);
                Gizmos.DrawLine(tip, tip + back - perp);
            }
        }
    }
}
