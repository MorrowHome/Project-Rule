using UnityEngine;
using RuleGame.Gameplay.Common;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 风场：区域内给实体一个**随高度线性变化**的力（风扇 / 上升气流 / 风道都用它）。
    /// </summary>
    /// <remarks>
    /// <para><b>区域与力</b>：区域就是自己身上的 <see cref="Collider2D"/>（用 trigger，不挡人）；
    /// 力沿底部风力的方向、大小随高度按<b>梯度</b>线性递减（<see cref="_forceGradient"/>，N/m）。</para>
    ///
    /// <para><b>为什么力要随高度衰减</b>：恒定力**做不出「停留位置」**。玩家要么一路加速
    /// 飞出风场，要么减速后掉回来 —— 只有阈值，没有平衡点。让力随高度衰减，玩家才会停在
    /// <c>F(y) = m × g</c> 那个高度上，而「质量 / 重力」改变的就是这个高度 —— 也就是这条玩法的主轴。
    /// 梯度填 0 即退化成恒定力（没有平衡点）。</para>
    ///
    /// <para><b>梯度决定「不同重力档之间分多开」</b>：平衡高度
    /// <c>y(g) = (F₀ − m·g) ÷ 梯度</c>，相邻两档的间距
    /// <c>Δy = m·Δg ÷ 梯度</c> —— 与梯度成反比。想让人在不同重力下分得更开，就调小梯度
    /// （或加长风场）；想挤在一起就调大。Gizmo 上会按场景里玩家的质量与重力倍率画出
    /// ×削弱 / 正常 / ×增强 三条平衡线（<see cref="_showEquilibriumLines"/> 控制开关）。</para>
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
        [Tooltip("区域**底部**的风力。正值 = 朝该轴正方向吹（(0, 150) = 向上吹）。力沿这个方向，大小随高度递减。")]
        [SerializeField] private Vector2 _forceAtBottom = new Vector2(0f, 150f);

        [Tooltip("力的梯度：每上升 1 米，风力减少多少牛顿（N/m）。0 = 恒定力。\n" +
                 "它决定「不同重力档在风里的落点间距」：间距 Δy = m·Δg ÷ 梯度（与梯度成反比）——\n" +
                 "想让人在不同重力下分得更开，就调小它；想挤在一起，就调大它。\n" +
                 "⚠️ 平衡点要在场内：底力必须大于 m·g（正常重力）。")]
        [SerializeField] private float _forceGradient = 15f;

        [Header("预览")]
        [Tooltip("在 Scene 视图画出「各重力档的平衡高度线」（×削弱 / 正常 / ×增强），\n" +
                 "按场景里的玩家的质量与重力倍率计算。纯编辑器辅助，不参与运行时判定。")]
        [SerializeField] private bool _showEquilibriumLines = true;

        [Header("阻尼")]
        [Tooltip("空气阻力（每秒速度衰减率），水平与竖直**都收**。0 = 无阻力 —— " +
                 "实测那样玩家会被一路加速弹出场顶（出场速度 18+），永远停不下来；给个 2 左右就会收敛到「风力 = 重力」的高度。")]
        [SerializeField, Min(0f)] private float _drag = 2f;

        [Tooltip("竖直阻尼（每秒速度衰减率）。**只收竖直方向**（沿当前重力轴）的速度，横向不动；与空气阻力叠加。\n" +
                 "默认 8 ≈ 临界阻尼（150N / 10m / 质量 1 时临界值约 7.7）：被吹起来后一路上升、不上下弹。\n" +
                 "想更「黏」就调大，想更有弹性就调小。")]
        [SerializeField, Min(0f)] private float _verticalDamping = 8f;

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
            float rise = Mathf.Max(point.y - b.min.y, 0f);   // 离区域底部的距离（米）
            return ForceAtBottomMagnitude(rise);
        }

        /// <summary>离底部 <paramref name="rise"/> 米处的风力：沿底部方向、大小按梯度递减，最低到 0。</summary>
        private Vector2 ForceAtBottomMagnitude(float rise)
        {
            float magnitude = Mathf.Max(_forceAtBottom.magnitude - _forceGradient * rise, 0f);
            return _forceAtBottom.normalized * magnitude;
        }

        /// <inheritdoc />
        public float DragAt(Vector2 point) => Contains(point) ? _drag : 0f;

        /// <inheritdoc />
        public float VerticalDampingAt(Vector2 point) => Contains(point) ? _verticalDamping : 0f;

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

        /// <summary>只给编辑器工具用：按实际区域尺寸摆好之后直接改底力与梯度。</summary>
        public void SetForce(Vector2 bottom, float gradient)
        {
            _forceAtBottom = bottom;
            _forceGradient = gradient;
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
            DrawForceRow(new Vector2(b.min.x, b.max.y), b.size.x, ForceAtBottomMagnitude(b.size.y));

            // 各重力档的平衡高度线（预览开关）。
            if (_showEquilibriumLines) DrawEquilibriumLines(b);
        }

        /// <summary>画出「各重力档的平衡高度线」—— 风力 = 该档重力的高度。</summary>
        /// <remarks>
        /// <para>纯编辑器预览：用**场景里玩家实体**的质量与重力倍率算；场景里没有玩家就按 m = 1、倍率 = 1。</para>
        /// <para>只对**竖直向上的风**有意义：水平风不会在竖直方向形成平衡点；恒定力（梯度 0）也没有平衡点。</para>
        /// </remarks>
        private void DrawEquilibriumLines(Bounds b)
        {
            if (Mathf.Abs(_forceAtBottom.x) > 0.001f || _forceAtBottom.y <= 0f) return;
            if (Mathf.Abs(_forceGradient) < 0.0001f) return;

#if UNITY_EDITOR
            float mass = 1f;
            float gravityScale = 1f;

            var player = UnityEngine.Object.FindAnyObjectByType<RuleGame.Gameplay.Player.PlayerController>();
            if (player != null)
            {
                var body = player.GetComponent<Rigidbody2D>();
                if (body != null) mass = body.mass;
                gravityScale = player.GravityScale;
            }

            DrawTierLine(b, "×削弱", 0.3f, new Color(0.55f, 0.75f, 1f, 0.95f), mass, gravityScale);
            DrawTierLine(b, "正常",   1f,   new Color(0.55f, 1f, 0.6f, 0.95f), mass, gravityScale);
            DrawTierLine(b, "×增强", 2f,   new Color(1f, 0.62f, 0.35f, 0.95f), mass, gravityScale);
#endif
        }

#if UNITY_EDITOR
        /// <summary>某一个重力档的平衡高度线。档位倍率与 <c>GravityRule</c> 的取值保持一致。</summary>
        private void DrawTierLine(Bounds b, string label, float gravityTier, Color color,
                                  float mass, float gravityScale)
        {
            float force = mass * 9.81f * gravityTier * gravityScale;   // 平衡所需的力（牛顿）
            float rise = (_forceAtBottom.magnitude - force) / _forceGradient;
            if (rise < 0f || rise > b.size.y) return;

            float y = b.min.y + rise;
            Gizmos.color = color;
            Gizmos.DrawLine(new Vector3(b.min.x, y, 0f), new Vector3(b.max.x, y, 0f));
            UnityEditor.Handles.Label(new Vector3(b.min.x, y + 0.12f, 0f),
                                      label + "  y=" + y.ToString("0.00"));
        }
#endif

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
