using UnityEditor;
using UnityEngine;

namespace RuleGame.Editor.Art
{
    /// <summary>
    /// 背景房子贴图的导入规格：PPU 128、底边中心轴心、Full Rect、mipmap、无压缩。
    /// </summary>
    /// <remarks>
    /// <para>这批图在落盘前已经整体缩到 0.4，所以这里按工程统一的 PPU 128 导入，
    /// 世界尺寸仍等于「原图像素 / 320」。</para>
    /// <para>⚠️ 不要改单张图的 PPU：世界尺寸会整体飘掉。要提清晰度请同时改
    /// <c>Tools/Art/prepare_houses.py</c> 的 SCALE 与本类的 PixelsPerUnit。</para>
    /// </remarks>
    public sealed class HouseArtPostprocessor : AssetPostprocessor
    {
        public const string HouseFolder = "Assets/_Project/Art/Sprites/Background/Houses/";

        public const float PixelsPerUnit = 128f;

        private void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(HouseFolder)) return;

            var importer = (TextureImporter)assetImporter;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;

            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
            importer.sRGBTexture = true;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(0.5f, 0f);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
        }
    }
}
