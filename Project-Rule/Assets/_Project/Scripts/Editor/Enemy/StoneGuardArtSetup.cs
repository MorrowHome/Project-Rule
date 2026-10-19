using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using RuleGame.Gameplay.Enemy;
using RuleGame.Presentation.Enemy;

namespace RuleGame.Editor.Enemy
{
    /// <summary>把「石头人」交付帧接到 StoneGuard：逐动作并集裁切 + 脚底轴心 + EnemySpriteAnimator。</summary>
    /// <remarks>
    /// <para>Walk 源目录有 24 张，实际是 12 张唯一帧各存两遍；这里只导入 12 张，按原生 8 fps 播。</para>
    /// <para>图朝左（锤子在左侧），所以所有序列 <c>FacesRight = false</c>。</para>
    /// </remarks>
    public static class StoneGuardArtSetup
    {
        public const string Folder = "Assets/_Project/Art/Sprites/Enemies/StoneGuard";
        public const string PrefabPath = "Assets/_Project/Prefabs/Enemies/FieldMinions/StoneGuard.prefab";
        public const string AnimationSetPath = "Assets/_Project/Data/Enemies/FieldMinions/StoneGuard_Animations.asset";
        private const string MaterialPath = "Assets/_Project/Data/Enemies/EnemySprite_Mat.mat";
        private const float PixelsPerUnit = 194f;

        [MenuItem("RuleGame/Enemies/接入石头人交付美术", false, 61)]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");

            AssetDatabase.Refresh();
            Require("StoneGuard_Idle_01.png");
            Require("StoneGuard_Walk_01.png");
            Require("StoneGuard_Attack_01.png");
            Require("StoneGuard_Dead_01.png");

            Rect idle = AlphaRect("StoneGuard_Idle_01");
            Rect walk = Union(Enumerable.Range(1, 12).Select(i => "StoneGuard_Walk_" + i.ToString("00")).ToArray());
            Rect attack = Union(Enumerable.Range(1, 4).Select(i => "StoneGuard_Attack_" + i.ToString("00")).ToArray());
            Rect dead = Union(Enumerable.Range(1, 7).Select(i => "StoneGuard_Dead_" + i.ToString("00")).ToArray());

            float bodyCenterX = idle.x + idle.width * .5f;
            Import("StoneGuard_Idle_01", idle, bodyCenterX);
            for (int i = 1; i <= 12; i++) Import("StoneGuard_Walk_" + i.ToString("00"), walk, bodyCenterX);
            for (int i = 1; i <= 4; i++) Import("StoneGuard_Attack_" + i.ToString("00"), attack, bodyCenterX);
            for (int i = 1; i <= 7; i++) Import("StoneGuard_Dead_" + i.ToString("00"), dead, bodyCenterX);

            EnemyAnimationSet set = CreateAnimationSet();
            UpdatePrefab(set);
            FitBody();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[StoneGuardArt] 石头人 24 帧已接入 StoneGuard.prefab（Idle 1 / Walk 12 / Attack 4 / Dead 7）。");
        }

        private static EnemyAnimationSet CreateAnimationSet()
        {
            var set = AssetDatabase.LoadAssetAtPath<EnemyAnimationSet>(AnimationSetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<EnemyAnimationSet>();
                AssetDatabase.CreateAsset(set, AnimationSetPath);
            }

            set.Sequences = new[]
            {
                Sequence("Idle", 1, "StoneGuard_Idle_01"),
                Sequence("Walk", 12, "StoneGuard_Walk_"),
                Sequence("Attack", 4, "StoneGuard_Attack_"),
                Sequence("Hurt", 1, "StoneGuard_Idle_01"),
                Sequence("Dead", 7, "StoneGuard_Dead_")
            };
            EditorUtility.SetDirty(set);
            return set;
        }

        private static EnemySpriteSequence Sequence(string action, int count, string prefix)
        {
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
                frames[i] = Load(count == 1 ? prefix : prefix + (i + 1).ToString("00"));
            return new EnemySpriteSequence
            {
                Action = action,
                Frames = frames,
                FrameRate = 8f,
                NativeFrameRate = true,
                FacesRight = false
            };
        }

        private static void UpdatePrefab(EnemyAnimationSet set)
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                var oldBody = root.transform.Find("WhiteboxBody");
                if (oldBody != null)
                {
                    var oldRenderer = oldBody.GetComponent<SpriteRenderer>();
                    if (oldRenderer != null && oldRenderer.sharedMaterial != null) material = oldRenderer.sharedMaterial;
                    UnityEngine.Object.DestroyImmediate(oldBody.gameObject);
                }

