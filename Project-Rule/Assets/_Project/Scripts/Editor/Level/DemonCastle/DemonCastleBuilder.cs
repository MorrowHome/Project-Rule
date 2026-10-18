using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuleGame.Gameplay.Common;
using RuleGame.Gameplay.Scene;
using RuleGame.Gameplay.Player;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Gameplay.Scene.DemonCastle;
using RuleGame.Development.DemonCastle;

namespace RuleGame.Editor.Level
{
    /// <summary>生成独立组件资产。已有 prefab 保留设计师改动，只补缺失资产。</summary>
    public static class DemonCastleBuilder
    {
        public const string Folder = "Assets/_Project/Prefabs/DemonCastle";
        public const string ScenePath = "Assets/_Project/Scenes/Test_DemonCastleComponents.unity";
        private const string ArtFolder = "Assets/_Project/Art/Sprites/DemonCastle";
        public static readonly string[] Names =
        {
            "DemonGround", "DemonPlatformSmall", "DemonPlatformLong", "DemonPlatformSquare",
            "DemonFirework", "DemonFireworkSwitch", "DemonSpring", "DemonRustBlock",
            "DemonStalactiteShort", "DemonStalactiteMedium", "DemonStalactiteLong", "DemonFlipPlatform",
            "DemonWindingHorizontal", "DemonWindingVertical", "DemonSlidingPuzzle", "DemonPuzzleRoom",
            "DemonCarousel", "DemonPianoWhite", "DemonPianoBlack", "DemonPianoRoom", "DemonSkateboard",
            "DemonGround02", "DemonGround03", "DemonGround04", "DemonPlatformMedium",
            "DemonPillar01", "DemonPillar02", "DemonPillar03", "DemonPillar04", "DemonPillar05", "DemonPillar06", "DemonYoyoVisual"
        };
        public static readonly string[] Labels =
        {
            "魔王城地面·平铺", "岩石平台·短", "岩石平台·长", "方块平台（占位）",
            "烟花箱", "烟花箱＋踩踏开关", "跳跃弹簧", "生锈铁块",
            "钟乳石·短", "钟乳石·中", "钟乳石·长", "翻转平台·粗糙／冰面",
            "横向发条", "纵向发条", "八块滑动拼图", "拼图房·发条＋门",
            "旋转木马·升降", "钢琴白键", "钢琴黑键", "钢琴房·琴键＋门", "滑板车·往返平台",
            "地块·右残缺", "地块·左残缺", "地块·两侧残缺", "岩石平台·中",
            "立柱·完整", "立柱·底座", "立柱·破损顶端", "立柱·短柱", "立柱·上段", "立柱·中段", "溜溜球·双帧美术"
        };
        private static Sprite _square, _circle;
        private static Material _material;
        private static PhysicsMaterial2D _rough, _ice, _rust;

