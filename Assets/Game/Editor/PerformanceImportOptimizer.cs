using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace junklite.Editor
{
    /// <summary>
    /// Applies repeatable import recommendations without hand-editing Unity meta files.
    /// Review the diff after running either command because source content can have
    /// intentional exceptions that an automated size rule cannot infer.
    /// </summary>
    public static class PerformanceImportOptimizer
    {
        private const string GameRoot = "Assets/Game";
        private const long StreamingAudioBytes = 5L * 1024L * 1024L;
        private const long CompressedAudioBytes = 512L * 1024L;
        private const int StreamedTextureMinimumDimension = 1024;

        [MenuItem("Tools/JunkLite/Performance/Optimize Game Audio Imports")]
        public static void OptimizeAudioImports()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { GameRoot });
            var changedPaths = new List<string>();

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (AssetImporter.GetAtPath(path) is not AudioImporter importer)
                    continue;

                long sourceBytes = GetSourceFileSize(path);
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                AudioClipLoadType desiredLoadType = sourceBytes >= StreamingAudioBytes
                    ? AudioClipLoadType.Streaming
                    : sourceBytes >= CompressedAudioBytes
                        ? AudioClipLoadType.CompressedInMemory
                        : AudioClipLoadType.DecompressOnLoad;

                bool changed = settings.loadType != desiredLoadType;
                settings.loadType = desiredLoadType;

                if (desiredLoadType != AudioClipLoadType.DecompressOnLoad)
                {
                    changed |= settings.compressionFormat != AudioCompressionFormat.Vorbis;
                    changed |= !Mathf.Approximately(settings.quality, 0.7f);
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                    settings.quality = 0.7f;
                }

                bool shouldLoadInBackground = desiredLoadType != AudioClipLoadType.DecompressOnLoad;
                bool shouldPreload = desiredLoadType == AudioClipLoadType.DecompressOnLoad;
                changed |= importer.loadInBackground != shouldLoadInBackground;
                changed |= settings.preloadAudioData != shouldPreload;
                importer.loadInBackground = shouldLoadInBackground;
                settings.preloadAudioData = shouldPreload;
                importer.defaultSampleSettings = settings;

                if (changed)
                    changedPaths.Add(path);
            }

            ReimportChangedAssets(changedPaths);
            Debug.Log($"[Performance Imports] Updated {changedPaths.Count} of {guids.Length} audio clips.");
        }

        [MenuItem("Tools/JunkLite/Performance/Enable World Texture Streaming")]
        public static void EnableWorldTextureStreaming()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { GameRoot });
            var changedPaths = new List<string>();

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                    continue;
                if (importer.textureType != TextureImporterType.Default &&
                    importer.textureType != TextureImporterType.NormalMap)
                    continue;
                if (!importer.mipmapEnabled || importer.streamingMipmaps)
                    continue;

                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                if (Mathf.Max(width, height) < StreamedTextureMinimumDimension)
                    continue;

                importer.streamingMipmaps = true;
                changedPaths.Add(path);
            }

            ReimportChangedAssets(changedPaths);
            Debug.Log($"[Performance Imports] Enabled mip streaming on {changedPaths.Count} world textures.");
        }

        [MenuItem("Tools/JunkLite/Performance/Open Frame Spike Logs")]
        public static void OpenFrameSpikeLogs()
        {
            string directory = Path.Combine(Application.persistentDataPath, "PerformanceLogs");
            Directory.CreateDirectory(directory);
            EditorUtility.RevealInFinder(directory);
        }

        private static long GetSourceFileSize(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            return File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0L;
        }

        private static void ReimportChangedAssets(List<string> paths)
        {
            if (paths.Count == 0)
                return;

            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < paths.Count; i++)
                    AssetDatabase.WriteImportSettingsIfDirty(paths[i]);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh();
        }
    }
}
