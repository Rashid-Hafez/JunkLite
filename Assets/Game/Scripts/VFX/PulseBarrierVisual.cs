using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace junklite
{
    /// <summary>Player-owned, reusable shield presentation. All ribbon and spark motion runs on the GPU.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10010)]
    public sealed class PulseBarrierVisual : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_ArcColor");
        private static readonly int ShapeId = Shader.PropertyToID("_ArcShape");
        private static readonly int StateId = Shader.PropertyToID("_ArcState");
        private static readonly int TimingId = Shader.PropertyToID("_ArcTiming");
        private MaterialPropertyBlock properties;
        private DamageShield shield;
        private PulseBarrierVisualProfile profile;
        private Transform facing;
        private Renderer bodyRenderer;
        private Vector3 localCenterOffset;
        private Vector2 radii;
        private GameObject host;
        private MeshRenderer arcRenderer;
        private Mesh mesh;
        private float startedAt, expiresAt, lastHitAt = -1000f, endedAt, endDuration;
        private float health = 1f;
        private bool finishing, shattered;

        public bool Show(DamageShield target, PulseBarrierVisualProfile settings, Transform orientation, Vector3 offset)
        {
            if (target == null || settings == null || settings.arcMaterial == null) return false;
            Unsubscribe();
            shield = target;
            profile = settings;
            if (properties == null) properties = new MaterialPropertyBlock();
            facing = orientation;
            bodyRenderer = facing != null ? facing.GetComponent<Renderer>() : null;
            localCenterOffset = transform.InverseTransformVector(offset);
            Collider body = target.GetComponent<Collider>();
            float halfHeight = body != null ? Mathf.Max(0.5f, body.bounds.extents.y) : 1f;
            radii = new Vector2(Mathf.Max(0.2f, profile.radiusScale.x), Mathf.Max(0.2f, profile.radiusScale.y)) * halfHeight;
            EnsureRenderer();
            arcRenderer.sharedMaterial = profile.arcMaterial;
            arcRenderer.sortingLayerID = bodyRenderer != null ? bodyRenderer.sortingLayerID : 0;
            arcRenderer.sortingOrder = bodyRenderer != null ? bodyRenderer.sortingOrder + 3 : 3;
            arcRenderer.renderingLayerMask = bodyRenderer != null ? bodyRenderer.renderingLayerMask : 1u;
            // Include sparks travelling beyond the expanding outer edge during hits and shattering.
            float padding = (profile.ribbonWidth + 0.11f) * 2f;
            mesh.bounds = new Bounds(Vector3.zero,
                new Vector3(radii.x * 5f + padding, radii.y * 5f + padding, radii.x * 4f));
            startedAt = Time.time;
            expiresAt = startedAt + shield.TimeRemaining;
            lastHitAt = -1000f;
            health = shield.NormalizedHP;
            finishing = shattered = false;
            shield.OnShieldDamaged += OnAbsorbed;
            enabled = true;
            arcRenderer.enabled = true;
            LateUpdate();
            return true;
        }

        /// <summary>Cancellation/death hides immediately; natural endings get a short cosmetic tail.</summary>
        public void Stop(bool immediate)
        {
            Unsubscribe();
            if (immediate) { Hide(); return; }
            if (finishing || arcRenderer == null || !arcRenderer.enabled || profile == null) return;
            finishing = true;
            shattered = Time.time < expiresAt - 0.02f;
            endedAt = Time.time;
            endDuration = Mathf.Max(0.05f, shattered ? profile.breakDuration : profile.expiryDuration);
            if (shattered) lastHitAt = Time.time;
        }

        private void OnAbsorbed(float current, float maximum)
        {
            health = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f;
            lastHitAt = Time.time;
        }

        private void LateUpdate()
        {
            if (arcRenderer == null || !arcRenderer.enabled || profile == null) return;
            if (!finishing && (shield == null || !shield.IsActive)) Stop(false);
            float exit = finishing ? Mathf.Clamp01((Time.time - endedAt) / endDuration) : 0f;
            if (finishing && exit >= 1f) { Hide(); return; }
            // Follow the rendered body, whose animated center can differ from the collision capsule.
            // Keep the fallback in player space so its horizontal offset follows lane turns too.
            Vector3 center = bodyRenderer != null && bodyRenderer.localBounds.size.sqrMagnitude > 0.0001f
                ? bodyRenderer.bounds.center
                : transform.TransformPoint(localCenterOffset);
            host.transform.SetPositionAndRotation(center,
                facing != null ? facing.rotation : transform.rotation);
            // Ignore the player's scale/flip: radii were fitted to its world-space collider.
            Vector3 scale = transform.lossyScale;
            host.transform.localScale = new Vector3(Inverse(scale.x), Inverse(scale.y), Inverse(scale.z));
            host.layer = gameObject.layer;
            properties.SetColor(ColorId, profile.color);
            properties.SetVector(ShapeId, new Vector4(radii.x, radii.y, profile.ribbonWidth, profile.orbitSpeed));
            properties.SetVector(StateId, new Vector4(health, exit, shattered ? 1f : 0f, profile.opacity));
            properties.SetVector(TimingId, new Vector4(Time.time - startedAt, Time.time - lastHitAt,
                Mathf.Max(0.05f, profile.activationDuration), finishing ? Time.time - endedAt : -1f));
            arcRenderer.SetPropertyBlock(properties);
        }

        private static float Inverse(float value) => Mathf.Abs(value) > 0.001f ? 1f / value : 1f;

        private void EnsureRenderer()
        {
            if (host != null) return;
            host = new GameObject("Pulse Arcs (Runtime)") { hideFlags = HideFlags.DontSave };
            host.transform.SetParent(transform, false);
            mesh = CreateMesh();
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            arcRenderer = host.AddComponent<MeshRenderer>();
            arcRenderer.shadowCastingMode = ShadowCastingMode.Off;
            arcRenderer.receiveShadows = false;
            arcRenderer.lightProbeUsage = LightProbeUsage.Off;
            arcRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            arcRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        private static Mesh CreateMesh()
        {
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var data = new List<Vector4>();
            var triangles = new List<int>();
            for (int ring = 0; ring < 3; ring++)
            {
                int arcs = ring == 0 ? 6 : 2;
                float length = ring == 0 ? 0.82f : 2.35f;
                const int steps = 24;
                for (int arc = 0; arc < arcs; arc++)
                {
                    float start = arc * Mathf.PI * 2f / arcs + ring * 0.6f;
                    for (int step = 0; step < steps; step++)
                    {
                        int first = vertices.Count;
                        for (int corner = 0; corner < 4; corner++)
                        {
                            float along = (step + (corner >= 2 ? 1f : 0f)) / steps;
                            float side = corner == 0 || corner == 3 ? -1f : 1f;
                            vertices.Add(new Vector3(start + along * length, side, 0f));
                            uv.Add(new Vector2(along, side));
                            data.Add(new Vector4(ring, start + length * 0.5f, 0f, arc * 0.173f));
                        }
                        AddQuad(triangles, first);
                    }
                }
            }
            for (int spark = 0; spark < 28; spark++)
            {
                int first = vertices.Count;
                for (int corner = 0; corner < 4; corner++)
                {
                    float x = corner == 0 || corner == 3 ? -1f : 1f;
                    float y = corner < 2 ? -1f : 1f;
                    vertices.Add(new Vector3(x, y, 0f)); uv.Add(new Vector2(x, y));
                    data.Add(new Vector4(0f, 0f, 1f, Mathf.Repeat(spark * 0.618034f, 1f)));
                }
                AddQuad(triangles, first);
            }
            var result = new Mesh { name = "Pulse Arc Ribbons and Sparks", hideFlags = HideFlags.DontSave };
            result.SetVertices(vertices); result.SetUVs(0, uv); result.SetUVs(1, data);
            result.SetTriangles(triangles, 0); result.UploadMeshData(true);
            return result;
        }

        private static void AddQuad(List<int> triangles, int first)
        {
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }

        private void Unsubscribe()
        {
            if (shield != null) shield.OnShieldDamaged -= OnAbsorbed;
            shield = null;
        }

        private void Hide()
        {
            if (arcRenderer != null) arcRenderer.enabled = false;
            finishing = false;
            enabled = false;
        }

        private void OnDisable() { Unsubscribe(); Hide(); }
        private void OnDestroy()
        {
            Unsubscribe();
            DestroyOwned(host);
            DestroyOwned(mesh);
        }

        private static void DestroyOwned(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
