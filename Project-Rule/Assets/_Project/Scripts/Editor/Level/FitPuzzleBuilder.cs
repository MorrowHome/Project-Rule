using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Development.Testing;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene;

namespace RuleGame.Editor.Level
{
    /// <summary>
    /// 一键生成**拼装缺口四件套**：两块重物的预制体 + 一间隔离的试验场景。
    /// </summary>
    /// <remarks>
    /// <para>菜单 <c>Tools/规则游戏/地图组件/⑮ 拼装缺口（预制体 + 试验场景）</c>。</para>
    /// <para><b>⚠️ 两个入口都不会碰当前打开的场景</b>：都在一个**附加的空场景**里搭，
    /// 存完就关掉，最后把活动场景还原回去（与 ⑬ 巨石生成器同一套做法）。</para>
    /// <para>试验场景 <c>Scenes/Test_FitSocket.unity</c> 里挂着一个 <c>FitPuzzleSmokeTest</c>：
    /// 打开 → Play → 看 Console，会自己把「推不动 / 质量×失效推进去 / 摩擦让球滚 / 翻重力顶进窝 / 闸门升起」跑一遍。</para>
    /// </remarks>
    public static class FitPuzzleBuilder
    {
        // ------------------------------------------------------------------ 资源

        private const string WhiteboxPath = "Assets/_Project/Art/Sprites/P4WhiteboxSquare.png";
        private const string TriangleSpritePath = "Assets/_Project/Art/Sprites/Brick/BrickTriangle.png";
        private const string CircleSpritePath = "Assets/_Project/Art/Sprites/Brick/BrickBall.png";
        private const string MaterialPath = "Assets/_Project/Data/Physics/StoneMaterial2D.asset";

        /// <summary>四个预制体都住在积木城那套里（与 PlayerSpawnPoint / 按钮同目录）。</summary>
        private const string PrefabFolder = "Assets/_Project/Prefabs/BrickCity/";

        /// <summary>三角块预制体。</summary>
        public const string TrianglePrefabPath = PrefabFolder + "FitBlockTriangle.prefab";

        /// <summary>圆球预制体。</summary>
        public const string RoundPrefabPath = PrefabFolder + "FitBlockRound.prefab";

        /// <summary>三角凹位预制体（石板 + 三角空缺）。</summary>
        public const string TriangleSocketPrefabPath = PrefabFolder + "FitSocketTriangle.prefab";

        /// <summary>圆窝预制体（石板 + 圆形空缺）。</summary>
        public const string RoundSocketPrefabPath = PrefabFolder + "FitSocketRound.prefab";

        /// <summary>机关预制体：拖进关卡即用（凹位自动收集，门拖一下就能开）。</summary>
        public const string PuzzlePrefabPath = PrefabFolder + "FitPuzzle.prefab";

        /// <summary>凹位外观 —— **就是件的外形**，没有外框：件多大，槽就多大。</summary>
        private const string PlateTrianglePath = "Assets/_Project/Art/Sprites/Brick/BrickSocketTriangle.png";
        private const string PlateRoundPath = "Assets/_Project/Art/Sprites/Brick/BrickSocketRound.png";

        /// <summary>槽位尺寸 = 件的尺寸（三角 1.6 × 1.55，圆 Ø1.2）。</summary>
        private static readonly Vector2 TriangleSocketSize = new Vector2(1.6f, 1.55f);

        private static readonly Vector2 RoundSocketSize = Vector2.one * 1.2f;

        /// <summary>凹位石板画在件**下面**，件入位才盖住空缺。</summary>
        private const int PlateOrder = 1;

        /// <summary>件画在石板与墙之上。</summary>
        private const int BlockOrder = 5;

        /// <summary>试验场景。</summary>
        public const string ScenePath = "Assets/_Project/Scenes/Test_FitSocket.unity";

        private const string SolidLayerName = "Ground";

        // ------------------------------------------------------------------ 尺寸（世界单位）

        private const float TriangleMass = 180f;
        private const float RoundMass = 260f;
        private static readonly Vector2 TriangleSize = new Vector2(1.6f, 1.55f);
        private const float RoundRadius = 0.6f;

