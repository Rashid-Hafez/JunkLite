using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace junklite
{
    /// <summary>One extra heat pass, using the original sprite or current Spine mesh without copying it.</summary>
    internal sealed class BurnRendererOverlay
    {
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BurnColor = Shader.PropertyToID("_BurnColor");
        private static readonly int Strength = Shader.PropertyToID("_BurnStrength");
        private static readonly int Tick = Shader.PropertyToID("_BurnTickTime");
        private static readonly int Pulse = Shader.PropertyToID("_BurnPulse");
        private static readonly int Bounds = Shader.PropertyToID("_BurnBounds");
        private static readonly int Seed = Shader.PropertyToID("_BurnSeed");
        private static readonly int Alpha = Shader.PropertyToID("_BurnAlpha");
        private static readonly int Flip = Shader.PropertyToID("_BurnFlip");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int TintColor = Shader.PropertyToID("_TintColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly Renderer source;
        private readonly Renderer overlay;
        private readonly GameObject host;
        private readonly Material material;
        private readonly SpriteRenderer sourceSprite;
        private readonly SpriteRenderer overlaySprite;
        private readonly MeshFilter sourceMesh;
        private readonly MeshFilter overlayMesh;
        private readonly List<Material> sourceMaterials = new List<Material>();
        private readonly List<Material> overlayMaterials = new List<Material>();
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        public BurnRendererOverlay(Renderer source, Material material)
        {
            this.source = source;
            this.material = material;
            host = new GameObject("Burn Heat (Runtime)") { hideFlags = HideFlags.DontSave };
            host.transform.SetParent(source.transform, false);
            sourceSprite = source as SpriteRenderer;
            if (sourceSprite != null)
            {
                overlaySprite = host.AddComponent<SpriteRenderer>();
                overlay = overlaySprite;
                overlay.sharedMaterial = material;
            }
            else
            {
                sourceMesh = source.GetComponent<MeshFilter>();
                overlayMesh = host.AddComponent<MeshFilter>();
                overlay = host.AddComponent<MeshRenderer>();
            }
            ConfigureRenderer(overlay);
            Hide();
        }

        internal static void ConfigureRenderer(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
        }

        public void Sync(BurnVisualProfile profile, float lastTick, float seed)
        {
            if (source == null || overlay == null) return;
            bool visible = source.enabled && source.gameObject.activeInHierarchy && !source.forceRenderingOff;
            overlay.enabled = visible;
            if (!visible) return;
            host.layer = source.gameObject.layer;
            overlay.sortingLayerID = source.sortingLayerID;
            overlay.sortingOrder = source.sortingOrder + 1;
            overlay.renderingLayerMask = source.renderingLayerMask;

            if (sourceSprite != null)
            {
                overlaySprite.sprite = sourceSprite.sprite;
                overlaySprite.flipX = sourceSprite.flipX;
                overlaySprite.flipY = sourceSprite.flipY;
                overlaySprite.drawMode = sourceSprite.drawMode;
                overlaySprite.size = sourceSprite.size;
                overlaySprite.tileMode = sourceSprite.tileMode;
                overlaySprite.maskInteraction = sourceSprite.maskInteraction;
                overlaySprite.spriteSortPoint = sourceSprite.spriteSortPoint;
                overlaySprite.color = Color.white;
            }
            else
            {
                // Spine can swap its double-buffered mesh and change submesh count after skin changes.
                overlayMesh.sharedMesh = sourceMesh != null ? sourceMesh.sharedMesh : null;
                if (overlayMesh.sharedMesh == null) { Hide(); return; }
            }

            source.GetSharedMaterials(sourceMaterials);
            int count = sourceSprite != null ? 1 : sourceMaterials.Count;
            if (sourceSprite == null && overlayMaterials.Count != count)
            {
                overlayMaterials.Clear();
                for (int i = 0; i < count; i++) overlayMaterials.Add(material);
                overlay.SetSharedMaterials(overlayMaterials);
            }

            UnityEngine.Bounds bounds = source.localBounds;
            for (int i = 0; i < count; i++)
            {
                Material original = i < sourceMaterials.Count ? sourceMaterials[i] : null;
                block.Clear();
                // Read only: preserve independent source flash/dissolve writers.
                source.GetPropertyBlock(block);
                Texture texture = block.GetTexture(MainTex);
                if (sourceSprite != null && sourceSprite.sprite != null) texture = sourceSprite.sprite.texture;
                if (texture == null && original != null && original.HasProperty(MainTex)) texture = original.GetTexture(MainTex);
                if (texture == null) texture = Texture2D.whiteTexture;
                float alpha = 1f;
                if (original != null)
                {
                    if (original.HasProperty(BaseColor)) alpha *= original.GetColor(BaseColor).a;
                    if (original.HasProperty(TintColor)) alpha *= original.GetColor(TintColor).a;
                    if (original.HasProperty(ColorId)) alpha *= original.GetColor(ColorId).a;
                }
                block.SetTexture(MainTex, texture);
                block.SetColor(BurnColor, profile.heatColor);
                block.SetFloat(Strength, profile.heatStrength);
                block.SetFloat(Tick, lastTick);
                block.SetVector(Pulse, new Vector4(profile.tickPulseStrength, profile.tickPulseDuration, 0f, 0f));
                block.SetVector(Bounds, new Vector4(bounds.min.x, bounds.min.y,
                    1f / Mathf.Max(0.001f, bounds.size.x), 1f / Mathf.Max(0.001f, bounds.size.y)));
                block.SetFloat(Seed, seed);
                block.SetFloat(Alpha, alpha * (sourceSprite != null ? sourceSprite.color.a : 1f));
                block.SetVector(Flip, new Vector4(sourceSprite != null && sourceSprite.flipX ? -1f : 1f,
                    sourceSprite != null && sourceSprite.flipY ? -1f : 1f, 0f, 0f));
                if (sourceSprite != null) overlay.SetPropertyBlock(block);
                else overlay.SetPropertyBlock(block, i);
            }
        }

        public void Hide() { if (overlay != null) overlay.enabled = false; }
        public void Dispose() => DestroyOwned(host);

        internal static void DestroyOwned(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
