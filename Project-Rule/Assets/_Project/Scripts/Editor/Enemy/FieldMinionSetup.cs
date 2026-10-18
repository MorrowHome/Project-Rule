using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Common;
using RuleGame.Presentation.Enemy;
using RuleGame.Infrastructure;

namespace RuleGame.Editor.Enemy
{
    public static class FieldMinionSetup
    {
        public static readonly EnemyKind[] Kinds = { EnemyKind.StoneGuard, EnemyKind.ExplosiveBlock, EnemyKind.BrickSoldier,
            EnemyKind.SpringToy, EnemyKind.ClockMouse, EnemyKind.ClockBomb, EnemyKind.PlushBird, EnemyKind.HugBear, EnemyKind.PlushSeal };
        public const string DataFolder = "Assets/_Project/Data/Enemies/FieldMinions";
        public const string PrefabFolder = "Assets/_Project/Prefabs/Enemies/FieldMinions";
        public const string SceneFolder = "Assets/_Project/Scenes/MinionLabs";
        private static readonly string[] Names = { "石头人小怪", "自爆方块", "积木士兵", "弹簧小怪", "发条鼠", "发条炸弹", "毛绒玩具鸟", "抱抱熊", "毛绒海豹" };
        private static readonly float[] Health = { 17, 1, 7, 20, 20, 1, 35, 50, 50 };
        private static readonly float[] Damage = { 5, 20, 10, 8, 8, 20, 13, 20, 20 };
        private static readonly Vector2[] Sizes = { new Vector2(1.8f,2.4f), Vector2.one, new Vector2(.8f,1.6f),
            new Vector2(1f,.8f), new Vector2(1.1f,.5f), new Vector2(.7f,.8f), new Vector2(1.2f,.8f), new Vector2(1.5f,1.8f), new Vector2(1.5f,.9f) };
        private static readonly float[] Speeds = { 1.2f, 1.8f, 1.6f, 3.2f, 3.5f, 1.2f, 2f, 1.2f, 1.4f };

