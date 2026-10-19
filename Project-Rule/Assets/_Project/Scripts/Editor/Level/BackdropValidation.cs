using System;
using System.IO;
using System.Linq;
using RuleGame.Infrastructure.LevelFlow;
using RuleGame.Presentation.SceneComponents;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 背景接线验证：④ 是只读的结构检查，③ 在空场景里跑一遍真实切换。
    /// </summary>
    /// <remarks>
    /// <para><b>③ 必须开新场景</b>：本工程 Enter Play Mode 的 Scene Reload 是关的，
    /// 在已打开的关卡里玩会把运行时改动落到编辑场景上（见 AGENTS.md 红线）。
    /// 这里走的是和 <c>AreaMusicValidation</c> 一样的流程：存场景组合 → 新建空场景 → Play → 退出后还原。</para>
    /// </remarks>
    [InitializeOnLoad]
    public static class BackdropValidation
    {
        private const string Pending = "RuleGame.BackdropValidation.Pending";
        private const string SceneState = "RuleGame.BackdropValidation.SceneState";

        private static readonly System.Collections.Generic.List<string> Results =
            new System.Collections.Generic.List<string>();

        private static BackdropDirector _director;
        private static Camera _camera;
        private static Transform _focus;
        private static BackdropZone _brickZone;
        private static BackdropProfile _start, _brick, _boss;
        private static int _step;
        private static double _due, _deadline;
        private static bool _running;

        [Serializable]
        private sealed class Setup { public SceneSetup[] Scenes; }

        static BackdropValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayState;
        }

        // ----------------------------------------------------------------
        //  ④ 只读结构检查
        // ----------------------------------------------------------------

        [MenuItem("Tools/规则游戏/背景/④ 检查背景接线（EditMode）", false, 4)]
        public static void CheckStructure()
        {
            int passed = 0;
            var report = new System.Text.StringBuilder();

            void Check(bool condition, string description)
            {
                report.AppendLine((condition ? "PASS " : "FAIL ") + description);
                if (condition) passed++;
                else throw new InvalidOperationException(description);
            }

            GameObject runtime = AssetDatabase.LoadAssetAtPath<GameObject>(BackdropSetupTool.RuntimePrefabPath);
            Check(runtime != null, "LevelRuntime 预制体存在");

            BackdropDirector[] directors = runtime.GetComponentsInChildren<BackdropDirector>(true);
            Check(directors.Length == 1, "LevelRuntime 里只有一个 BackdropDirector");

            BackdropDirector director = directors[0];
            Check(director.GetComponentInParent<Camera>(true) != null, "Backdrop 挂在相机下面");
            Check(director.CurrentProfile == null && HasDefault(director), "默认背景已配置");

            var serialized = new SerializedObject(director);
            var front = serialized.FindProperty("_front").objectReferenceValue as SpriteRenderer;
            var back = serialized.FindProperty("_back").objectReferenceValue as SpriteRenderer;
            Check(front != null && back != null, "Front / Back 两个 SpriteRenderer 都已接线");
            Check(front.sortingOrder == BackdropDirector.FrontOrder, "Front 排序 -999");
            Check(back.sortingOrder == BackdropDirector.BackOrder, "Back 排序 -1000");
            Check(front.GetComponent<Collider2D>() == null && back.GetComponent<Collider2D>() == null,
                "背景没有碰撞体");

            GameObject gameRoot = AssetDatabase.LoadAssetAtPath<GameObject>(BackdropSetupTool.GameRootPrefabPath);
            Check(gameRoot != null, "GameRoot 预制体存在");
            Check(gameRoot.GetComponentsInChildren<BackdropDirector>(true).Length == 1,
                "GameRoot 嵌套预制体继承且只有一个背景组件");

            foreach (string name in new[]
                     { BackdropSetupTool.StartProfile, BackdropSetupTool.BrickProfile, BackdropSetupTool.BrickBossProfile })
            {
                var profile = AssetDatabase.LoadAssetAtPath<BackdropProfile>(
                    BackdropSetupTool.DataFolder + "/" + name + ".asset");
                Check(profile != null && profile.Sprite != null, name + " 配置有贴图");
                Vector2 size = profile.Sprite.bounds.size;
                Check(Mathf.Abs(size.x - 16f) < 0.05f && Mathf.Abs(size.y - 9f) < 0.05f,
                    name + " 世界尺寸 16×9（实际 " + size + "）");
                Check(Mathf.Abs(profile.Sprite.bounds.center.x) < 0.01f
                      && Mathf.Abs(profile.Sprite.bounds.center.y) < 0.01f, name + " 轴心居中");
            }

            Scene building = SceneManager.GetSceneByPath(BackdropSetupTool.BuildingScenePath);
            bool openedHere = !building.IsValid();
            if (openedHere)
            {
                building = EditorSceneManager.OpenScene(BackdropSetupTool.BuildingScenePath, OpenSceneMode.Additive);
            }

            try
            {
                BackdropZone[] zones = building.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<BackdropZone>(true)).ToArray();
                Check(zones.Length == 2, "积木王国里有两个背景区域（实际 " + zones.Length + "）");
                Check(zones.All(zone => zone.GetComponents<Renderer>().Length == 0
                                        && zone.GetComponents<Collider2D>().Length == 0),
                    "背景区域没有 Renderer / Collider");

                BackdropZone brick = zones.First(zone => zone.Priority == 0);
                BackdropZone boss = zones.First(zone => zone.Priority == 10);
                Check(brick.Size.x > 1f && brick.Size.y > 1f && boss.Size.x > 1f && boss.Size.y > 1f,
                    "两个区域尺寸有效");

                Check(brick.Contains(boss.transform.position),
                    "BOSS 区域在积木区域内部（离开 BOSS 房会回落到积木背景）");

                var catalog = AssetDatabase.LoadAssetAtPath<PartitionCatalog>(
                    "Assets/_Project/Data/Config/PartitionCatalog.asset");
                bool registered = false;
                for (int i = 0; i < catalog.Count; i++)
                {
                    if (catalog[i].ScenePath == BackdropSetupTool.BuildingScenePath) registered = true;
                }

                report.AppendLine(registered
                    ? "PASS 积木王国已登记进分区表"
                    : "WARN 积木王国还没登记进 PartitionCatalog —— 流式加载不会加载它，先跑「世界分区/②」");
                if (registered) passed++;
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(building, true);
            }

            report.AppendLine("passed=" + passed);
            Debug.Log("[背景] ④ 检查完成\n" + report);

            Directory.CreateDirectory("Logs/Backdrop");
            File.WriteAllLines("Logs/Backdrop/Structure.txt",
                report.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static bool HasDefault(BackdropDirector director)
        {
            var serialized = new SerializedObject(director);
            return serialized.FindProperty("_defaultProfile").objectReferenceValue != null;
        }

        // ----------------------------------------------------------------
        //  ③ PlayMode 冒烟
        // ----------------------------------------------------------------

        [MenuItem("Tools/规则游戏/背景/③ 验证背景切换（PlayMode）", false, 3)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("请先退出 Play Mode。");
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    throw new InvalidOperationException("请先保存当前场景，验证结束后会自动恢复。");
                }
            }

            SessionState.SetString(SceneState,
                JsonUtility.ToJson(new Setup { Scenes = EditorSceneManager.GetSceneManagerSetup() }));
            SessionState.SetBool(Pending, true);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) Begin();
            if (state != PlayModeStateChange.EnteredEditMode) return;

            SessionState.SetBool(Pending, false);
            string setup = SessionState.GetString(SceneState, "");
            if (!string.IsNullOrEmpty(setup))
            {
                EditorSceneManager.RestoreSceneManagerSetup(JsonUtility.FromJson<Setup>(setup).Scenes);
            }
        }

        private static void Begin()
        {
            Results.Clear();
            _step = 0;
            _running = true;
            _deadline = EditorApplication.timeSinceStartup + 40d;

            try
            {
                Application.runInBackground = true;

                _start = Load(BackdropSetupTool.StartProfile);
                _brick = Load(BackdropSetupTool.BrickProfile);
                _boss = Load(BackdropSetupTool.BrickBossProfile);
                Check(_start != null && _brick != null && _boss != null, "三份背景配置都能加载");

                var cameraHost = new GameObject("BackdropValidation_Camera");
                _camera = cameraHost.AddComponent<Camera>();
                _camera.orthographic = true;
                _camera.orthographicSize = 4f;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                cameraHost.transform.position = new Vector3(0f, 0f, -10f);

                var backdrop = new GameObject("Backdrop");
                backdrop.transform.SetParent(cameraHost.transform, false);
                SpriteRenderer front = NewRenderer(backdrop.transform, "Backdrop_Front", BackdropDirector.FrontOrder);
                SpriteRenderer back = NewRenderer(backdrop.transform, "Backdrop_Back", BackdropDirector.BackOrder);

                _director = backdrop.AddComponent<BackdropDirector>();

                var serialized = new SerializedObject(_director);
                serialized.FindProperty("_defaultProfile").objectReferenceValue = _start;
                serialized.FindProperty("_front").objectReferenceValue = front;
                serialized.FindProperty("_back").objectReferenceValue = back;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                _focus = new GameObject("BackdropValidation_Focus").transform;
                serialized = new SerializedObject(_director);
                serialized.FindProperty("_focus").objectReferenceValue = _focus;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                _brickZone = MakeZone("Validation_Brick", _brick, new Vector3(25f, 0f, 0f), new Vector2(20f, 20f), 0);
                MakeZone("Validation_Boss", _boss, new Vector3(32f, 0f, 0f), new Vector2(10f, 10f), 10);

                _focus.position = Vector3.zero;
                _due = EditorApplication.timeSinceStartup + 0.4d;
                EditorApplication.update += Tick;
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        private static SpriteRenderer NewRenderer(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            return renderer;
        }

        private static BackdropZone MakeZone(string name, BackdropProfile profile, Vector3 position, Vector2 size, int priority)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var zone = go.AddComponent<BackdropZone>();
            var serialized = new SerializedObject(zone);
            serialized.FindProperty("_profile").objectReferenceValue = profile;
            serialized.FindProperty("_size").vector2Value = size;
            serialized.FindProperty("_priority").intValue = priority;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return zone;
        }

        private static BackdropProfile Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<BackdropProfile>(
                BackdropSetupTool.DataFolder + "/" + name + ".asset");
        }

        private static double Now => EditorApplication.timeSinceStartup;

        private static float ExpectedCover(Sprite sprite)
        {
            float worldHeight = _camera.orthographicSize * 2f;
            float worldWidth = worldHeight * _camera.aspect;
            Vector2 size = sprite.bounds.size;
            return Mathf.Max(worldWidth / size.x, worldHeight / size.y);
        }

        private static void Tick()
        {
            if (!_running) return;
            if (Now > _deadline)
            {
                Fail(new TimeoutException("背景验证超时，step=" + _step));
                return;
            }
            if (Now < _due) return;

            try
            {
                SpriteRenderer front = _director.FrontRenderer;
                SpriteRenderer back = _director.BackRenderer;

                switch (_step++)
                {
                    case 0:
                        Check(_director.CurrentProfile == _start, "开局显示默认背景");
                        Check(front.enabled && front.sprite == _start.Sprite && front.color.a >= 0.99f,
                            "默认背景不透明显示在 Front");
                        Check(!back.enabled, "不在切换时 Back 是关的");
                        Check(_camera.backgroundColor == _start.ClearColor, "相机底色跟随背景配置");
                        Check(Mathf.Abs(front.transform.lossyScale.x - ExpectedCover(_start.Sprite) * 1.02f) < 0.02f,
                            "背景按相机视野 cover（16:9 下盖满）");
                        _focus.position = new Vector3(25f, 0f, 0f);
                        _due = Now + 0.6d;
                        break;

                    case 1:
                        Check(_director.CurrentProfile == _brick && !_director.IsFading, "进入积木区域切到积木背景");
                        Check(front.sprite == _brick.Sprite && front.color.a >= 0.99f, "淡化结束后积木背景不透明");
                        Check(!back.enabled, "淡化结束后不再占用 Back");
                        _focus.position = new Vector3(30f, 0f, 0f);
                        _due = Now + 0.6d;
                        break;

                    case 2:
                        Check(_director.CurrentProfile == _boss, "重叠区域里 Priority 10 的 BOSS 区域赢");
                        _focus.position = new Vector3(20f, 0f, 0f);
                        _due = Now + 0.6d;
                        break;

                    case 3:
                        Check(_director.CurrentProfile == _brick, "离开 BOSS 房回到积木背景");
                        _camera.orthographicSize = 2.8f;
                        _due = Now + 0.15d;
                        break;

                    case 4:
                        Check(Mathf.Abs(_director.FrontRenderer.transform.lossyScale.x
                                        - ExpectedCover(_brick.Sprite) * 1.02f) < 0.02f,
                            "规则选择推近（ortho 2.8）后背景重新贴屏、不跟着放大");
                        Check(_director.FrontRenderer.transform.lossyScale.x < 1f, "推近时背景缩放变小");
                        _director.SetProfile(_boss);
                        _due = Now + 0.02d;
                        break;

                    case 5:
                        Check(_director.IsFading, "切换开始进入淡化");
                        Check(back.enabled && back.sprite == _boss.Sprite && back.color.a >= 0.99f
                              && back.sortingOrder == BackdropDirector.BackOrder,
                            "新图先满不透明垫在下面（淡化中画面不发灰）");
                        Check(front.sortingOrder == BackdropDirector.FrontOrder && front.color.a > 0.5f,
                            "旧图盖在上面从满不透明开始淡出");
                        _due = Now + 0.5d;
                        break;

                    case 6:
                        Check(_director.CurrentProfile == _boss && !_director.IsFading, "淡化结束收敛到 BOSS 背景");
                        Check(_director.FrontRenderer.sprite == _boss.Sprite
                              && _director.FrontRenderer.color.a >= 0.99f, "BOSS 背景成为不透明 Front");
                        Check(!_director.BackRenderer.enabled, "淡化结束释放 Back");
                        _camera.orthographicSize = 4f;
                        _focus.position = Vector3.zero;
                        _brickZone.gameObject.SetActive(false);
                        _due = Now + 0.6d;
                        break;

                    case 7:
                        Check(_director.CurrentProfile == _start, "区域停用（分区卸载）后回落到默认背景");
                        Check(_brickZone.GetComponents<Renderer>().Length == 0
                              && _brickZone.GetComponents<Collider2D>().Length == 0,
                            "背景区域没有 Renderer / Collider");
                        Finish(true);
                        break;
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
            }
        }

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            Results.Add("PASS " + description);
        }

        private static void Fail(Exception ex)
        {
            Results.Add("FAIL step=" + _step + " " + ex);
            Debug.LogException(ex);
            Finish(false);
        }

        private static void Finish(bool passed)
        {
            _running = false;
            EditorApplication.update -= Tick;

            Directory.CreateDirectory("Logs/Backdrop");
            string summary = "passed=" + Results.Count(line => line.StartsWith("PASS"))
                             + " failed=" + (passed ? 0 : 1);
            File.WriteAllLines("Logs/Backdrop/Validation.txt", Results.Concat(new[] { summary }));
            Debug.Log("[BackdropValidation] " + summary);
            EditorApplication.isPlaying = false;
        }
    }
}