        // 房间
        /// <summary>闸门右缘 = 三角石板的右缘（石板 2.4 宽、中心在座位点 1.72）。</summary>
        private const float GateRight = 2.92f;
        private const float GateTop = 3.2f;
        private static readonly Vector2 TriangleSeat = new Vector2(1.72f, 0.775f);
        private const float PocketBaseLeft = 1.05f;
        private const float PocketApex = 1.55f;
        private const float PocketCeiling = 1.6f;
        private const float PocketRight = 2.4f;

        private const float CeilingY = 5.2f;
        private const float CeilingTop = 6.4f;
        private const float ShaftX = 8.4f;
        private const float DomeRadius = 0.8f;
        private static readonly Vector2 BallSeat = new Vector2(ShaftX, 5.4f);
        private const float PitLeft = 7.6f;
        private const float PitRight = 9.2f;
        private const float PitBottom = -1.55f;
        private const float RoomRight = 12.6f;

        private static readonly Vector2 TriangleStart = new Vector2(5.9f, 0.775f);
        /// <summary>
        /// 球开局就在**井底**。
        /// </summary>
        /// <remarks>
        /// ⚠️ 不能把球摆在斜坡上等人用「摩擦 × 削弱」把它滚下来 —— 实测（2026-10-18）：
        /// 球在 20° 斜面上**只要有一点初速就再也停不住**（静摩擦挡得住滑、挡不住滚），
        /// 球会自己滚进井里，「摩擦」那一步就不成立了。球真正的锁是**人在下面够不着它**：
        /// 井有 1.5 m 深，推得动也举不起来，只有「重力 × 反转」能把它送上去。
        /// </remarks>
        private static readonly Vector2 BallStart = new Vector2(ShaftX, -0.9f);

        // 配色：积木世界是**暖浅灰的塑料**，彩色的件才跳得出来
        private static readonly Color WallColor = new Color(0.66f, 0.63f, 0.60f, 1f);
        private static readonly Color FloorColor = new Color(0.46f, 0.43f, 0.40f, 1f);

        /// <summary>闸门换个色，别和墙糊成一片（积木城就是靠色块区分机关的）。</summary>
        private static readonly Color GateColor = new Color(0.33f, 0.40f, 0.48f, 1f);

        /// <summary>跳穿平台：比墙亮一档，一眼看出「这块能穿」。</summary>
        private static readonly Color PlatformColor = new Color(0.80f, 0.77f, 0.72f, 1f);

        /// <summary>件自己的贴图已经带了颜色，这里不要再乘色。</summary>
        private static readonly Color StoneColor = Color.white;

        // ------------------------------------------------------------------ 菜单

        [MenuItem("Tools/规则游戏/地图组件/⑮ 拼装缺口（预制体 + 试验场景）", false, 65)]
        public static void BuildAll()
        {
            GameObject tri = BuildPrefabs();
            if (tri == null) return;
            BuildScene();
        }

        [MenuItem("Tools/规则游戏/地图组件/⑮a 拼装缺口（只生成预制体）", false, 66)]
        public static void BuildPrefabsOnly()
        {
            GameObject tri = BuildPrefabs();
            if (tri != null) Selection.activeObject = tri;
        }

        [MenuItem("Tools/规则游戏/地图组件/⑮b 拼装缺口（只生成试验场景）", false, 67)]
        public static void BuildSceneOnly() => BuildScene();

        // ------------------------------------------------------------------ 预制体

