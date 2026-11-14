using UnityEngine;
using TMPro;

namespace junklite
{
    public class DamagePopup : MonoBehaviour
    {
        private TextMeshPro textMesh;
        private DamagePopupManager owner;
        private Camera viewCamera;
        private Color textColor;
        private float elapsed;
        private float moveSpeed;
        private float lifetime;
        private Vector3 originalScale;
        private Vector3 restingScale;
        private Vector3 spawnPosition;
        private FontStyles originalFontStyle;
        private float originalCharacterSpacing;
        private float glyphHeight;
        private float emphasis;
        private float horizontalTravel;
        private bool experiencePopup;
        private bool tickPopup;

        private void Awake()
        {
            textMesh = GetComponent<TextMeshPro>();
            UIFonts.ApplyWorld(textMesh);
            originalScale = transform.localScale;
            originalFontStyle = textMesh.fontStyle;
            originalCharacterSpacing = textMesh.characterSpacing;
            textMesh.alignment = TextAlignmentOptions.Center;
            textMesh.textWrappingMode = TextWrappingModes.NoWrap;
            textMesh.overflowMode = TextOverflowModes.Overflow;
            textMesh.enableAutoSizing = false;
            textMesh.raycastTarget = false;
        }

        public void Bind(DamagePopupManager manager, Camera camera)
        {
            owner = manager;
            viewCamera = camera;
        }

        public void Setup(float damageAmount, DamageType type = DamageType.Physical,
            bool isTickDamage = false, float sideTravel = 0f)
        {
            if (textMesh == null) Awake();
            if (owner == null) owner = DamagePopupManager.Instance;
            moveSpeed = owner != null ? owner.MoveSpeed : 1f;
            lifetime = owner != null ? owner.Lifetime : 1f;
            tickPopup = isTickDamage;
            horizontalTravel = sideTravel;
            emphasis = owner != null ? owner.GetDamageScale(damageAmount, isTickDamage) : 1f;
            textColor = owner != null ? owner.GetDamageColor(type) : Color.white;

            // Keep small successful hits visible instead of rounding them down to a misleading zero.
            if (damageAmount < 1f)
                textMesh.SetText(damageAmount < 0.1f ? "<0.1" : "{0:1}", damageAmount);
            else
                textMesh.SetText("{0:0}", Mathf.Round(damageAmount));

            textMesh.fontStyle = isTickDamage ? FontStyles.Bold : FontStyles.Bold | FontStyles.Italic;
            textMesh.characterSpacing = 1.5f;
            textMesh.colorGradientPreset = null;
            textMesh.enableVertexGradient = true;
            // A restrained face highlight; the shared plain overlay material stays untouched.
            Color top = Color.Lerp(textColor, Color.white, 0.22f);
            Color bottom = textColor;
            top.a = bottom.a = 1f;
            textMesh.colorGradient = new VertexGradient(top, top, bottom, bottom);
            textMesh.color = Color.white;
            ApplyGlow(owner != null ? owner.DamageGlowMaterial : null);
            textMesh.ForceMeshUpdate();
            glyphHeight = Mathf.Max(0.01f, textMesh.textBounds.size.y);

            Begin(false, emphasis);
        }

        public void SetupExperience(long amount, PlayerProgressionConfig config)
        {
            if (textMesh == null) Awake();
            if (owner == null) owner = DamagePopupManager.Instance;
            ApplyGlow(owner != null ? owner.ExperienceGlowMaterial : null);
            textMesh.text = $"+{amount} XP";
            textColor = config.xpColor;
            moveSpeed = config.xpPopupRiseSpeed;
            lifetime = config.xpPopupLifetime;
            tickPopup = false;
            horizontalTravel = 0f;
            textMesh.enableVertexGradient = false;
            textMesh.fontStyle = originalFontStyle;
            textMesh.characterSpacing = originalCharacterSpacing;
            Begin(true, config.xpPopupScale);
        }

        // Glow materials share the Play font atlas; without one the plain world material stays.
        private void ApplyGlow(Material glow)
        {
            if (glow != null) textMesh.fontSharedMaterial = glow;
            else UIFonts.ApplyWorld(textMesh);
        }

        private void Begin(bool isExperience, float scale)
        {
            experiencePopup = isExperience;
            restingScale = originalScale * scale;
            spawnPosition = transform.position;
            elapsed = 0f;
            RenderFrame();
        }

        private void LateUpdate()
        {
            elapsed += Time.deltaTime;
            RenderFrame();
            if (elapsed < Mathf.Max(0.01f, lifetime)) return;
            if (owner != null) owner.ReturnPopup(this);
            else gameObject.SetActive(false);
        }

        private void RenderFrame()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera != null) transform.rotation = viewCamera.transform.rotation;

            float life = Mathf.Max(0.01f, lifetime);
            float t = Mathf.Clamp01(elapsed / life);

            // Hop up fast with a small overshoot, then drift slowly while it holds.
            float hop = BackOut(Mathf.Clamp01(elapsed / 0.4f));
            float rise = 0.75f * hop + 0.25f * t;
            transform.position = spawnPosition + transform.up * (moveSpeed * life * rise)
                + transform.right * (horizontalTravel * (1f - (1f - t) * (1f - t)));

            // Fully visible until the last Fade Duration seconds, then a quick fade.
            float fadeDuration = owner != null ? owner.FadeDuration : 0.25f;
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(life - fadeDuration, life, elapsed));
            Color fadedColor = experiencePopup ? textColor : Color.white;
            fadedColor.a = textColor.a * (1f - fade);
            textMesh.color = fadedColor;

            // Spring pop-in: starts small, overshoots past full size, wobbles and settles; shrinks a little as it fades.
            float start = owner != null ? owner.PopStartScale : 0.2f;
            if (tickPopup) start = Mathf.Lerp(start, 1f, 0.5f);
            float damping = owner != null ? owner.BounceDamping : 9f;
            float frequency = owner != null ? owner.BounceFrequency : 24f;
            float animatedScale = 1f + (start - 1f) * Mathf.Exp(-damping * elapsed) * Mathf.Cos(frequency * elapsed);
            animatedScale *= Mathf.Lerp(1f, 0.85f, fade);

            Vector3 scale = restingScale;
            textMesh.renderer.enabled = true;
            if (!experiencePopup && viewCamera != null)
            {
                // Normalize readability across zooms without generating text meshes every frame.
                float depth = Vector3.Dot(transform.position - viewCamera.transform.position,
                    viewCamera.transform.forward);
                textMesh.renderer.enabled = depth >= viewCamera.nearClipPlane && depth <= viewCamera.farClipPlane;
                float viewHeight = viewCamera.orthographic ? viewCamera.orthographicSize * 2f
                    : 2f * Mathf.Max(viewCamera.nearClipPlane, depth)
                        * Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float pixels = owner != null ? owner.DamageTextHeight : 28f;
                float worldHeight = Mathf.Clamp(viewHeight * pixels / 1080f, 0.16f, 0.75f) * emphasis;
                float parentScale = transform.parent != null ? Mathf.Abs(transform.parent.lossyScale.y) : 1f;
                scale = originalScale * (worldHeight / Mathf.Max(0.001f, glyphHeight * Mathf.Abs(originalScale.y) * parentScale));
            }
            transform.localScale = scale * animatedScale;
        }

        private static float BackOut(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float p = x - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}
