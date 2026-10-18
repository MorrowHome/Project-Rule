using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Combat;

namespace RuleGame.Editor.Enemy
{
    /// <summary>从已接线的小怪白盒复制独立寻路场景，保留已有资源与当前打开场景。</summary>
    public static class EnemyNavigationLabSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/MinionLabs/Test_Minion_Pathfinding.unity";
        private const string DefinitionPath = "Assets/_Project/Data/Enemies/FieldMinions/PathfindingLab_Definition.asset";

        [MenuItem("RuleGame/Enemies/创建平台寻路白盒（不覆盖）")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play Mode。");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) { Debug.Log("平台寻路白盒已存在：" + ScenePath); return; }
            if (!AssetDatabase.CopyAsset("Assets/_Project/Scenes/MinionLabs/Test_Minion_BrickSoldier.unity", ScenePath))
                throw new InvalidOperationException("缺少已经接线的积木士兵白盒场景。");
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                EnemyActor actor = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.TryGetComponent<EnemyActor>(out var enemy)) actor = enemy;
                    if (root.TryGetComponent<PlayerController>(out var player))
                    {
                        player.transform.position = new Vector2(-3.9f, .65f);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
                    }
                    if (root.TryGetComponent<Camera>(out var camera)) { camera.transform.position = new Vector3(0f, 3.5f, -10f); camera.orthographicSize = 6f; }
                }
                if (actor == null) throw new InvalidOperationException("源场景缺少 EnemyActor。");
                var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefinitionPath);
                if (definition == null)
                {
                    definition = UnityEngine.Object.Instantiate(actor.Definition);
                    definition.name = "PathfindingLab_Definition";
                    AssetDatabase.CreateAsset(definition, DefinitionPath);
                }
                actor.Definition = definition; actor.transform.position = new Vector2(1.8f, definition.BodySize.y * .5f + .05f);
                actor.Target = null; actor.Combat = null;
                if (actor.GetComponentInChildren<Hurtbox>() == null)
                {
                    var hurtbox = new GameObject("Hurtbox"); hurtbox.transform.SetParent(actor.transform, false); hurtbox.layer = Hurtbox.Layer;
                    var collider = hurtbox.AddComponent<BoxCollider2D>(); collider.size = definition.BodySize; collider.isTrigger = true;
                    hurtbox.AddComponent<Hurtbox>();
                }
                Platform("FirstPlatform", new Vector2(-3f, .975f), new Vector2(1.4f, .25f));
                Platform("SecondPlatform", new Vector2(0f, 2.075f), new Vector2(2f, .25f));
                Platform("ThirdPlatform", new Vector2(3f, 3.175f), new Vector2(1.4f, .25f));
                PrefabUtility.RecordPrefabInstancePropertyModifications(actor);
                PrefabUtility.RecordPrefabInstancePropertyModifications(actor.transform);
                EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
                Debug.Log("平台寻路白盒已创建：" + ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                SceneManager.SetActiveScene(previous);
            }
        }
        private static void Platform(string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name); go.layer = 8; go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = size;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/P4WhiteboxSquare.png");
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Data/Enemies/EnemySprite_Mat.mat");
            renderer.drawMode = SpriteDrawMode.Tiled; renderer.size = size; renderer.color = new Color(.27f, .55f, .68f);
        }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("批处理仅用于隔离工程。");
            var context = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(context, "Assets/NavigationBuildContext.unity");
            Create(); EditorApplication.Exit(0);
        }
    }
}
