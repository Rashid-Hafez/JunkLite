using TMPro;
using UnityEngine;

namespace junklite
{
    public static class UIFonts
    {
        public enum Role
        {
            World,
            HudTitle,
            HudBody
        }

        private const string OverlayShaderName = "TextMeshPro/Mobile/Distance Field Overlay";
        private static readonly string[] FancyKeywords =
        {
            "GLOW_ON", "OUTLINE_ON", "_EMISSION", "UNDERLAY_ON",
            "_PATTERN_DOTS", "_PATTERNCOORDS_SCREEN"
        };

        private static UIFontCatalog catalog;
        private static TMP_FontAsset runtimePlay;
        private static TMP_FontAsset runtimeZuume;
        private static TMP_FontAsset runtimeSatoshi;
        private static Shader overlayShader;

        public static TMP_FontAsset Play => GetOrCreate(ref runtimePlay, Catalog?.playFont, Catalog?.playSource);
        public static TMP_FontAsset HudTitle => GetOrCreate(ref runtimeZuume, Catalog?.zuumeEdgeFont, Catalog?.zuumeEdgeSource);
        public static TMP_FontAsset HudBody => GetOrCreate(ref runtimeSatoshi, Catalog?.satoshiFont, Catalog?.satoshiSource);
        public static Material DefaultMaterial => Catalog != null ? Catalog.defaultFontMaterial : null;

        private static UIFontCatalog Catalog
        {
            get
            {
                if (catalog == null)
                    catalog = Resources.Load<UIFontCatalog>("UIFontCatalog");
                return catalog;
            }
        }

        public static void ApplyWorld(TMP_Text text) => Apply(text, Play);

        public static void ApplyHudTitle(TMP_Text text) => Apply(text, HudTitle);

        public static void ApplyHudBody(TMP_Text text) => Apply(text, HudBody);

        public static void Apply(TMP_Text text, Role role)
        {
            switch (role)
            {
                case Role.HudTitle:
                    ApplyHudTitle(text);
                    break;
                case Role.HudBody:
                    ApplyHudBody(text);
                    break;
                default:
                    ApplyWorld(text);
                    break;
            }
        }

        public static void ApplyWorldTree(Transform root)
        {
            if (root == null) return;
            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
                ApplyWorld(texts[i]);
        }

        public static void Apply(TMP_Text text, TMP_FontAsset font)
        {
            if (text == null || font == null) return;

            text.font = font;
            StyleMaterial(font.material);
            text.fontSharedMaterial = font.material;
        }

        private static TMP_FontAsset GetOrCreate(
            ref TMP_FontAsset runtimeAsset,
            TMP_FontAsset authored,
            Font source)
        {
            if (authored != null)
            {
                StyleMaterial(authored.material);
                return authored;
            }

            if (runtimeAsset != null)
                return runtimeAsset;

            if (source != null)
            {
                runtimeAsset = TMP_FontAsset.CreateFontAsset(source);
                StyleMaterial(runtimeAsset != null ? runtimeAsset.material : null);
            }

            return runtimeAsset != null ? runtimeAsset : TMP_Settings.defaultFontAsset;
        }

        private static void StyleMaterial(Material material)
        {
            if (material == null) return;

            if (overlayShader == null)
                overlayShader = Shader.Find(OverlayShaderName);

            Material preset = DefaultMaterial;
            Shader shader = preset != null ? preset.shader : overlayShader;
            if (shader != null)
                material.shader = shader;

            for (int i = 0; i < FancyKeywords.Length; i++)
                material.DisableKeyword(FancyKeywords[i]);

            CopyFloat(preset, material, "_OutlineWidth", 0f);
            CopyFloat(preset, material, "_OutlineSoftness", 0f);
            CopyFloat(preset, material, "_FaceDilate", 0f);
            CopyFloat(preset, material, "_GlowPower", 0f);
            CopyFloat(preset, material, "_GlowInner", 0f);
            CopyFloat(preset, material, "_GlowOuter", 0f);
            CopyFloat(preset, material, "_UnderlaySoftness", 0f);
            CopyFloat(preset, material, "_UnderlayDilate", 0f);
            CopyColor(preset, material, "_GlowColor", Color.clear);
            CopyColor(preset, material, "_FaceColor", Color.white);
            CopyColor(preset, material, "_UnderlayColor", Color.clear);
        }

        private static void CopyFloat(Material preset, Material dest, string property, float fallback)
        {
            if (!dest.HasProperty(property)) return;
            dest.SetFloat(property, preset != null && preset.HasProperty(property)
                ? preset.GetFloat(property)
                : fallback);
        }

        private static void CopyColor(Material preset, Material dest, string property, Color fallback)
        {
            if (!dest.HasProperty(property)) return;
            dest.SetColor(property, preset != null && preset.HasProperty(property)
                ? preset.GetColor(property)
                : fallback);
        }
    }
}
