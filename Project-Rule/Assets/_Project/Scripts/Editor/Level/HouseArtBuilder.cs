using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuleGame.Gameplay.Scene;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 背景房子与玩具盒平台的资产生成器。
    /// </summary>
    /// <remarks>
    /// <para>背景：每张房子图一个预制体，只有 <see cref="SpriteRenderer"/>（Order −20），
    /// 没有碰撞体 —— 手动拖进 <c>Grid/Bg_Houses</c> 即可。</para>
    /// <para>平台：<c>Prefabs/Level/ToyBoxPlatform.prefab</c>，Ground 层、静态 BoxCollider2D、
    /// Order −1、默认 Scale 0.65（顶面约 2.47，单跳能上）。</para>
    /// </remarks>
    public static class HouseArtBuilder
    {
        public const string HouseSpriteFolder = "Assets/_Project/Art/Sprites/Background/Houses";
        public const string BackgroundPrefabFolder = "Assets/_Project/Prefabs/Background/Houses";
        public const string PlatformPrefabPath = "Assets/_Project/Prefabs/Level/ToyBoxPlatform.prefab";
        public const string PlatformSpritePath = HouseSpriteFolder + "/House_StreetShop_03.png";

        public const int BackgroundSortingOrder = -20;
        public const int PlatformSortingOrder = -1;
        public const float PlatformScale = 0.65f;

        private const string PlatformSpriteName = "House_StreetShop_03";

        // ==================================================================
        //  导入
        // ==================================================================

        [MenuItem("Tools/规则游戏/背景房子/⓪ 重设导入设置", false, 1)]
        public static void ReimportHouseArt()
        {
            if (!AssetDatabase.IsValidFolder(HouseSpriteFolder))
            {
                Debug.LogError($"[背景房子] 找不到贴图目录：{HouseSpriteFolder}");
                return;
            }

            AssetDatabase.ImportAsset(
                HouseSpriteFolder,
                ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log($"[背景房子] 已重设导入设置：{HouseSpriteFolder}");
        }

        // ==================================================================
        //  背景预制体
        // ==================================================================

        [MenuItem("Tools/规则游戏/背景房子/① 生成背景预制体", false, 2)]
        public static void BuildBackgroundPrefabs()
        {
            EnsureFolder(BackgroundPrefabFolder);

            List<string> paths = AssetDatabase
                .FindAssets("t:Sprite", new[] { HouseSpriteFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

            if (paths.Count == 0)
            {
                Debug.LogError($"[背景房子] {HouseSpriteFolder} 里没有 PNG。先跑 Tools/Art/prepare_houses.py。");
                return;
            }

            int built = 0;
            foreach (string path in paths)
            {
                string spriteName = Path.GetFileNameWithoutExtension(path);
                if (spriteName == PlatformSpriteName) continue; // 这张是平台，不进背景

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) continue;

                string prefabPath = BackgroundPrefabFolder + "/" + spriteName + ".prefab";
                var go = new GameObject(spriteName);
                go.layer = 0;

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = BackgroundSortingOrder;

                PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                UnityEngine.Object.DestroyImmediate(go);
                built++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[背景房子] 已生成/更新 {built} 个背景预制体 -> {BackgroundPrefabFolder}");
        }

        [MenuItem("Tools/规则游戏/背景房子/② 在当前场景建 Bg_Houses 容器", false, 3)]
        public static void CreateBgHousesContainer()
        {
            Scene scene = SceneManager.GetActiveScene();
            Transform grid = scene.GetRootGameObjects()
                .Select(root => root.transform)
                .FirstOrDefault(t => t.name == "Grid");

            if (grid == null)
            {
                Debug.LogError($"[背景房子] 当前场景 {scene.name} 里没有 Grid。先跑「在场景里搭好关卡骨架」。");
                return;
            }

            Transform existing = grid.Find("Bg_Houses");
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log($"[背景房子] {scene.name}/Grid/Bg_Houses 已存在。");
                return;
            }

            var go = new GameObject("Bg_Houses");
            go.transform.SetParent(grid, false);
            go.transform.localPosition = Vector3.zero;
            Undo.RegisterCreatedObjectUndo(go, "创建 Bg_Houses");
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = go;
            Debug.Log($"[背景房子] 已建 {scene.name}/Grid/Bg_Houses");
        }

        // ==================================================================
        //  平台
        // ==================================================================

        [MenuItem("Tools/规则游戏/地图组件/⑮ 玩具盒平台（房子碰撞平台）", false, 20)]
        public static void PlacePlatform()
        {
            GameObject prefab = EnsurePlatformPrefab(false);
            if (prefab == null) return;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null) return;

            SceneView view = SceneView.lastActiveSceneView;
            Vector3 pivot = view != null ? view.pivot : Vector3.zero;
            float y = FindGroundY(pivot.x, pivot.y);
            instance.transform.position = new Vector3(pivot.x, y, 0f);
            instance.transform.localScale = Vector3.one * PlatformScale;

            Undo.RegisterCreatedObjectUndo(instance, "放置玩具盒平台");
            EditorSceneManager.MarkSceneDirty(instance.scene);
            Selection.activeGameObject = instance;
            Debug.Log($"[玩具盒平台] 已放置：{instance.name} @ {instance.transform.position}（Ground 层，Order {PlatformSortingOrder}）");
        }

        [MenuItem("Tools/规则游戏/地图组件/⑮b 重建玩具盒平台预制体", false, 21)]
        public static void RebuildPlatformPrefab()
        {
            GameObject prefab = EnsurePlatformPrefab(true);
            if (prefab != null) Selection.activeObject = prefab;
        }

        /// <summary>确保平台预制体存在；<paramref name="overwrite"/> = true 时按当前规格重建。</summary>
        public static GameObject EnsurePlatformPrefab(bool overwrite)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlatformPrefabPath);
            if (existing != null && !overwrite) return existing;

            EnsureFolder(Path.GetDirectoryName(PlatformPrefabPath).Replace('\\', '/'));

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlatformSpritePath);
            if (sprite == null)
            {
                Debug.LogError($"[玩具盒平台] 找不到 Sprite：{PlatformSpritePath}\n先跑 Tools/Art/prepare_houses.py，再跑「背景房子/⓪ 重设导入设置」。");
                return null;
            }

            int groundLayer = LayerMask.NameToLayer("Ground");
            var go = new GameObject("ToyBoxPlatform");
            go.layer = groundLayer >= 0 ? groundLayer : 8;
            go.transform.localScale = Vector3.one * PlatformScale;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = PlatformSortingOrder;

            var body = go.AddComponent<BoxCollider2D>();
            body.isTrigger = false;

            var platform = go.AddComponent<ToyBoxPlatform>();
            platform.FitToSprite();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PlatformPrefabPath);
            UnityEngine.Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();

            Debug.Log($"[玩具盒平台] 已生成预制体：{PlatformPrefabPath}（Scale {PlatformScale}，顶面约 " +
                      $"{sprite.bounds.size.y * PlatformScale:0.00} 单位）");
            return prefab;
        }

        // ==================================================================
        //  工具
        // ==================================================================

        private static float FindGroundY(float x, float fallbackY)
        {
            var hit = Physics2D.Raycast(
                new Vector2(x, fallbackY + 20f),
                Vector2.down,
                200f,
                1 << 8);

            return hit.collider != null ? hit.point.y : fallbackY;
        }

        private static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
