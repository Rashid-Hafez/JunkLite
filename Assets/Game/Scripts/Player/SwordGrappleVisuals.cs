using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>Prefab-owned presentation; gameplay supplies positions and visibility only.</summary>
    [DisallowMultipleComponent]
    public sealed class SwordGrappleVisuals : MonoBehaviour
    {
        [Header("Prefab Renderers")]
        [SerializeField] private LineRenderer preview;
        [SerializeField] private LineRenderer reticle;
        [SerializeField] private LineRenderer rope;
        [SerializeField] private SpriteRenderer sword;
        [Tooltip("Optional prefab mesh for bracket, status glyph and travelling dash geometry. Its renderer owns the material and sorting.")]
        [SerializeField] private MeshFilter aimDetails;

        public bool HasRequiredRenderers => preview != null && reticle != null && rope != null && sword != null;

        [Header("Aim Marker")]
        [SerializeField] private Color validAimColor = new(0.28f, 0.96f, 0.88f, 1f);
        [SerializeField] private Color invalidAimColor = new(1f, 0.38f, 0.29f, 1f);
        [SerializeField] private Color centerColor = new(0.9f, 1f, 0.98f, 1f);
        [SerializeField] private Color contrastColor = new(0.015f, 0.035f, 0.045f, 0.85f);
        [SerializeField, Min(0.05f)] private float reticleRadius = 0.22f;
        [SerializeField] private bool scaleMarkerWithCamera = true;
        [SerializeField, Range(8f, 40f)] private float reticlePixelRadius = 22f;
        [SerializeField] private Vector2 worldRadiusLimits = new(0.1f, 0.65f);
        [SerializeField, Range(0.02f, 0.2f)] private float bracketThickness = 0.125f;
        [SerializeField, Range(0f, 0.3f)] private float focusPulseAmount = 0.14f;
        [SerializeField, Min(0.01f)] private float focusPulseDuration = 0.18f;

        [Header("Aim Guide")]
        [SerializeField, Range(0f, 1f)] private float guideOpacity = 0.22f;
        [SerializeField, Min(0.01f)] private float dashLength = 0.12f;
        [SerializeField, Min(0.01f)] private float dashSpacing = 0.32f;
        [SerializeField, Min(0f)] private float dashTravelSpeed = 1.1f;
        [SerializeField, Range(4, 96)] private int maximumDashes = 48;
        [SerializeField, Min(0f)] private float guideStartOffset = 0.25f;

        [Header("Thrown Sword")]
        [Tooltip("Use the equipped weapon icon; turn off to use the Sprite Renderer sprite authored in this prefab.")]
        [SerializeField] private bool useWeaponIcon = true;
        [SerializeField, Min(0.1f)] private float swordVisualLength = 1.25f;
        [SerializeField] private float swordSpriteAngleOffset = -45f;

        private Mesh detailMesh;
        private Renderer detailRenderer;
        private readonly List<Vector3> vertices = new(512);
        private readonly List<Color> colors = new(512);
        private readonly List<Vector2> uvs = new(512);
        private readonly List<int> triangles = new(768);
        private Matrix4x4 detailWorldToLocal;
        private bool wasAiming;
        private bool wasValid;
        private float focusStarted;

        private void Awake() => Hide();

        public void PrepareForUse()
        {
            gameObject.SetActive(true);
            enabled = true;
            // Positions belong to the grapple. Appearance remains prefab-owned.
            preview.useWorldSpace = rope.useWorldSpace = reticle.useWorldSpace = true;
            preview.positionCount = rope.positionCount = 2;
            reticle.positionCount = Mathf.Max(3, reticle.positionCount);
            reticle.loop = true;
            if (aimDetails != null && detailMesh == null)
            {
                detailRenderer = aimDetails.GetComponent<Renderer>();
                detailMesh = new Mesh { name = "Grapple Aim Details (Runtime)" };
                detailMesh.MarkDynamic();
                aimDetails.sharedMesh = detailMesh;
                if (detailRenderer != null) detailRenderer.enabled = false;
            }
        }

        public void SetSword(Sprite sprite)
        {
            if (sword == null || !useWeaponIcon) return;
            sword.sprite = sprite;
            if (sprite == null) return;
            float size = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            sword.transform.localScale = Vector3.one * (swordVisualLength / Mathf.Max(0.01f, size));
        }

        public void Render(Vector3 origin, Vector3 end, Vector3 planeAxis,
            bool valid, bool aiming, Vector3 swordPosition, Vector3 anchor, bool deployed, Camera camera)
        {
            if (this == null) return;
            if (aiming && (!wasAiming || (valid && !wasValid))) focusStarted = Time.time;
            wasAiming = aiming;
            wasValid = valid;
            float radius = GetMarkerRadius(end, camera);
            float focus = 1f - Mathf.Clamp01((Time.time - focusStarted) / Mathf.Max(0.01f, focusPulseDuration));
            radius *= 1f + focus * focus * focusPulseAmount;
            Vector3 markerRight = camera != null ? camera.transform.right : planeAxis;
            Vector3 markerUp = camera != null ? camera.transform.up : Vector3.up;
            // A tiny camera-facing offset avoids surface z-fighting; the actual
            // target and all targeting/collision decisions remain unchanged.
            Vector3 markerCenter = end + (camera != null ? -camera.transform.forward * 0.015f : Vector3.zero);
            Color tint = valid ? validAimColor : invalidAimColor;
            Vector3 direction = (end - origin).normalized;
            float distance = Vector3.Distance(origin, end);
            float start = Mathf.Min(guideStartOffset, distance);
            float finish = Mathf.Max(start, distance - radius * 1.35f);
            if (preview != null)
            {
                preview.enabled = aiming;
                if (aiming)
                {
                    preview.startColor = WithAlpha(tint, guideOpacity * 0.3f);
                    preview.endColor = WithAlpha(tint, guideOpacity);
                    preview.SetPosition(0, origin + direction * start);
                    preview.SetPosition(1, origin + direction * finish);
                }
            }
            if (reticle != null)
            {
                reticle.enabled = aiming;
                if (aiming)
                {
                    reticle.startColor = reticle.endColor = WithAlpha(tint, detailRenderer != null ? 0.25f : 1f);
                    for (int i = 0; i < reticle.positionCount; i++)
                    {
                        float angle = i * Mathf.PI * 2f / reticle.positionCount;
                        reticle.SetPosition(i, markerCenter + (markerRight * Mathf.Cos(angle) + markerUp * Mathf.Sin(angle)) * radius * 0.78f);
                    }
                }
            }
            DrawAimDetails(origin, direction, start, finish, markerCenter, markerRight, markerUp, radius, tint, valid, aiming);
            if (rope != null)
            {
                rope.enabled = deployed;
                if (deployed)
                {
                    // Tile mode and texture scale are authored on the renderer,
                    // so the braid repeats per world unit instead of stretching.
                    rope.SetPosition(0, origin);
                    rope.SetPosition(1, swordPosition);
                }
            }
            if (sword == null) return;
            sword.enabled = deployed && sword.sprite != null;
            if (!deployed) return;
            sword.transform.position = swordPosition;
            if (camera != null)
            {
                Vector3 screenDirection = camera.transform.InverseTransformDirection(anchor - origin);
                float angle = Mathf.Atan2(screenDirection.y, screenDirection.x) * Mathf.Rad2Deg;
                sword.transform.rotation = camera.transform.rotation *
                    Quaternion.Euler(0f, 0f, angle + swordSpriteAngleOffset);
            }
        }

        public void HideAim()
        {
            wasAiming = false;
            if (preview != null) preview.enabled = false;
            if (reticle != null) reticle.enabled = false;
            if (detailRenderer != null) detailRenderer.enabled = false;
            else if (aimDetails != null && aimDetails.TryGetComponent<Renderer>(out var renderer)) renderer.enabled = false;
        }

        private float GetMarkerRadius(Vector3 position, Camera camera)
        {
            if (!scaleMarkerWithCamera || camera == null || camera.pixelHeight <= 0) return reticleRadius;
            float viewHeight = camera.orthographic ? camera.orthographicSize * 2f :
                2f * Mathf.Max(camera.nearClipPlane, Vector3.Dot(position - camera.transform.position, camera.transform.forward)) *
                Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return Mathf.Clamp(viewHeight * reticlePixelRadius / camera.pixelHeight,
                Mathf.Max(0.01f, worldRadiusLimits.x), Mathf.Max(worldRadiusLimits.x, worldRadiusLimits.y));
        }

        private void DrawAimDetails(Vector3 origin, Vector3 direction, float start, float finish,
            Vector3 center, Vector3 right, Vector3 up, float radius, Color tint, bool valid, bool aiming)
        {
            if (detailRenderer == null || detailMesh == null || aimDetails == null) return;
            detailRenderer.enabled = aiming;
            if (!aiming) return;
            vertices.Clear(); colors.Clear(); uvs.Clear(); triangles.Clear();
            detailWorldToLocal = aimDetails.transform.worldToLocalMatrix;
            Vector3 viewNormal = Vector3.Cross(right, up).normalized;
            float width = radius * bracketThickness;
            float span = finish - start;
            float spacing = Mathf.Max(dashSpacing, span / Mathf.Max(4, maximumDashes));
            float phase = valid ? Mathf.Repeat(Time.time * dashTravelSpeed, spacing) : 0f;
            // One bounded, reused mesh holds every dash and marker detail.
            for (int i = 0; i < maximumDashes; i++)
            {
                float from = start + phase + i * spacing;
                if (from >= finish) break;
                float to = Mathf.Min(from + Mathf.Min(dashLength, spacing * 0.65f), finish);
                float alpha = Mathf.Lerp(0.35f, valid ? 1f : 0.65f, (from - start) / Mathf.Max(0.01f, span));
                Stroke(origin + direction * from, origin + direction * to, viewNormal, width * 0.8f, WithAlpha(tint, alpha));
            }
            // Four open corners remain readable against both dark and bright scenery.
            // Draw all backing strokes first, so overlapping corners and the X
            // cannot cover each other's foreground with a later dark stroke.
            for (int pass = 0; pass < 2; pass++)
            {
                Vector3 layerCenter = center - viewNormal * (pass * 0.001f);
                Color layerColor = pass == 0 ? contrastColor : tint;
                float layerWidth = pass == 0 ? width * 2.6f : width;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    Vector3 corner = layerCenter + (right * x + up * y) * radius;
                    Vector3 a = corner - right * x * radius * 0.46f;
                    Vector3 b = corner - up * y * radius * 0.46f;
                    Stroke(a, corner, viewNormal, layerWidth, layerColor);
                    Stroke(corner, b, viewNormal, layerWidth, layerColor);
                }
                if (!valid)
                {
                    Vector3 diagonalA = (right + up) * radius * 0.25f;
                    Vector3 diagonalB = (right - up) * radius * 0.25f;
                    Stroke(layerCenter - diagonalA, layerCenter + diagonalA, viewNormal, layerWidth, layerColor);
                    Stroke(layerCenter - diagonalB, layerCenter + diagonalB, viewNormal, layerWidth, layerColor);
                }
            }
            if (valid)
            {
                float r = radius * 0.18f;
                Quad(center + up * r, center + right * r, center - up * r, center - right * r, centerColor);
            }
            detailMesh.Clear();
            detailMesh.SetVertices(vertices); detailMesh.SetColors(colors); detailMesh.SetUVs(0, uvs);
            detailMesh.SetTriangles(triangles, 0, true);
        }

        private static Color WithAlpha(Color color, float opacity) => new(color.r, color.g, color.b, color.a * opacity);

        private void Stroke(Vector3 a, Vector3 b, Vector3 normal, float width, Color color)
        {
            Vector3 side = Vector3.Cross(normal, b - a).normalized * width * 0.5f;
            Quad(a - side, a + side, b + side, b - side, color);
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            int first = vertices.Count;
            vertices.Add(detailWorldToLocal.MultiplyPoint3x4(a)); vertices.Add(detailWorldToLocal.MultiplyPoint3x4(b));
            vertices.Add(detailWorldToLocal.MultiplyPoint3x4(c)); vertices.Add(detailWorldToLocal.MultiplyPoint3x4(d));
            for (int i = 0; i < 4; i++) colors.Add(color);
            uvs.Add(Vector2.zero); uvs.Add(Vector2.up); uvs.Add(Vector2.one); uvs.Add(Vector2.right);
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }

        private void OnDestroy() => ReleaseDetailMesh();

        private void ReleaseDetailMesh()
        {
            if (detailMesh == null) return;
            if (aimDetails != null) aimDetails.sharedMesh = null;
            if (Application.isPlaying) Destroy(detailMesh);
            else DestroyImmediate(detailMesh);
            detailMesh = null;
        }

        public void Hide()
        {
            // The independent visual root can unload before its owning player.
            HideAim();
            if (rope != null) rope.enabled = false;
            if (sword != null) sword.enabled = false;
        }

        public void Dispose()
        {
            if (this == null) return;
            ReleaseDetailMesh();
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }
    }
}