        [MenuItem("RuleGame/Enemies/创建九种关卡小怪白盒（保留已有参数）")]
        public static void CreateAll()
        {
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                Debug.LogError("请先保存当前场景，再创建小怪白盒。");
                return;
            }
            Directory.CreateDirectory(DataFolder); Directory.CreateDirectory(PrefabFolder); Directory.CreateDirectory(SceneFolder);
            AssetDatabase.Refresh();
            for (int i = 0; i < Kinds.Length; i++)
            {
                string path = DataFolder + "/" + Kinds[i] + "_Definition.asset";
                var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
                if (definition == null)
                {
                    definition = NewDefinition(Kinds[i]);
                    AssetDatabase.CreateAsset(definition, path);
                }
                string prefabPath = PrefabFolder + "/" + Kinds[i] + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) prefab = CreatePrefab(definition, i, prefabPath);
                string scenePath = SceneFolder + "/Test_Minion_" + Kinds[i] + ".unity";
                if (!File.Exists(scenePath)) CreateScene(prefab, scenePath);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[FieldMinions] 九种小怪、独立配置及可玩白盒已创建；已有资源与参数未覆盖。");
        }
        public static void CreateBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("批处理入口只用于隔离工程。");
            var context = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(context, "Assets/FieldMinionBuildContext.unity");
            CreateAll(); EditorApplication.Exit(0);
        }
        public static EnemyDefinition NewDefinition(EnemyKind kind)
        {
            int i = Array.IndexOf(Kinds, kind);
            if (i < 0) throw new ArgumentOutOfRangeException(nameof(kind));
            var d = ScriptableObject.CreateInstance<EnemyDefinition>();
            d.Kind = kind; d.DisplayName = Names[i]; d.Health = Health[i]; d.Damage = Damage[i]; d.BodySize = Sizes[i];
            d.MoveSpeed = Speeds[i]; d.Energy = 0f; d.EnergyRegen = 0f; d.ContactDamage = kind == EnemyKind.ClockMouse;
            d.AttackCooldown = kind == EnemyKind.PlushBird ? 1.8f : kind == EnemyKind.HugBear ? 5f : 3f;
            d.AttackRange = kind == EnemyKind.PlushBird ? 7f : kind == EnemyKind.SpringToy ? 1.5f : 1.8f;
            d.Flying = kind == EnemyKind.PlushBird;
            d.MinionSenseRange = kind == EnemyKind.ClockMouse ? 3f : kind == EnemyKind.PlushBird ? 8f : 6f;
            d.MinionPatrolDistance = kind == EnemyKind.ClockBomb ? 1.5f : 3f;
            d.MinionReassembles = kind == EnemyKind.BrickSoldier || kind == EnemyKind.SpringToy;
            d.HurtCooldown = 1.2f;
            d.MinionStaggerThreshold = kind == EnemyKind.StoneGuard || kind == EnemyKind.HugBear || kind == EnemyKind.PlushSeal ? 20f : 12f;
            if (kind == EnemyKind.ClockBomb) d.MoveSpeed = 2.4f;
            if (kind == EnemyKind.HugBear) d.MoveSpeed = 2.6f;
            if (kind == EnemyKind.PlushSeal)
            {
                d.Energy = 100f; d.EnergyRegen = 10f;
                d.RuleAttribute = RuleGame.Infrastructure.Rules.AttributeId.Gravity;
                d.RuleRelation = RuleGame.Infrastructure.Rules.RelationId.Weaken;
            }
            string[] actions = { "Idle", "Walk", "Attack", "Hurt", "Dead", "Stand", "Sit", "Pop", "Hide", "Rebirth", "Howl", "BellySlap", "Explode", "Hop", "Fly", "Hug", "Slide" };
            d.Timings = new EnemyActionTiming[actions.Length];
            for (int n = 0; n < actions.Length; n++) d.Timings[n] = new EnemyActionTiming { Action = actions[n], Duration = actions[n] == "Hurt" ? .25f : actions[n] == "Explode" ? .4f : .8f };
            return d;
        }
        private static GameObject CreatePrefab(EnemyDefinition d, int index, string path)
        {
            var go = new GameObject(d.Kind.ToString()); go.SetActive(false); go.layer = 9;
            var actor = go.AddComponent<EnemyActor>(); actor.Definition = d;
            go.GetComponent<BoxCollider2D>().size = d.BodySize;
            ConfigurePlacement(actor);
            var body = go.GetComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0;
            go.AddComponent<RuleAffectedBody>();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/P4WhiteboxSquare.png");
            if (sprite == null) throw new InvalidOperationException("缺少 P4WhiteboxSquare.png");
            var visual = new GameObject("WhiteboxBody"); visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = new Vector3(d.BodySize.x / sprite.bounds.size.x, d.BodySize.y / sprite.bounds.size.y, 1f);
            var renderer = visual.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = 5;
            renderer.color = Color.HSVToRGB(index / 9f, .55f, .85f);
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Data/Enemies/EnemySprite_Mat.mat");
            var view = go.AddComponent<FieldMinionView>(); view.Actor = actor; view.Body = renderer;
            actor.ProjectileSprite = sprite;
            go.SetActive(true);
            var saved = PrefabUtility.SaveAsPrefabAsset(go, path); UnityEngine.Object.DestroyImmediate(go); return saved;
        }

        [MenuItem("RuleGame/Enemies/补齐九种小怪的放置配置（保留战斗参数）")]
        public static void RepairPlacement()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");
            foreach (var kind in Kinds)
            {
                string path = PrefabFolder + "/" + kind + ".prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                    throw new InvalidOperationException("缺少小怪预制体：" + path);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ConfigurePlacement(root.GetComponent<EnemyActor>());
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[FieldMinions] 九种小怪已补齐受击箱和出生点相对活动范围；保留已有战斗参数与场景引用。");
        }

        private static void ConfigurePlacement(EnemyActor actor)
        {
            actor.UseSpawnRelativeArena = true;
            Hurtbox hurtbox = null;
            foreach (var existing in actor.GetComponentsInChildren<Hurtbox>(true))
                if ((UnityEngine.Object)existing.GetComponentInParent<IDamageable>(true) == actor) { hurtbox = existing; break; }
            if (hurtbox == null)
            {
                var go = new GameObject("Hurtbox"); go.transform.SetParent(actor.transform, false);
                go.layer = Hurtbox.Layer;
                var area = go.AddComponent<BoxCollider2D>();
                var body = actor.GetComponent<BoxCollider2D>();
                area.size = body.size; area.offset = body.offset; area.isTrigger = true;
                hurtbox = go.AddComponent<Hurtbox>();
            }
            var serialized = new SerializedObject(hurtbox);
            serialized.FindProperty("_owner").objectReferenceValue = actor;
            serialized.FindProperty("m_EditorClassIdentifier").stringValue =
                typeof(Hurtbox).Assembly.GetName().Name + "::" + typeof(Hurtbox).FullName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void CreateScene(GameObject prefab, string path)
        {
            Scene previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var systems = new GameObject("Systems");
                var rules = systems.AddComponent<RuleSystemDriver>();
                Set(rules, "_catalog", AssetDatabase.LoadAssetAtPath<RuleCatalog>("Assets/_Project/Data/Rules/RuleCatalog.asset"));
                var combat = systems.AddComponent<CombatSystem>();
                var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/EnemyLabPlayer.prefab");
                if (playerPrefab == null) throw new InvalidOperationException("缺少现有 EnemyLabPlayer.prefab");
                var player = ((GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene)).GetComponent<PlayerController>();
                player.transform.position = new Vector3(-4f, .65f, 0f); Set(player, "_combat", combat);
                Set(player.GetComponent<EntityDamageable>(), "_combat", combat);
                var enemy = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)).GetComponent<EnemyActor>();
                enemy.Target = player.transform; enemy.Combat = combat;
                enemy.transform.position = new Vector3(0f, enemy.Definition.Flying ? 3.5f : enemy.Definition.BodySize.y * .5f + .05f, 0f);
                Geometry("Floor", new Vector2(0,-.5f), new Vector2(28,1));
                Geometry("LeftWall", new Vector2(-14,5), new Vector2(1,12));
                Geometry("RightWall", new Vector2(14,5), new Vector2(1,12));
                var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
                camera.gameObject.AddComponent<AudioListener>();
                camera.orthographic = true; camera.orthographicSize = 6f;
                camera.transform.position = new Vector3(0f,3f,-10f);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.18f,.2f,.23f);
                EditorSceneManager.SaveScene(scene, path);
            }
            finally { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); }
        }
        private static void Geometry(string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name); go.layer = 8; go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = size;
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/P4WhiteboxSquare.png");
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.color = new Color(.4f,.43f,.46f);
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Data/Enemies/EnemySprite_Mat.mat");
            go.transform.localScale = Vector3.one;
            renderer.drawMode = SpriteDrawMode.Tiled; renderer.size = size;
        }
        private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target); serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
