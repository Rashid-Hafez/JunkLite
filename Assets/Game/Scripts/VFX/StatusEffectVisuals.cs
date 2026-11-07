using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Enemy-owned status presentation. Reuses the animated source geometry and shared
    /// materials; never replaces Spine atlas materials or writes to the source's property block.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)] // Spine has finished updating its mesh for this frame.
    public sealed class StatusEffectVisuals : MonoBehaviour
    {
        [SerializeField] private BurnVisualProfile burnProfile;
        [Tooltip("Optional body root. EnemyCharacter supplies its authored enemyVisual automatically.")]
        [SerializeField] private Transform visualRoot;
        [Tooltip("Optional explicit body renderers. Otherwise finds sprites and Spine renderers under Visual Root.")]
        [SerializeField] private Renderer[] bodyRenderers;
        [Tooltip("Optional flame position/orientation. Without an anchor, flames fit the largest body renderer.")]
        [SerializeField] private Transform flameAnchor;
        [SerializeField, Min(0.1f)] private float flameScale = 1f;

        private readonly List<BurnRendererOverlay> overlays = new List<BurnRendererOverlay>();
        private StatusEffectHandler handler;
        private Transform excludedVisualRoot;
        private BurnFlameVisual flames;
        private Renderer primaryRenderer;
        private bool subscribed;
        private bool initialized;
        private bool warnedMissingProfile;
        private float lastTickTime = -10000f;
        private float seed;

        public bool IsBurning { get; private set; }
        public BurnVisualProfile Profile => burnProfile;

        public void Initialize(StatusEffectHandler statusHandler, Transform bodyRoot, Transform excludedRoot = null)
        {
            Unsubscribe();
            handler = statusHandler;
            excludedVisualRoot = excludedRoot;
            if (handler != null) handler.DisableLegacyBurnVisuals();
            if (visualRoot == null) visualRoot = bodyRoot;
            if (isActiveAndEnabled)
            {
                Subscribe();
                RefreshBurn();
            }
        }

        private void OnEnable()
        {
            if (handler == null) handler = GetComponent<StatusEffectHandler>();
            if (handler != null) handler.DisableLegacyBurnVisuals();
            Subscribe();
            RefreshBurn();
        }

        private void Subscribe()
        {
            if (subscribed || handler == null) return;
            handler.OnEffectApplied += OnStatusChanged;
            handler.OnEffectRefreshed += OnStatusChanged;
            handler.OnEffectRemoved += OnStatusChanged;
            handler.OnEffectTick += OnStatusTick;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (handler != null)
            {
                handler.OnEffectApplied -= OnStatusChanged;
                handler.OnEffectRefreshed -= OnStatusChanged;
                handler.OnEffectRemoved -= OnStatusChanged;
                handler.OnEffectTick -= OnStatusTick;
            }
            subscribed = false;
        }

        private void OnStatusChanged(StatusEffectType type)
        {
            if (type == StatusEffectType.Burn) RefreshBurn();
        }

        private void OnStatusTick(StatusEffectType type, float appliedDamage)
        {
            if (type == StatusEffectType.Burn && appliedDamage > 0f && IsBurning)
                lastTickTime = Time.time;
        }

        private void RefreshBurn()
        {
            // Removal is per instance: another independently stacked burn can still be alive.
            bool active = handler != null && handler.HasEffect(StatusEffectType.Burn);
            if (active && !initialized) InitializeVisuals();
            IsBurning = active && initialized;
            if (!IsBurning)
            {
                lastTickTime = -10000f;
                HideVisuals();
            }
        }

        private void InitializeVisuals()
        {
            if (burnProfile == null)
                burnProfile = Resources.Load<BurnVisualProfile>(BurnVisualProfile.ResourceName);
            if (burnProfile == null || !burnProfile.IsConfigured)
            {
                if (!warnedMissingProfile)
                {
                    Debug.LogWarning("Burn visuals need the Burn Visual Profile. Run Tools > JunkLite > VFX > Install Burn Visual Assets.", this);
                    warnedMissingProfile = true;
                }
                return;
            }

            seed = (GetInstanceID() & 1023) * 0.017f;
            Transform root = visualRoot != null ? visualRoot : transform;
            var sources = new List<Renderer>();
            if (bodyRenderers != null && bodyRenderers.Length > 0)
                sources.AddRange(bodyRenderers);
            else
            {
                // Spine enemies also contain sprite-based warnings and other presentation.
                // Prefer their actual skeleton meshes; sprite bodies are the fallback.
                foreach (SkeletonRenderer skeleton in root.GetComponentsInChildren<SkeletonRenderer>(true))
                {
                    if (IsExcluded(skeleton.transform)) continue;
                    Renderer renderer = skeleton.GetComponent<Renderer>();
                    if (renderer != null) sources.Add(renderer);
                }
                if (sources.Count == 0)
                    sources.AddRange(root.GetComponentsInChildren<SpriteRenderer>(true));
            }

            float largestArea = -1f;
            var unique = new HashSet<Renderer>();
            for (int i = 0; i < sources.Count; i++)
            {
                Renderer source = sources[i];
                if (source == null || !unique.Add(source)) continue;
                if (IsExcluded(source.transform)) continue;
                if (source.GetComponentInParent<StatusEffectVisuals>() != this) continue;
                // Skip particles, lines, UI and meshes without geometry.
                if (!(source is SpriteRenderer) && source.GetComponent<MeshFilter>() == null) continue;
                overlays.Add(new BurnRendererOverlay(source, burnProfile.heatMaterial));
                Vector3 size = Vector3.Scale(source.localBounds.size, source.transform.lossyScale);
                float area = Mathf.Abs(size.x * size.y);
                if (area > largestArea)
                {
                    primaryRenderer = source;
                    largestArea = area;
                }
            }

            if (primaryRenderer != null)
                flames = new BurnFlameVisual(primaryRenderer, burnProfile.flameMaterial);
            initialized = overlays.Count > 0;
        }

        private bool IsExcluded(Transform candidate) =>
            excludedVisualRoot != null && candidate.IsChildOf(excludedVisualRoot);

        private void LateUpdate()
        {
            if (!IsBurning) return;
            for (int i = 0; i < overlays.Count; i++)
                overlays[i].Sync(burnProfile, lastTickTime, seed);
            flames?.Sync(burnProfile, flameAnchor, flameScale, seed);
        }

        private void HideVisuals()
        {
            for (int i = 0; i < overlays.Count; i++) overlays[i].Hide();
            flames?.Hide();
        }

        private void OnDisable()
        {
            Unsubscribe();
            IsBurning = false;
            lastTickTime = -10000f;
            HideVisuals();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            IsBurning = false;
            for (int i = 0; i < overlays.Count; i++) overlays[i].Dispose();
            overlays.Clear();
            flames?.Dispose();
        }
    }
}