        /// <summary>把两块件装成预制体（旁边开个临时空场景装，不碰当前场景）。</summary>
        public static GameObject BuildPrefabs()
        {
            PhysicsMaterial2D material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(MaterialPath);
            if (material == null)
            {
                Debug.LogWarning("[地图组件] 找不到 " + MaterialPath + "（摩擦 Combine = Minimum），" +
                                 "退回引擎默认摩擦 —— 「摩擦 × 削弱」的效果会变钝。");
            }

            Sprite triangleSprite = LoadSprite(TriangleSpritePath, WhiteboxPath);
            Sprite circleSprite = LoadSprite(CircleSpritePath, WhiteboxPath);
            if (triangleSprite == null) return null;

            GameObject triangle = SavePrefab(TrianglePrefabPath, root =>
            {
                BuildBody(root, FitShape.Triangle, TriangleMass, material, false);
                var poly = root.AddComponent<PolygonCollider2D>();
                poly.SetPath(0, new[]
                {
                    new Vector2(-TriangleSize.x * 0.5f, -TriangleSize.y * 0.5f),
                    new Vector2(TriangleSize.x * 0.5f, -TriangleSize.y * 0.5f),
                    new Vector2(0f, TriangleSize.y * 0.5f),
                });
                if (material != null) poly.sharedMaterial = material;
                Quad(root.transform, "Art", Vector2.zero, TriangleSize, StoneColor, BlockOrder, triangleSprite);
            });

            GameObject round = SavePrefab(RoundPrefabPath, root =>
            {
                BuildBody(root, FitShape.Round, RoundMass, material, true);
                var circle = root.AddComponent<CircleCollider2D>();
                circle.radius = RoundRadius;
                if (material != null) circle.sharedMaterial = material;
                Quad(root.transform, "Art", Vector2.zero, Vector2.one * RoundRadius * 2f, StoneColor, BlockOrder, circleSprite);
            });

            SavePrefab(TriangleSocketPrefabPath, root =>
                BuildSocket(root, FitShape.Triangle, new Vector2(0.45f, 0.45f), TriangleSocketSize,
                    LoadSprite(PlateTrianglePath, WhiteboxPath)));

            SavePrefab(RoundSocketPrefabPath, root =>
                BuildSocket(root, FitShape.Round, new Vector2(0.45f, 0.45f), RoundSocketSize,
                    LoadSprite(PlateRoundPath, WhiteboxPath)));

            // 机关本体：**空物体 + 一个组件**，拖进场景就能用。
            // 关卡里只有 GameObject 不能加组件的话，拖预制体就是唯一的路。
            SavePrefab(PuzzlePrefabPath, root => root.AddComponent<FitPuzzle>());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[地图组件] 拼装缺口四件套已生成（都在 Prefabs/BrickCity/）：\n" +
                      "· FitBlockTriangle（180 kg，冻结旋转）\n" +
                      "· FitBlockRound（260 kg，允许滚动）\n" +
                      "· FitSocketTriangle（三角槽位，与件同形同大）\n" +
                      "· FitSocketRound（圆槽位，与件同形同大）\n" +
                      "· FitPuzzle（机关：两块都合体 → 按一次按钮 / 开门）\n" +
                      "用法：石块拖进关卡；凹位拖到「座位中心」，缺口朝向与件的推入方向对齐。\n" +
                      "⚠️ 凹位石板只是**外观 + 触发盒**，没有实体碰撞 —— 墙上的凹腔要靠关卡那面墙自己切。");
            return triangle;
        }

        // ------------------------------------------------------------------ 场景

        /// <summary>在**附加场景**里搭试验台，存成 Test_FitSocket.unity，不碰当前场景。</summary>
        public static void BuildScene()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene temp = default;

            try
            {
                temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(temp);
            }
            catch (System.InvalidOperationException)
            {
                Debug.LogError("[地图组件] 当前场景没保存过，无法附加临时场景。先 Ctrl+S 再来一次 —— " +
                               "本工具**不会**往你正开着的关卡里塞东西。");
                return;
            }

