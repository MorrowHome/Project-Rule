using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// <see cref="TileRegion"/> 的 Inspector：**多个矩形**按「格」设范围 + Scene 视图四角手柄。
    /// </summary>
    /// <remarks>
    /// <para>和 <see cref="RevealRegionEditor"/> 同一套操作：宽/高按**格**填、可以吸附到格、
    /// Scene 里每个矩形都有四角手柄（对角固定），不用去改 Transform 的 Scale。</para>
    ///
    /// <para>比揭示区域多一列：每个矩形要选**这一片用哪套瓦片**。一个分区只属于一个王国时
    /// 只填上面的「默认套件」就够了；横跨两个王国才需要加矩形。</para>
    /// </remarks>
    [CustomEditor(typeof(TileRegion))]
    public sealed class TileRegionEditor : UnityEditor.Editor
    {
        /// <summary>与 Grid 的 Cell Size 一致（0.5 世界单位）。</summary>
        private const float CellSize = 0.5f;

        public override void OnInspectorGUI()
        {
            var region = (TileRegion)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("_defaultKit"));

            var list = serializedObject.FindProperty("_areas");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("区域矩形（可多个）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "铺装时按世界坐标查表：命中哪个矩形就用哪个套件，都没命中用上面的默认套件。\n" +
                "Scene 视图里每个矩形都有四角手柄，拖角就能改大小。",
                MessageType.None);

            for (int i = 0; i < list.arraySize; i++)
            {
                var item = list.GetArrayElementAtIndex(i);
                var boxProp = item.FindPropertyRelative("Box");
                var kitProp = item.FindPropertyRelative("Kit");
                var box = boxProp.objectReferenceValue as BoxCollider2D;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"矩形 {i + 1}", GUILayout.Width(54));

                    if (box != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        float cx = Mathf.Max(1f, EditorGUILayout.FloatField(box.size.x / CellSize, GUILayout.Width(52)));
                        float cy = Mathf.Max(1f, EditorGUILayout.FloatField(box.size.y / CellSize, GUILayout.Width(52)));
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(box, "修改区域矩形");
                            box.size = new Vector2(cx * CellSize, cy * CellSize);
                            EditorUtility.SetDirty(box);
                        }

                        EditorGUILayout.LabelField("格", GUILayout.Width(20));

                        if (GUILayout.Button("选中", GUILayout.Width(44))) Selection.activeGameObject = box.gameObject;
                        if (GUILayout.Button("吸附", GUILayout.Width(44))) SnapBox(box);
                    }
                    else
                    {
                        EditorGUILayout.LabelField("（矩形丢了，删掉重加）", EditorStyles.miniLabel);
                    }

                    if (GUILayout.Button("删", GUILayout.Width(30)))
                    {
                        RemoveArea(list, i, box);
                        serializedObject.ApplyModifiedProperties();
                        return;     // 列表已经变了，本帧到此为止
                    }
                }

                EditorGUI.indentLevel++;
                kitProp.objectReferenceValue = EditorGUILayout.ObjectField(
                    "套件", kitProp.objectReferenceValue, typeof(TileRegionKit), false);
                EditorGUI.indentLevel--;
            }

            if (GUILayout.Button("＋ 添加矩形"))
            {
                AddArea(region, list);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>删掉第 <paramref name="index"/> 个矩形：先从列表摘掉，再销毁它的物体。</summary>
        private static void RemoveArea(SerializedProperty list, int index, BoxCollider2D box)
        {
            int before = list.arraySize;
            list.DeleteArrayElementAtIndex(index);
            // 托管引用数组要删两次：第一次只是把引用置空。
            if (list.arraySize == before) list.DeleteArrayElementAtIndex(index);

            if (box != null) Undo.DestroyObjectImmediate(box.gameObject);
        }

        private static void AddArea(TileRegion region, SerializedProperty list)
        {
            var source = list.arraySize > 0
                ? list.GetArrayElementAtIndex(0).FindPropertyRelative("Box").objectReferenceValue as BoxCollider2D
                : null;

            var go = new GameObject($"Region{list.arraySize + 1}");
            Undo.RegisterCreatedObjectUndo(go, "添加区域矩形");
            go.transform.SetParent(region.transform, false);

            if (source != null)
            {
                go.transform.localPosition = source.transform.localPosition + new Vector3(source.size.x, 0f, 0f);
            }
            else
            {
                // 没有参考框就放在 Scene 视图中心，免得扔到看不见的地方。
                var view = SceneView.lastActiveSceneView;
                go.transform.position = view != null
                    ? new Vector3(view.pivot.x, view.pivot.y, 0f)
                    : region.transform.position;
            }

            var nb = Undo.AddComponent<BoxCollider2D>(go);
            nb.isTrigger = true;                                  // 只是范围定义，不参与物理
            nb.size = source != null ? source.size : new Vector2(4f, 4f);   // 默认 8 × 8 格

            list.arraySize++;
            var item = list.GetArrayElementAtIndex(list.arraySize - 1);
            item.FindPropertyRelative("Box").objectReferenceValue = nb;
            item.FindPropertyRelative("Kit").objectReferenceValue = null;

            Selection.activeGameObject = go;
        }

        private static void SnapBox(BoxCollider2D box)
        {
            Undo.RecordObject(box, "吸附区域矩形");
            box.size = new Vector2(Mathf.Max(CellSize, Mathf.Round(box.size.x / CellSize) * CellSize),
                                   Mathf.Max(CellSize, Mathf.Round(box.size.y / CellSize) * CellSize));

            var t = box.transform;
            Undo.RecordObject(t, "吸附区域矩形");
            var p = t.position;
            t.position = new Vector3(Mathf.Round(p.x / CellSize) * CellSize,
                                     Mathf.Round(p.y / CellSize) * CellSize, p.z);

            EditorUtility.SetDirty(box);
            EditorUtility.SetDirty(t);
        }

        private void OnSceneGUI()
        {
            var region = (TileRegion)target;
            var areas = region.Areas;
            var opposite = new int[] { 3, 2, 1, 0 };

            for (int b = 0; b < areas.Count; b++)
            {
                var box = areas[b] != null ? areas[b].Box : null;
                if (box == null) continue;

                Vector3 center = box.transform.TransformPoint(box.offset);
                float halfX = box.size.x * 0.5f;
                float halfY = box.size.y * 0.5f;

                // 矩形 1 用绿色，其余蓝色 —— 和揭示区域保持一致，一眼看出是两套系统但同一种操作。
                Handles.color = b == 0 ? new Color(0.25f, 0.95f, 0.45f, 1f) : new Color(0.35f, 0.8f, 1f, 1f);
                Handles.DrawWireCube(center, new Vector3(box.size.x, box.size.y, 0f));

                var kit = areas[b].Kit;
                Handles.Label(new Vector3(center.x - halfX, center.y + halfY + 0.1f, 0f),
                    kit != null ? kit.DisplayName : "（未指定套件）");

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

                    Undo.RecordObject(box, "拖动区域手柄");
                    box.size = new Vector2(Mathf.Max(CellSize, max.x - min.x), Mathf.Max(CellSize, max.y - min.y));
                    box.offset = box.transform.InverseTransformPoint((min + max) * 0.5f);
                    EditorUtility.SetDirty(box);
                    break;
                }
            }
        }
    }
}
