#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;

namespace junklite
{
    public static class UIFontAssetBuilder
    {
        private const string FontsFolder = "Assets/Game/New UI/Fonts";
        private const string OverlayShaderName = "TextMeshPro/Mobile/Distance Field Overlay";

        [MenuItem("JunkLite/UI/Rebuild New UI Font Assets")]
        public static void Rebuild()
        {
            Build("Play-Regular.ttf", "Play-Regular SDF.asset");
            Build("ZuumeEdge-Regular.ttf", "ZuumeEdge-Regular SDF.asset");
            Build("Satoshi-Variable.ttf", "Satoshi-Variable SDF TMP.asset");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Rebuilt JunkLite New UI TMP font assets.");
        }

        private static void Build(string ttfName, string sdfName)
        {
            Font source = AssetDatabase.LoadAssetAtPath<Font>($"{FontsFolder}/{ttfName}");
            if (source == null)
            {
                Debug.LogWarning($"Missing source font {FontsFolder}/{ttfName}");
                return;
            }

            string sdfPath = $"{FontsFolder}/{sdfName}";
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(sdfPath);
            if (existing != null)
                AssetDatabase.DeleteAsset(sdfPath);

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(source);

            AssetDatabase.CreateAsset(fontAsset, sdfPath);
            if (fontAsset.atlasTexture != null)
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTexture, fontAsset);
            if (fontAsset.material != null)
            {
                Shader overlay = Shader.Find(OverlayShaderName);
                if (overlay != null)
                    fontAsset.material.shader = overlay;
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            EditorUtility.SetDirty(fontAsset);
        }
    }
}
#endif
