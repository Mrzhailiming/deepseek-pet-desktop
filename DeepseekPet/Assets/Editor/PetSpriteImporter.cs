using UnityEditor;
using UnityEngine;

namespace DeepseekPet.EditorTools
{
    /// <summary>
    /// 立绘的导入设置（Assets/Characters/**/WhaleSprites|FormSprites/*.png）：
    /// Sprite、底边居中的轴心、最大 512（源图 1024，桌宠最大也就两三百像素高）、不压缩且可读 ——
    /// 窗口点击穿透要按像素 alpha 判断「点在不在角色身上」，压缩格式读不准边缘。
    /// </summary>
    sealed class PetSpriteImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var path = assetPath.Replace('\\', '/');
            if (path.Contains("/Resources/Emoji/"))
            {
                // emoji 图标：96px 源图缩到十几二十像素画，要 mipmap 才不发毛；不需要可读。
                var emoji = (TextureImporter)assetImporter;
                emoji.textureType = TextureImporterType.Sprite;
                emoji.spriteImportMode = SpriteImportMode.Single;
                emoji.mipmapEnabled = true;
                emoji.alphaIsTransparency = true;
                emoji.isReadable = false;
                emoji.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            if (!path.Contains("/WhaleSprites/") && !path.Contains("/FormSprites/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = true;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
        }
    }
}