        [MenuItem("Tools/规则游戏/地图组件/魔王城/补齐组件预制体", false, 20)]
        public static void BuildPrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play。");
            Directory.CreateDirectory(Folder); Directory.CreateDirectory(ArtFolder); AssetDatabase.Refresh();
            _square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/P4WhiteboxSquare.png");
            _circle = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Sprites/WhiteBoxCircle.png");
            _material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Data/Enemies/EnemySprite_Mat.mat");
            if (_square == null || _circle == null) throw new InvalidOperationException("缺少项目白盒 Sprite。");
            _rough = Material("DemonRough", 1.6f); _ice = Material("DemonIce", .02f); _rust = Material("DemonRust", .7f);
            Make("DemonGround", () => Block("DemonGround", new Vector2(8f, 1f), new Color(.2f, .14f, .27f), true));
            Make("DemonPlatformSmall", () => Block("DemonPlatformSmall", new Vector2(2f, .4f), new Color(.3f, .24f, .39f)));
            Make("DemonPlatformLong", () => Block("DemonPlatformLong", new Vector2(4f, .4f), new Color(.3f, .24f, .39f)));
            Make("DemonPlatformSquare", () => Block("DemonPlatformSquare", Vector2.one * 2f, new Color(.3f, .24f, .39f)));
            Make("DemonFirework", Firework);
            Make("DemonFireworkSwitch", FireworkSwitch);
            Make("DemonSpring", Spring);
            Make("DemonRustBlock", RustBlock);
            Make("DemonStalactiteShort", () => Stalactite("DemonStalactiteShort", .9f));
            Make("DemonStalactiteMedium", () => Stalactite("DemonStalactiteMedium", 1.6f));
            Make("DemonStalactiteLong", () => Stalactite("DemonStalactiteLong", 2.4f));
            Make("DemonFlipPlatform", Flip);
            Make("DemonSlidingPuzzle", Puzzle);
            Make("DemonWindingHorizontal", () => Winding(false));
            Make("DemonWindingVertical", () => Winding(true));
            Make("DemonPuzzleRoom", PuzzleRoom);
            Make("DemonCarousel", Carousel);
            Make("DemonPianoWhite", () => Piano(false));
            Make("DemonPianoBlack", () => Piano(true));
            Make("DemonPianoRoom", PianoRoom);
            Make("DemonSkateboard", Skateboard);
            DemonCastleArtSetup.Apply();
            AssetDatabase.SaveAssets();
            Debug.Log("[魔王城] 组件、美术预制体已就绪；已有机关参数保留。");
        }
        private static PhysicsMaterial2D Material(string name, float friction)
        {
            string path = Folder + "/" + name + ".physicsMaterial2D";
            var asset = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(path);
            if (asset != null) return asset;
            var source = Resources.Load<PhysicsMaterial2D>("RuleDefaultMaterial2D");
            if (source == null) throw new InvalidOperationException("缺少默认 PhysicsMaterial2D 模板。");
            asset = UnityEngine.Object.Instantiate(source); asset.name = name; asset.friction = friction; asset.bounciness = 0f;
            AssetDatabase.CreateAsset(asset, path); return asset;
        }
        private static void Make(string name, Func<GameObject> create)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + name + ".prefab") != null) return;
            var go = create(); go.name = name;
            try { PrefabUtility.SaveAsPrefabAsset(go, Folder + "/" + name + ".prefab"); }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
        private static GameObject Shape(string name, Vector2 size, Color color, bool trigger = false, int layer = 8)
        {
            var go = new GameObject(name); go.layer = layer;
            var box = go.AddComponent<BoxCollider2D>(); box.size = size; box.isTrigger = trigger; box.sharedMaterial = _rough;
            Visual(go.transform, "Visual", size, Vector2.zero, color);
            return go;
        }
        private static Transform Visual(Transform parent, string name, Vector2 size, Vector2 at, Color color, bool circle = false)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = at;
            var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = circle ? _circle : _square; sr.color = color;
            sr.sharedMaterial = _material; sr.sortingOrder = name == "Visual" ? -1 : 0;
            go.transform.localScale = new Vector3(size.x / sr.sprite.bounds.size.x, size.y / sr.sprite.bounds.size.y, 1f);
            return go.transform;
        }
        private static void Label(Transform parent, string text, Vector2 at, float size = .11f)
        {
            var go = new GameObject("Label"); go.transform.SetParent(parent, false); go.transform.localPosition = at;
            var tm = go.AddComponent<TextMesh>(); tm.text = text; tm.fontSize = 64; tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter; tm.color = new Color(.9f, .83f, .99f);
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
            go.GetComponent<MeshRenderer>().sortingOrder = 2;
        }
        private static GameObject Block(string name, Vector2 size, Color color, bool stone = false)
        {
            var go = Shape(name, size, color);
            if (stone)
            {
                var sr = go.transform.Find("Visual").GetComponent<SpriteRenderer>();
                var sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Sprites/Tiles/Stone/Ground_Stone_Slab_04.png").OfType<Sprite>().FirstOrDefault();
                if (sprite != null)
                {
                    sr.sprite = sprite; sr.transform.localScale = Vector3.one; sr.drawMode = SpriteDrawMode.Tiled; sr.size = size;
                    Vector2 pivot = sprite.pivot / sprite.rect.size;
                    sr.transform.localPosition = (pivot - Vector2.one * .5f) * size;
                }
            }
            else
            {
                Visual(go.transform, "MetalRim", new Vector2(size.x, .08f), new Vector2(0f, size.y * .5f - .04f), new Color(.53f, .4f, .64f));
                for (int i = -1; i <= 1; i += 2)
                    Visual(go.transform, "Rivet", Vector2.one * .12f, new Vector2(i * (size.x * .5f - .18f), 0f), new Color(.7f, .57f, .3f), true);
            }
            return go;
        }
        private static void NoBodyFriction(GameObject go)
        {
            var data = new SerializedObject(go.GetComponent<RuleAffectedBody>());
            data.FindProperty("_applyFriction").boolValue = false; data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static GameObject Firework()
        {
            var go = Shape("DemonFirework", new Vector2(1.2f, 1f), new Color(.4f, .15f, .3f), true, 0);
            var firework = go.AddComponent<DemonFirework>();
            for (int i = 0; i < 3; i++)
                Visual(go.transform, "Tube", new Vector2(.25f, .25f), new Vector2((i - 1) * .35f, .5f), new Color(.8f, .45f, .25f), true);
            Visual(go.transform, "WarningBand", new Vector2(1.2f, .12f), Vector2.zero, new Color(.9f, .6f, .2f));
            var burst = new GameObject("Burst"); burst.transform.SetParent(go.transform, false);
            var ps = burst.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.loop = false; main.duration = .8f;
            main.startLifetime = .65f; main.startSpeed = 5f; main.startSize = .1f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .25f, .55f), new Color(1f, .8f, .25f));
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 64;
            var emission = ps.emission; emission.rateOverTime = 0f; emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 48) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = .12f;
            burst.GetComponent<ParticleSystemRenderer>().sharedMaterial = _material;
            firework.Burst = ps;
            return go;
        }
        private static GameObject FireworkSwitch()
        {
            var root = new GameObject("DemonFireworkSwitch");
            var bomb = Nested("DemonFirework", root.transform, new Vector2(2.4f, .3f)).GetComponent<DemonFirework>();
            var button = Shape("FireworkButton", new Vector2(1f, .2f), new Color(.7f, .25f, .2f), true, 0);
            button.transform.SetParent(root.transform, false);
            var control = button.AddComponent<CitySwitch>(); control.Toggle = false; control.PressByPlayerProximity = true;
            control.FollowMotor = bomb;
            return root;
        }
        private static GameObject Spring()
        {
            var go = Shape("DemonSpring", new Vector2(1.8f, .3f), new Color(.6f, .4f, .7f));
            var spring = go.AddComponent<DemonSpringPlatform>();
            var coil = Visual(go.transform, "Coil", new Vector2(.35f, 1f), new Vector2(0f, -.65f), new Color(.65f, .56f, .4f));
            // 标准 1×1 Sprite，长度由机关按压缩量更新。
            spring.Coil = coil;
            Visual(coil, "Base", new Vector2(1.8f / .35f, .18f), new Vector2(0f, -.5f), new Color(.36f, .3f, .45f));
            return go;
        }
        private static GameObject RustBlock()
        {
            var go = Shape("DemonRustBlock", Vector2.one * 1.5f, new Color(.48f, .29f, .21f));
            go.GetComponent<BoxCollider2D>().edgeRadius = .07f; go.GetComponent<BoxCollider2D>().sharedMaterial = _rust;
            var body = go.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Dynamic; body.mass = 16f;
            body.gravityScale = 1f; body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            go.AddComponent<RuleAffectedBody>(); go.AddComponent<PushableBody>();
            for (int i = 0; i < 5; i++) Visual(go.transform, "Rust", Vector2.one * .17f,
                new Vector2((i % 3 - 1) * .4f, (i / 3f - .6f) * .5f), new Color(.68f, .35f, .17f), true);
            return go;
        }
        private static GameObject Stalactite(string name, float height)
        {
            var go = Shape(name, new Vector2(.65f, height), new Color(.45f, .36f, .55f), true, 0);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<BoxCollider2D>());
            var poly = go.AddComponent<PolygonCollider2D>(); poly.points = new[]
            { new Vector2(-.4f, height * .5f), new Vector2(.4f, height * .5f), new Vector2(.06f, -height * .5f) };
            poly.isTrigger = true;
            var visual = go.transform.Find("Visual");
            // 用同一三角网格生成精灵，视觉与命中范围一致。
            string path = ArtFolder + "/Stalactite.png";
            if (!File.Exists(path))
            {
                var tex = new Texture2D(32, 96, TextureFormat.RGBA32, false);
                for (int y = 0; y < 96; y++) for (int x = 0; x < 32; x++)
                    tex.SetPixel(x, y, Mathf.Abs(x - 16f) < (y + 1f) / 6f ? new Color(.7f + x * .007f, .7f + x * .007f, .75f + x * .007f) : Color.clear);
                tex.Apply(); File.WriteAllBytes(path, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite; importer.spritePixelsPerUnit = 32; importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            var sr = visual.GetComponent<SpriteRenderer>(); sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            visual.localScale = new Vector3(.8f / sr.sprite.bounds.size.x, height / sr.sprite.bounds.size.y, 1f);
            go.AddComponent<DemonStalactite>(); return go;
        }
        private static GameObject Flip()
        {
            var go = Shape("DemonFlipPlatform", new Vector2(3f, .4f), new Color(.22f, .2f, .3f));
            var flip = go.AddComponent<DemonFlipPlatform>(); flip.RoughMaterial = _rough; flip.IceMaterial = _ice;
            NoBodyFriction(go);
            flip.Face = new GameObject("Face").transform; flip.Face.SetParent(go.transform, false);
            Visual(flip.Face, "RoughSide", new Vector2(3f, .17f), Vector2.up * .115f, new Color(.85f, .27f, .3f));
            Visual(flip.Face, "IceSide", new Vector2(3f, .17f), Vector2.down * .115f, new Color(.28f, .65f, .95f));
            Visual(flip.Face, "Axle", Vector2.one * .28f, Vector2.zero, new Color(.9f, .75f, .3f), true);
            return go;
        }
        private static GameObject Winding(bool vertical)
        {
            var go = Shape(vertical ? "DemonWindingVertical" : "DemonWindingHorizontal", new Vector2(1.6f, .3f), new Color(.5f, .4f, .27f));
            var key = go.AddComponent<DemonWindingKey>(); key.Vertical = vertical;
            key.KeyVisual = Visual(go.transform, "Key", new Vector2(.9f, .35f), new Vector2(0f, .3f), new Color(.8f, .66f, .3f));
            Label(go.transform, vertical ? "V" : "H", new Vector2(0f, .7f)); return go;
        }
        private static GameObject Puzzle()
        {
            var go = Shape("DemonSlidingPuzzle", new Vector2(3.6f, 3.6f), new Color(.1f, .07f, .17f), true, 0);
            var puzzle = go.AddComponent<DemonSlidingPuzzle>();
            for (int i = 0; i < 8; i++)
            {
                var tile = new GameObject("Tile" + (i + 1)); tile.transform.SetParent(go.transform, false);
                tile.transform.localPosition = new Vector3((i % 3 - 1) * puzzle.CellSize, (1 - i / 3) * puzzle.CellSize);
                puzzle.Tiles[i] = tile.transform;
                var face = Visual(tile.transform, "Visual", Vector2.one * 1.02f, Vector2.zero, new Color(.28f + i % 3 * .08f, .18f, .4f + i / 3 * .08f));
                face.GetComponent<SpriteRenderer>().sortingOrder = 0;
                Label(tile.transform, (i + 1).ToString(), Vector2.zero, .12f);
            }
            return go;
        }
        private static GameObject PuzzleRoom()
        {
            var go = new GameObject("DemonPuzzleRoom");
            var puzzle = Nested("DemonSlidingPuzzle", go.transform, new Vector2(0f, 3f)).GetComponent<DemonSlidingPuzzle>();
            Nested("DemonWindingHorizontal", go.transform, new Vector2(-1.8f, .2f)).GetComponent<DemonWindingKey>().Puzzle = puzzle;
            Nested("DemonWindingVertical", go.transform, new Vector2(1.8f, .2f)).GetComponent<DemonWindingKey>().Puzzle = puzzle;
            var gate = Gate(go.transform, new Vector2(4.2f, 1.5f));
            UnityEventTools.AddPersistentListener(puzzle.OnSolved, gate.SetOn);
            return go;
        }
        private static GameObject Carousel()
        {
            var go = Block("DemonCarousel", new Vector2(2.6f, .4f), new Color(.55f, .3f, .55f));
            var motor = go.AddComponent<CityPathMotor>(); motor.EndOffset = Vector2.up * 3f;
            motor.Speed = 1.5f; motor.AutoReturn = true; motor.TargetEnd = true; motor.StationWait = .5f;
            Visual(go.transform, "Pole", new Vector2(.1f, 3.2f), new Vector2(0f, .9f), new Color(.7f, .55f, .25f));
            Visual(go.transform, "HorseBody", new Vector2(1.1f, .55f), new Vector2(0f, .55f), new Color(.75f, .55f, .7f), true);
            Visual(go.transform, "HorseHead", new Vector2(.35f, .55f), new Vector2(.5f, .85f), new Color(.75f, .55f, .7f), true);
            return go;
        }
        private static GameObject Skateboard()
        {
            var go = Block("DemonSkateboard", new Vector2(2.5f, .3f), new Color(.5f, .3f, .6f));
            var motor = go.AddComponent<CityPathMotor>(); motor.EndOffset = Vector2.right * 5f;
            motor.Speed = 2f; motor.AutoReturn = true; motor.TargetEnd = true;
            for (int i = -1; i <= 1; i += 2) Visual(go.transform, "Wheel", Vector2.one * .3f, new Vector2(i * .8f, -.2f), new Color(.3f, .3f, .35f), true);
            return go;
        }
        private static GameObject Piano(bool black)
        {
            var go = Shape(black ? "DemonPianoBlack" : "DemonPianoWhite", new Vector2(1.2f, .25f), black ? new Color(.12f, .1f, .18f) : new Color(.9f, .86f, .95f));
            var key = go.AddComponent<DemonPianoKey>(); key.Note = black ? 1 : 0; key.OnStep = true;
            key.KeyVisual = go.transform.Find("Visual");
            key.Audio = go.AddComponent<AudioSource>(); key.Audio.playOnAwake = false; key.Audio.spatialBlend = .35f; key.Audio.volume = .35f;
            string path = ArtFolder + (black ? "/PianoSharp.wav" : "/PianoC.wav");
            if (!File.Exists(path))
            {
                const int rate = 22050, samples = 11025;
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                    writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                    for (int i = 0; i < samples; i++)
                    {
                        float t = (float)i / rate, frequency = black ? 277.18f : 261.63f;
                        float envelope = Mathf.Min(1f, t * 180f) * Mathf.Exp(-8f * t) * Mathf.Min(1f, (samples - i) / 300f);
                        float sound = Mathf.Sin(2f * Mathf.PI * frequency * t) + .22f * Mathf.Sin(4f * Mathf.PI * frequency * t);
                        writer.Write((short)(sound * envelope * 18000f));
                    }
                }
                AssetDatabase.ImportAsset(path);
            }
            key.Clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            return go;
        }
        private static GameObject PianoRoom()
        {
            var go = new GameObject("DemonPianoRoom");
            var controller = Shape("MelodyLock", new Vector2(.2f, .2f), Color.clear, true, 0);
            controller.transform.SetParent(go.transform, false);
            var melody = controller.AddComponent<DemonMelodyLock>();
            Nested("DemonPianoWhite", go.transform, new Vector2(-.8f, .15f)).GetComponent<DemonPianoKey>().Melody = melody;
            Nested("DemonPianoBlack", go.transform, new Vector2(.8f, .15f)).GetComponent<DemonPianoKey>().Melody = melody;
            var gate = Gate(go.transform, new Vector2(3.5f, 1.5f));
            UnityEventTools.AddPersistentListener(melody.OnUnlocked, gate.SetOn);
            Label(go.transform, "C  #  C  C  #", new Vector2(0f, 2f), .08f);
            return go;
        }
        private static CityPathMotor Gate(Transform parent, Vector2 position)
        {
            var go = Block("PuzzleGate", new Vector2(.5f, 3f), new Color(.4f, .27f, .5f));
            go.transform.SetParent(parent, false); go.transform.localPosition = position;
            var gate = go.AddComponent<CityPathMotor>(); gate.EndOffset = Vector2.up * 3.5f;
            gate.CarryRiders = false; gate.BlockActors = true; return gate;
        }
        private static GameObject Nested(string name, Transform parent, Vector2 at)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + name + ".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.transform.SetParent(parent, false); go.transform.localPosition = at; return go;
        }
        [MenuItem("Tools/规则游戏/地图组件/魔王城/生成测试场景", false, 21)]
        public static void BuildDemo() => BuildTestScene(false);
        [MenuItem("Tools/规则游戏/地图组件/魔王城/重建测试场景（重置布局）", false, 23)]
        public static void RebuildDemo() => BuildTestScene(true);
        private static void BuildTestScene(bool rebuild)
        {
            BuildPrefabs();
            if (!rebuild && !Application.isBatchMode && File.Exists(ScenePath))
            {
                Debug.Log("[魔王城] 演示场景已存在，保留编辑内容；可从菜单打开。"); return;
            }
            var previous = SceneManager.GetActiveScene();
            bool additive = !string.IsNullOrEmpty(previous.path) && previous.path != ScenePath;
            if (!additive && !Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, additive ? NewSceneMode.Additive : NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            try
            {
                var runtime = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/LevelRuntime.prefab"));
                var player = runtime.GetComponentInChildren<PlayerController>(true);
                player.transform.position = new Vector3(-3f, 1f);
                foreach (var component in runtime.GetComponentsInChildren<MonoBehaviour>(true))
                    if (component.GetType().Name == "P3DebugOverlay" || component.GetType().Name == "RuleSystemTestInput") component.enabled = false;
                var camera = runtime.GetComponentInChildren<Camera>();
                if (camera != null)
                {
                    camera.backgroundColor = new Color(.075f, .06f, .105f);
                    camera.transform.position = player.transform.position + new Vector3(0f, .7f, -10f);
                }
                var controls = new GameObject("DemonCastleLabControls").AddComponent<DemonCastleLabControls>();
                controls.Player = player;
                controls.ChineseFont = AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>("Assets/_Project/UI/Fonts/P4ChineseFont.asset");
                controls.Interactor = player.GetComponent<CityInteractor>() ?? player.gameObject.AddComponent<CityInteractor>();
                controls.PanelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>("Assets/_Project/UI/RuleWheel/RuleWheelPanelSettings.asset");
                var floor = new GameObject("00_Ground").transform;
                for (int i = 0; i < 22; i++) Nested("DemonGround", floor, new Vector2(-4f + 8f * i, -.5f));
                var zones = new Transform[DemonCastleLabControls.Stations.Length];
                for (int i = 0; i < zones.Length; i++) zones[i] = new GameObject("Zone_" + (i + 1).ToString("00") + "_" + new[]
                { "Firework", "Spring", "RustBlock", "Stalactites", "FlipPlatform", "SlidingPuzzle", "Carousel", "Piano", "Skateboard", "Terrain", "Pillars", "YoyoArt" }[i]).transform;
                Nested("DemonFireworkSwitch", zones[0], new Vector2(1f, .2f));
                Nested("DemonSpring", zones[1], new Vector2(14f, 1.2f));
                Nested("DemonPlatformLong", zones[1], new Vector2(17f, 4.5f));
                Nested("DemonRustBlock", zones[2], new Vector2(27f, .8f));
                Nested("DemonStalactiteShort", zones[3], new Vector2(37f, 5f));
                Nested("DemonStalactiteMedium", zones[3], new Vector2(40f, 5.5f));
                Nested("DemonStalactiteLong", zones[3], new Vector2(43f, 6f));
                Nested("DemonFlipPlatform", zones[4], new Vector2(52f, 1.3f));
                Nested("DemonPuzzleRoom", zones[5], new Vector2(65f, .2f));
                Nested("DemonCarousel", zones[6], new Vector2(80f, .6f));
                Nested("DemonPianoRoom", zones[7], new Vector2(94f, .2f));
                Nested("DemonSkateboard", zones[8], new Vector2(103f, 1f));
                Nested("DemonPlatformSmall", zones[9], new Vector2(117f, 1.2f));
                Nested("DemonPlatformLong", zones[9], new Vector2(121f, 2.3f));
                Nested("DemonPlatformMedium", zones[9], new Vector2(126f, 1.3f));
                for (int i = 2; i <= 4; i++)
                    Nested("DemonGround" + i.ToString("00"), zones[9], new Vector2(129f + (i - 2) * 3f, .57f));
                Nested("DemonPlatformSquare", zones[9], new Vector2(138f, 1f));
                for (int i = 1; i <= 6; i++)
                {
                    var pillar = Nested("DemonPillar" + i.ToString("00"), zones[10], Vector2.zero);
                    var shape = pillar.GetComponent<BoxCollider2D>();
                    pillar.transform.localPosition = new Vector2(144f + (i - 1) * 3f, shape.size.y * .5f);
                }
                Nested("DemonYoyoVisual", zones[11], new Vector2(163f, 1.2f));
                Physics2D.SyncTransforms();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (additive) { EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous); }
            }
            Debug.Log("[魔王城] 独立演示场景已生成：" + ScenePath);
        }
        [MenuItem("Tools/规则游戏/地图组件/魔王城/打开测试场景", false, 22)]
        public static void OpenDemo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ScenePath)) BuildDemo();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
        public static void BuildBatch() { BuildDemo(); }
    }
}
