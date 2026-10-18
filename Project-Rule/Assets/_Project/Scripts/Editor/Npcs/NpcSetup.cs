using System;
using System.Collections.Generic;
using System.IO;
using RuleGame.Core.Rules;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Dialogue;
using RuleGame.Gameplay.Npcs;
using RuleGame.Development.Npcs;
using RuleGame.UI.Dialogue;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Yarn.Unity;

namespace RuleGame.Editor.Npcs
{
    /// <summary>只创建缺少的 NPC 资产；不覆盖关卡、用户参数及原有台词资产。</summary>
    public static class NpcSetup
    {
        public const string Data = "Assets/_Project/Data/Npcs";
        public const string Prefabs = "Assets/_Project/Prefabs/Npcs";
        public const string ScenePath = "Assets/_Project/Scenes/Test_NpcConfiguration.unity";
        public const string ContentPath = Data + "/ConfiguredNpcs_Content.asset";
        [Serializable] public sealed class Manifest { public ArtActor[] actors; }
        [Serializable] public sealed class ArtActor { public string id; public float height; public ArtFrame[] frames; }
        [Serializable] public sealed class ArtFrame { public string path; public float ppu; }

        [MenuItem("Tools/规则游戏/NPC/生成配置、预制体与试播场景")]
        public static void Create()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出 Play Mode。");
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText("Assets/_Project/Art/Sprites/Npcs/NpcArtManifest.json"));
            EnsureFolder(Prefabs);
            var definitions = new List<NpcDefinition>();
            foreach (ArtActor actor in manifest.actors)
            {
                foreach (ArtFrame frame in actor.frames) Import(frame);
                NpcDefinition definition = AssetDatabase.LoadAssetAtPath<NpcDefinition>(Data + "/" + actor.id + "_Definition.asset");
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<NpcDefinition>();
                    definition.Id = actor.id;
                    definition.Role = actor.id.StartsWith("Resident", StringComparison.Ordinal) ? NpcRole.Resident : (NpcRole)Enum.Parse(typeof(NpcRole), actor.id);
                    definition.DisplayName = DisplayName(actor.id);
                    definition.Region = definition.Role == NpcRole.GearKeeper ? "发条王国"
                        : definition.Role == NpcRole.Guard || definition.Role == NpcRole.MysteryMerchant ? "魔王城" : "中心小镇";
                    definition.IdleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(actor.frames[0].path);
                    definition.TalkSprite = AssetDatabase.LoadAssetAtPath<Sprite>(actor.frames[actor.frames.Length - 1].path);
                    definition.MissingAnimations = MissingAnimations(definition.Role);
                    definition.Products = Products(definition.Role);
                    AssetDatabase.CreateAsset(definition, Data + "/" + actor.id + "_Definition.asset");
                }
                definitions.Add(definition);
                EditorUtility.SetDirty(definition);
                CreateNpcPrefab(definition);
            }
            DialogueContent content = CreateContent(definitions);
            CreateHost(content);
            CreateLab(definitions);
            ConnectPlayableLab();
            AssetDatabase.SaveAssets();
            Debug.Log("[NPC] 已配置 " + definitions.Count + " 个角色；菜单中的校验可检查台词、资源与接线。");
        }

        private static void Import(ArtFrame frame)
        {
            var importer = AssetImporter.GetAtPath(frame.path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("先运行 Tools/NpcAssets/import_npcs.py 并等待 Unity 导入。");
            if (importer.textureType == TextureImporterType.Sprite && Mathf.Approximately(importer.spritePixelsPerUnit, frame.ppu)) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = frame.ppu;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(0.5f, 0f);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static void CreateNpcPrefab(NpcDefinition definition)
        {
            string path = Prefabs + "/" + definition.Id + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
            Scene preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject(definition.Id);
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                Rigidbody2D body = root.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Dynamic;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.mass = 3f;
                if (definition.Role != NpcRole.Resident) body.constraints |= RigidbodyConstraints2D.FreezePositionX;
                var collider = root.AddComponent<CapsuleCollider2D>();
                float height = definition.IdleSprite.bounds.size.y;
                collider.size = new Vector2(Mathf.Min(definition.IdleSprite.bounds.size.x * 0.7f, 0.8f), height * 0.9f);
                collider.offset = new Vector2(0f, collider.size.y * 0.5f);
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                SpriteRenderer view = visual.AddComponent<SpriteRenderer>();
                view.sprite = definition.IdleSprite;
                view.sortingOrder = 10;
                root.AddComponent<RuleAffectedBody>();
                DialogueNpc dialogue = root.AddComponent<DialogueNpc>();
                Set(dialogue, "_interactionRadius", definition.InteractionRadius);
                Set(dialogue, "_repeatDelay", definition.RepeatDelay);
                NpcActor actor = root.AddComponent<NpcActor>();
                Set(actor, "_definition", definition);
                Set(actor, "_view", view);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static DialogueContent CreateContent(List<NpcDefinition> definitions)
        {
            DialogueContent content = AssetDatabase.LoadAssetAtPath<DialogueContent>(ContentPath);
            if (content != null) return content;
            YarnProject project = AssetDatabase.LoadAssetAtPath<YarnProject>(Data + "/ConfiguredNpcs.yarnproject");
            if (project == null || project.compiledYarnProgram == null || project.compiledYarnProgram.Length == 0)
                throw new InvalidOperationException("等待 ConfiguredNpcs.yarnproject 编译完成后重试。");
            content = ScriptableObject.CreateInstance<DialogueContent>();
            var fields = new SerializedObject(content);
            fields.FindProperty("_project").objectReferenceValue = project;
            var scripts = fields.FindProperty("_scripts");
            scripts.arraySize = 1;
            scripts.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(Data + "/ConfiguredNpcs.yarn");
            var speakers = fields.FindProperty("_speakers");
            var entries = new List<(NpcDefinition, NpcState)>();
            foreach (NpcDefinition definition in definitions)
                foreach (NpcState state in DialogueStates(definition.Role)) entries.Add((definition, state));
            speakers.arraySize = entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                string id = entries[i].Item1.SpeakerId(entries[i].Item2);
                var speaker = speakers.GetArrayElementAtIndex(i);
                speaker.FindPropertyRelative("Id").stringValue = id;
                speaker.FindPropertyRelative("DisplayName").stringValue = entries[i].Item1.DisplayName + " / " + entries[i].Item2;
                speaker.FindPropertyRelative("EntryNode").stringValue = id;
                speaker.FindPropertyRelative("FallbackNode").stringValue = id + "Fallback";
            }
            fields.ApplyModifiedPropertiesWithoutUndo();
            var errors = DialogueContentValidator.Validate(content);
            if (errors.Count > 0) { UnityEngine.Object.DestroyImmediate(content); throw new InvalidOperationException(string.Join("\n", errors)); }
            AssetDatabase.CreateAsset(content, ContentPath);
            return content;
        }

        private static void CreateHost(DialogueContent content)
        {
            string path = Prefabs + "/NpcDialogueSystem.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
            GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/UI/Dialogue/DialogueSystem.prefab");
            if (original == null) throw new InvalidOperationException("缺少既有 DialogueSystem.prefab。");
            GameObject root = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(original));
            try
            {
                root.name = "NpcDialogueSystem";
                Set(root.GetComponent<DialogueHost>(), "_content", content);
                root.GetComponent<DialogueRunner>().SetProject(content.Project);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void CreateLab(List<NpcDefinition> definitions)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                Camera camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                camera.transform.position = new Vector3(0f, 1.5f, -10f);
                camera.orthographic = true;
                camera.orthographicSize = 8f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.28f, 0.3f, 0.34f);
                var ground = new GameObject("Ground", typeof(BoxCollider2D));
                ground.layer = 8;
                ground.transform.position = new Vector3(0f, -0.3f, 0f);
                ground.GetComponent<BoxCollider2D>().size = new Vector2(40f, 0.6f);
                RuleSystemDriver driver = new GameObject("Rules").AddComponent<RuleSystemDriver>();
                Set(driver, "_catalog", AssetDatabase.LoadAssetAtPath<RuleCatalog>("Assets/_Project/Data/Dialogue/DialogueLabRules.asset"));
                DialogueHost host = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/NpcDialogueSystem.prefab"), scene)).GetComponent<DialogueHost>();
                var conditions = new Dictionary<string, DialogueSceneFlag>();
                foreach (string key in new[] { "abilityReady", "buttonPowered", "gearReleased", "hasPass", "townSoldOut" })
                {
                    DialogueSceneFlag flag = new GameObject(key).AddComponent<DialogueSceneFlag>();
                    var flagFields = new SerializedObject(flag);
                    flagFields.FindProperty("_key").stringValue = "npc_" + key;
                    flagFields.ApplyModifiedPropertiesWithoutUndo();
                    conditions.Add(key, flag);
                }
                var actors = new List<NpcActor>();
                for (int i = 0; i < definitions.Count; i++)
                {
                    GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/" + definitions[i].Id + ".prefab"), scene);
                    root.transform.position = new Vector3((i - 4) * 2.6f, 0f, 0f);
                    NpcActor actor = root.GetComponent<NpcActor>();
                    Set(actor, "_host", host);
                    foreach (var condition in conditions) Set(actor, "_" + condition.Key, condition.Value);
                    actors.Add(actor);
                }
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        public static NpcState[] DialogueStates(NpcRole role)
        {
            switch (role)
            {
                case NpcRole.Merchant: return new[] { NpcState.Talk, NpcState.Bye };
                case NpcRole.Mayor:
                case NpcRole.Guard: return new[] { NpcState.Talk1, NpcState.Talk2 };
                case NpcRole.GearKeeper: return new[] { NpcState.Talk1, NpcState.Talk2, NpcState.Talk3, NpcState.Talk4, NpcState.Talk5 };
                case NpcRole.MysteryMerchant: return new[] { NpcState.Talk1, NpcState.Talk2, NpcState.Bye };
                default: return new[] { NpcState.Talk };
            }
        }

        [MenuItem("Tools/规则游戏/NPC/接入玩家与场景内交互")]
        public static void ConnectPlayableLab()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("先退出 Play，接入后重新运行测试场景。");
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var actors = new List<NpcActor>();
                var flags = new Dictionary<string, DialogueSceneFlag>();
                DialogueHost host = null;
                GameObject player = null;
                Camera camera = null;
                GameObject ground = null;
                NpcLabControls controls = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    actors.AddRange(root.GetComponentsInChildren<NpcActor>(true));
                    foreach (DialogueSceneFlag flag in root.GetComponentsInChildren<DialogueSceneFlag>(true)) flags[flag.name] = flag;
                    if (root.GetComponent<DialogueHost>() != null) host = root.GetComponent<DialogueHost>();
                    if (root.GetComponent<Camera>() != null) camera = root.GetComponent<Camera>();
                    if (root.GetComponent<NpcPlayerInteraction>() != null) player = root;
                    if (root.name == "Ground") ground = root;
                    if (root.GetComponent<NpcLabControls>() != null) controls = root.GetComponent<NpcLabControls>();
                }
                if (host == null || camera == null || ground == null) throw new InvalidOperationException("NPC 试验场缺少 Host、摄像机或地面。");
                if (player == null)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/EnemyLabPlayer.prefab");
                    if (prefab == null) throw new InvalidOperationException("缺少现有主角预制体 EnemyLabPlayer。");
                    player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    player.name = "Player";
                    player.tag = "Player";
                    player.transform.position = new Vector3(-13.5f, 1f, 0f);
                    // 复用真实角色与动画，移除占据测试画面的旧 P3 调试面板。
                    foreach (MonoBehaviour component in player.GetComponents<MonoBehaviour>())
                        if (component != null && component.GetType().Name == "P3DebugOverlay") UnityEngine.Object.DestroyImmediate(component);
                    player.AddComponent<NpcPlayerInteraction>();
                }
                NpcPlayerInteraction interaction = player.GetComponent<NpcPlayerInteraction>();
                Set(interaction, "_host", host);
                var interactionFields = new SerializedObject(interaction);
                SerializedProperty targets = interactionFields.FindProperty("_npcs");
                targets.arraySize = actors.Count;
                for (int i = 0; i < actors.Count; i++) targets.GetArrayElementAtIndex(i).objectReferenceValue = actors[i];
                interactionFields.ApplyModifiedPropertiesWithoutUndo();
                UnityEventTools.RemovePersistentListener(interaction.AdvanceDialogue, host.GetComponent<DialogueToolkitPresenter>().Advance);
                UnityEventTools.AddPersistentListener(interaction.AdvanceDialogue, host.GetComponent<DialogueToolkitPresenter>().Advance);
                EditorUtility.SetDirty(interaction);
                foreach (NpcActor actor in actors)
                    if (actor.Definition.Role == NpcRole.Guard) Set(actor, "_proximityActor", player.transform);
                camera.orthographicSize = 4f;
                camera.backgroundColor = new Color(0.78f, 0.8f, 0.82f);
                camera.transform.position = new Vector3(-12.5f, 1.6f, -10f);
                SpriteRenderer floor = ground.GetComponent<SpriteRenderer>();
                if (floor == null) floor = ground.AddComponent<SpriteRenderer>();
                floor.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/P4WhiteboxSquare.png");
                floor.drawMode = SpriteDrawMode.Tiled;
                floor.size = new Vector2(40f, 0.6f);
                floor.color = new Color(0.28f, 0.34f, 0.38f);
                floor.sortingOrder = -10;
                foreach (int direction in new[] { -1, 1 })
                {
                    string name = direction < 0 ? "LeftBoundary" : "RightBoundary";
                    bool exists = false;
                    foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == name) exists = true;
                    if (exists) continue;
                    var wall = new GameObject(name, typeof(BoxCollider2D));
                    wall.layer = 8;
                    wall.transform.position = new Vector3(direction * 19.8f, 2.5f, 0f);
                    wall.GetComponent<BoxCollider2D>().size = new Vector2(0.4f, 6f);
                }
                if (controls == null) controls = new GameObject("NPC Interaction HUD", typeof(UIDocument)).AddComponent<NpcLabControls>();
                UIDocument document = controls.GetComponent<UIDocument>();
                document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Dialogue/DialoguePanelSettings.asset");
                document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Npcs/NpcLab.uxml");
                document.sortingOrder = 50;
                Set(controls, "_interaction", interaction);
                Set(controls, "_camera", camera);
                var fields = new SerializedObject(controls);
                SerializedProperty conditions = fields.FindProperty("_conditions");
                string[] keys = { "abilityReady", "buttonPowered", "gearReleased", "hasPass", "townSoldOut" };
                conditions.arraySize = keys.Length;
                for (int i = 0; i < keys.Length; i++) conditions.GetArrayElementAtIndex(i).objectReferenceValue = flags[keys[i]];
                fields.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                Debug.Log("[NPC] 玩家与 Game 画面交互已接好：A/D 移动，空格跳跃，E 交谈/继续，Esc 结束；右上角切换条件。");
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static NpcProduct[] Products(NpcRole role)
        {
            if (role != NpcRole.Merchant && role != NpcRole.MysteryMerchant) return Array.Empty<NpcProduct>();
            bool town = role == NpcRole.Merchant;
            return town ? new[] {
                Product("town_hp_1", "Hp+（1）", "增加最大血量"), Product("town_hp_2", "Hp+（2）", "增加最大血量"),
                Product("town_magic_1", "Magic+（1）", "增加最大法力"), Product("town_magic_2", "Magic+（2）", "增加最大法力") }
                : new[] { Product("castle_hp_3", "Hp+（3）", "增加最大血量"), Product("castle_magic_3", "Magic+（3）", "增加最大法力"),
                    Product("magic_battery", "魔力电池", "CD 减半"), Product("rule_magnifier", "规则放大镜", "效果持续时间加倍") };
        }

        private static NpcProduct Product(string id, string name, string effect) => new NpcProduct { Id = id, DisplayName = name, Effect = effect, Price = -1 };
        private static string DisplayName(string id)
        {
            if (id.StartsWith("Resident", StringComparison.Ordinal)) return "居民" + id.Substring(8);
            switch (id)
            {
                case "Merchant": return "商人";
                case "Mayor": return "镇长";
                case "GearKeeper": return "齿轮人";
                case "Guard": return "守卫";
                default: return "神秘商人";
            }
        }

        private static string MissingAnimations(NpcRole role)
        {
            switch (role)
            {
                case NpcRole.Resident: return "行走动画未交付，当前为静态精灵随物理移动。";
                case NpcRole.Merchant: return "嘴部张合、道谢/鞠躬动画未交付。";
                case NpcRole.Mayor: return "嘴部张合、砸法杖赐予能力动画未交付。";
                case NpcRole.GearKeeper: return "齿轮持续旋转、嘴部齿轮与拧发条动画未交付。";
                case NpcRole.Guard: return "说话、前戳攻击和转身放行动画未交付。";
                default: return "眼睛滑入/滑出与堡垒弹性动画未交付，当前切换两张姿态。";
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static void Set(UnityEngine.Object target, string property, object value)
        {
            var fields = new SerializedObject(target);
            SerializedProperty field = fields.FindProperty(property);
            if (value is float number) field.floatValue = number;
            else field.objectReferenceValue = value as UnityEngine.Object;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Tools/规则游戏/NPC/校验全部 NPC 配置")]
        public static void Validate()
        {
            DialogueContent content = AssetDatabase.LoadAssetAtPath<DialogueContent>(ContentPath);
            var errors = DialogueContentValidator.Validate(content);
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:NpcDefinition", new[] { Data }))
            {
                NpcDefinition definition = AssetDatabase.LoadAssetAtPath<NpcDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                count++;
                if (definition.IdleSprite == null || definition.TalkSprite == null) errors.Add(definition.Id + " 缺少姿态精灵。");
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/" + definition.Id + ".prefab");
                if (prefab == null || prefab.GetComponent<NpcActor>() == null || prefab.GetComponent<RuleAffectedBody>() == null)
                    errors.Add(definition.Id + " 预制体接线不完整。");
                foreach (NpcState state in DialogueStates(definition.Role))
                    if (content == null || content.FindSpeaker(definition.SpeakerId(state)) == null)
                        errors.Add(definition.Id + " 缺少台词状态 " + state);
            }
            if (count != 9) errors.Add("应有 9 个 NPC 配置，实际 " + count);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            Debug.Log("[NPC] 9 个角色配置、18 个台词入口、独立兜底与预制体引用校验通过。");
        }
    }
}
