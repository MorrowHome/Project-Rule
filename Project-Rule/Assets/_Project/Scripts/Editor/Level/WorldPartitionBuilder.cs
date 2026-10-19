using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Scene;
using RuleGame.Infrastructure.LevelFlow;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 世界分区的一键搭建工具：生成 GameRoot 预制体、Boot 场景、分区表资产，并登记 Build Settings。
    /// </summary>
    /// <remarks>
    /// <para><b>产物</b>：</para>
    /// <list type="bullet">
    /// <item><c>Assets/_Project/Data/Config/PartitionCatalog.asset</c> —— 分区表</item>
    /// <item><c>Assets/_Project/Prefabs/GameRoot.prefab</c> —— 常驻宿主（GameRoot + WorldStreamer）</item>
    /// <item><c>Assets/_Project/Scenes/Boot.unity</c> —— 启动场景，放 GameRoot 预制体实例 + 启动开关</item>
    /// </list>
    /// <para><b>不会覆盖已有资产</b>：已经存在的预制体 / 场景只做提示，不重建（避免抹掉手工调整）。</para>
    /// <para><b>Boot 必须排在 Build Settings 第 0 位</b>，否则打包后第一个加载的是分区场景，
    /// 常驻宿主不会出生。</para>
    /// </remarks>
    public static class WorldPartitionBuilder
    {
        private const string CatalogPath  = "Assets/_Project/Data/Config/PartitionCatalog.asset";
        private const string PrefabPath   = "Assets/_Project/Prefabs/GameRoot.prefab";
        private const string BootScenePath = "Assets/_Project/Scenes/Boot.unity";
        private const string LevelRuntimePrefabPath = "Assets/_Project/Prefabs/LevelRuntime.prefab";

        private const string RootName = "GameRoot";
        private const string StarterName = "BootStarter";

        [MenuItem("Tools/规则游戏/世界分区/① 生成 GameRoot 预制体与 Boot 场景", false, 40)]
        public static void Build()
        {
            var report = new System.Text.StringBuilder("[世界分区]\n");

            PartitionCatalog catalog = EnsureCatalog(report);
            GameObject prefab = EnsureGameRootPrefab(catalog, report);
            EnsureLevelRuntimeNested(report);
            EnsureBootScene(prefab, report);
            EnsureBootFirstInBuildSettings(report);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(report.ToString());
        }

        [MenuItem("Tools/规则游戏/世界分区/③ 把 LevelRuntime 嵌进 GameRoot 预制体", false, 42)]
        public static void NestLevelRuntime()
        {
            var report = new System.Text.StringBuilder("[世界分区]\n");
            EnsureLevelRuntimeNested(report);
            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        [MenuItem("Tools/规则游戏/世界分区/② 把当前场景登记为分区", false, 41)]
        public static void RegisterCurrentScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                Debug.LogError("[世界分区] 当前场景还没保存过，先 Ctrl+S 再登记。");
                return;
            }

            // ⚠️ Boot 不是分区：它只装着 GameRoot / 主菜单 / 传送服务 / 黑场。
            // 把它登记进去会让流式加载卸载它（主菜单、传送面板一起消失）再加载第二份（第二个开始菜单）。
            if (string.Equals(scene.path, BootScenePath, System.StringComparison.Ordinal))
            {
                Debug.LogError("[世界分区] Boot 是启动场景，不是世界分区，已拒绝登记。");
                return;
            }

            PartitionCatalog catalog = AssetDatabase.LoadAssetAtPath<PartitionCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[世界分区] 找不到分区表：{CatalogPath}。先跑一次 ①。");
                return;
            }

            if (!TryComputeSceneBounds(scene, out Bounds bounds))
            {
                Debug.LogError("[世界分区] 这个场景里没有任何 Renderer，算不出范围。");
                return;
            }

            var so = new SerializedObject(catalog);
            SerializedProperty arr = so.FindProperty("_partitions");

            int existing = IndexOfScene(arr, scene.path);
            SerializedProperty entry = existing >= 0
                ? arr.GetArrayElementAtIndex(existing)
                : AppendElement(arr);

            if (existing < 0)
                entry.FindPropertyRelative("PartitionId").intValue = NextPartitionId(arr);

            entry.FindPropertyRelative("ScenePath").stringValue = scene.path;
            entry.FindPropertyRelative("Center").vector2Value = bounds.center;
            entry.FindPropertyRelative("Size").vector2Value = bounds.size;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            Debug.Log($"[世界分区] 已登记 {scene.name}\n" +
                      $"  路径 {scene.path}\n" +
                      $"  中心 ({bounds.center.x:0.##}, {bounds.center.y:0.##})\n" +
                      $"  尺寸 ({bounds.size.x:0.##} x {bounds.size.y:0.##})\n" +
                      $"  预载半径 0（用默认值）· 常驻关");
        }

        // ==================================================================
        //  资产生成
        // ==================================================================

        private static PartitionCatalog EnsureCatalog(System.Text.StringBuilder report)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PartitionCatalog>(CatalogPath);
            if (catalog != null)
            {
                report.Append("  · 分区表已存在，跳过\n");
                return catalog;
            }

            EnsureFolder("Assets/_Project/Data/Config");

            catalog = ScriptableObject.CreateInstance<PartitionCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            report.Append("  · 新建分区表 ").Append(CatalogPath).Append("\n");
            return catalog;
        }

        private static GameObject EnsureGameRootPrefab(PartitionCatalog catalog, System.Text.StringBuilder report)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                report.Append("  · GameRoot 预制体已存在，跳过\n");
                return existing;
            }

            EnsureFolder("Assets/_Project/Prefabs");

            var go = new GameObject(RootName);
            var streamer = go.AddComponent<WorldStreamer>();
            var root = go.AddComponent<GameRoot>();

            // 序列化字段只能走 SerializedObject —— 直接赋值不会写进预制体。
            var rootSo = new SerializedObject(root);
            rootSo.FindProperty("_streamer").objectReferenceValue = streamer;
            rootSo.ApplyModifiedPropertiesWithoutUndo();

            var streamerSo = new SerializedObject(streamer);
            streamerSo.FindProperty("_catalog").objectReferenceValue = catalog;
            streamerSo.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);

            report.Append("  · 新建 GameRoot 预制体（GameRoot + WorldStreamer）").Append(PrefabPath).Append("\n");
            return prefab;
        }

        /// <summary>
        /// 把 LevelRuntime 作为**嵌套预制体**嵌进 GameRoot。
        /// </summary>
        /// <remarks>用嵌套而不是复制内容：以后改玩家 / 相机 / 规则系统只改 LevelRuntime.prefab 一处，
        /// GameRoot 自动跟着变。用 LoadPrefabContents 而不是直接改实例，是为了不依赖场景里有没有实例。</remarks>
        private static void EnsureLevelRuntimeNested(System.Text.StringBuilder report)
        {
            var runtimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LevelRuntimePrefabPath);
            if (runtimePrefab == null)
            {
                report.Append("  !! 找不到 ").Append(LevelRuntimePrefabPath).Append("，常驻装配没嵌进去\n");
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (contents == null)
            {
                report.Append("  !! 打不开 ").Append(PrefabPath).Append("，先跑一次 ①\n");
                return;
            }

            try
            {
                foreach (Transform child in contents.transform)
                {
                    if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == runtimePrefab)
                    {
                        report.Append("  · LevelRuntime 已经嵌在 GameRoot 里，跳过\n");
                        return;
                    }
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(runtimePrefab, contents.transform);
                instance.name = "LevelRuntime";
                instance.transform.localPosition = Vector3.zero;

                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                report.Append("  · 已把 LevelRuntime 作为嵌套预制体嵌进 GameRoot\n");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void EnsureBootScene(GameObject prefab, System.Text.StringBuilder report)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath) != null)
            {
                report.Append("  · Boot 场景已存在，跳过\n");
                return;
            }

            if (prefab == null)
            {
                report.Append("  !! 没有 GameRoot 预制体，Boot 场景没生成\n");
                return;
            }

            EnsureFolder("Assets/_Project/Scenes");

            // 用附加方式建，避免把用户正在编辑的场景顶掉；建完关掉并切回原场景。
            Scene original = SceneManager.GetActiveScene();
            Scene boot = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(boot);

            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, boot);
                instance.name = RootName;

                var starter = new GameObject(StarterName);
                starter.AddComponent<BootFlowStarter>();

                EditorSceneManager.SaveScene(boot, BootScenePath);
                report.Append("  · 新建 Boot 场景（GameRoot 预制体实例 + BootFlowStarter）").Append(BootScenePath).Append("\n");
            }
            finally
            {
                EditorSceneManager.CloseScene(boot, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
        }

        private static void EnsureBootFirstInBuildSettings(System.Text.StringBuilder report)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int at = scenes.FindIndex(s => s.path == BootScenePath);

            if (at == 0)
            {
                report.Append("  · Boot 已在 Build Settings 第 0 位\n");
                return;
            }

            if (at > 0) scenes.RemoveAt(at);
            scenes.Insert(0, new EditorBuildSettingsScene(BootScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            report.Append("  · Boot 已插到 Build Settings 第 0 位\n");
        }

        // ==================================================================
        //  工具
        // ==================================================================

        /// <summary>按场景里所有 Renderer 的并集算世界矩形。UI / 别的场景的对象会被排除。</summary>
        private static bool TryComputeSceneBounds(Scene scene, out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
            foreach (Renderer renderer in renderers)
            {
                if (renderer.gameObject.scene != scene) continue;

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return any;
        }

        private static int IndexOfScene(SerializedProperty arr, string scenePath)
        {
            for (int i = 0; i < arr.arraySize; i++)
            {
                if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("ScenePath").stringValue == scenePath)
                    return i;
            }

            return -1;
        }

        private static int NextPartitionId(SerializedProperty arr)
        {
            int max = 0;
            for (int i = 0; i < arr.arraySize; i++)
            {
                int id = arr.GetArrayElementAtIndex(i).FindPropertyRelative("PartitionId").intValue;
                if (id > max) max = id;
            }

            return max + 1;
        }

        private static SerializedProperty AppendElement(SerializedProperty arr)
        {
            arr.arraySize++;
            return arr.GetArrayElementAtIndex(arr.arraySize - 1);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
