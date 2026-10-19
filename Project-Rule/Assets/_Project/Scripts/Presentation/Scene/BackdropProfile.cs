using UnityEngine;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>
    /// 一张背景图的配置：贴图、整体染色、相机底色、淡化时长。
    /// </summary>
    /// <remarks>
    /// <para><b>它只是数据</b>，不认识相机也不认识玩家；切换逻辑在 <see cref="BackdropDirector"/>。</para>
    /// <para><b>世界尺寸不写在这里</b>：贴图多大由 PPU 决定，Director 每帧按
    /// <c>sprite.bounds</c> 和相机视野算 cover 缩放 —— 换图不会出现「看起来 16 米、实际 12 米」。</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "Area_Backdrop", menuName = "RuleGame/背景/背景配置")]
    public sealed class BackdropProfile : ScriptableObject
    {
        [Tooltip("背景贴图。轴心必须是 Center，Director 按中心做 cover 缩放。")]
        [SerializeField] private Sprite _sprite;

        [Tooltip("整体乘以这个颜色。黄砖和浅背景贴在一起时用它压暗。")]
        [SerializeField] private Color _tint = Color.white;

        [Tooltip("同步给 Camera.backgroundColor。任何没被贴图盖住的像素都是这个颜色，不会露出青黑底。")]
        [SerializeField] private Color _clearColor = new Color(0.941f, 0.890f, 0.812f, 1f);

        [Tooltip("切到这张图的淡化时长（秒，非缩放时间）。")]
        [SerializeField, Min(0f)] private float _fadeSeconds = 0.35f;

        public Sprite Sprite => _sprite;
        public Color Tint => _tint;
        public Color ClearColor => _clearColor;
        public float FadeSeconds => Mathf.Max(0.01f, _fadeSeconds);
    }
}
