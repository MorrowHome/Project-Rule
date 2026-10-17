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
            SerializedProperty launch = serializedObject.FindProperty("_launchSpeed");
            SerializedProperty hold = serializedObject.FindProperty("_holdSeconds");
            serializedObject.Update();
            if (k != null && k.floatValue > 0f)
            {
                // 无阻尼时「从一端到另一端」是半个周期 ≈ π/ω，ω = √k。实际有阻尼会稍慢一点。
                float settle = Mathf.PI / Mathf.Sqrt(k.floatValue);
                EditorGUILayout.LabelField("压到底耗时", $"≈ {settle:0.##} 秒（无阻尼近似）");
            }

            if (platform.Travel <= 0f)
            {
                EditorGUILayout.HelpBox("行程为 0：自然长度和压缩长度一样，这根弹簧不会动。", MessageType.Warning);
            }
            else if (launch != null && launch.floatValue > 0f)
            {
                EditorGUILayout.HelpBox(
                    $"弹跳板：踩上去压到底后停 {hold?.floatValue:0.##} 秒，" +
                    $"按 {launch.floatValue:0.#} 的速度把人弹飞。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"只沉不弹：压到底后停 {hold?.floatValue:0.##} 秒自己浮回自然长度，" +
                    "不给速度。把停顿调得很大就是「压住不动」的承重台。", MessageType.Info);
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
