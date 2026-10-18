using System;
using RuleGame.Core.Rules;
using RuleGame.Core.Rules.Debugging;
using RuleGame.Development.P3;
using RuleGame.Development.RuleWheel;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Player;
using RuleGame.Presentation.RuleWheel;
using RuleGame.UI.RuleWheel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace RuleGame.Editor.RuleWheel
{
    /// <summary>在正式测试场景接入轮盘，保留 P3 的角色、动画、调试面板与控制参数。</summary>
    public static class RuleWheelLabBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Test_RuleWheel.unity";
        public const string SourceScene = "Assets/_Project/Scenes/Test_RuleSystem.unity";
        private const string UIPath = "Assets/_Project/UI/RuleWheel/";

        [MenuItem("RuleGame/Rule Wheel/Open Test Scene")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            RuleOrbSetup.Install();
            EditorSceneManager.OpenScene(SourceScene);
        }

        public static void InstallIntoSourceScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");
            Scene scene = SceneManager.GetSceneByPath(SourceScene);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (!openedHere && scene.isDirty)
                throw new InvalidOperationException("Test_RuleSystem 有未保存改动，请先保存后再接入轮盘。");
            if (openedHere) scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive);
            Scene previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            try
            {
                Wire(scene, false);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("RuleGame/Rule Wheel/Create Test Scene")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
            if (!AssetDatabase.CopyAsset(SourceScene, ScenePath))
                throw new InvalidOperationException("无法复制 Test_RuleSystem，请先检查源场景。");
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                Wire(scene, true);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static void Wire(Scene scene, bool labHUD)
        {
            PlayerController player = Find<PlayerController>(scene);
            RuleSystemDriver rules = Find<RuleSystemDriver>(scene);
            Camera camera = Find<Camera>(scene);
            if (player == null || rules == null || camera == null)
                throw new InvalidOperationException("测试场景缺少玩家、规则系统或摄像机。");
            // 正式测试场景保留原调试工具；兼容副本才使用独立试播 HUD。
            // 不换 Sprite，不生成方块眼睛，也不改 Animator 或玩家控制字段。
            if (labHUD)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    // 副本由新 UI 显示状态，关闭旧调试面板，避免两套规则入口并存。
                    foreach (P3DebugOverlay overlay in root.GetComponentsInChildren<P3DebugOverlay>(true)) overlay.enabled = false;
                    foreach (RuleSystemTestInput input in root.GetComponentsInChildren<RuleSystemTestInput>(true)) input.enabled = false;
                    foreach (Rigidbody2D body2D in root.GetComponentsInChildren<Rigidbody2D>(true))
                        if (body2D.GetComponent<Collider2D>() != null && body2D.GetComponent<RuleAffectedBody>() == null)
                            body2D.gameObject.AddComponent<RuleAffectedBody>();
                }
            }
            RuleWheelLabUnlocks unlocks = player.GetComponent<RuleWheelLabUnlocks>();
            if (unlocks == null) unlocks = Undo.AddComponent<RuleWheelLabUnlocks>(player.gameObject);
            RuleSelectionCamera focus = camera.GetComponent<RuleSelectionCamera>();
            if (focus == null) focus = Undo.AddComponent<RuleSelectionCamera>(camera.gameObject);
            Set(focus, "_target", player.transform);
            Set(focus, "_normalFollow", camera.GetComponent<P3FollowCamera>());
            PanelSettings panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(UIPath + "RuleWheelPanelSettings.asset");
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panel.referenceResolution = new Vector2Int(1280, 720);
                panel.match = 0.5f;
                AssetDatabase.CreateAsset(panel, UIPath + "RuleWheelPanelSettings.asset");
            }
            RuleWheelPresenter wheel = Find<RuleWheelPresenter>(scene);
            if (wheel == null)
            {
                UIDocument wheelDocument = Document("Rule Wheel", panel, "RuleWheel.uxml", 100);
                SceneManager.MoveGameObjectToScene(wheelDocument.gameObject, scene);
                wheel = Undo.AddComponent<RuleWheelPresenter>(wheelDocument.gameObject);
            }
            Set(wheel, "_player", player);
            Set(wheel, "_catalog", new SerializedObject(rules).FindProperty("_catalog").objectReferenceValue);
            Set(wheel, "_unlocks", unlocks);
            Set(wheel, "_cameraFocus", focus);
            Set(wheel, "_worldCamera", camera);
            if (labHUD && Find<RuleWheelLabHUD>(scene) == null)
            {
                UIDocument labDocument = Document("Lab HUD", panel, "RuleWheelLab.uxml", 0);
                SceneManager.MoveGameObjectToScene(labDocument.gameObject, scene);
                RuleWheelLabHUD hud = labDocument.gameObject.AddComponent<RuleWheelLabHUD>();
                Set(hud, "_status", player.GetComponent<EntityStatus>());
                Set(hud, "_wheel", wheel);
            }
            AssetDatabase.SaveAssetIfDirty(panel);
        }
        private static T Find<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null) return component;
            }
            return null;
        }
        private static UIDocument Document(string name, PanelSettings panel, string layout, int order)
        {
            var document = new GameObject(name).AddComponent<UIDocument>();
            Undo.RegisterCreatedObjectUndo(document.gameObject, "接入规则轮盘");
            document.panelSettings = panel;
            document.sortingOrder = order;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UIPath + layout);
            return document;
        }
        private static void Set(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
