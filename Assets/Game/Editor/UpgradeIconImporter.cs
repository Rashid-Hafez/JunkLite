using UnityEditor;

namespace junklite.Editor
{
    /// <summary>
    /// Every texture under the upgrade icon folder imports as a UI sprite (single, alpha, no mipmaps),
    /// so the generated icons need no hand-written import settings.
    /// </summary>
    public sealed class UpgradeIconImporter : AssetPostprocessor
    {
        public const string IconFolder = "Assets/Game/New UI/Icons/Upgrades/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(IconFolder, System.StringComparison.OrdinalIgnoreCase)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.maxTextureSize = 256;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