            try
            {
                BuildSceneContents();
                EditorSceneManager.SaveScene(temp, ScenePath);
                Debug.Log("[地图组件] 试验场景已生成：" + ScenePath + "\n打开它 → Play → Console 会自己跑一遍冒烟测试。");
            }
            finally
            {
                if (temp.IsValid()) EditorSceneManager.CloseScene(temp, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static void BuildSceneContents()
        {
            int layer = LayerMask.NameToLayer(SolidLayerName);
            if (layer < 0)
            {
                Debug.LogError("[地图组件] 工程里没有 " + SolidLayerName + " 层，件放下去谁也碰不到。");
                return;
            }

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.4f;
            camera.transform.position = new Vector3(6.3f, 2.4f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.11f, 0.12f, 0.14f, 1f);

            // 灯只做「场景里有个主光」的交代：2D 白盒用的是 Sprite 默认 shader，不靠它照。
            // ⚠️ 位置必须离开原点 —— 灯图标会画在灯的世界坐标上，压在画面里很碍事。
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            lightGo.transform.position = new Vector3(6.3f, 18f, -10f);

            var level = new GameObject("Level").transform;

            // --- 地面（井口两侧）+ 井
            Solid(level, "Floor_Left", new Vector2(PitLeft * 0.5f, -0.2f), new Vector2(PitLeft, 0.4f), FloorColor, layer);
            Solid(level, "Floor_Right", new Vector2((RoomRight + PitRight) * 0.5f, -0.2f), new Vector2(RoomRight - PitRight, 0.4f), FloorColor, layer);
            Solid(level, "Pit_Left", new Vector2(PitLeft - 0.1f, (PitBottom - 0.4f) * 0.5f), new Vector2(0.2f, 0.4f - PitBottom), FloorColor, layer);
            Solid(level, "Pit_Right", new Vector2(PitRight + 0.1f, (PitBottom - 0.4f) * 0.5f), new Vector2(0.2f, 0.4f - PitBottom), FloorColor, layer);
            Solid(level, "Pit_Bottom", new Vector2(ShaftX, PitBottom - 0.1f), new Vector2(PitRight - PitLeft + 0.4f, 0.2f), FloorColor, layer);

            // --- 天花板 + 圆窝（一圈小盒拼出半圆内壁）
            Solid(level, "Ceil_Left", new Vector2(PitLeft * 0.5f, (CeilingY + CeilingTop) * 0.5f), new Vector2(PitLeft, CeilingTop - CeilingY), WallColor, layer);
            Solid(level, "Ceil_Right", new Vector2((RoomRight + PitRight) * 0.5f, (CeilingY + CeilingTop) * 0.5f), new Vector2(RoomRight - PitRight, CeilingTop - CeilingY), WallColor, layer);
            Solid(level, "Ceil_Top", new Vector2(ShaftX, (CeilingY + DomeRadius + CeilingTop) * 0.5f), new Vector2(PitRight - PitLeft, CeilingTop - CeilingY - DomeRadius), WallColor, layer);

            // 圆窝内壁：一圈小盒。**画在天花板下面（order -1）** ——
            // 否则盒子之间的抗锯齿接缝会盖在天花板上，看起来像一圈锯齿。
            const int segments = 9;
            const float halfThickness = 0.12f;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(12f, 168f, i / (float)segments) * Mathf.Deg2Rad;
                Vector2 center = new Vector2(
                    ShaftX + (DomeRadius + halfThickness) * Mathf.Cos(angle),
                    CeilingY + (DomeRadius + halfThickness) * Mathf.Sin(angle));
                Solid(level, "Dome_" + i, center, new Vector2(0.42f, halfThickness * 2f), WallColor, layer,
                    angle + 90f, -1);
            }

            // --- 斜坡（18°，球在上面靠静摩擦停住）
            Vector2 rampA = new Vector2(10.9f, 0.70f);
            Vector2 rampB = new Vector2(9.2f, 0.06f);
            Vector2 rampMid = (rampA + rampB) * 0.5f;
            float rampLength = Vector2.Distance(rampA, rampB);
            float rampAngle = Mathf.Atan2(rampA.y - rampB.y, rampA.x - rampB.x) * Mathf.Rad2Deg;
            Solid(level, "Ramp", rampMid + new Vector2(0f, -0.08f), new Vector2(rampLength, 0.16f), WallColor, layer, rampAngle);

            // --- 右墙
            Solid(level, "Wall_Right", new Vector2(RoomRight + 0.2f, CeilingY * 0.5f), new Vector2(0.4f, CeilingY), WallColor, layer);

            // --- 竖井里的跳穿平台：球必须能**从下面穿上来**（这是 OneWayPlatform 的验收点）
            var platformGo = new GameObject("OneWayPlatform_Shaft");
            platformGo.layer = layer;
            platformGo.transform.position = new Vector3(ShaftX, 2.6f, 0f);
            var platformBox = platformGo.AddComponent<BoxCollider2D>();
            platformBox.size = new Vector2(1.9f, 0.28f);
            platformGo.AddComponent<OneWayPlatform>();
            Quad(platformGo.transform, "Art", Vector2.zero, new Vector2(1.9f, 0.28f), PlatformColor, 1,
                LoadSprite(WhiteboxPath, WhiteboxPath));

            // --- 闸门（左下角切出三角凹位；开口朝右）
            var gate = new GameObject("Gate");
            gate.layer = layer;
            var gatePoly = gate.AddComponent<PolygonCollider2D>();
            gatePoly.SetPath(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(PocketBaseLeft, 0f),
                new Vector2(TriangleSeat.x, PocketApex),
                new Vector2(TriangleSeat.x, PocketCeiling),
                new Vector2(PocketRight, PocketCeiling),
                new Vector2(GateRight, PocketCeiling),
                new Vector2(GateRight, GateTop),
                new Vector2(0f, GateTop),
            });

            Quad(gate.transform, "Art_Column", new Vector2(PocketBaseLeft * 0.5f + 0.1f, GateTop * 0.5f), new Vector2(PocketBaseLeft + 0.2f, GateTop), GateColor, 0, LoadSprite(WhiteboxPath, WhiteboxPath));
            Quad(gate.transform, "Art_Upper", new Vector2((PocketBaseLeft + GateRight) * 0.5f, (PocketCeiling + GateTop) * 0.5f), new Vector2(GateRight - PocketBaseLeft, GateTop - PocketCeiling), GateColor, 0, LoadSprite(WhiteboxPath, WhiteboxPath));
            Quad(gate.transform, "Art_Slope", new Vector2((PocketBaseLeft + TriangleSeat.x) * 0.5f - 0.06f, PocketApex * 0.5f + 0.03f), new Vector2(Vector2.Distance(new Vector2(PocketBaseLeft, 0f), new Vector2(TriangleSeat.x, PocketApex)), 0.2f), GateColor, 0, LoadSprite(WhiteboxPath, WhiteboxPath), 66.6f);

            // --- 闸门上方那截固定墙（闸门升进去，视觉上被它挡住）
            Solid(level, "Wall_AboveGate", new Vector2(GateRight * 0.5f, (GateTop + CeilingY) * 0.5f), new Vector2(GateRight, CeilingY - GateTop), WallColor, layer);

            // --- 两块件
            GameObject triangle = InstantiatePrefab(TrianglePrefabPath);
            if (triangle != null) triangle.transform.position = new Vector3(TriangleStart.x, TriangleStart.y, 0f);
            GameObject ball = InstantiatePrefab(RoundPrefabPath);
            if (ball != null) ball.transform.position = new Vector3(BallStart.x, BallStart.y, 0f);

            FitBlock triangleBlock = triangle != null ? triangle.GetComponent<FitBlock>() : null;
            FitBlock ballBlock = ball != null ? ball.GetComponent<FitBlock>() : null;

            // --- 两个凹位（空物体摆在座位中心，FitSocket 自己补 Trigger）
            FitSocket triangleSocket = SpawnSocket(TriangleSocketPrefabPath, "Socket_Triangle", TriangleSeat,
                FitShape.Triangle, new Vector2(0.45f, 0.45f), gate.transform);
            FitSocket ballSocket = SpawnSocket(RoundSocketPrefabPath, "Socket_Round", BallSeat,
                FitShape.Round, new Vector2(0.45f, 0.45f), null);

            // --- 指示灯
            GameObject lampTri = MakeLamp("Lamp_Triangle", new Vector2(3.0f, 3.55f), new Color(0.96f, 0.68f, 0.33f, 1f));
            GameObject lampBall = MakeLamp("Lamp_Round", new Vector2(3.5f, 3.55f), new Color(0.31f, 0.82f, 0.77f, 1f));

            // --- 机关
            var puzzleGo = new GameObject("Puzzle");
            var puzzle = puzzleGo.AddComponent<FitPuzzle>();
            puzzle.Configure(new[] { triangleSocket, ballSocket }, gate.transform, new[] { lampTri, lampBall }, 2f);

            // --- 冒烟测试
            var testGo = new GameObject("SmokeTest");
            var test = testGo.AddComponent<FitPuzzleSmokeTest>();
            test.Configure(triangleBlock, ballBlock, triangleSocket, ballSocket, puzzle, gate.transform);
        }

