using TMPro;
using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(menuName = "JunkLite/UI Font Catalog", fileName = "UIFontCatalog")]
    public class UIFontCatalog : ScriptableObject
    {
        [Header("TMP Font Assets")]
        public TMP_FontAsset playFont;
        public TMP_FontAsset zuumeEdgeFont;
        public TMP_FontAsset satoshiFont;

        [Header("Source Fonts (runtime fallback)")]
        public Font playSource;
        public Font zuumeEdgeSource;
        public Font satoshiSource;

        [Header("Shared bland material")]
        public Material defaultFontMaterial;
    }
}
