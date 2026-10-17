using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 在当前场景里一键搭好「可跑的关卡骨架」：
    ///   LevelRuntime 实例（Systems + Player + Main Camera + Rule Wheel，来自 Prefab）
    ///   P4Hud 实例（P4 的 HUD，来自 Prefab）
    ///   Grid / Logic / Art（地形骨架，1 格 = 0.5 单位，和瓦片管道对齐）
    ///
    /// 还会清掉「手工粘过来的散件」—— 那些散件之间的引用是断的
    /// （PlayerController._combat / RuleWheelPresenter._player 等会变成空），
    /// 这正是「粘过来但不好用」的原因。清理走 Undo，Ctrl+Z 可还原。
    ///
    /// 用法：菜单 Tools/规则游戏/在场景里搭好关卡骨架（Ctrl+Shift+R）。
    /// </summary>
    public static class LevelRigBuilder
    {
        private const string RuntimePrefabPath = "Assets/_Project/Prefabs/LevelRuntime.prefab";
        private const string HudPrefabPath = "Assets/_Project/Prefabs/P4Hud.prefab";
        private const string RuntimeRootName = "LevelRuntime";
        private const string HudRootName = "P4Hud";

        // 手工粘散件时常见的根物体名；它们会被 LevelRuntime / P4Hud 取代
        private static readonly string[] LooseNames = { "Systems", "Player", "Rule Wheel", "Lab HUD" };

        [MenuItem("Tools/规则游戏/在场景里搭好关卡骨架 %#r", false, 1)]
        public static void Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var report = new System.Text.StringBuilder();
            report.Append("[骨架] 场景: ").Append(scene.path).Append("\n");

            var runtime = GameObject.Find(RuntimeRootName);
            if (runtime != null)
            {
                report.Append("  · LevelRuntime 已存在，跳过\n");
            }
            else
            {
                // 先清散件
                var removed = new List<string>();
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (System.Array.IndexOf(LooseNames, root.name) >= 0)
                    {
                        removed.Add(root.name);
                        Undo.DestroyObjectImmediate(root);
                    }
                }
                // 再清没接线的多余相机（LevelRuntime 自带接好线的那台）
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name == "Main Camera" && root.GetComponent<Camera>() != null)
                    {
                        removed.Add("Main Camera（模板自带、没接线）");
                        Undo.DestroyObjectImmediate(root);
                    }
                }
                if (removed.Count > 0)
                {
                    report.Append("  · 清掉散件: ").Append(string.Join(" / ", removed)).Append("（Ctrl+Z 可还原）\n");
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimePrefabPath);
                if (prefab == null)
                {
                    report.Append("  !! 找不到 ").Append(RuntimePrefabPath).Append("\n");
                }
                else
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    inst.name = RuntimeRootName;
                    inst.transform.position = Vector3.zero;
                    Undo.RegisterCreatedObjectUndo(inst, "搭建关卡骨架");
                    report.Append("  · 已放入 LevelRuntime（Systems + Player + Main Camera + Rule Wheel）\n");
                }
            }

            if (GameObject.Find(HudRootName) != null)
            {
                report.Append("  · P4Hud 已存在，跳过\n");
            }
            else
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
                if (prefab == null) report.Append("  !! 找不到 ").Append(HudPrefabPath).Append("\n");
                else
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    inst.name = HudRootName;
                    Undo.RegisterCreatedObjectUndo(inst, "搭建关卡骨架");
                    report.Append("  · 已放入 P4Hud\n");
                }
            }

            var anyTilemap = Object.FindAnyObjectByType<Tilemap>();
            if (anyTilemap != null)
            {
                report.Append("  · 场景里已有 Tilemap（").Append(anyTilemap.gameObject.name).Append("），跳过地形骨架\n");
            }
            else
            {
                var gridGo = new GameObject("Grid");
                Undo.RegisterCreatedObjectUndo(gridGo, "搭建关卡骨架");
                var grid = gridGo.AddComponent<Grid>();
                grid.cellSize = new Vector3(0.5f, 0.5f, 0f);

                var logicGo = new GameObject("Logic");
                logicGo.transform.SetParent(gridGo.transform, false);
                int groundLayer = LayerMask.NameToLayer("Ground");
                if (groundLayer >= 0) logicGo.layer = groundLayer;
                logicGo.AddComponent<Tilemap>();
                var renderer = logicGo.AddComponent<TilemapRenderer>();
                renderer.sortingOrder = -2;
                logicGo.AddComponent<TilemapCollider2D>();

                var artGo = new GameObject("Art");
                artGo.transform.SetParent(gridGo.transform, false);

                Selection.activeGameObject = gridGo;
                report.Append("  · 已建 Grid(0.5) / Logic / Art\n");
            }

            var cameras = Object.FindObjectsByType<Camera>();
            report.Append("  · 场景里的相机 ").Append(cameras.Length).Append(" 台");
            if (cameras.Length == 1) report.Append("（LevelRuntime 里那台）");
            report.Append("\n");

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(report.ToString());
        }
    }
}
