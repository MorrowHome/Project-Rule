using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleGame.Gameplay.Scene
{
    /// <summary>
    /// 黑色遮罩层的外观开关：**编辑器里半透明**（方便对位），**运行时纯黑**（真正挡住摄像机）。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么不用改瓦片资产</b>：瓦片是共享资产，改它会影响所有用到的地方。
    /// 这里改的是 <see cref="Tilemap.color"/> —— 它和每格颜色相乘，只影响这一张图。</para>
    /// <para><b>运行时永远纯黑</b>：进入 Play 时自动把 alpha 拉满，退出 Play 自动回到编辑器透明度
    /// （运行时改的值不会写回场景）。</para>
    /// <para><b>铺装完想立刻看最终效果</b>：跑一次 <c>Ctrl+Shift+G</c> 后遮罩会自动变纯黑；
    /// 想改回半透明继续对位，用菜单 <c>Tools/规则游戏/关卡瓦片/③ 遮罩：半透明 ↔ 纯黑</c>。</para>
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Tilemap))]
    public sealed class MaskLayer : MonoBehaviour
    {
        [Tooltip("编辑器里（未运行时）的遮罩透明度。1 = 纯黑看不见，0.5 = 半透明方便看底下的白盒。")]
        [SerializeField, Range(0f, 1f)] private float _editorAlpha = 0.5f;

        private Tilemap _tilemap;

        /// <summary>编辑器里使用的透明度。</summary>
        public float EditorAlpha => _editorAlpha;

        /// <summary>运行时的透明度：永远纯黑。</summary>
        public float RuntimeAlpha => 1f;

        private void OnEnable()
        {
            _tilemap = GetComponent<Tilemap>();
            Apply();
        }

        private void OnValidate()
        {
            if (_tilemap == null) _tilemap = GetComponent<Tilemap>();

            // ⚠️ 不能在 OnValidate（以及 Awake / CheckConsistency —— **保存场景时会走**）里改
            // Tilemap.color：set_color 内部会 SendMessage(OnTilemapChanged)，Unity 明确禁止在这几个回调里调用，
            // 否则每次保存/校验都会刷 "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate"。
            // 所以推迟到编辑器空闲时再应用。
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall -= ApplyDeferred;
                UnityEditor.EditorApplication.delayCall += ApplyDeferred;
                return;
            }
#endif
            Apply();
        }

#if UNITY_EDITOR
        private void ApplyDeferred()
        {
            if (this == null) return;      // 对象可能已经被删掉了
            Apply();
        }
#endif

        private void Update()
        {
            // ExecuteAlways：编辑器里也要跟着改，这样拖 Inspector 滑条能立刻看到
            if (!Application.isPlaying) Apply();
        }

        /// <summary>设置编辑器透明度（0~1）。运行时不受影响。</summary>
        public void SetEditorAlpha(float alpha)
        {
            _editorAlpha = Mathf.Clamp01(alpha);
            if (_tilemap == null) _tilemap = GetComponent<Tilemap>();
            Apply();
        }

        private void Apply()
        {
            if (_tilemap == null) return;

            float alpha = Application.isPlaying ? RuntimeAlpha : _editorAlpha;
            Color c = _tilemap.color;
            if (Mathf.Approximately(c.a, alpha)) return;

            _tilemap.color = new Color(1f, 1f, 1f, alpha);
        }
    }
}
