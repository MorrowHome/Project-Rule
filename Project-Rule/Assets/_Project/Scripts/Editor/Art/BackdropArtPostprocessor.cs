using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Art
{
    /// <summary>
    /// 背景贴图的导入规格：两张子目录、两个 PPU，世界尺寸都是 16×9。
    /// </summary>
    /// <remarks>
    /// <para><b>为什么轴心是 Center</b>：背景是贴屏的，Director 按中心做 cover 缩放；
    /// 房子那套「底边中心」是给摆在地上的东西用的，不能混。</para>
    /// <para><b>为什么分两个 PPU</b>：纯色渐变 1024×576 就够（放大无感，省显存）；
    /// BOSS 室内图有笔触，用 2048×1152。两边算出来都是 16×9 米。</para>
    /// <para>⚠️ 换图要同时改 <c>Tools/Art/prepare_backdrops.py</c> 的输出尺寸，否则世界尺寸会飘。</para>
    /// </remarks>
    public sealed class BackdropArtPostprocessor : AssetPostprocessor
    {
        public const string Root = "Assets/_Project/Art/Sprites/Backdrops/";
        public const string PaintedFolder = Root + "Painted/";
        public const string GradientFolder = Root + "Gradient/";

        public const float PaintedPixelsPerUnit = 128f;
        public const float GradientPixelsPerUnit = 64f;

        private void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');

            float pixelsPerUnit;
            if (path.StartsWith(PaintedFolder)) pixelsPerUnit = PaintedPixelsPerUnit;
            else if (path.StartsWith(GradientFolder)) pixelsPerUnit = GradientPixelsPerUnit;
            else return;

            var importer = (TextureImporter)assetImporter;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;

            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.alphaIsTransparency = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
            importer.sRGBTexture = true;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(0.5f, 0.5f);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
        }
    }
}
