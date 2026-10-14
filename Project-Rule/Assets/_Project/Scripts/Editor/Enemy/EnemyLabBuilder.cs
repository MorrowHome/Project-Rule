using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Enemy;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Common;
using RuleGame.Presentation.Player;
using RuleGame.Presentation.Enemy;
using RuleGame.Presentation.RuleWheel;
using RuleGame.Development.P3;
using RuleGame.Development.Enemy;
using RuleGame.Development.RuleWheel;
using RuleGame.UI.RuleWheel;
using RuleGame.Infrastructure.Rules;
using RuleGame.Infrastructure;

namespace RuleGame.Editor.Enemy
{
    /// <summary>生成独立白盒与可复用 Prefab；原测试场景、Build Settings 和其它资源不修改。</summary>
    public static class EnemyLabBuilder
    {
        private const string DataFolder = "Assets/_Project/Data/Enemies";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Enemies";
        public const string SceneFolder = "Assets/_Project/Scenes/EnemyLabs";
        private static Sprite _square, _circle;
        private static Material _material;
        private static Font _font;

        /// <summary>只更新闹钟表现；保留六个场景和已调好的敌人参数。</summary>
        public static void RefreshClockPresentation()
        {
            EnemyAnimationSet animations = CreateAnimationSet(EnemyKind.ClockBoss, EnemyArtImporter.Load());
            string path = PrefabFolder + "/ClockBoss.prefab";
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ClockHandView view = contents.GetComponent<ClockHandView>();
                Transform visual = contents.transform.Find("Visual");
                EnemySpriteSequence idle = Array.Find(animations.Sequences, sequence => sequence.Action == "Idle");
                for (int i = 0; i < view.Hands.Length; i++)
                {
                    Transform hand = view.Hands[i];
                    if (idle != null && idle.ClockFaceCenters.Length > 0)
                        hand.position = visual.TransformPoint(idle.ClockFaceCenters[0]);
                    hand.localRotation = Quaternion.Euler(0f, 0f, i == 1 ? 180f : 0f);
                }
                var second = view.Hands[0].GetComponent<SpriteRenderer>();
                second.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Enemies/ClockBoss/ClockBoss_Hand_2.png");
                second.color = new Color(0.8f, 0.35f, 0.22f);
                view.Hands[0].localScale = new Vector3(0.45f, 1.45f, 1f);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        [MenuItem("RuleGame/Enemies/重建全部敌人白盒（覆盖生成配置）")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureFolder(DataFolder); EnsureFolder(PrefabFolder); EnsureFolder(SceneFolder);
            _square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/P4WhiteboxSquare.png");
            _circle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/WhiteBoxCircle.png");
            if (_circle == null) _circle = _square;
            if (_square == null) throw new InvalidOperationException("缺少白盒 Sprite。");
            _material = AssetDatabase.LoadAssetAtPath<Material>(DataFolder + "/EnemySprite_Mat.mat");
            if (_material == null)
            {
                _material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
                AssetDatabase.CreateAsset(_material, DataFolder + "/EnemySprite_Mat.mat");
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Font", new[] { "Assets/_Project" }))
            {
                _font = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
                if (_font != null) break;
            }
            EnemyArtManifest manifest = EnemyArtImporter.Load();
            var definitions = new Dictionary<EnemyKind, EnemyDefinition>();
            var animations = new Dictionary<EnemyKind, EnemyAnimationSet>();
            foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
            {
                EnemyAnimationSet animation = CreateAnimationSet(kind, manifest);
                animations[kind] = animation;
                definitions[kind] = CreateDefinition(kind, animation);
            }
            var prefabs = new Dictionary<EnemyKind, EnemyActor>();
            Scene original = SceneManager.GetActiveScene();
            Scene generation = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(generation);
            // Unity 禁止在未命名的普通场景旁再新建场景；仅保存工具自己的中间场景。
            const string generationPath = SceneFolder + "/__EnemyGeneration.unity";
            EditorSceneManager.SaveScene(generation, generationPath);
            try
            {
            foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
                prefabs[kind] = CreatePrefab(definitions[kind], animations[kind]);
            // Prefab 引用用第二遍写入，避免召唤物对生成次序的依赖。
            foreach (EnemyKind kind in new[] { EnemyKind.BrickBoss, EnemyKind.BatBoss })
            {
                string path = PrefabFolder + "/" + kind + ".prefab";
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                EnemyActor actor = contents.GetComponent<EnemyActor>();
                actor.MinionPrefab = prefabs[EnemyKind.BrickMinion];
                actor.FlyingMinionPrefab = prefabs[EnemyKind.BatMinion];
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                PrefabUtility.UnloadPrefabContents(contents);
                prefabs[kind] = AssetDatabase.LoadAssetAtPath<EnemyActor>(path);
            }
            GameObject playerPrefab = CreatePlayerPrefab();
            foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind))) BuildScene(kind, prefabs[kind], playerPrefab);
            }
            finally
            {
                EditorSceneManager.CloseScene(generation, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                AssetDatabase.DeleteAsset(generationPath);
            }
            Debug.Log("[EnemyLabs] 六个独立白盒与六个敌人 Prefab 已生成，主角无敌，Build Settings 未修改。");
        }
        private static EnemyAnimationSet CreateAnimationSet(EnemyKind kind, EnemyArtManifest manifest)
        {
            string path = DataFolder + "/" + kind + "_Animations.asset";
            EnemyAnimationSet set = LoadOrCreate<EnemyAnimationSet>(path);
            var sequences = new List<EnemySpriteSequence>();
            var metadata = new Dictionary<string, EnemyArtFrame>();
            foreach (EnemyArtFrame frame in manifest.frames) metadata[frame.path] = frame;
            string source = kind == EnemyKind.BatMinion ? "BatBoss" : kind.ToString();
            foreach (EnemyArtSequence entry in manifest.sequences)
            {
                if (entry.actor != source) continue;
                if (kind == EnemyKind.BatMinion && entry.action != "Idle" && entry.action != "Fly" && entry.action != "Hurt" && entry.action != "Dead") continue;
                var frames = new Sprite[entry.frames.Length];
                var centers = new Vector2[frames.Length];
                for (int i = 0; i < frames.Length; i++)
                {
                    frames[i] = AssetDatabase.LoadAssetAtPath<Sprite>(entry.frames[i]);
                    if (frames[i] == null) throw new InvalidOperationException("请先通过 EnemyArtImporter.Import 导入 Sprite：" + entry.frames[i]);
                    EnemyArtFrame frame = metadata[entry.frames[i]];
                    if (frame.clockFaceY > 0f)
                        centers[i] = new Vector2((frame.clockFaceX - frame.pivotX) * frame.width / frame.ppu,
                            (frame.clockFaceY - frame.pivotY) * frame.height / frame.ppu);
                }
                sequences.Add(new EnemySpriteSequence
                {
                    Action = kind == EnemyKind.BatMinion && entry.action == "Fly" ? "Walk" : entry.action,
                    Frames = frames, ClockFaceCenters = centers, FrameRate = manifest.frameRate, FacesRight = entry.facesRight
                });
            }
            set.Sequences = sequences.ToArray();
            EditorUtility.SetDirty(set); AssetDatabase.SaveAssetIfDirty(set);
            return set;
        }
        private static EnemyDefinition CreateDefinition(EnemyKind kind, EnemyAnimationSet animations)
        {
            EnemyDefinition definition = LoadOrCreate<EnemyDefinition>(DataFolder + "/" + kind + "_Definition.asset");
            definition.Kind = kind;
            definition.DisplayName = new[] { "积木分裂小怪", "积木 Boss", "发条 Boss", "毛绒兔子 Boss", "毛绒蝙蝠魔王", "蝙蝠召唤物" }[(int)kind];
            definition.Flying = kind == EnemyKind.BatBoss || kind == EnemyKind.BatMinion;
            definition.Health = kind == EnemyKind.BrickMinion ? 11f : kind == EnemyKind.BatMinion ? 25f : kind == EnemyKind.ClockBoss ? 150f : kind == EnemyKind.BatBoss ? 300f : 100f;
            definition.Damage = kind == EnemyKind.BrickMinion || kind == EnemyKind.BatMinion ? 5f : 10f;
            definition.BodySize = kind == EnemyKind.BrickMinion ? new Vector2(0.9f, 1f)
                : kind == EnemyKind.BatMinion ? new Vector2(1f, 1.05f)
                : kind == EnemyKind.BatBoss ? new Vector2(2.5f, 2.8f)
                : kind == EnemyKind.BrickBoss ? new Vector2(2f, 3.2f) : new Vector2(2f, 3f);
            definition.MoveSpeed = kind == EnemyKind.BrickMinion ? 4.5f : kind == EnemyKind.BatMinion ? 5f : kind == EnemyKind.BrickBoss ? 1.8f : 2.5f;
            definition.Energy = 100f; definition.EnergyRegen = 2f;
            definition.RuleAttribute = kind == EnemyKind.ClockBoss ? AttributeId.Time : AttributeId.Gravity;
            definition.RuleRelation = RelationId.Weaken;
            var timings = new List<EnemyActionTiming>();
            foreach (EnemySpriteSequence sequence in animations.Sequences)
                timings.Add(new EnemyActionTiming { Action = sequence.Action, Duration = Mathf.Max(0.2f, sequence.Duration) });
            definition.Timings = timings.ToArray();
            EditorUtility.SetDirty(definition); AssetDatabase.SaveAssetIfDirty(definition);
            return definition;
        }
        private static EnemyActor CreatePrefab(EnemyDefinition definition, EnemyAnimationSet animations)
        {
            var go = new GameObject(definition.Kind.ToString()); go.layer = 9;
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<BoxCollider2D>().size = definition.BodySize;
            EntityStatus status = go.AddComponent<EntityStatus>();
            SetFloat(status, "_healthMax", definition.Health); SetFloat(status, "_health", definition.Health);
            SetFloat(status, "_energyMax", definition.Energy); SetFloat(status, "_energy", definition.Energy);
            EnemyActor actor = go.AddComponent<EnemyActor>();
            actor.Definition = definition; actor.ProjectileSprite = _circle;
            go.AddComponent<RuleAffectedBody>();
            var visual = new GameObject("Visual"); visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = Vector3.down * definition.BodySize.y * 0.5f;
            var renderer = visual.AddComponent<SpriteRenderer>(); renderer.sharedMaterial = _material; renderer.sortingOrder = 5;
            renderer.sprite = animations.Sequences.Length > 0 ? animations.Sequences[0].Frames[0] : _square;
            var animator = visual.AddComponent<EnemySpriteAnimator>(); animator.Actor = actor; animator.AnimationSet = animations;
            if (definition.Kind == EnemyKind.RabbitBoss)
            {
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localScale = new Vector3(definition.BodySize.x, definition.BodySize.y, 1f);
                renderer.color = new Color(0.65f, 0.62f, 0.56f);
            }
            if (definition.Kind == EnemyKind.BatMinion) visual.transform.localScale = Vector3.one * 0.375f;
            if (definition.Kind == EnemyKind.ClockBoss)
            {
                var core = new GameObject("ClockCore"); core.layer = 9; core.transform.SetParent(go.transform, false);
                core.transform.localPosition = Vector3.up * (definition.BodySize.y * 0.5f + 0.3f);
                core.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                var coreCollider = core.AddComponent<BoxCollider2D>();
                coreCollider.size = Vector2.one * 0.65f; coreCollider.enabled = false;
                var spring = core.AddComponent<SpriteRenderer>(); spring.sprite = _circle; spring.sharedMaterial = _material;
                spring.sortingOrder = 10; spring.color = new Color(0.92f, 0.65f, 0.2f);
                spring.enabled = false; // 仅眩晕 Open 时显示，编辑器预览也不露出弱点。
                EnemyWeakPoint weak = core.AddComponent<EnemyWeakPoint>(); weak.Owner = actor; actor.WeakPoint = weak;
                core.AddComponent<RuleAffectedBody>();
                var handView = go.AddComponent<ClockHandView>(); handView.Actor = actor;
                handView.Hands = new Transform[3];
                for (int i = 0; i < 3; i++)
                {
                    var hand = new GameObject("Hand" + i); hand.transform.SetParent(go.transform, false);
                    hand.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                    var handRenderer = hand.AddComponent<SpriteRenderer>();
                    handRenderer.sharedMaterial = _material; handRenderer.sortingOrder = 9;
                    foreach (EnemySpriteSequence sequence in animations.Sequences)
                        if (sequence.Action == "Hand" + i) handRenderer.sprite = sequence.Frames[0];
                    if (handRenderer.sprite == null)
                    {
                        // 缺失秒针时借用交付时针的描边和连接圆点，避免塞一根白盒矩形。
                        foreach (EnemySpriteSequence sequence in animations.Sequences)
                            if (sequence.Action == "Hand2") handRenderer.sprite = sequence.Frames[0];
                        hand.transform.localScale = new Vector3(0.45f, 1.45f, 1f);
                        handRenderer.color = new Color(0.8f, 0.35f, 0.22f);
                    }
                    handView.Hands[i] = hand.transform;
                }
            }
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, PrefabFolder + "/" + definition.Kind + ".prefab");
            UnityEngine.Object.DestroyImmediate(go);
            return saved.GetComponent<EnemyActor>();
        }
        private static GameObject CreatePlayerPrefab()
        {
            const string source = "Assets/_Project/Scenes/Test_RuleSystem.unity";
            const string path = PrefabFolder + "/EnemyLabPlayer.prefab";
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(source);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(source, OpenSceneMode.Additive);
            try
            {
            PlayerController sourcePlayer = Find<PlayerController>(scene);
            if (sourcePlayer == null) throw new InvalidOperationException("Test_RuleSystem 没有 PlayerController。");
            // SaveAsPrefabAsset 不连接/修改源场景。完整保存子层级，保留当前主角的动画与内部引用。
            PrefabUtility.SaveAsPrefabAsset(sourcePlayer.gameObject, path);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                contents.name = "EnemyLabPlayer";
                EntityStatus status = contents.GetComponent<EntityStatus>();
                PlayerController controller = contents.GetComponent<PlayerController>();
                EntityDamageable damageable = contents.GetComponent<EntityDamageable>();
                if (damageable == null) damageable = contents.AddComponent<EntityDamageable>();
                Set(controller, "_status", status); Set(damageable, "_status", status);
                Set(controller, "_combat", null); Set(damageable, "_combat", null);
                SetFloat(damageable, "_resistance", 0f); SetFloat(damageable, "_knockbackResistance", 0f);
                var faction = new SerializedObject(damageable);
                faction.FindProperty("_faction").intValue = (int)Faction.Player; faction.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        /// <summary>只替换白盒主角并重接引用，不重建敌人配置、动画、地形与镜头。</summary>
        public static void RefreshLabPlayers()
        {
            GameObject playerPrefab = CreatePlayerPrefab();
            Scene previous = SceneManager.GetActiveScene();
            foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
            {
                string path = SceneFolder + "/Test_Enemy_" + kind + ".unity";
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    PlayerController oldPlayer = Find<PlayerController>(scene);
                    Vector3 position = oldPlayer != null ? oldPlayer.transform.position : new Vector3(-6f, 0.6f, 0f);
                    GameObject playerObject = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
                    playerObject.transform.position = position;
                    PlayerController player = playerObject.GetComponent<PlayerController>();
                    CombatSystem combat = Find<CombatSystem>(scene);
                    Set(player, "_combat", combat); Set(playerObject.GetComponent<EntityDamageable>(), "_combat", combat);
                    Find<EnemyActor>(scene).Target = player.transform;
                    Set(Find<RuleWheelPresenter>(scene), "_player", player);
                    Set(Find<RuleSelectionCamera>(scene), "_target", player.transform);
                    Find<EnemyLabControls>(scene).Player = playerObject.GetComponent<EntityStatus>();
                    if (oldPlayer != null) UnityEngine.Object.DestroyImmediate(oldPlayer.gameObject);
                    EditorSceneManager.SaveScene(scene, path);
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
        private static void BuildScene(EnemyKind kind, EnemyActor prefab, GameObject playerPrefab)
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var systems = new GameObject("Systems");
                var rules = systems.AddComponent<RuleSystemDriver>();
                RuleCatalog catalog = AssetDatabase.LoadAssetAtPath<RuleCatalog>("Assets/_Project/Data/Rules/RuleCatalog.asset");
                Set(rules, "_catalog", catalog);
                CombatSystem combat = systems.AddComponent<CombatSystem>();
                GameObject playerObject = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
                playerObject.transform.position = new Vector3(-6f, 0.6f, 0f);
                PlayerController player = playerObject.GetComponent<PlayerController>();
                Set(player, "_combat", combat); Set(playerObject.GetComponent<EntityDamageable>(), "_combat", combat);
                EnemyActor enemy = ((GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, scene)).GetComponent<EnemyActor>();
                enemy.transform.position = new Vector3(5f, enemy.Definition.Flying ? 4f : enemy.Definition.BodySize.y * 0.5f + 0.1f, 0f);
                enemy.Target = player.transform; enemy.Combat = combat;
                if (kind == EnemyKind.BatMinion) enemy.Summoner = null; // 单独白盒直接追击，Boss 召唤时则等待指令。
                Geometry("Floor", new Vector3(0f, -1f, 0f), new Vector2(30f, 2f));
                Geometry("LeftWall", new Vector3(-15f, 5f, 0f), new Vector2(1f, 12f));
                Geometry("RightWall", new Vector3(15f, 5f, 0f), new Vector2(1f, 12f));
                Geometry("Ceiling", new Vector3(0f, 11f, 0f), new Vector2(30f, 1f));
                var cameraObject = new GameObject("Main Camera"); cameraObject.tag = "MainCamera";
                Camera camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true;
                camera.orthographicSize = 9f; camera.transform.position = new Vector3(0f, 4f, -10f);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.91f, 0.9f, 0.87f);
                cameraObject.AddComponent<AudioListener>();
                RuleSelectionCamera focus = cameraObject.AddComponent<RuleSelectionCamera>(); Set(focus, "_target", player.transform);
                RuleWheelLabUnlocks unlocks = systems.AddComponent<RuleWheelLabUnlocks>();
                var ui = new GameObject("Rule Wheel");
                UIDocument document = ui.AddComponent<UIDocument>();
                document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/RuleWheel/RuleWheelPanelSettings.asset");
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/RuleWheel/RuleWheel.uxml");
                document.sortingOrder = 100;
                RuleWheelPresenter wheel = ui.AddComponent<RuleWheelPresenter>();
                Set(wheel, "_player", player); Set(wheel, "_catalog", catalog); Set(wheel, "_unlocks", unlocks);
                Set(wheel, "_cameraFocus", focus); Set(wheel, "_worldCamera", camera);
                EnemyLabControls controls = systems.AddComponent<EnemyLabControls>();
                controls.Enemy = enemy; controls.Player = playerObject.GetComponent<EntityStatus>(); controls.Font = _font;
                EditorSceneManager.SaveScene(scene, SceneFolder + "/Test_Enemy_" + kind + ".unity");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
        private static void Geometry(string name, Vector3 position, Vector2 size)
        {
            var go = new GameObject(name); go.layer = 8; go.transform.position = position;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            var sprite = go.AddComponent<SpriteRenderer>(); sprite.sprite = _square; sprite.sharedMaterial = _material;
            sprite.color = new Color(0.35f, 0.34f, 0.31f);
        }
        private static T Find<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            { T component = root.GetComponentInChildren<T>(true); if (component != null) return component; }
            return null;
        }
        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset;
        }
        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/'); string parent = parts[0];
            for (int i = 1; i < parts.Length; i++)
            { string child = parent + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(child)) AssetDatabase.CreateFolder(parent, parts[i]); parent = child; }
        }
        private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
        { var serialized = new SerializedObject(target); serialized.FindProperty(field).objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }
        private static void SetFloat(UnityEngine.Object target, string field, float value)
        { var serialized = new SerializedObject(target); serialized.FindProperty(field).floatValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
