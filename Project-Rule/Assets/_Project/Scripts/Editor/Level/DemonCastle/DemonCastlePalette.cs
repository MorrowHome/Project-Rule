using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace RuleGame.Editor.Level
{
    /// <summary>保留 prefab 连接的二维放置面板；网格、连续点放、撤销与 Prefab Stage。</summary>
    public sealed class DemonCastlePalette : EditorWindow
    {
        [SerializeField] private string _selected = "DemonFireworkSwitch";
        [SerializeField] private float _grid = .5f;
        [SerializeField] private bool _snap = true;
        [SerializeField] private Transform _parent;
        private bool _placing;
        private string _search = "";
        private VisualElement _catalog;
        private Label _status;
        private Button _paint;
        private Vector3 _preview;
        private Image _artPreview;
        private Bounds _previewBounds = new Bounds(Vector3.zero, new Vector3(1f, .5f, .05f));

        [MenuItem("Tools/规则游戏/地图组件/魔王城/组件放置面板", false, 0)]
        public static void Open() => GetWindow<DemonCastlePalette>("魔王城组件");
        private void OnEnable() { SceneView.duringSceneGui += DuringSceneGUI; }
        private void OnDisable() { SceneView.duringSceneGui -= DuringSceneGUI; _placing = false; }
        public void CreateGUI()
        {
            minSize = new Vector2(310f, 410f);
            var root = rootVisualElement; root.Clear(); root.style.paddingLeft = 12f; root.style.paddingRight = 12f;
            var title = new Label("魔王城 · 地图组件"); title.style.fontSize = 19f; title.style.marginTop = 12f; title.style.marginBottom = 8f; root.Add(title);
            var help = new HelpBox("先选组件，再放到视图中心或连续点放。组合件已接好机关与门；单独琴键和发条需指定目标。", HelpBoxMessageType.Info); root.Add(help);
            var search = new ToolbarSearchField(); search.style.marginTop = 8f; search.RegisterValueChangedCallback(e => { _search = e.newValue; RefreshCatalog(); }); root.Add(search);
            _artPreview = new Image { scaleMode = ScaleMode.ScaleToFit };
            _artPreview.style.height = 72f; _artPreview.style.marginTop = 6f; root.Add(_artPreview);
            var scroll = new ScrollView(); scroll.style.flexGrow = 1f; root.Add(scroll); _catalog = scroll;
            var snap = new Toggle("网格吸附") { value = _snap }; snap.RegisterValueChangedCallback(e => _snap = e.newValue); root.Add(snap);
            var grid = new FloatField("网格间距") { value = _grid }; grid.RegisterValueChangedCallback(e => _grid = Mathf.Max(.05f, e.newValue)); root.Add(grid);
            var parent = new ObjectField("放置父物体（可空）") { objectType = typeof(Transform), allowSceneObjects = true, value = _parent };
            parent.RegisterValueChangedCallback(e => _parent = e.newValue as Transform); root.Add(parent);
            root.Add(new Button(() => PlaceAt(_selected, Snap(Center()), _parent)) { text = "在 Scene 视图中心放置" });
            _paint = new Button(() => { _placing = !_placing; RefreshStatus(); SceneView.RepaintAll(); }) { text = "开始连续点放" }; root.Add(_paint);
            _status = new Label(); _status.style.whiteSpace = WhiteSpace.Normal; _status.style.marginTop = 6f; _status.style.marginBottom = 8f; root.Add(_status);
            root.Add(new Button(() => { DemonCastleBuilder.BuildPrefabs(); RefreshStatus(); }) { text = "补齐缺失预制体（保留已有参数）" });
            RefreshCatalog(); RefreshStatus();
        }
        private void RefreshCatalog()
        {
            if (_catalog == null) return;
            _catalog.Clear();
            for (int i = 0; i < DemonCastleBuilder.Names.Length; i++)
            {
                string name = DemonCastleBuilder.Names[i], label = DemonCastleBuilder.Labels[i];
                if (!string.IsNullOrWhiteSpace(_search) && (name + label).IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (string.IsNullOrWhiteSpace(_search) && (i == 0 || i == 4 || i == 12 || i == 16 || i == 21))
                {
                    var heading = new Label(i == 0 ? "地形" : i == 4 ? "机关与危险" : i == 12 ? "发条拼图" : i == 16 ? "载具与琴键" : "已交付的地形与美术");
                    heading.style.marginTop = 10f; heading.style.unityFontStyleAndWeight = FontStyle.Bold; _catalog.Add(heading);
                }
                var button = new Button(() => { _selected = name; RefreshCatalog(); RefreshStatus(); SceneView.RepaintAll(); }) { text = (_selected == name ? "● " : "   ") + label };
                button.style.unityTextAlign = TextAnchor.MiddleLeft; button.style.height = 26f;
                if (_selected == name) button.style.backgroundColor = new Color(.32f, .25f, .43f);
                _catalog.Add(button);
            }
        }
        private void RefreshStatus()
        {
            int i = Array.IndexOf(DemonCastleBuilder.Names, _selected);
            RefreshArtPreview();
            if (_paint != null) _paint.text = _placing ? "停止连续点放" : "开始连续点放";
            if (_status != null) _status.text = "当前：" + (i >= 0 ? DemonCastleBuilder.Labels[i] : _selected) +
                (_placing ? "\nScene 左键放置 · Esc 结束 · Alt 保留视图导航 · Ctrl/Cmd+Z 撤销" : "\n" + ArtStatus(_selected) + "；放置后可调参数。");
        }
        private static string ArtStatus(string name)
        {
            if (name == "DemonWindingHorizontal" || name == "DemonWindingVertical") return "功能已实现，需指定目标拼图";
            if (name == "DemonPianoWhite" || name == "DemonPianoBlack") return "功能已实现，需指定曲调锁";
            if (name == "DemonSlidingPuzzle") return "拼图本体，需连接发条；可直接用拼图房组合";
            if (name == "DemonFirework") return "爆炸功能已实现，需连接点火开关";
            if (name == "DemonYoyoVisual") return "已交付双帧，仅美术预览";
            if (name == "DemonFireworkSwitch") return "按钮已接美术，烟花箱占位";
            if (name == "DemonGround" || name.StartsWith("DemonGround0", StringComparison.Ordinal) || name.StartsWith("DemonPillar", StringComparison.Ordinal) ||
                name == "DemonPlatformSmall" || name == "DemonPlatformLong" || name == "DemonPlatformMedium") return "已接入交付美术";
            return "机关功能已接入，美术待补";
        }
        private void RefreshArtPreview()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(DemonCastleBuilder.Folder + "/" + _selected + ".prefab");
            var renderers = asset != null ? asset.GetComponentsInChildren<SpriteRenderer>(true) : Array.Empty<SpriteRenderer>();
            Sprite sprite = null; bool any = false;
            foreach (var renderer in renderers)
            {
                if (renderer.sprite == null) continue;
                if (sprite == null) sprite = renderer.sprite;
                var b = renderer.localBounds; // 包含 Tiled 的实际 size，不只取原始图片的宽高。
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
                {
                    Vector3 point = asset.transform.InverseTransformPoint(renderer.transform.TransformPoint(b.center + new Vector3(x * b.extents.x, y * b.extents.y)));
                    if (!any) { _previewBounds = new Bounds(point, Vector3.zero); any = true; }
                    else _previewBounds.Encapsulate(point);
                }
            }
            if (!any) _previewBounds = new Bounds(Vector3.zero, new Vector3(1f, .5f, .05f));
            if (_artPreview != null) { _artPreview.sprite = sprite; _artPreview.style.display = sprite != null ? DisplayStyle.Flex : DisplayStyle.None; }
        }
        private Vector3 Snap(Vector3 position)
        {
            position.z = 0f;
            if (_snap) { float grid = Mathf.Max(.05f, _grid); position.x = Mathf.Round(position.x / grid) * grid; position.y = Mathf.Round(position.y / grid) * grid; }
            return position;
        }
        private static Vector3 Center()
        {
            Vector3 point = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            point.z = 0f; return point;
        }
        public static GameObject PlaceAt(string name, Vector3 position, Transform parent = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return null;
            string path = DemonCastleBuilder.Folder + "/" + name + ".prefab";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                DemonCastleBuilder.BuildPrefabs(); asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) throw new InvalidOperationException("无法加载预制体：" + path);
            }
            if (parent != null && EditorUtility.IsPersistent(parent)) throw new InvalidOperationException("父物体必须属于当前场景。");
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && parent == null) parent = stage.prefabContentsRoot.transform;
            Scene scene = parent != null ? parent.gameObject.scene : SceneManager.GetActiveScene();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("放置魔王城组件");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            Undo.RegisterCreatedObjectUndo(instance, "放置魔王城组件");
            if (parent != null) Undo.SetTransformParent(instance.transform, parent, "设置组件父物体");
            position.z = 0f; instance.transform.position = position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            Undo.CollapseUndoOperations(group); Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(scene); return instance;
        }
        private void DuringSceneGUI(SceneView view)
        {
            if (!_placing || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            { _placing = false; RefreshStatus(); evt.Use(); view.Repaint(); return; }
            if (evt.alt) return;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            if (evt.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            var ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
            if (!new Plane(Vector3.forward, Vector3.zero).Raycast(ray, out float distance)) return;
            _preview = Snap(ray.GetPoint(distance));
            Handles.color = new Color(.8f, .6f, 1f, .9f);
            Handles.DrawWireCube(_preview + _previewBounds.center, _previewBounds.size);
            Handles.Label(_preview + Vector3.up * .4f, _selected);
            if (evt.type == EventType.MouseMove) view.Repaint();
            if (evt.type == EventType.MouseDown && evt.button == 0)
            { PlaceAt(_selected, _preview, _parent); evt.Use(); view.Repaint(); }
        }
        private static void Quick(string name) => PlaceAt(name, Center());
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/魔王城地面（平铺）", false, 100)]
        public static void Place0() => Quick("DemonGround");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/岩石平台·短", false, 101)]
        public static void Place1() => Quick("DemonPlatformSmall");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/岩石平台·长", false, 102)]
        public static void Place2() => Quick("DemonPlatformLong");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/方块平台（占位）", false, 103)]
        public static void Place3() => Quick("DemonPlatformSquare");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/烟花箱（需连接开关）", false, 104)]
        public static void Place4() => Quick("DemonFirework");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/组合机关/烟花箱与踩踏开关（已接线）", false, 105)]
        public static void Place5() => Quick("DemonFireworkSwitch");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/跳跃弹簧", false, 106)]
        public static void Place6() => Quick("DemonSpring");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/生锈铁块", false, 107)]
        public static void Place7() => Quick("DemonRustBlock");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/钟乳石·短", false, 108)]
        public static void Place8() => Quick("DemonStalactiteShort");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/钟乳石·中", false, 109)]
        public static void Place9() => Quick("DemonStalactiteMedium");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/钟乳石·长", false, 110)]
        public static void Place10() => Quick("DemonStalactiteLong");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/翻转平台", false, 111)]
        public static void Place11() => Quick("DemonFlipPlatform");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/横向发条（需指定拼图）", false, 112)]
        public static void Place12() => Quick("DemonWindingHorizontal");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/纵向发条（需指定拼图）", false, 113)]
        public static void Place13() => Quick("DemonWindingVertical");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/拼图本体（需配发条）", false, 114)]
        public static void Place14() => Quick("DemonSlidingPuzzle");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/组合机关/拼图房（已接线）", false, 115)]
        public static void Place15() => Quick("DemonPuzzleRoom");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/升降木马", false, 116)]
        public static void Place16() => Quick("DemonCarousel");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/钢琴白键（需指定曲调锁）", false, 117)]
        public static void Place17() => Quick("DemonPianoWhite");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/钢琴黑键（需指定曲调锁）", false, 118)]
        public static void Place18() => Quick("DemonPianoBlack");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/组合机关/钢琴房（已接线）", false, 119)]
        public static void Place19() => Quick("DemonPianoRoom");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/机关/滑板车", false, 120)]
        public static void Place20() => Quick("DemonSkateboard");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/地块·右残缺", false, 140)]
        public static void PlaceArt0() => Quick("DemonGround02");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/地块·左残缺", false, 141)]
        public static void PlaceArt1() => Quick("DemonGround03");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/地块·两侧残缺", false, 142)]
        public static void PlaceArt2() => Quick("DemonGround04");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/岩石平台·中", false, 143)]
        public static void PlaceArt3() => Quick("DemonPlatformMedium");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/立柱·01", false, 144)]
        public static void PlaceArt4() => Quick("DemonPillar01");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/立柱·02", false, 145)]
        public static void PlaceArt5() => Quick("DemonPillar02");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/立柱·03", false, 146)]
        public static void PlaceArt6() => Quick("DemonPillar03");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/立柱·04", false, 147)]
        public static void PlaceArt7() => Quick("DemonPillar04");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/立柱·05", false, 148)]
        public static void PlaceArt8() => Quick("DemonPillar05");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/地形与装饰/立柱·06", false, 149)]
        public static void PlaceArt9() => Quick("DemonPillar06");
        [MenuItem("Tools/规则游戏/地图组件/魔王城/放置/美术展示/溜溜球（仅动画）", false, 150)]
        public static void PlaceArt10() => Quick("DemonYoyoVisual");
    }
}
