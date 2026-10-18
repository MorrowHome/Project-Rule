using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Core.Rules;
using RuleGame.Development.Testing;
using RuleGame.Gameplay.Combat;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Infrastructure;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 生成**巨石断剑机关的试验场景**：一块地 + 一套机关 + 一个假玩家 + 一个自动跑的冒烟测试。
    /// </summary>
    /// <remarks>
    /// <para>为什么要假玩家：真玩家会被「重力 × 反转」一起掀到天上，而本场景要验的是
    /// <b>机关本身</b>（巨石 / 按钮 / 碎块 / 剑）以及「有人站在剑边」这一条。
    /// 假玩家只要一个 <c>BoxCollider2D</c> + <c>EntityDamageable</c>（阵营 = 玩家）就够了 ——
    /// <c>SwordInStone</c> 认的是「玩家阵营」，不是某个具体类型。</para>
    ///
    /// <para>机关本体是**从预制体实例化**的（<c>Prefabs/BoulderSwordMechanism.prefab</c>）——
    /// 也就是说这个场景验的就是关卡里拖进去的那一个，不是另搭一套长得像的散件。</para>
    ///
    /// <para>跑法：打开生成的场景 → Play → 看 Console。全部通过是普通 Log，
    /// 有一条失败是 LogError（见 <c>BoulderSwordSmokeTest</c>）。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑫b 生成试验场景（巨石断剑）。</para>
    /// </remarks>
    public static class BoulderSwordLabBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Test_BoulderSword.unity";

        private const string SpritePath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string CatalogPath = "Assets/_Project/Data/Rules/RuleCatalog.asset";
        private const string SolidLayerName = "Ground";

        private const string GroundName = "Ground";
        private const string CeilingName = "Ceiling";
        private const string SystemsName = "Systems";
        private const string PullerName = "FakePlayer";
        private const string SmokeTestName = "SmokeTest";

        /// <summary>地面顶面 = y 0；巨石静止时中心在 1.2（= BoulderSwordBuilder.RestHeight）。</summary>
        private const float GroundTop = 0f;

        /// <summary>假玩家初始位置：离机关远一点，碎开之后再挪到剑边上。</summary>
        private static readonly Vector3 PullerHome = new Vector3(9f, 1f, 0f);

        private static readonly Color GroundColor = new Color(0.22f, 0.22f, 0.21f, 1f);
        private static readonly Color CeilingColor = new Color(0.55f, 0.55f, 0.52f, 1f);

        [MenuItem("Tools/规则游戏/地图组件/⑫c 生成试验场景（巨石断剑）", false, 62)]
        public static void CreateLabScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BuildLabScene();
        }

        /// <summary>
        /// 真正干活的那一半：**不弹「要不要保存当前场景」**，给自动化脚本直接调
        /// （菜单入口只负责先问一句）。
        /// </summary>
        public static void BuildLabScene()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            try
            {
                if (!Populate()) return;

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.SaveAssets();

                Debug.Log($"[地图组件] 已生成试验场景 {ScenePath}：Play 之后冒烟测试会自己把整条流程跑一遍，" +
                          "结果打在 Console。\n" +
                          "· 期望顺序：举起来 → 撞按钮 → 震裂 → 落下来碎开 → 露出剑 → 摩擦反转拔剑\n" +
                          "· 想自己上手玩：把这套机关（菜单 ⑫）拖进正式关卡，那里有真玩家");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        /// <summary>
        /// 把试验场景的内容铺进**当前激活的场景**（不新建、不保存）——
        /// 给自动化脚本用：先在空场景里铺，再自己 <c>SaveScene</c>。
        /// </summary>
        /// <returns>资源齐备返回 true；缺白盒方块 / 层 / 规则目录时打日志并返回 false。</returns>
        public static bool Populate()
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (sprite == null)
            {
                Debug.LogError("[地图组件] 找不到白盒方块：" + SpritePath);
                return false;
            }

            int layer = LayerMask.NameToLayer(SolidLayerName);
            if (layer < 0)
            {
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，试验场景搭不起来。");
                return false;
            }

            RuleCatalog catalog = AssetDatabase.LoadAssetAtPath<RuleCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError("[地图组件] 找不到规则目录：" + CatalogPath + "，机关没有规则可读。");
                return false;
            }

            BuildCamera();
            BuildLights();
            BuildGround(sprite, layer);
            BuildSystems(catalog);

            // ---- 机关本体：**实例化预制体** ---------------------------------
            // 试验场景验的就是关卡里拖的那个预制体 —— 不是另搭一套长得像的散件。
            GameObject prefab = EnsurePrefab();
            if (prefab == null) return false;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = new Vector3(0f, BoulderSwordBuilder.RestHeight, 0f);

            GravityBoulder boulder = instance.GetComponentInChildren<GravityBoulder>(true);
            SwordInStone sword = instance.transform.Find("Sword").GetComponent<SwordInStone>();

            // ---- 巨石上方那个按钮：拖的**就是**关卡里那个 CityButton ------------
            CitySwitch button = BuildCeilingButton();
            if (button == null) return false;

            // ---- 解锁按钮：巨石左边地面上，玩家走进去就按下 -------------------
            CitySwitch release = BuildReleaseButton();
            if (release == null) return false;

            // 与配 Gate 一样：把巨石挂到这个按钮的 FollowMotor 上（预制体里存不了场景引用，写在实例上）。
            release.FollowMotor = boulder;

            // ---- 假玩家 -------------------------------------------------
            Transform puller = BuildFakePlayer();

            // ---- 冒烟测试 -----------------------------------------------
            var testObject = new GameObject(SmokeTestName);
            var smoke = testObject.AddComponent<BoulderSwordSmokeTest>();
            SetObject(smoke, "_boulder", boulder);
            SetObject(smoke, "_armButton", button);
            SetObject(smoke, "_releaseButton", release);
            SetObject(smoke, "_sword", sword);
            SetObject(smoke, "_puller", puller);
            SetObject(smoke, "_catalog", catalog);

            return true;
        }

        // ==================================================================
        //  场景零件
        // ==================================================================

        private static void BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 4.5f, -10f);

            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.13f, 0.15f, 1f);
        }

        private static void BuildLights()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
        }

        /// <summary>地板 + 一条悬在天花板按钮上方的梁（纯装饰，让按钮看起来是装在天花板上的）。</summary>
        private static void BuildGround(Sprite sprite, int layer)
        {
            // 地板：顶面正好在 y = 0（中心 -1，高 2）。
            Transform ground = NewSolid(GroundName, new Vector3(0f, GroundTop - 1f, 0f), new Vector2(60f, 2f), layer);
            BuildBlock(ground, sprite, new Vector2(60f, 2f), GroundColor, -3);

            // 天花板：CityButton 的碰撞体是 **Trigger**，巨石会**穿过**按钮继续上升 ——
            // 所以这条梁就是把它拦住的那块顶（顺带让按钮看着像装在天花板上）。
            float ceilingY = BoulderSwordBuilder.RestHeight + BoulderSwordBuilder.LiftHeight + 0.7f;
            Transform ceiling = NewSolid(CeilingName, new Vector3(0f, ceilingY, 0f), new Vector2(10f, 0.3f), layer);
            BuildBlock(ceiling, sprite, new Vector2(10f, 0.3f), CeilingColor, -3);
        }

        /// <summary>
        /// 巨石上方那个按钮：实例化的**就是** <c>Prefabs/BrickCity/CityButton.prefab</c> ——
        /// 与关卡里拖的按钮一模一样，不是另做一个（它**不在**机关预制体里）。
        /// </summary>
        private static CitySwitch BuildCeilingButton()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoulderSwordBuilder.CityButtonPath);
            if (prefab == null)
            {
                Debug.LogError("[地图组件] 找不到按钮预制体：" + BoulderSwordBuilder.CityButtonPath);
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = new Vector3(
                0f, BoulderSwordBuilder.RestHeight + BoulderSwordBuilder.LiftHeight, 0f);

            return instance.GetComponent<CitySwitch>();
        }

        /// <summary>
        /// 解锁按钮：巨石左边、贴地放一个 CityButton，**打开 <c>PressByPlayerProximity</c>** ——
        /// 玩家走进触发区就按下（不用交互键，项目的交互键还没接进正式输入）。
        /// </summary>
        private static CitySwitch BuildReleaseButton()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoulderSwordBuilder.CityButtonPath);
            if (prefab == null)
            {
                Debug.LogError("[地图组件] 找不到按钮预制体：" + BoulderSwordBuilder.CityButtonPath);
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "ReleaseButton";
            instance.transform.position = new Vector3(-2.4f, BoulderSwordBuilder.RestHeight - 0.5f, 0f);

            var button = instance.GetComponent<CitySwitch>();
            button.PressByPlayerProximity = true;   // 玩家走进去就按下
            button.Toggle = false;                  // 按一下就锁住，玩家离开不复位

            return button;
        }

        /// <summary>拿机关预制体；没有就现生成一份（试验场景**验的就是关卡里拖的那个预制体**）。</summary>
        private static GameObject EnsurePrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoulderSwordBuilder.PrefabPath);
            if (prefab != null) return prefab;

            prefab = BoulderSwordBuilder.SavePrefab(BoulderSwordBuilder.PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[地图组件] 生成巨石断剑预制体失败，试验场景搭不起来。");
            }

            return prefab;
        }

        private static void BuildSystems(RuleCatalog catalog)
        {
            var systems = new GameObject(SystemsName);
            var driver = systems.AddComponent<RuleSystemDriver>();
            SetObject(driver, "_catalog", catalog);
        }

        /// <summary>
        /// 假玩家：静态碰撞体 + 玩家阵营的 <c>EntityDamageable</c>。它不会动，也不需要动。
        /// </summary>
        /// <remarks>留在 <c>Default</c> 层（和真玩家一样），不要跟地面共层 —— 否则它自己就是一块地形。</remarks>
        private static Transform BuildFakePlayer()
        {
            var go = new GameObject(PullerName);
            go.transform.position = PullerHome;

            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(0.8f, 1.6f);
            box.offset = Vector2.zero;

            // EntityDamageable 会自己 RequireComponent(EntityStatus)，不用手工加。
            var damageable = go.AddComponent<EntityDamageable>();
            SetEnum(damageable, "_faction", (int)Faction.Player);

            return go.transform;
        }

        private static Transform NewSolid(string name, Vector3 position, Vector2 size, int layer)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.position = position;

            var box = go.AddComponent<BoxCollider2D>();
            box.size = size;
            box.offset = Vector2.zero;

            return go.transform;
        }

        private static void BuildBlock(Transform parent, Sprite sprite, Vector2 size, Color color, int order)
        {
            var go = new GameObject("Art");
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
        }

        // ==================================================================
        //  私有字段写入
        // ==================================================================

        private static void SetObject(Object target, string property, Object value)
        {
            SerializedProperty prop = Find(target, property);
            if (prop == null) return;

            prop.objectReferenceValue = value;
            prop.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string property, int index)
        {
            SerializedProperty prop = Find(target, property);
            if (prop == null) return;

            prop.enumValueIndex = index;
            prop.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty Find(Object target, string property)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty prop = serialized.FindProperty(property);
            if (prop == null)
            {
                Debug.LogError($"[地图组件] {target.GetType().Name} 上没有字段 {property} —— 试验场景会缺一环。");
            }

            return prop;
        }
    }
}
