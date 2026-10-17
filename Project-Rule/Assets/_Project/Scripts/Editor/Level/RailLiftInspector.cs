using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="RailLift"/> 的 Inspector：摆档位用几个按钮，省得手敲本地 y。
    /// </summary>
    /// <remarks>
    /// 摆轨道的常规流程：选中轨道 → 在 Scene 里把滑块拖到**常态**该在的高度
    /// → 点「中档 = 滑块当前位置」→ 再填「升 / 沉」两个高度差。
    /// 三个「滑块移到…」按钮用来跳过去对位置（低档 / 中档 / 高档各看一眼）。
    /// </remarks>
    [CustomEditor(typeof(RailLift))]
    public sealed class RailLiftInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var lift = (RailLift)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("档位工具", EditorStyles.boldLabel);

            if (GUILayout.Button("中档 = 滑块当前位置"))
            {
                Undo.RecordObject(lift, "设置滑轨中档");
                lift.SetTiers(lift.CarriageLocalY, lift.RiseHeight, lift.SinkDepth);
                EditorUtility.SetDirty(lift);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                CarriageButton(lift, RailLift.Tier.Low, "滑块移到低档（重）");
                CarriageButton(lift, RailLift.Tier.Middle, "滑块移到中档");
                CarriageButton(lift, RailLift.Tier.High, "滑块移到高档（轻）");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("低档", $"{lift.HeightOf(RailLift.Tier.Low):0.###}（本地 y）");
            EditorGUILayout.LabelField("中档", $"{lift.HeightOf(RailLift.Tier.Middle):0.###}（本地 y）");
            EditorGUILayout.LabelField("高档", $"{lift.HeightOf(RailLift.Tier.High):0.###}（本地 y）");

            float span = lift.RiseHeight + lift.SinkDepth;
            EditorGUILayout.LabelField("全行程", $"{span:0.###} 单位");

            if (span > 0f)
            {
                float longest = Mathf.Max(lift.RiseHeight, lift.SinkDepth);
                EditorGUILayout.LabelField("单档耗时", $"{longest / lift.Speed:0.##} 秒（最长那一档）");
            }
            else
            {
                EditorGUILayout.HelpBox("升 / 沉都是 0，这个滑轨不会动。", MessageType.Warning);
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "档位是**本地坐标**（相对轨道原点的 y）。迁移这一段时拖根物体即可，三档跟着走。\n" +
                    "⚠️ 不要缩放根物体 —— 父级缩放会被滑块继承，碰撞箱与位移都会变形。",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("当前档", lift.CurrentTier.ToString());
            EditorGUILayout.LabelField("本帧速度", $"{lift.Velocity.y:0.###} 单位/秒");
        }

        /// <summary>把滑块搬回某一档（只在编辑模式下用，运行期它自己会走）。</summary>
        private static void CarriageButton(RailLift lift, RailLift.Tier tier, string label)
        {
            if (!GUILayout.Button(label)) return;

            Transform carriage = lift.Carriage;
            if (carriage == null)
            {
                Debug.LogWarning("[滑轨] 没有滑块，先在「引用」栏指定。", lift);
                return;
            }

            Vector3 local = carriage.localPosition;
            local.y = lift.HeightOf(tier);

            Undo.RecordObject(carriage, "移动滑轨滑块");
            carriage.localPosition = local;

            // 摆位顺带把「中档」记下来，省一次手敲。
            if (tier == RailLift.Tier.Middle)
            {
                Undo.RecordObject(lift, "设置滑轨中档");
                lift.SetTiers(local.y, lift.RiseHeight, lift.SinkDepth);
                EditorUtility.SetDirty(lift);
            }
        }
    }
}