                var visual = root.transform.Find("Visual");
                if (visual == null)
                {
                    var go = new GameObject("Visual");
                    go.transform.SetParent(root.transform, false);
                    visual = go.transform;
                }
                var body = root.GetComponent<BoxCollider2D>();
                visual.localPosition = new Vector3(body.offset.x, body.offset.y - body.size.y * .5f, 0f);
                visual.localScale = Vector3.one;

                var renderer = visual.GetComponent<SpriteRenderer>();
                if (renderer == null) renderer = visual.gameObject.AddComponent<SpriteRenderer>();
                renderer.sprite = Load("StoneGuard_Idle_01");
                renderer.color = Color.white;
                renderer.sortingOrder = 5;
                renderer.sharedMaterial = material;

                var view = visual.GetComponent<EnemySpriteAnimator>();
                if (view == null) view = visual.gameObject.AddComponent<EnemySpriteAnimator>();
                view.Actor = root.GetComponent<EnemyActor>();
                view.AnimationSet = set;

                var oldView = root.GetComponent<FieldMinionView>();
                if (oldView != null) UnityEngine.Object.DestroyImmediate(oldView);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>交付图比白盒窄：身体/受击箱收到 1.5×2.4，定义同步；石头人按 8 kg 计，能顶倒三棍支架。</summary>
        private static void FitBody()
        {
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/_Project/Data/Enemies/FieldMinions/StoneGuard_Definition.asset");
            if (definition == null) throw new InvalidOperationException("缺少 StoneGuard_Definition.asset");
            definition.BodySize = new Vector2(1.5f, 2.4f);
            EditorUtility.SetDirty(definition);

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var body = root.GetComponent<BoxCollider2D>();
                if (body != null) body.size = definition.BodySize;
                var rigidbody = root.GetComponent<Rigidbody2D>();
                if (rigidbody != null) rigidbody.mass = 8f;
                var hurtbox = root.transform.Find("Hurtbox");
                if (hurtbox != null)
                {
                    var area = hurtbox.GetComponent<BoxCollider2D>();
                    if (area != null) area.size = definition.BodySize;
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------
        //  导入
        // ------------------------------------------------------------------

        private static void Require(string file)
        {
            if (!File.Exists(Folder + "/" + file))
                throw new InvalidOperationException("缺少交付图：" + Folder + "/" + file);
        }

        private static void Import(string name, Rect rect, float bodyCenterX)
        {
            string path = Folder + "/" + name + ".png";
            Require(name + ".png");
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("无法读取纹理导入器：" + path);

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) throw new InvalidOperationException("该导入器不支持 Sprite 数据：" + path);
            provider.InitSpriteEditorDataProvider();

            var existing = provider.GetSpriteRects().FirstOrDefault(x => x.name == name);
            var pivot = new Vector2((bodyCenterX - rect.x) / rect.width, 0f);
            var sprite = new SpriteRect
            {
                name = name,
                rect = rect,
                alignment = SpriteAlignment.Custom,
                pivot = pivot,
                spriteID = existing != null ? existing.spriteID : GUID.Generate()
            };
            provider.SetSpriteRects(new[] { sprite });

            var ids = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (ids == null) throw new InvalidOperationException("无法维护 Sprite 稳定引用：" + path);
            ids.SetNameFileIdPairs(new[] { new SpriteNameFileIdPair(name, sprite.spriteID) });
            provider.Apply();

            importer.userData = "RuleGame.StoneGuardArt.v1:" + Hash(path);
            importer.SaveAndReimport();
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        private static Sprite Load(string name)
        {
            string path = Folder + "/" + name + ".png";
            var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault(x => x.name == name)
                ?? AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
            if (sprite == null) throw new InvalidOperationException("缺少已导入精灵：" + path);
            return sprite;
        }

        private static Rect Union(params string[] names)
        {
            Rect result = AlphaRect(names[0]);
            for (int i = 1; i < names.Length; i++)
            {
                Rect next = AlphaRect(names[i]);
                result = Rect.MinMaxRect(Mathf.Min(result.xMin, next.xMin), Mathf.Min(result.yMin, next.yMin),
                    Mathf.Max(result.xMax, next.xMax), Mathf.Max(result.yMax, next.yMax));
            }
            return result;
        }

        private static Rect AlphaRect(string name)
        {
            string path = Folder + "/" + name + ".png";
            Require(name + ".png");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("读取 PNG 失败：" + path);
                var pixels = texture.GetPixels32();
                int minX = texture.width, minY = texture.height, maxX = -1, maxY = -1;
                for (int y = 0; y < texture.height; y++)
                    for (int x = 0; x < texture.width; x++)
                    {
                        if (pixels[y * texture.width + x].a == 0) continue;
                        minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
                    }
                if (maxX < minX) throw new InvalidOperationException("图片全透明：" + path);
                return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