        // ------------------------------------------------------------------ 小工具

        /// <summary>把凹位预制体摆到座位中心（在闸门上就挂成子物体，跟着门一起升）。</summary>
        private static FitSocket SpawnSocket(string path, string name, Vector2 position, FitShape shape,
                                             Vector2 tolerance, Transform seatParent)
        {
            GameObject instance = InstantiatePrefab(path);
            if (instance == null) return null;

            instance.name = name;
            if (seatParent != null) instance.transform.SetParent(seatParent, true);
            instance.transform.position = new Vector3(position.x, position.y, 0f);

            var socket = instance.GetComponent<FitSocket>();
            if (socket != null) socket.Configure(shape, tolerance, seatParent);
            return socket;
        }

        private static GameObject MakeLamp(string name, Vector2 position, Color color)
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(position.x, position.y, 0f);
            Quad(go.transform, "Art", Vector2.zero, Vector2.one * 0.3f, color, 2, LoadSprite(WhiteboxPath, WhiteboxPath));
            go.SetActive(false);
            return go;
        }

        private static void BuildBody(GameObject root, FitShape shape, float mass, PhysicsMaterial2D material, bool freeRotation)
        {
            int layer = LayerMask.NameToLayer(SolidLayerName);
            if (layer >= 0) root.layer = layer;

            var body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Dynamic;
            body.mass = mass;
            body.gravityScale = 1f;
            body.constraints = freeRotation ? RigidbodyConstraints2D.None : RigidbodyConstraints2D.FreezeRotation;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.angularDamping = freeRotation ? 2.5f : 0f;

            root.AddComponent<RuleAffectedBody>();
            var block = root.AddComponent<FitBlock>();
            block.ConfigureShape(shape);
        }

        /// <summary>凹位：触发盒 + FitSocket + 一块「方形石板 + 空缺」的外观。</summary>
        private static void BuildSocket(GameObject root, FitShape shape, Vector2 tolerance, Vector2 plateSize, Sprite plate)
        {
            var box = root.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = tolerance * 2f;
            box.offset = Vector2.zero;

            var socket = root.AddComponent<FitSocket>();
            socket.Configure(shape, tolerance);

            // 石板只是外观 + 触发盒，**没有实体碰撞** —— 墙上那道凹腔由关卡自己切（见文档 §6）。
            Quad(root.transform, "Art", Vector2.zero, plateSize, Color.white, PlateOrder, plate);
        }

        private static GameObject SavePrefab(string path, System.Action<GameObject> build)
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene temp = default;
            GameObject root = null;

            try
            {
                temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(temp);
            }
            catch (System.InvalidOperationException)
            {
                temp = default;
            }

            try
            {
                root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
                if (temp.IsValid()) SceneManager.MoveGameObjectToScene(root, temp);

                build(root);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally
            {
                if (temp.IsValid()) EditorSceneManager.CloseScene(temp, true);
                else if (root != null) Object.DestroyImmediate(root);

                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static GameObject InstantiatePrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning("[地图组件] 找不到预制体：" + path + "（先跑一次 ⑮a）");
                return null;
            }

            return PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        }

        private static Sprite LoadSprite(string path, string fallback)
        {
            Sprite sprite = EnsureSprite(path);
            if (sprite != null) return sprite;

            if (path != fallback) Debug.LogWarning("[地图组件] 找不到外观 " + path + "，改用白盒方块。");
            return EnsureSprite(fallback);
        }

        /// <summary>把一张 PNG 强制导成 Sprite（工程里新加的图可能还是 Default 类型）。</summary>
        private static Sprite EnsureSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;

            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return null;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void Solid(Transform parent, string name, Vector2 center, Vector2 size, Color color, int layer,
                                  float angleDeg = 0f, int order = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(center.x, center.y, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
            go.layer = layer;

            var box = go.AddComponent<BoxCollider2D>();
            box.size = size;

            Quad(go.transform, "Art", Vector2.zero, size, color, order, LoadSprite(WhiteboxPath, WhiteboxPath));
        }

        private static SpriteRenderer Quad(Transform parent, string name, Vector2 center, Vector2 size, Color color,
                                           int order, Sprite sprite, float angleDeg = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(center.x, center.y, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
            go.transform.localScale = Vector3.one;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;

            if (sprite != null)
            {
                Vector2 native = sprite.bounds.size;
                if (native.x < 0.001f) native.x = 1f;
                if (native.y < 0.001f) native.y = 1f;
                go.transform.localScale = new Vector3(size.x / native.x, size.y / native.y, 1f);
            }

            return renderer;
        }
    }
}
