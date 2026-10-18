using UnityEngine;
using RuleGame.Infrastructure;

namespace RuleGame.Gameplay.Combat
{
    /// <summary>
    /// 受击箱：实体「可以被命中」的**唯一**几何表达。
    /// </summary>
    /// <remarks>
    /// <para><b>物理碰撞体只回答「撞不撞得到」，受击箱只回答「打不打得到」</b> —— 两块几何各自独立。
    /// 要调「打起来什么手感」改这里，要调「往哪走」改物理碰撞体。</para>
    ///
    /// <para><b>必须是独立子物体</b>：碰撞体的图层跟 GameObject 走，而受击箱要住在
    /// <c>Hurtbox</c> 层（10）上 —— 命中判定的所有查询入口都只查这一层
    /// （<see cref="CombatSystem.CanHit"/> 负责「自己不打自己 / 不打同阵营」）。</para>
    ///
    /// <para><b>一个实体可以有多个</b>（本体的、弱点的）：命中判定按承受方去重，
    /// 所以一刀同时砍中同一实体的两个箱子，只结算一次。</para>
    ///
    /// <para><b>关掉它 = 打不到</b>：<see cref="SetHittable"/> 表达「这个部位现在不可被击中」
    /// （弱点开合之类）。⚠️ 抗性 / 无敌帧**不在**这里 —— 那些写在承受方的 <c>ApplyDamage</c> 里
    /// （约束 C4），受击箱只管「这块几何算不算被打中」。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class Hurtbox : MonoBehaviour
    {
        /// <summary>受击层名。命中判定的所有查询入口都只查这一层。</summary>
        public const string LayerName = "Hurtbox";

        private static int  _layer            = -1;
        private static int  _mask             = -1;
        private static bool _warnedMissingLayer;

        [Tooltip("承受方：实现 IDamageable 的组件（EntityDamageable / EnemyActor / EnemyWeakPoint）。" +
                 "留空则自动取父链上的第一个 —— 把受击箱挂在承受方之下就够用。")]
        [SerializeField] private MonoBehaviour _owner;

        private Collider2D  _area;
        private IDamageable _resolved;
        private bool        _warnedResolve;

        // ==================================================================
        //  静态：层与掩码
        // ==================================================================

        /// <summary>受击层号。<b>找不到层会报错</b> —— 否则表现是「所有攻击都打空」，很难查。</summary>
        public static int Layer
        {
            get
            {
                if (_layer >= 0) return _layer;

                _layer = UnityEngine.LayerMask.NameToLayer(LayerName);
                if (_layer < 0 && !_warnedMissingLayer)
                {
                    _warnedMissingLayer = true;
                    Debug.LogError($"[Hurtbox] 工程里没有 '{LayerName}' 层 —— 命中判定会全部打空。" +
                                   "请在 Project Settings > Tags and Layers 里补上它。");
                }
                return _layer;
            }
        }

        /// <summary>受击层的掩码。命中判定的查询一律用它，<b>不允许各调用点自带 mask</b>。</summary>
        public static LayerMask Mask
        {
            get
            {
                if (_mask < 0) _mask = Layer >= 0 ? 1 << _layer : 0;
                return _mask;
            }
        }

        // ==================================================================
        //  实例
        // ==================================================================

        /// <summary>承受方。解析一次后缓存（懒解析：别的组件可能在 Awake 之前就来读）。</summary>
        public IDamageable Owner
        {
            get { Resolve(); return _resolved; }
        }

        /// <summary>受击区域（Trigger 碰撞体）。</summary>
        public Collider2D Area
        {
            get { Resolve(); return _area; }
        }

        /// <summary>这个部位现在能不能被打到。⚠️ 与抗性 / 无敌帧无关，那是承受方自己的事。</summary>
        public void SetHittable(bool hittable)
        {
            if (Area != null) Area.enabled = hittable;
        }

        /// <summary>
        /// 碰撞体 → 承受方。层上有裸碰撞体（没挂本组件）时返回 null ——
        /// 命中判定会跳过它并当作配置错误，**不静默回退**到别的解析方式。
        /// </summary>
        public static IDamageable ResolveOwner(Collider2D collider)
        {
            if (collider == null) return null;

            var box = collider.GetComponentInParent<Hurtbox>();
            return box != null ? box.Owner : null;
        }

        // ==================================================================
        //  生命周期
        // ==================================================================

        private void Awake()
        {
            Resolve();
            ApplyLayer();
        }

        /// <summary>新建时（Reset）：受击箱一律是 Trigger。</summary>
        private void Reset() => EnsureTrigger();

        /// <summary>
        /// 编辑器里改完立刻纠正两件会静默出错的事：不是 Trigger、放错图层。
        /// </summary>
        /// <remarks>
        /// 放错图层 = 谁也打不到它（或反过来被别的查询捞走），而场景里看不出来 ——
        /// 所以这里直接纠正，而不是只留个警告。
        /// </remarks>
        private void OnValidate()
        {
            EnsureTrigger();
            ApplyLayer();
        }

        private void Resolve()
        {
            // ⚠️ 只有**解析成功**才缓存。以前用「尝试过就不再试」的标记，一旦
            // Awake 时父物体还没激活（GetComponentInParent 默认跳过未激活对象），
            // 就会把 null 永久缓存下来 —— 表现是「所有攻击都打空」，且不报错。
            if (_resolved != null) return;

            _area = GetComponent<Collider2D>();
            if (_area == null)
            {
                Warn("[Hurtbox] 同物体上没有 Collider2D —— 这个受击箱永远不会被查到。");
                return;
            }

            if (_owner != null)
            {
                _resolved = _owner as IDamageable;
                if (_resolved == null)
                    Warn($"[Hurtbox] 指定的 _owner（{_owner.GetType().Name}）没有实现 IDamageable。");
                return;
            }

            // includeInactive：Prefab 实例化 / 层级启用顺序不确定，未激活的父物体也要找。
            _resolved = GetComponentInParent<IDamageable>(true);
            if (_resolved == null)
                Warn("[Hurtbox] 找不到承受方（IDamageable）：把它挂到承受方之下，" +
                     "或在 _owner 上显式指定跨层级的承受方。");
        }

        /// <summary>同一个受击箱只报一次，避免每帧刷屏。</summary>
        private void Warn(string message)
        {
            if (_warnedResolve) return;
            _warnedResolve = true;
            Debug.LogError(message, this);
        }

        private void EnsureTrigger()
        {
            var area = GetComponent<Collider2D>();
            if (area != null) area.isTrigger = true;
        }

        private void ApplyLayer()
        {
            if (Layer < 0 || gameObject.layer == _layer) return;
            gameObject.layer = _layer;
        }

        // ==================================================================
        //  可视化
        // ==================================================================

        /// <summary>
        /// 受击区域的红框。**大小与位置只能靠它目视对齐** —— 箱子不对齐的话，
        /// 表现就是「看着砍中了却没掉血」或反过来。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            var area = GetComponent<Collider2D>();
            if (area == null) return;

            Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.9f);
            Gizmos.DrawWireCube(area.bounds.center, area.bounds.size);
        }
    }
}
