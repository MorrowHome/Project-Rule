using UnityEngine;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 平台板面：**一张可配置的瓦片图** + 跟着它走的碰撞箱。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么单独一个组件</b>：<see cref="TrackMotor"/> 只管「沿轨道走」，一行美术都不认识；
    /// 平台长什么样、多宽多厚，住在这里。于是同一个会动的平台**换一张图就是另一个王国的东西**
    /// （石块 → 发条 → 积木），玩法、轨道、承载一行都不用动。</para>
    ///
    /// <para><b>换图之后碰撞箱会跟着改</b>：默认 <see cref="FillMode.Native"/>「按原图尺寸」——
    /// 图多宽，碰撞就多宽，**不会把画好的图拉变形**。要定长再切到平铺 / 拉伸。
    /// 厚度固定 **1 格 = 0.5 单位**（与关卡里的薄台同规格）：美术实心厚 0.484，
    /// 多出来那 0.016 压在板底下看不出来，但碰撞落在整数格上。</para>
    ///
    /// <para><b>轴心约定</b>：根物体 = 板面**上表面的中点**。所以把它摆在格线上，
    /// 人踩的那一面就正好落在格线上。美术子物体按瓦片**自己的轴心**（地面 / 平台是左上角、
    /// 方块是中心）自动贴到碰撞箱左上角 —— **换图不用手动对位**。</para>
    ///
    /// <para>⚠️ 平铺模式要求瓦片是 <c>Full Rect</c> 网格（本工程 <c>Tiles/</c> 下的图都是），
    /// 且两端有羽化收尾的「整段短台」（<c>Platform_Stone_Short_01/02/03</c>）**不要用平铺**
    /// —— 羽化边叠羽化边会压出一条亮带（见 本地文档/瓦片组件转场景制作方案.md §4）。
    /// 要长平台用「端帽 + 中段」拼，或者接受 <see cref="FillMode.Stretch"/> 的小幅拉伸。</para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class PlatformSkin : MonoBehaviour
    {
        /// <summary>图比碰撞箱宽/窄时怎么铺。</summary>
        public enum FillMode
        {
            /// <summary>按原图尺寸：碰撞箱宽度**跟着图走**。默认，不拉变形。</summary>
            Native = 0,

            /// <summary>平铺：按 <see cref="Width"/> 重复铺满（⚠️ 端头有羽化的整段短台别用）。</summary>
            Tiled = 1,

            /// <summary>拉伸：把图拉到 <see cref="Width"/>（⚠️ 拉太多裂纹会一起变宽）。</summary>
            Stretch = 2,
        }

        [Header("图像")]
        [Tooltip("板面用哪张瓦片图。换图后碰撞箱宽度会跟着走（原图尺寸模式）。\n" +
                 "石块平台：Tiles/Stone/Platform_Stone_Short_01（整段短台，两端都收尾）；\n" +
                 "各王国的方块：Tiles/Regions/<区域>_Kit.asset 里的 Blocks。")]
        [SerializeField] private Sprite _sprite;

        [Tooltip("板面美术。留空会自动找子物体里的 SpriteRenderer（约定叫 Art）。")]
        [SerializeField] private SpriteRenderer _art;

        [Header("尺寸")]
        [Tooltip("板面多宽（世界单位）。1 格 = 0.5。**只在 平铺 / 拉伸 两种模式下生效**；\n" +
                 "原图尺寸模式下宽度由「图像」决定。")]
        [SerializeField, Min(0.05f)] private float _width = 2.75f;

        [Tooltip("碰撞厚（世界单位）。1 格 = 0.5 —— 与关卡里的薄台同规格，别乱改。")]
        [SerializeField, Min(0.05f)] private float _thickness = 0.5f;

        [Tooltip("按原图尺寸 / 平铺 / 拉伸。默认「原图尺寸」，不会把画好的图拉变形。")]
        [SerializeField] private FillMode _fill = FillMode.Native;

        /// <summary>当前用的板面图（可能是 null）。</summary>
        public Sprite Sprite => _sprite;

        /// <summary>当前碰撞箱宽度（世界单位）。</summary>
        public float Width => _fill == FillMode.Native && _sprite != null
            ? _sprite.bounds.size.x
            : Mathf.Max(0.05f, _width);

        /// <summary>当前碰撞厚（世界单位）。</summary>
        public float Thickness => Mathf.Max(0.05f, _thickness);

        /// <summary>换一张板面图（运行时 / 工具用）。</summary>
        public void SetSprite(Sprite sprite)
        {
            _sprite = sprite;
            Apply();
        }

        /// <summary>
        /// 把图与碰撞箱按当前配置摆好。**幂等**：值没变就不写，
        /// 免得每次重载 / 重编译都平白把场景标脏。
        /// </summary>
        [ContextMenu("重新应用（贴图 + 对齐碰撞箱）")]
        public void Apply()
        {
            if (_art == null) _art = ResolveArt();
            if (_art == null || _sprite == null) return;

            Vector2 native = _sprite.bounds.size;
            if (native.x <= 0.0001f || native.y <= 0.0001f) return;

            float width = Width;
            float thickness = Thickness;

            // ① 碰撞：上表面永远落在根物体的 y 上（摆在格线上，踩的那面就在格线上）。
            var box = GetComponent<BoxCollider2D>();
            if (box != null)
            {
                var size = new Vector2(width, thickness);
                var offset = new Vector2(0f, -thickness * 0.5f);
                if (box.size != size) box.size = size;
                if (box.offset != offset) box.offset = offset;
            }

            // ② 美术：先决定它实际铺多大，再把它自己的「左上角」贴到碰撞箱的左上角。
            Vector2 drawn;
            switch (_fill)
            {
                case FillMode.Tiled:
                    _art.drawMode = SpriteDrawMode.Tiled;
                    _art.tileMode = SpriteTileMode.Continuous;
                    _art.size = new Vector2(width, native.y);
                    if (_art.transform.localScale != Vector3.one) _art.transform.localScale = Vector3.one;
                    drawn = new Vector2(width, native.y);
                    break;

                case FillMode.Stretch:
                    _art.drawMode = SpriteDrawMode.Simple;
                    _art.size = native;
                    var scale = new Vector3(width / native.x, 1f, 1f);
                    if (_art.transform.localScale != scale) _art.transform.localScale = scale;
                    drawn = new Vector2(width, native.y);
                    break;

                default: // Native：图多宽，平台就多宽。
                    _art.drawMode = SpriteDrawMode.Simple;
                    _art.size = native;
                    if (_art.transform.localScale != Vector3.one) _art.transform.localScale = Vector3.one;
                    drawn = native;
                    break;
            }

            if (_art.sprite != _sprite) _art.sprite = _sprite;

            // 瓦片自带轴心（地面 / 平台 = 左上角，方块 = 中心）：把它换算成「相对自己 transform 的左上角」，
            // 再让那个点去对齐碰撞箱左上角。这样换任何轴心的图都不用人工对位。
            Vector2 pivot = new Vector2(_sprite.pivot.x / _sprite.rect.width,
                                        _sprite.pivot.y / _sprite.rect.height);
            var artTopLeft = new Vector2(-pivot.x * drawn.x, (1f - pivot.y) * drawn.y);
            var wanted = new Vector3(-width * 0.5f - artTopLeft.x, -artTopLeft.y, 0f);
            if (_art.transform.localPosition != wanted) _art.transform.localPosition = wanted;
        }

        /// <summary>子物体里的 SpriteRenderer（约定叫 Art）；找不到就退而求其次找任意一个。</summary>
        private SpriteRenderer ResolveArt()
        {
            Transform child = transform.Find("Art");
            if (child != null)
            {
                var named = child.GetComponent<SpriteRenderer>();
                if (named != null) return named;
            }
            return GetComponentInChildren<SpriteRenderer>(true);
        }

        /// <summary>运行时兜底：实例化出来的对象也保证图与碰撞是一致的。</summary>
        private void Awake() => Apply();

        /// <summary>Inspector 里改一个值就立刻生效（不用进 Play 看效果）。</summary>
        /// <remarks>
        /// ⚠️ <b>不能在这里直接改 SpriteRenderer</b>：写 <c>drawMode</c> / <c>size</c> / <c>sprite</c>
        /// 会让 SpriteRenderer 走一次内部 SendMessage，而 Unity 在 <c>OnValidate</c> 期间禁止 SendMessage
        /// —— Console 会刷 "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate
        /// (Art: OnSpriteTilingPropertyChange)"。所以编辑模式下**推迟一拍**再应用。
        /// </remarks>
        private void OnValidate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall -= DeferredApply;
                UnityEditor.EditorApplication.delayCall += DeferredApply;
                return;
            }
#endif
            Apply();
        }

#if UNITY_EDITOR
        /// <summary>推迟到编辑器空闲时应用，见 <see cref="OnValidate"/>。</summary>
        private void DeferredApply()
        {
            UnityEditor.EditorApplication.delayCall -= DeferredApply;
            if (this == null) return;   // 这一拍之间被删掉了
            Apply();
        }
#endif

#if UNITY_EDITOR
        /// <summary>编辑器里画一条「踩面」标记：上表面那条线就是人站的位置。</summary>
        private void OnDrawGizmosSelected()
        {
            float width = Width;
            Gizmos.color = new Color(0.35f, 0.9f, 1f, 0.9f);
            Gizmos.DrawLine(transform.position + new Vector3(-width * 0.5f, 0f, 0f),
                            transform.position + new Vector3(width * 0.5f, 0f, 0f));
        }
#endif
    }
}
