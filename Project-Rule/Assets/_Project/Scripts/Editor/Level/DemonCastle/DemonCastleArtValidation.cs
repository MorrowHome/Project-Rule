using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using RuleGame.Gameplay.Scene.Components;
using RuleGame.Presentation.DemonCastle;

namespace RuleGame.Editor.Level
{
    /// <summary>实际美术的裁切、比例、稳定引用、按钮选帧和 URP 预览检查。</summary>
    public static class DemonCastleArtValidation
    {
        public static void RunPreviewBoundsBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("仅用于独立批处理验证。");
            var ground = AssetDatabase.LoadAssetAtPath<GameObject>(DemonCastleBuilder.Folder + "/DemonGround.prefab");
            var renderer = ground.GetComponentInChildren<SpriteRenderer>();
            if (Mathf.Abs(renderer.localBounds.size.x - 8f) > .001f)
                throw new InvalidOperationException("平铺地面放置预览必须覆盖完整 8 单位宽度。");
            Debug.Log("[DemonCastleArtValidation] Tiled placement preview spans 8 world units: PASS");
            EditorApplication.Exit(0);
        }
        public static void CheckAssets(Action<bool, string> check)
        {
            foreach (string path in Directory.GetFiles(DemonCastleArtSetup.Folder, "*.png"))
            {
                var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Single();
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                check(sprite.rect.width < sprite.texture.width && sprite.rect.height < sprite.texture.height &&
                    importer.filterMode == FilterMode.Bilinear && !importer.mipmapEnabled, sprite.name + " imports cropped hand-drawn art without transparent canvas padding");
            }
            foreach (string prefix in new[] { "DemonButton", "DemonYoyo" })
            {
                var first = DemonCastleArtSetup.Load(prefix + "_01"); var second = DemonCastleArtSetup.Load(prefix + "_02");
                check(first.rect == second.rect && first.pivot == second.pivot, prefix + " animation frames keep a shared crop and pivot");
            }
            foreach (string name in new[] { "DemonPlatformSmall", "DemonPlatformLong" })
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(DemonCastleBuilder.Folder + "/" + name + ".prefab");
                var box = asset.GetComponent<BoxCollider2D>(); var visual = asset.transform.Find("Visual").GetComponent<SpriteRenderer>();
                float top = visual.transform.localPosition.y + visual.sprite.bounds.extents.y * visual.transform.localScale.y;
                check(Mathf.Abs(top - (box.offset.y + box.size.y * .5f)) < .001f &&
                    Mathf.Abs(visual.transform.localScale.x - visual.transform.localScale.y) < .001f && visual.color == Color.white,
                    name + " preserves sprite aspect ratio and aligns the visible top to collision");
            }
            var combo = AssetDatabase.LoadAssetAtPath<GameObject>(DemonCastleBuilder.Folder + "/DemonFireworkSwitch.prefab");
            var switchView = combo.GetComponentInChildren<DemonCastleSpriteView>();
            check(switchView != null && switchView.Switch != null && switchView.Frames.Length == 2 && switchView.Switch.FollowMotor != null,
                "button delivered frames retain existing switch and firework target");
            var yoyo = AssetDatabase.LoadAssetAtPath<GameObject>(DemonCastleBuilder.Folder + "/DemonYoyoVisual.prefab");
            check(yoyo.GetComponent<DemonCastleSpriteView>().Frames.Length == 2 && yoyo.GetComponents<Collider2D>().Length == 0,
                "yoyo is an explicit visual preview without an invented damage collider");
        }
        public static void CheckRuntime(Action<bool, string> check)
        {
            var button = UnityEngine.Object.FindAnyObjectByType<CitySwitch>();
            var view = button.GetComponent<DemonCastleSpriteView>();
            var saved = button.CaptureSnapshotState();
            button.SetOn(true); view.SendMessage("LateUpdate");
            check(view.Target.sprite == view.Frames[1] && view.Target.color == Color.white, "pressed button displays delivered pressed frame in original colors");
            button.RestoreSnapshotState(saved); view.SendMessage("LateUpdate");
            check(view.Target.sprite == view.Frames[button.IsOn ? 1 : 0], "button art follows restored snapshot state");
        }
        public static void CaptureGallery(Action<bool, string> check)
        {
            var root = new GameObject("DeliveredArtGallery"); root.transform.position = new Vector3(220f, 0f);
            var cameraObject = new GameObject("DeliveredArtCamera");
            var camera = cameraObject.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 5.7f;
            camera.transform.position = new Vector3(220f, 0f, -10f); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.15f, .13f, .19f);
            var rt = new RenderTexture(1440, 810, 24); var image = new Texture2D(1440, 810, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                Spawn("DemonGround", new Vector2(-5.6f, -3.4f), root.transform);
                for (int i = 2; i <= 4; i++) Spawn("DemonGround" + i.ToString("00"), new Vector2(1.8f + (i - 2) * 2.6f, -3.4f), root.transform);
                for (int i = 1; i <= 6; i++) Spawn("DemonPillar" + i.ToString("00"), new Vector2(-7.8f + (i - 1) * 2.35f, -.3f), root.transform);
                Spawn("DemonPlatformSmall", new Vector2(-6.5f, 3.3f), root.transform);
                Spawn("DemonPlatformLong", new Vector2(-1f, 3.3f), root.transform);
                Spawn("DemonPlatformMedium", new Vector2(5.3f, 3.3f), root.transform);
                Spawn("DemonYoyoVisual", new Vector2(7.4f, -.5f), root.transform);
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 1440, 810), 0, 0); image.Apply();
                Directory.CreateDirectory("Logs/DemonCastlePreview");
                File.WriteAllBytes("Logs/DemonCastlePreview/DeliveredArt.png", image.EncodeToPNG());
                check(image.GetPixels().Count(x => x.b > .15f && x.r < .12f) > 10000, "actual delivered terrain and yoyo render in URP");
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous; rt.Release();
                UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(root);
            }
        }
        private static void Spawn(string name, Vector2 at, Transform parent)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(DemonCastleBuilder.Folder + "/" + name + ".prefab");
            var instance = UnityEngine.Object.Instantiate(asset, parent); instance.transform.localPosition = at;
        }
    }
}
