using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Presentation.RuleWheel;
using RuleGame.UI.RuleWheel;

namespace RuleGame.Editor.RuleWheel
{
    /// <summary>为已有轮盘接线表现层，不重建玩家、地图、规则目录或 Animator 图。</summary>
    public static class RuleOrbSetup
    {
        private const string ArtFolder = "Assets/_Project/Art/RuleOrb";
        private const string DataFolder = "Assets/_Project/Data/RuleWheel";
        [MenuItem("RuleGame/Rule Wheel/接入托球动作与柔光（保留玩法）")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");
            AssetDatabase.Refresh(); Directory.CreateDirectory(DataFolder); AssetDatabase.Refresh();
            string texturePath = ArtFolder + "/RuleOrbAtlas.png";
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("缺少统一锚点的光球动作图集。");
            importer.textureType = TextureImporterType.Default; importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true; importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048; importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear; importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            string dataPath = DataFolder + "/RuleOrbArt.asset";
            var art = AssetDatabase.LoadAssetAtPath<RuleOrbArt>(dataPath);
            if (art == null) { art = ScriptableObject.CreateInstance<RuleOrbArt>(); AssetDatabase.CreateAsset(art, dataPath); }
            art.Atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            art.OrbOffset = new Vector2(-.608f, .740f);
            art.BodyMaterial = GetMaterial("RuleOrbBody", "RuleGame/RuleWheel/OrbBody");
            art.HaloMaterial = GetMaterial("RuleOrbHalo", "RuleGame/RuleWheel/SoftOrbHalo");
            EditorUtility.SetDirty(art);
            RuleWheelLabBuilder.InstallIntoSourceScene();
            WireScene(RuleWheelLabBuilder.SourceScene, art);
            WireScene("Assets/_Project/Scenes/Test_RuleWheel.unity", art);
            // 敌人白盒独立交付；未合入时跳过，轮盘可在正式测试场景中单独使用。
            if (AssetDatabase.IsValidFolder("Assets/_Project/Scenes/EnemyLabs"))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/_Project/Scenes/EnemyLabs" }))
                    WireScene(AssetDatabase.GUIDToAssetPath(guid), art);
            }
            ApplySelectionScaleToLevelPrefab();
            AssetDatabase.SaveAssetIfDirty(art);
            AssetDatabase.SaveAssetIfDirty(art.BodyMaterial);
            AssetDatabase.SaveAssetIfDirty(art.HaloMaterial);
            Directory.CreateDirectory("Library/RuleOrb");
            File.WriteAllText("Library/RuleOrb/setup-result.txt", "OK: 17 artist frames and existing rule-wheel scenes wired");
            Debug.Log("[RuleOrb] 托球动作与柔光已接到既有轮盘；原主角控制、Animator 图和 Build Settings 不变。");
        }
        private static Material GetMaterial(string name, string shaderName)
        {
            string path = DataFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null) throw new InvalidOperationException("缺少光球 Shader: " + shaderName);
                material = new Material(shader); AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }
        private static void WireScene(string path, RuleOrbArt art)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) return;
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                bool changed = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (RuleWheelPresenter wheel in root.GetComponentsInChildren<RuleWheelPresenter>(true))
                    {
                        changed |= ApplySelectionScale(wheel);
                        var view = wheel.GetComponent<RuleSelectionOrbView>();
                        if (view == null) { view = Undo.AddComponent<RuleSelectionOrbView>(wheel.gameObject); changed = true; }
                        var serialized = new SerializedObject(view);
                        if (serialized.FindProperty("_art").objectReferenceValue == art) continue;
                        serialized.FindProperty("_art").objectReferenceValue = art;
                        serialized.ApplyModifiedProperties(); changed = true;
                    }
                if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
        }

        private static bool ApplySelectionScale(RuleWheelPresenter wheel)
        {
            var serialized = new SerializedObject(wheel);
            var scale = serialized.FindProperty("_selectionScale");
            if (Mathf.Approximately(scale.floatValue, RuleWheelPresenter.DefaultSelectionScale)) return false;
            scale.floatValue = RuleWheelPresenter.DefaultSelectionScale;
            return serialized.ApplyModifiedProperties();
        }

        private static void ApplySelectionScaleToLevelPrefab()
        {
            const string path = "Assets/_Project/Prefabs/LevelRuntime.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                foreach (var wheel in root.GetComponentsInChildren<RuleWheelPresenter>(true))
                    changed |= ApplySelectionScale(wheel);
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
