using UnityEngine;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 玩具盒平台：把一张「包装盒」房子图当成**实心、可站立**的跳跃平台。
    /// </summary>
    /// <remarks>
    /// <para>碰撞体永远按精灵的可见包围盒拟合 —— 改图 / 改 Scale 后不会出现
    /// 「看起来 2.4、实际 2.0」。拟合结果写在 <see cref="BoxCollider2D"/> 上，
    /// 运行时不再改。</para>
    ///
    /// <para>⚠️ <b>必须放 Ground 层（8）</b>：玩家的 Slide 与接地探测只扫这一层，
    /// 放错层就是一块穿得过去的空气。</para>
    ///
    /// <para><b>不需要 Rigidbody2D</b>：静态碰撞体就够；本组件不移动、不承载、不受击。
    /// 要可推 / 可被重力规则影响，请另开 <c>Rigidbody2D + RuleAffectedBody</c>，
    /// 不要往这个平台上加。</para>
    ///
    /// <para>轴心约定：精灵的 Pivot 是**底边中心**。这样拖进场景时底边就落在鼠标位置，
    /// 关卡设计只需要关心 X 和地面 Y。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer), typeof(BoxCollider2D))]
    public sealed class ToyBoxPlatform : MonoBehaviour
    {
        [Header("碰撞体拟合")]
        [Tooltip("顶面向下收多少（世界单位）。留一点缝，避免玩家站在描边外沿看起来悬空")]
        [SerializeField] private float _topInset = 0.03f;

        [Tooltip("左右各收多少（世界单位）。留一点缝，避免站在圆角外")]
        [SerializeField] private float _sideInset = 0.04f;

        [Tooltip("底边向上收多少（世界单位）。通常不用改")]
        [SerializeField] private float _bottomInset = 0f;

        [Tooltip("改图 / 改 Scale 后自动重算碰撞体（编辑器）")]
        [SerializeField] private bool _autoFitInEditor = true;

        [Header("表面")]
        [Tooltip("可选的表面物理材质；留空 = 默认摩擦（0.4），与普通地面一致")]
        [SerializeField] private PhysicsMaterial2D _surfaceMaterial;

        private SpriteRenderer _renderer;
        private BoxCollider2D _body;

        /// <summary>玩家能站的顶面世界 Y。</summary>
        public float TopWorldY => Body != null ? Body.bounds.max.y : transform.position.y;

        /// <summary>主体碰撞体。</summary>
        public BoxCollider2D Body
        {
            get
            {
                if (_body == null) _body = GetComponent<BoxCollider2D>();
                return _body;
            }
        }

        private void Awake()
        {
            Cache();
            if (_body != null && _body.size.sqrMagnitude <= 0f) FitToSprite();
            ApplyMaterial();
        }

        private void OnValidate()
        {
            Cache();
            if (_autoFitInEditor && !Application.isPlaying) FitToSprite();
            ApplyMaterial();
            ValidateSetup();
        }

        /// <summary>按精灵包围盒重算 BoxCollider2D。根节点缩放不影响这个计算。</summary>
        [ContextMenu("把碰撞体拟合到精灵")]
        public void FitToSprite()
        {
            Cache();
            if (_renderer == null || _renderer.sprite == null || _body == null) return;

            Bounds spriteBounds = _renderer.sprite.bounds;
            float side = Mathf.Max(0f, _sideInset);
            float top = Mathf.Max(0f, _topInset);
            float bottom = Mathf.Max(0f, _bottomInset);

            float width = Mathf.Max(0.01f, spriteBounds.size.x - side * 2f);
            float height = Mathf.Max(0.01f, spriteBounds.size.y - top - bottom);

            _body.size = new Vector2(width, height);
            _body.offset = new Vector2(
                spriteBounds.center.x,
                spriteBounds.min.y + bottom + height * 0.5f);
        }

        private void Cache()
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (_body == null) _body = GetComponent<BoxCollider2D>();
        }

        private void ApplyMaterial()
        {
            if (_body == null) return;
            if (_body.sharedMaterial != _surfaceMaterial) _body.sharedMaterial = _surfaceMaterial;
        }

        private void ValidateSetup()
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer >= 0 && gameObject.layer != groundLayer)
            {
                Debug.LogWarning($"[玩具盒平台] {name} 不在 Ground 层（当前 {LayerMask.LayerToName(gameObject.layer)}）—— " +
                                 "玩家的位移与接地探测只认 Ground，放错层会变成可穿过的空气。", this);
            }

            if (_body != null && _body.isTrigger)
            {
                Debug.LogWarning($"[玩具盒平台] {name} 的 BoxCollider2D 勾了 Is Trigger，玩家不会站上去。", this);
            }

            if (TryGetComponent<Rigidbody2D>(out _))
            {
                Debug.LogWarning($"[玩具盒平台] {name} 身上有 Rigidbody2D；这个平台是静态碰撞体，不需要刚体" +
                                 "（加了反而会被重力 / 质量规则推走）。", this);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (Body == null) return;

            Bounds bounds = Body.bounds;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 1f);
            Gizmos.DrawLine(
                new Vector3(bounds.min.x, bounds.max.y, 0f),
                new Vector3(bounds.max.x, bounds.max.y, 0f));

            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
