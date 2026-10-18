using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Presentation.DemonCastle;

namespace RuleGame.Editor.Level
{
    /// <summary>从原始画布导入实际 SpriteRect；只更新本批地形外观和按钮，保留机关接线。</summary>
    public static class DemonCastleArtSetup
    {
        public const string Folder = "Assets/_Project/Art/Sprites/DemonCastle/Delivered";
        private const string Revision = "RuleGame.DemonArt.v1:";
        public static bool Available => File.Exists(Folder + "/DemonGround_01.png");
        public static readonly string[] TerrainNames =
        {
            "DemonGround02", "DemonGround03", "DemonGround04", "DemonPlatformMedium",
            "DemonPillar01", "DemonPillar02", "DemonPillar03", "DemonPillar04", "DemonPillar05", "DemonPillar06", "DemonYoyoVisual"
        };
        public static void Apply()
        {
            if (!Available) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play。");
            AssetDatabase.Refresh();
            Rect buttonRect = Union("DemonButton", 2), yoyoRect = Union("DemonYoyo", 2);
            foreach (string path in Directory.GetFiles(Folder, "*.png").OrderBy(x => x))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                Rect rect = name.StartsWith("DemonButton", StringComparison.Ordinal) ? buttonRect :
                    name.StartsWith("DemonYoyo", StringComparison.Ordinal) ? yoyoRect : AlphaRect(path);
                Import(path, rect);
            }
            UpdateGround();
            UpdatePlatform("DemonPlatformSmall", "DemonPlatform_01");
            UpdatePlatform("DemonPlatformLong", "DemonPlatform_02");
            for (int i = 2; i <= 4; i++) CreateTerrain("DemonGround" + i.ToString("00"), "DemonGround_" + i.ToString("00"));
            CreateTerrain("DemonPlatformMedium", "DemonPlatform_03");
            for (int i = 1; i <= 6; i++) CreateTerrain("DemonPillar" + i.ToString("00"), "DemonPillar_" + i.ToString("00"));
            UpdateButton(); CreateYoyo(); AssetDatabase.SaveAssets();
        }
        private static string Signature(string path)
        {
            using (var hash = SHA256.Create()) return Revision + Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
        }
        private static void Import(string path, Rect rect)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("无法读取纹理导入器：" + path);
            string signature = Signature(path);
            if (importer.userData == signature) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 128f;
            importer.filterMode = FilterMode.Bilinear; importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true; importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            var factory = new SpriteDataProviderFactories(); factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) throw new InvalidOperationException("该导入器不支持 Sprite 数据：" + path);
            provider.InitSpriteEditorDataProvider();
            var capability = provider.GetDataProvider<ISpriteFrameEditCapability>();
            if (capability == null) throw new InvalidOperationException("无法检查 Sprite 编辑能力：" + path);
            var allowed = capability.GetEditCapability();
            foreach (var required in new[] { EEditCapability.CreateAndDeleteSprite, EEditCapability.EditSpriteRect, EEditCapability.EditSpriteName, EEditCapability.EditPivot })
                if (!allowed.HasCapability(required)) throw new InvalidOperationException("Sprite 导入器不允许 " + required + "：" + path);
            string name = Path.GetFileNameWithoutExtension(path);
            var existing = provider.GetSpriteRects().FirstOrDefault(x => x.name == name);
            var sprite = new SpriteRect { name = name, rect = rect, alignment = SpriteAlignment.Center, pivot = Vector2.one * .5f,
                spriteID = existing != null ? existing.spriteID : GUID.Generate() };
            provider.SetSpriteRects(new[] { sprite });
            var ids = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (ids == null) throw new InvalidOperationException("无法维护 Sprite 稳定引用：" + path);
            ids.SetNameFileIdPairs(new[] { new SpriteNameFileIdPair(name, sprite.spriteID) });
            provider.Apply(); importer.userData = signature; importer.SaveAndReimport();
        }
        private static Rect Union(string prefix, int count)
        {
            Rect result = AlphaRect(Folder + "/" + prefix + "_01.png");
            for (int i = 2; i <= count; i++)
            {
                Rect next = AlphaRect(Folder + "/" + prefix + "_" + i.ToString("00") + ".png");
                result = Rect.MinMaxRect(Mathf.Min(result.xMin, next.xMin), Mathf.Min(result.yMin, next.yMin), Mathf.Max(result.xMax, next.xMax), Mathf.Max(result.yMax, next.yMax));
            }
            return result;
        }
        private static Rect AlphaRect(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("读取 PNG 失败：" + path);
                var pixels = texture.GetPixels32(); int minX = texture.width, minY = texture.height, maxX = -1, maxY = -1;
                for (int y = 0; y < texture.height; y++) for (int x = 0; x < texture.width; x++)
                {
                    if (pixels[y * texture.width + x].a == 0) continue;
                    minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y); maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
                }
                if (maxX < minX) throw new InvalidOperationException("图片全透明：" + path);
                return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        public static Sprite Load(string name) => AssetDatabase.LoadAllAssetsAtPath(Folder + "/" + name + ".png").OfType<Sprite>().FirstOrDefault()
            ?? throw new InvalidOperationException("缺少已导入精灵：" + name);
        private static void EditPrefab(string name, Action<GameObject> edit)
        {
            string path = DemonCastleBuilder.Folder + "/" + name + ".prefab";
            if (!File.Exists(path)) return;
            var importer = AssetImporter.GetAtPath(path);
            if (importer != null && importer.userData == Revision) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            if (importer != null) { importer.userData = Revision; importer.SaveAndReimport(); }
        }
        private static void RemoveDetails(GameObject root)
        {
            foreach (var child in root.transform.Cast<Transform>().Where(x => x.name == "MetalRim" || x.name == "Rivet").ToArray())
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
        private static void UpdateGround()
        {
            EditPrefab("DemonGround", root =>
            {
                var box = root.GetComponent<BoxCollider2D>(); var visual = root.transform.Find("Visual").GetComponent<SpriteRenderer>();
                visual.sprite = Load("DemonGround_01"); visual.color = Color.white;
                visual.transform.localScale = Vector3.one; visual.drawMode = SpriteDrawMode.Tiled;
                visual.size = new Vector2(box.size.x, visual.sprite.bounds.size.y);
                visual.transform.localPosition = new Vector3(box.offset.x, box.offset.y + box.size.y * .5f - visual.size.y * .5f);
            });
        }
        private static void UpdatePlatform(string name, string spriteName)
        {
            EditPrefab(name, root =>
            {
                RemoveDetails(root);
                var box = root.GetComponent<BoxCollider2D>(); var visual = root.transform.Find("Visual").GetComponent<SpriteRenderer>();
                visual.sprite = Load(spriteName); visual.color = Color.white; visual.drawMode = SpriteDrawMode.Simple;
                float scale = box.size.x / visual.sprite.bounds.size.x;
                visual.transform.localScale = new Vector3(scale, scale, 1f);
                visual.transform.localPosition = new Vector3(box.offset.x, box.offset.y + box.size.y * .5f - visual.sprite.bounds.size.y * scale * .5f);
            });
        }
        private static void CreateTerrain(string name, string spriteName)
        {
            string path = DemonCastleBuilder.Folder + "/" + name + ".prefab";
            if (File.Exists(path)) return;
            var root = new GameObject(name); root.layer = 8;
            try
            {
                var sprite = Load(spriteName);
                root.AddComponent<BoxCollider2D>().size = sprite.bounds.size;
                var visual = new GameObject("Visual"); visual.transform.SetParent(root.transform, false);
                var renderer = visual.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = -1;
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Data/Enemies/EnemySprite_Mat.mat");
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void UpdateButton()
        {
            EditPrefab("DemonFireworkSwitch", root =>
            {
                var button = root.transform.Find("FireworkButton");
                if (button == null) return;
                var renderer = button.Find("Visual").GetComponent<SpriteRenderer>();
                var frames = new[] { Load("DemonButton_01"), Load("DemonButton_02") };
                renderer.sprite = frames[0]; renderer.color = Color.white;
                float scale = 1f / frames[0].bounds.size.x, height = frames[0].bounds.size.y * scale;
                renderer.transform.localScale = new Vector3(scale, scale, 1f);
                renderer.transform.localPosition = new Vector3(0f, -.1f + height * .5f);
                var shape = button.GetComponent<BoxCollider2D>(); shape.size = new Vector2(1f, height); shape.offset = new Vector2(0f, -.1f + height * .5f);
                var view = button.GetComponent<DemonCastleSpriteView>() ?? button.gameObject.AddComponent<DemonCastleSpriteView>();
                view.Target = renderer; view.Frames = frames; view.Switch = button.GetComponent<CitySwitch>();
            });
        }
        private static void CreateYoyo()
        {
            string path = DemonCastleBuilder.Folder + "/DemonYoyoVisual.prefab";
            if (File.Exists(path)) return;
            var root = new GameObject("DemonYoyoVisual");
            try
            {
                var renderer = root.AddComponent<SpriteRenderer>(); renderer.sprite = Load("DemonYoyo_01");
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Data/Enemies/EnemySprite_Mat.mat");
                var view = root.AddComponent<DemonCastleSpriteView>(); view.Target = renderer;
                view.Frames = new[] { renderer.sprite, Load("DemonYoyo_02") };
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        public static void RunBatch() { DemonCastleBuilder.BuildPrefabs(); }
    }
}
