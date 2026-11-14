using UnityEngine;
using UnityEngine.Rendering;

namespace junklite
{
    public enum CameraOcclusionMode
    {
        /// <summary>Renderers stop drawing but keep casting shadows. Works with any shader.</summary>
        Cutaway = 0,
        /// <summary>Renderers swap to one shared transparent URP Lit material and fade. Non-Lit materials cut away.</summary>
        Fade = 1
    }

    /// <summary>
    /// Art that gets out of the way when it sits between the gameplay camera and the player.
    /// Put trigger BoxColliders on the CameraOcclusion layer on this object, sized around the wall,
    /// and point artRoot (or targets) at the meshes. CameraOcclusionFader drives it.
    /// No per-material setup: originals are restored exactly when the player is visible again.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraOccluder : MonoBehaviour
    {
        public const string LayerName = "CameraOcclusion";

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Renderers under this root are collected into Targets in the Editor.")]
        [SerializeField] private Transform artRoot;
        [SerializeField] private Renderer[] targets = new Renderer[0];

        [SerializeField] private CameraOcclusionMode mode = CameraOcclusionMode.Cutaway;
        [Tooltip("Fade mode only. The shared transparent URP Lit material (OccluderFade.mat).")]
        [SerializeField] private Material fadeMaterial;
        [Tooltip("Fade mode only. Alpha while occluding. 0 ends as a cutaway.")]
        [SerializeField, Range(0f, 1f)] private float fadedAlpha = 0.2f;
        [Tooltip("Fade mode only. Seconds (unscaled) to fade out or back in.")]
        [SerializeField, Min(0f)] private float fadeDuration = 0.2f;

        private ShadowCastingMode[] originalShadows;
        private Material[][] originalMaterials;
        private Material[][] fadeMaterials;
        private bool[] canFade;
        private MaterialPropertyBlock block;

        private bool occluded;
        private float alpha = 1f;
        private bool swapped;

        public Renderer[] Targets => targets;
        public bool IsOccluded => occluded;

        private void Awake()
        {
            int count = targets.Length;
            originalShadows = new ShadowCastingMode[count];
            for (int i = 0; i < count; i++)
                if (targets[i] != null) originalShadows[i] = targets[i].shadowCastingMode;

            if (mode == CameraOcclusionMode.Fade)
                CacheFadeData();
        }

        private void CacheFadeData()
        {
            int count = targets.Length;
            originalMaterials = new Material[count][];
            fadeMaterials = new Material[count][];
            canFade = new bool[count];
            block = new MaterialPropertyBlock();
            bool warned = false;

            for (int i = 0; i < count; i++)
            {
                Renderer r = targets[i];
                if (r == null) continue;

                Material[] shared = r.sharedMaterials;
                originalMaterials[i] = shared;
                bool ok = fadeMaterial != null;
                for (int m = 0; m < shared.Length && ok; m++)
                    ok = shared[m] != null && shared[m].HasProperty(BaseMapId);

                canFade[i] = ok;
                if (!ok)
                {
                    if (!warned && fadeMaterial != null)
                        Debug.LogWarning($"[CameraOccluder] '{name}': '{r.name}' has a non-URP-Lit material; it cuts away instead of fading.", r);
                    warned = true;
                    continue;
                }

                Material[] faded = new Material[shared.Length];
                for (int m = 0; m < faded.Length; m++) faded[m] = fadeMaterial;
                fadeMaterials[i] = faded;
            }
        }

        /// <summary>Returns true when the change needs Step calls to finish animating.</summary>
        public bool SetOccluded(bool value)
        {
            if (occluded == value || originalShadows == null) return false;
            occluded = value;

            if (mode == CameraOcclusionMode.Cutaway || fadeDuration <= 0f)
            {
                alpha = value ? FadeTarget : 1f;
                Apply();
                return false;
            }

            return true;
        }

        /// <summary>Advances a fade. Returns false once it has settled.</summary>
        public bool Step(float deltaTime)
        {
            float target = occluded ? FadeTarget : 1f;
            alpha = Mathf.MoveTowards(alpha, target, deltaTime / fadeDuration);
            Apply();
            return !Mathf.Approximately(alpha, target);
        }

        private float FadeTarget => mode == CameraOcclusionMode.Fade ? fadedAlpha : 0f;

        private void Apply()
        {
            bool visible = alpha >= 0.999f;
            bool hidden = alpha <= 0.001f;

            for (int i = 0; i < targets.Length; i++)
            {
                Renderer r = targets[i];
                if (r == null) continue;

                bool fades = mode == CameraOcclusionMode.Fade && canFade != null && canFade[i];
                if (!fades)
                {
                    // Cutaway: any non-visible state hides the mesh but keeps its shadow.
                    r.shadowCastingMode = visible ? originalShadows[i] : ShadowCastingMode.ShadowsOnly;
                    continue;
                }

                r.shadowCastingMode = hidden ? ShadowCastingMode.ShadowsOnly : originalShadows[i];
                if (visible) RestoreMaterials(i, r);
                else ApplyFade(i, r);
            }

            swapped = !visible && mode == CameraOcclusionMode.Fade;
        }

        private void ApplyFade(int i, Renderer r)
        {
            Material[] originals = originalMaterials[i];
            if (!swapped) r.sharedMaterials = fadeMaterials[i];

            for (int m = 0; m < originals.Length; m++)
            {
                Material source = originals[m];
                block.Clear();
                Texture baseMap = source.GetTexture(BaseMapId);
                block.SetTexture(BaseMapId, baseMap != null ? baseMap : Texture2D.whiteTexture);
                if (source.HasProperty(BaseMapStId)) block.SetVector(BaseMapStId, source.GetVector(BaseMapStId));
                Color color = source.HasProperty(BaseColorId) ? source.GetColor(BaseColorId) : Color.white;
                color.a *= alpha;
                block.SetColor(BaseColorId, color);
                r.SetPropertyBlock(block, m);
            }
        }

        private void RestoreMaterials(int i, Renderer r)
        {
            if (!swapped) return;
            r.sharedMaterials = originalMaterials[i];
            Material[] originals = originalMaterials[i];
            for (int m = 0; m < originals.Length; m++)
                r.SetPropertyBlock(null, m);
        }

        private void OnDisable()
        {
            // Never leave art hidden or swapped when the occluder goes away mid-occlusion.
            if (originalShadows == null || (!occluded && alpha >= 0.999f)) return;
            occluded = false;
            alpha = 1f;
            Apply();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (artRoot != null)
                targets = artRoot.GetComponentsInChildren<Renderer>(true);
        }
#endif
    }
}
