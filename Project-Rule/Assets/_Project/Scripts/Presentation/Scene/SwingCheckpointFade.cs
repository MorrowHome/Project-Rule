using UnityEngine;

namespace RuleGame.Presentation.SceneComponents
{
    /// <summary>过场用的全屏黑场。用 OnGUI 画一层黑，不依赖 Canvas / UI 包。</summary>
    public sealed class SwingCheckpointFade : MonoBehaviour
    {
        [Range(0f, 1f)] public float Alpha;

        private void OnGUI()
        {
            if (Alpha <= 0.001f) return;

            int depth = GUI.depth;
            Color color = GUI.color;

            // 压在最上面；画完恢复，避免影响其它 OnGUI 调试层。
            GUI.depth = -1000;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(Alpha));
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

            GUI.color = color;
            GUI.depth = depth;
        }
    }
}
