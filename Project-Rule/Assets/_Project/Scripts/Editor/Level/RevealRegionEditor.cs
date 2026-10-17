using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="RevealRegion"/> 的 Inspector：**多个矩形**按「格」设范围 + Scene 视图四角手柄。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么需要它</b>：2D 碰撞体的拖拽手柄在 Unity 6 里并不总是出现，
    /// 很多人只能去改 Transform 的 Scale —— 但 Scale 会连带缩放子物体，不是改碰撞框的正确做法。</para>
    /// <para>异形房间就「＋ 添加矩形」再加几个长方形拼起来；Scene 里每个矩形都有自己的四角手柄。</para>
    /// </remarks>
    [CustomEditor(typeof(RevealRegion))]
    public sealed class RevealRegionEditor : UnityEditor.Editor
    {
        /// <summary>与 Grid 的 Cell Size 一致（0.5 世界单位）。</summary>
        private const float CellSize = 0.5f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var region = (RevealRegion)target;
            var boxes = new List<BoxCollider2D>(region.Boxes);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("形状（可多个矩形）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "玩家落在任意一个矩形里就算进入。异形房间用几个长方形拼起来即可；\n" +
                "Scene 视图里每个矩形都有四角手柄，拖角就能改大小。",
                MessageType.None);

            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box == null) continue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"矩形 {i + 1}", GUILayout.Width(54));

                    EditorGUI.BeginChangeCheck();
                    float cx = Mathf.Max(1f, EditorGUILayout.FloatField(box.size.x / CellSize, GUILayout.Width(52)));
                    float cy = Mathf.Max(1f, EditorGUILayout.FloatField(box.size.y / CellSize, GUILayout.Width(52)));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(box, "修改揭示区域矩形");
                        box.size = new Vector2(cx * CellSize, cy * CellSize);
                        EditorUtility.SetDirty(box);
                    }

                    EditorGUILayout.LabelField("格", GUILayout.Width(20));

                    if (GUILayout.Button("选中", GUILayout.Width(44))) Selection.activeGameObject = box.gameObject;
                    if (GUILayout.Button("吸附", GUILayout.Width(44))) SnapBox(box);

                    using (new EditorGUI.DisabledScope(boxes.Count <= 1))
                    {
                        if (GUILayout.Button("删", GUILayout.Width(30)))
                        {
                            Undo.DestroyObjectImmediate(box.gameObject);
                            return;     // 列表已经变了，本帧到此为止
                        }
                    }
                }
            }

            if (GUILayout.Button("＋ 添加矩形"))
            {
                var source = boxes.Count > 0 ? boxes[0] : null;
                var go = new GameObject($"Box{boxes.Count + 1}");
                Undo.RegisterCreatedObjectUndo(go, "添加揭示区域矩形");
                go.transform.SetParent(region.transform, false);

                if (source != null)
                    go.transform.localPosition = source.transform.localPosition + new Vector3(source.size.x, 0f, 0f);

                var nb = Undo.AddComponent<BoxCollider2D>(go);
                nb.isTrigger = true;
                nb.size = source != null ? source.size : new Vector2(2f, 2f);
                Selection.activeGameObject = go;
            }

            EditorGUILayout.LabelField(
                $"共 {boxes.Count} 个矩形；并集范围 {region.Bounds.size.x:0.##} × {region.Bounds.size.y:0.##}",
                EditorStyles.miniLabel);
        }

        private static void SnapBox(BoxCollider2D box)
        {
            Undo.RecordObject(box, "吸附揭示区域矩形");
            box.size = new Vector2(Mathf.Max(CellSize, Mathf.Round(box.size.x / CellSize) * CellSize),
                                   Mathf.Max(CellSize, Mathf.Round(box.size.y / CellSize) * CellSize));

            var t = box.transform;
            Undo.RecordObject(t, "吸附揭示区域矩形");
            var p = t.position;
            t.position = new Vector3(Mathf.Round(p.x / CellSize) * CellSize,
                                     Mathf.Round(p.y / CellSize) * CellSize, p.z);

            EditorUtility.SetDirty(box);
            EditorUtility.SetDirty(t);
        }

        private void OnSceneGUI()
        {
            var region = (RevealRegion)target;
            var boxes = region.Boxes;
            var opposite = new int[] { 3, 2, 1, 0 };

            for (int b = 0; b < boxes.Count; b++)
            {
                var box = boxes[b];
                if (box == null) continue;

                Vector3 center = box.transform.TransformPoint(box.offset);
                float halfX = box.size.x * 0.5f;
                float halfY = box.size.y * 0.5f;

                Handles.color = b == 0 ? new Color(0.25f, 0.95f, 0.45f, 1f) : new Color(0.35f, 0.8f, 1f, 1f);
                Handles.DrawWireCube(center, new Vector3(box.size.x, box.size.y, 0f));

                var corners = new Vector3[]
                {
                    center + new Vector3(-halfX, -halfY, 0f),
                    center + new Vector3( halfX, -halfY, 0f),
                    center + new Vector3(-halfX,  halfY, 0f),
                    center + new Vector3( halfX,  halfY, 0f),
                };

                for (int i = 0; i < corners.Length; i++)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.FreeMoveHandle(corners[i], 0.15f, Vector3.zero, Handles.RectangleHandleCap);
                    if (!EditorGUI.EndChangeCheck()) continue;

                    Vector3 anchor = corners[opposite[i]];      // 对角固定
                    Vector3 min = Vector3.Min(moved, anchor);
                    Vector3 max = Vector3.Max(moved, anchor);

                    Undo.RecordObject(box, "拖动揭示区域手柄");
                    box.size = new Vector2(Mathf.Max(CellSize, max.x - min.x), Mathf.Max(CellSize, max.y - min.y));
                    box.offset = box.transform.InverseTransformPoint((min + max) * 0.5f);
                    EditorUtility.SetDirty(box);
                    break;
                }
            }
        }
    }
}
