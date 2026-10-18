using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="SpringPlatform"/> 的 Inspector：加几个按钮，省得手敲锚点坐标，
    /// 外加把「这根弹簧调出来是什么感觉」当场算给策划看。
    /// </summary>
    /// <remarks>
    /// 摆弹簧的常规流程：把物体拖到想要的高度 → 点「锚点 = 平台正下方」→ 弹簧就立起来了。
    /// 想看看压到底有多低，点「平台移到压缩位」。
    /// </remarks>
    [CustomEditor(typeof(SpringPlatform))]
    public sealed class SpringPlatformInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var platform = (SpringPlatform)target;
            var t = platform.transform;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("锚点工具", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("锚点 = 平台正下方 · 自然长度"))
                {
                    SetAnchor(platform, new Vector2(t.position.x, t.position.y - platform.RestLength));
                }
                if (GUILayout.Button("平台移到自然长度位"))
                {
                    MoveTo(t, platform.AnchorPoint + Vector2.up * platform.RestLength);
                }
            }

            if (GUILayout.Button("平台移到压缩位（预演压到底）"))
            {
                MoveTo(t, platform.AnchorPoint + Vector2.up * platform.CompressedLength);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("弹簧", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("自然长度", $"{platform.RestLength:0.###} 单位");
            EditorGUILayout.LabelField("压缩长度", $"{platform.CompressedLength:0.###} 单位");
            EditorGUILayout.LabelField("行程", $"{platform.Travel:0.###} 单位");

            SerializedProperty k = serializedObject.FindProperty("_springConstant");
            SerializedProperty fullMass = serializedObject.FindProperty("_fullCompressionMass");
            serializedObject.Update();
            if (k != null && k.floatValue > 0f)
            {
                // 无阻尼时「从一端到另一端」是半个周期 ≈ π/ω，ω = √k。实际有阻尼会稍慢一点。
                float settle = Mathf.PI / Mathf.Sqrt(k.floatValue);
                EditorGUILayout.LabelField("压到底耗时", $"≈ {settle:0.##} 秒（无阻尼近似）");
            }

            if (fullMass != null)
            {
                EditorGUILayout.LabelField("压到底需要", $"{fullMass.floatValue:0.##} kg");
            }

            if (platform.Travel <= 0f)
            {
                EditorGUILayout.HelpBox("行程为 0：自然长度和压缩长度一样，这根弹簧不会动。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"压上来的东西停在「重力 = 弹簧力」的位置，载重拿掉才回弹。\n" +
                    $"载重达到 {fullMass?.floatValue:0.##} kg 时压到底，更轻的停在半路 —— " +
                    "所以质量规则会直接改变弹簧被压多深。", MessageType.Info);
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("运行状态", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("阶段", platform.PhaseName);
                EditorGUILayout.LabelField("载重", $"{platform.LoadMass:0.###} kg");
                EditorGUILayout.LabelField("压缩量", $"{platform.Compression:0.###}");

                // 让这些数字在 play 中实时刷新（OnInspectorGUI 本身不会每帧自动重绘）。
                Repaint();
            }
        }

        private static void SetAnchor(SpringPlatform platform, Vector2 anchor)
        {
            Undo.RecordObject(platform, "设置弹簧锚点");
            platform.SetAnchor(anchor);
            EditorUtility.SetDirty(platform);
        }

        private static void MoveTo(Transform t, Vector2 target)
        {
            Undo.RecordObject(t, "移动弹簧平台");
            t.position = new Vector3(target.x, target.y, t.position.z);
        }
    }
}
