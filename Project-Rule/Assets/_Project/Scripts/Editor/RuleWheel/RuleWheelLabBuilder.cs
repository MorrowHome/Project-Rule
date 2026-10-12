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
    /// <summary>复制队友的测试场景，只在副本接入外观与 UI；原场景和 P3 控制参数保持原样。</summary>
    public static class RuleWheelLabBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Test_RuleWheel.unity";
        private const string SourceScene = "Assets/_Project/Scenes/Test_RuleSystem.unity";
        private const string UIPath = "Assets/_Project/UI/RuleWheel/";

        [MenuItem("RuleGame/Rule Wheel/Open Test Scene")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorSceneManager.OpenScene(ScenePath);
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
                PlayerController player = Find<PlayerController>(scene);
                RuleSystemDriver rules = Find<RuleSystemDriver>(scene);
                Camera camera = Find<Camera>(scene);
                if (player == null || rules == null || camera == null)
                    throw new InvalidOperationException("测试场景缺少玩家、规则系统或摄像机。");
                // 保留队友的刚体、碰撞体、层、输入动作与手感，只替换显示用的 Sprite。
                Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/P4WhiteboxSquare.png");
                SpriteRenderer body = player.GetComponent<SpriteRenderer>();
                body.sprite = square;
                body.color = new Color(0.13f, 0.13f, 0.12f);
                body.sortingOrder = 5;
                foreach (float x in new[] { -0.2f, 0.2f })
                {
                    var eye = new GameObject("White eye", typeof(SpriteRenderer));
                    eye.transform.SetParent(player.transform, false);
                    eye.transform.localPosition = new Vector3(x, 0.23f, 0f);
                    eye.transform.localScale = new Vector3(0.15f, 0.13f, 1f);
                    var ink = eye.GetComponent<SpriteRenderer>();
                    ink.sprite = square;
                    ink.sharedMaterial = body.sharedMaterial;
                    ink.color = new Color(0.95f, 0.945f, 0.92f);
                    ink.sortingOrder = 6;
                }
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    // 副本由新 UI 显示状态，关闭旧调试面板，避免两套规则入口并存。
                    foreach (P3DebugOverlay overlay in root.GetComponentsInChildren<P3DebugOverlay>(true)) overlay.enabled = false;
                    foreach (RuleSystemTestInput input in root.GetComponentsInChildren<RuleSystemTestInput>(true)) input.enabled = false;
                    foreach (Rigidbody2D body2D in root.GetComponentsInChildren<Rigidbody2D>(true))
                        if (body2D.GetComponent<Collider2D>() != null && body2D.GetComponent<RuleAffectedBody>() == null)
                            body2D.gameObject.AddComponent<RuleAffectedBody>();
                }
                RuleWheelLabUnlocks unlocks = player.gameObject.AddComponent<RuleWheelLabUnlocks>();
                RuleSelectionCamera focus = camera.gameObject.AddComponent<RuleSelectionCamera>();
                Set(focus, "_target", player.transform);
                Set(focus, "_normalFollow", camera.GetComponent<P3FollowCamera>());
                RuleWheelLabTimeSource time = new GameObject("Lab time adapter - replace with P1").AddComponent<RuleWheelLabTimeSource>();
                PanelSettings panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(UIPath + "RuleWheelPanelSettings.asset");
                if (panel == null)
                {
                    panel = ScriptableObject.CreateInstance<PanelSettings>();
                    panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                    panel.referenceResolution = new Vector2Int(1280, 720);
                    panel.match = 0.5f;
                    AssetDatabase.CreateAsset(panel, UIPath + "RuleWheelPanelSettings.asset");
                }
                UIDocument wheelDocument = Document("Rule Wheel", panel, "RuleWheel.uxml", 100);
                RuleWheelPresenter wheel = wheelDocument.gameObject.AddComponent<RuleWheelPresenter>();
                Set(wheel, "_player", player);
                Set(wheel, "_catalog", new SerializedObject(rules).FindProperty("_catalog").objectReferenceValue);
                Set(wheel, "_unlocks", unlocks);
                Set(wheel, "_timeSource", time);
                Set(wheel, "_cameraFocus", focus);
                Set(wheel, "_worldCamera", camera);
                UIDocument labDocument = Document("Lab HUD", panel, "RuleWheelLab.uxml", 0);
                RuleWheelLabHUD hud = labDocument.gameObject.AddComponent<RuleWheelLabHUD>();
                Set(hud, "_status", player.GetComponent<EntityStatus>());
                Set(hud, "_wheel", wheel);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssetIfDirty(panel);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
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
