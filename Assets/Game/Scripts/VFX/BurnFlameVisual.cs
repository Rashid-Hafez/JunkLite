using UnityEngine;
using System.Collections.Generic;

namespace junklite
{
    /// <summary>Five flames and twenty body embers, animated on the GPU in one draw. Reused for later burns.</summary>
    internal sealed class BurnFlameVisual
    {
        private static readonly int Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int Seed = Shader.PropertyToID("_BurnSeed");
        private static readonly int EmberColor = Shader.PropertyToID("_EmberColor");
        private static readonly int EmberOpacity = Shader.PropertyToID("_EmberOpacity");
        private static readonly int EmberSize = Shader.PropertyToID("_EmberSize");
        private readonly Renderer source;
        private readonly GameObject host;
        private readonly MeshRenderer renderer;
        private readonly Mesh mesh;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        public BurnFlameVisual(Renderer source, Material material)
        {
            this.source = source;
            host = new GameObject("Burn Flames (Runtime)") { hideFlags = HideFlags.DontSave };
            host.transform.SetParent(source.transform, false);
            mesh = CreateMesh();
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            BurnRendererOverlay.ConfigureRenderer(renderer);
            Hide();
        }

        private static Mesh CreateMesh()
        {
            const int flameCards = 5;
            const int cards = flameCards + 20;
            var vertices = new Vector3[cards * 4];
            var uv = new Vector2[vertices.Length];
            var data = new List<Vector4>(vertices.Length);
            var triangles = new int[cards * 6];
            for (int i = 0; i < cards; i++)
            {
                bool ember = i >= flameCards;
                // Stable distribution through the body, without touching gameplay's random state.
                float phase = Mathf.Repeat(i * 0.618034f, 1f);
                float x = ember ? (Mathf.Repeat(i * 0.754878f, 1f) - 0.5f) * 0.65f : (i - 2f) * 0.18f;
                float y = ember ? 0.12f + Mathf.Repeat(i * 0.56984f, 1f) * 0.72f : (i == 2 ? 0.76f : 0.18f + (i % 3) * 0.18f);
                float width = ember ? Mathf.Lerp(0.009f, 0.018f, phase) : 0.20f;
                float height = ember ? width * 1.4f : 0.30f + (i % 2) * 0.08f;
                int v = i * 4;
                vertices[v] = new Vector3(x - width * 0.5f, y, 0f);
                vertices[v + 1] = new Vector3(x + width * 0.5f, y, 0f);
                vertices[v + 2] = new Vector3(x + width * 0.5f, y + height, 0f);
                vertices[v + 3] = new Vector3(x - width * 0.5f, y + height, 0f);
                uv[v] = Vector2.zero; uv[v + 1] = Vector2.right;
                uv[v + 2] = Vector2.one; uv[v + 3] = Vector2.up;
                for (int j = 0; j < 4; j++) data.Add(new Vector4(ember ? phase : i * 0.173f, ember ? 1f : 0f, width, height));
                int t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            var result = new Mesh { name = "Burn Cards", hideFlags = HideFlags.DontSave };
            result.vertices = vertices; result.uv = uv; result.SetUVs(1, data); result.triangles = triangles;
            // Includes shader displacement of rising embers.
            result.bounds = new Bounds(new Vector3(0f, 0.8f, 0f), new Vector3(1.7f, 1.8f, 0.1f));
            result.UploadMeshData(true);
            return result;
        }

        public void Sync(BurnVisualProfile profile, Transform anchor, float scale, float seed)
        {
            if (source == null || renderer == null) return;
            bool visible = (profile.showFlames || profile.showEmbers) && source.enabled && source.gameObject.activeInHierarchy && !source.forceRenderingOff;
            renderer.enabled = visible;
            if (!visible) return;
            host.layer = source.gameObject.layer;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder + 2;
            renderer.renderingLayerMask = source.renderingLayerMask;
            Bounds bounds = source.localBounds;
            Transform parent = anchor != null ? anchor : source.transform;
            if (host.transform.parent != parent) host.transform.SetParent(parent, false);
            host.transform.localRotation = Quaternion.identity;
            host.transform.localPosition = anchor != null ? Vector3.zero : new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            float multiplier = Mathf.Max(0.1f, scale * profile.flameScale);
            float width = Mathf.Min(bounds.size.x, bounds.size.y * profile.flameMaxWidthToHeight);
            host.transform.localScale = anchor != null ? Vector3.one * multiplier :
                new Vector3(Mathf.Max(0.1f, width), Mathf.Max(0.1f, bounds.size.y), 1f) * multiplier;
            float alpha = source is SpriteRenderer sprite ? sprite.color.a : 1f;
            block.SetFloat(Opacity, profile.showFlames ? profile.flameOpacity * alpha : 0f);
            block.SetColor(EmberColor, profile.emberColor);
            block.SetFloat(EmberOpacity, profile.showEmbers ? profile.emberOpacity * alpha : 0f);
            block.SetFloat(EmberSize, profile.emberSize);
            block.SetFloat(Seed, seed);
            renderer.SetPropertyBlock(block);
        }

        public void Hide() { if (renderer != null) renderer.enabled = false; }
        public void Dispose()
        {
            BurnRendererOverlay.DestroyOwned(host);
            BurnRendererOverlay.DestroyOwned(mesh);
        }
    }
}
