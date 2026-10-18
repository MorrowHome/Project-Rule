using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键生成 / 摆放**轨道移动平台**：<see cref="TrackMotor"/> 管「沿轨道走」，
    /// 本工具补上「平台」那一半 —— 板面（<see cref="PlatformSkin"/>）+ 用瓦片铺出来的图像。
    /// </summary>
    /// <remarks>
    /// <para><b>图像是可配置的</b>：板面挂在 <see cref="PlatformSkin"/> 上，关卡里换一张瓦片图就行，
    /// 碰撞箱会跟着图改。本工具只负责**给出一个默认**（<c>Platform_Stone_Short_01</c>＝美术的「平台1」，
    /// 两端都收尾的整段短台）—— 换皮不必重跑本工具，直接在 Inspector 里换图。</para>
    ///
    /// <para><b>轴心 = 板面「上表面的中点」</b>：把根物体摆在**格线**上，人踩的那一面就落在格线上。
    /// 美术子物体按瓦片自己的轴心自动贴到碰撞箱左上角（见 <see cref="PlatformSkin"/>）。</para>
    ///
    /// <para>⚠️ <b>必须落 <c>Ground</c> 层</b>（层 8）：玩家的接触探测只扫这一层，
    /// 放错层就是一块穿得过去的空气，承载（<c>IMovingSurface</c>）也读不到。</para>
    ///
    /// <para>⚠️ <b>重建预制体不动你开着的场景</b>：走 <c>PrefabUtility.LoadPrefabContents</c>，
    /// 它在**预览场景**里加载资产、改完再写回，不会往关卡场景里塞临时物体、也不会把场景标脏
    /// （工程约定：这个工程的 Play 没有「退出即还原」这层保险，所以编辑器工具一律别碰已打开的场景）。</para>
    ///
    /// <para>用法：菜单 Tools/规则游戏/地图组件/⑬ 轨道移动平台（生成预制体并放一个实例）。</para>
    /// </remarks>
    public static class MovingPlatformTrackBuilder
    {
        /// <summary>预制体落点：地图道具单独一个文件夹，免得跟角色 / 敌人混在一起。</summary>
        public const string Folder = "Assets/_Project/Prefabs/Level";
        public const string PrefabPath = Folder + "/MovingPlatform_Track.prefab";

        /// <summary>默认板面图：整段短台（两端收尾），2.74 单位宽、1 格厚。</summary>
        private const string DefaultSpritePath = "Assets/_Project/Art/Sprites/Tiles/Stone/Platform_Stone_Short_01.png";

        /// <summary>地面层：玩家的 <c>MotionTuning.SolidLayer</c> 必须能扫到它。</summary>
        private const string SolidLayerName = "Ground";

        private const string RootName = "MovingPlatform_Track";
        private const string ArtName = "Art";

        /// <summary>默认轨道：从出生点往右 3 个单位往返。之后在 Inspector 里改航点。</summary>
        private static readonly Vector2 DefaultRun = new Vector2(3f, 0f);

        /// <summary>美术的 Order in Layer：压在地形白盒（−2）之上、角色（0）之后。</summary>
        private const int ArtSortingOrder = -1;

        [MenuItem("Tools/规则游戏/地图组件/⑬ 轨道移动平台（TrackMotor）", false, 62)]
        public static void Create()
        {
            GameObject prefab = BuildPrefab();
            if (prefab == null) return;

            PlaceInstance(prefab);
        }

        [MenuItem("Tools/规则游戏/地图组件/⑬b 只放一个轨道移动平台实例", false, 63)]
        public static void PlaceOnly()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[地图组件] 还没有预制体：" + PrefabPath + "，先跑菜单 ⑬ 生成。");
                return;
            }

            PlaceInstance(prefab);
        }

        /// <summary>
        /// 按默认配置重建预制体（**会覆盖同名资产的板面**）。返回资产；配置有问题时返回 null。
        /// </summary>
        /// <remarks>只在**预览场景**里改，不碰任何已打开的关卡场景。</remarks>
        public static GameObject BuildPrefab()
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultSpritePath);
            if (sprite == null)
            {
                Debug.LogError("[地图组件] 找不到平台瓦片：" + DefaultSpritePath);
                return null;
            }

            int layer = LayerMask.NameToLayer(SolidLayerName);
            if (layer < 0)
            {
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，平台放下去玩家踩不到。");
                return null;
            }

            EnsureFolder(Folder);

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject prefab = existing != null
                ? RebuildInPlace(sprite, layer)
                : CreateFromScratch(sprite, layer);

            if (prefab == null) return null;

            Debug.Log("[地图组件] 已生成 " + PrefabPath + "（层级 " + SolidLayerName + "）。"
                      + "轴心在板面**上表面的中点** —— 摆在格线上，踩的那面就在格线上；"
                      + "图像在 PlatformSkin 上，换一张瓦片图碰撞箱就会跟着改。");

            return prefab;
        }

        // ==================================================================
        //  写预制体（都不碰已打开的场景）
        // ==================================================================

        /// <summary>
        /// 就地重写已有预制体：<c>LoadPrefabContents</c> 把资产加载进**预览场景**，
        /// 改完 <c>SaveAsPrefabAsset</c> 写回。**不往任何关卡场景里放东西**，
        /// 所以不会像「先 new 一个再删掉」那样把场景标脏。
        /// </summary>
        private static GameObject RebuildInPlace(Sprite sprite, int layer)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (root == null)
            {
                Debug.LogError("[地图组件] 预制体加载失败：" + PrefabPath);
                return null;
            }

            try
            {
                // 只**确保**平台那几件东西在位、并把默认值刷回去；
                // 不删你另外加的子物体 / 组件（重建不该顺手清掉别人的东西）。
                Configure(root, sprite, layer);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 资产不存在时从零搭一个：物体建在**预览场景**里，写完就把预览场景关掉。
        /// </summary>
        /// <remarks>
        /// <c>new GameObject()</c> 会先落在当前激活的场景，所以要立刻搬进预览场景 ——
        /// 这一下可能把激活场景标脏（Unity 的行为）。**预制体已经存在时走
        /// <see cref="RebuildInPlace"/>，不会有这个问题。**
        /// </remarks>
        private static GameObject CreateFromScratch(Sprite sprite, int layer)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, preview);
                Configure(root, sprite, layer);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (root != null) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        // ==================================================================
        //  装配
        // ==================================================================

        /// <summary>把「平台」那一半装到根物体上：板面 + 碰撞 + 刚体 + TrackMotor。</summary>
        private static void Configure(GameObject root, Sprite sprite, int layer)
        {
            root.name = RootName;
            root.layer = layer;
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            // Kinematic：位置由 TrackMotor 在物理步进里用 MovePosition 提交，
            // 暂停（Physics2D.simulationMode = Script）时自然停住。
            var body = root.GetComponent<Rigidbody2D>();
            if (body == null) body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            // 尺寸由 PlatformSkin 按图写：这里只保证组件在。
            if (root.GetComponent<BoxCollider2D>() == null) root.AddComponent<BoxCollider2D>();

            // 复用已有的 Art 子物体：重建时不该长出第二个。
            Transform art = root.transform.Find(ArtName);
            if (art == null)
            {
                var artObject = new GameObject(ArtName);
                artObject.transform.SetParent(root.transform, false);
                art = artObject.transform;
            }
            var renderer = art.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = art.gameObject.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = ArtSortingOrder;

            var skin = root.GetComponent<PlatformSkin>();
            if (skin == null) skin = root.AddComponent<PlatformSkin>();
            var data = new SerializedObject(skin);
            data.FindProperty("_sprite").objectReferenceValue = sprite;
            data.FindProperty("_art").objectReferenceValue = renderer;
            data.ApplyModifiedPropertiesWithoutUndo();
            skin.Apply();   // 立刻把图与碰撞箱摆好，不等下一次 OnValidate

            var motor = root.GetComponent<TrackMotor>();
            if (motor == null) motor = root.AddComponent<TrackMotor>();
            // 航点是**相对出生点的偏移**，所以同一个预制体摆在哪都从自己脚下长出轨道。
            motor.SetPoints(new[] { DefaultRun });
        }

        private static void PlaceInstance(GameObject prefab)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = SceneViewPivot();
            Undo.RegisterCreatedObjectUndo(instance, "放置轨道移动平台");

            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);

            Debug.Log("[地图组件] 已放置 " + RootName + "。默认轨道「往右 " + DefaultRun.x
                      + " 单位往返」，速度 / 停站 / 往返模式在 TrackMotor 的「节奏」里；"
                      + "航点填的是**相对出生点**的偏移。板面图在 PlatformSkin 上换。");
        }

        /// <summary>没开 Scene 视图时退回原点，不至于把物体扔到看不见的地方。</summary>
        private static Vector3 SceneViewPivot()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return Vector3.zero;

            Vector3 pivot = view.pivot;
            pivot.z = 0f;
            return pivot;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = System.IO.Path.GetDirectoryName(folder);
            string leaf = System.IO.Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;

            parent = parent.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
