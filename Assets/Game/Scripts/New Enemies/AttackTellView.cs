using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Animates an authored attack warning sign (it builds nothing). On <see cref="Play"/> the sign
    /// pops in, and the closing ring shrinks from its start scale onto its authored scale exactly
    /// when the hit lands: the parry read. Pulse sprites blink faster as the hit nears.
    /// Sits on the enemy's attackWarningVfx object; EnemyCharacter calls Play when it shows.
    /// An unparryable tell (e.g. the Brute grab) tints every sprite and shakes the sign:
    /// "dodge this, don't parry it".
    /// </summary>
    public sealed class AttackTellView : MonoBehaviour
    {
        [Tooltip("Ring that closes in on the hit (e.g. Outer Circle). It ends at its authored scale.")]
        [SerializeField] private Transform closingRing;
        [Tooltip("Closing ring's scale at the start, times its authored scale.")]
        [SerializeField, Min(1f)] private float ringStartScale = 2.6f;
        [Tooltip("Overshoot of the whole sign when it appears, times its authored scale.")]
        [SerializeField, Min(1f)] private float popScale = 1.4f;
        [SerializeField, Min(0.01f)] private float popDuration = 0.12f;
        [Tooltip("Sprites whose alpha blinks, faster near the hit (e.g. Inner).")]
        [SerializeField] private SpriteRenderer[] pulseSprites;
        [SerializeField, Min(0f)] private float pulseStartHz = 4f;
        [SerializeField, Min(0f)] private float pulseEndHz = 14f;

        [Header("Unparryable")]
        [Tooltip("Colour of every sprite in the sign when the attack can't be parried (alpha keeps the authored value).")]
        [SerializeField] private Color unparryableTint = new Color(1f, 0.12f, 0.12f, 1f);
        [Tooltip("Shake distance (local units) of the unparryable sign.")]
        [SerializeField, Min(0f)] private float shakeAmplitude = 0.08f;
        [SerializeField, Min(0f)] private float shakeHz = 30f;

        private Vector3 rootScale;
        private Vector3 ringScale;
        private float[] pulseAlpha;
        private SpriteRenderer[] tintSprites;
        private Color[] tintBaseColors;
        private Vector3 shakeOffset;
        private float elapsed;
        private float duration;
        private bool playing;
        private bool unparryable;

        private void Awake()
        {
            rootScale = transform.localScale;
            if (closingRing != null) ringScale = closingRing.localScale;
            pulseAlpha = new float[pulseSprites != null ? pulseSprites.Length : 0];
            for (int i = 0; i < pulseAlpha.Length; i++)
                pulseAlpha[i] = pulseSprites[i] != null ? pulseSprites[i].color.a : 1f;

            tintSprites = GetComponentsInChildren<SpriteRenderer>(true);
            tintBaseColors = new Color[tintSprites.Length];
            for (int i = 0; i < tintSprites.Length; i++)
                tintBaseColors[i] = tintSprites[i].color;
        }

        /// <summary>Start the tell; the ring closes over <paramref name="secondsToHit"/>. Unparryable: red and shaking.</summary>
        public void Play(float secondsToHit, bool unparryableAttack = false)
        {
            duration = Mathf.Max(0.05f, secondsToHit);
            elapsed = 0f;
            playing = true;
            unparryable = unparryableAttack;
            ApplyTint(unparryable);
            ClearShake();
            Apply();
        }

        private void Update()
        {
            if (!playing) return;
            elapsed += Time.deltaTime;
            Apply();
        }

        private void Apply()
        {
            float t = Mathf.Clamp01(elapsed / duration);

            // Pop: overshoot then settle.
            float pop = Mathf.Clamp01(elapsed / popDuration);
            transform.localScale = rootScale * Mathf.Lerp(popScale, 1f, pop * pop);

            if (closingRing != null)
                closingRing.localScale = ringScale * Mathf.Lerp(ringStartScale, 1f, t * t);

            float hz = Mathf.Lerp(pulseStartHz, pulseEndHz, t);
            float blink = 0.55f + 0.45f * Mathf.Cos(elapsed * hz * Mathf.PI * 2f);
            for (int i = 0; i < pulseAlpha.Length; i++)
            {
                SpriteRenderer sprite = pulseSprites[i];
                if (sprite == null) continue;
                Color c = sprite.color;
                c.a = pulseAlpha[i] * (t >= 1f ? 1f : blink);
                sprite.color = c;
            }

            if (unparryable && shakeAmplitude > 0f)
            {
                // Remove last frame's offset first, so a parent moving the sign is never fought.
                float phase = elapsed * shakeHz;
                Vector3 next = new Vector3(
                    (Mathf.PerlinNoise(phase, 0.37f) - 0.5f) * 2f,
                    (Mathf.PerlinNoise(0.71f, phase) - 0.5f) * 2f,
                    0f) * shakeAmplitude;
                transform.localPosition += next - shakeOffset;
                shakeOffset = next;
            }
        }

        // Sets RGB only; alpha stays with the authored value and the pulse.
        private void ApplyTint(bool tinted)
        {
            if (tintSprites == null) return;
            for (int i = 0; i < tintSprites.Length; i++)
            {
                SpriteRenderer sprite = tintSprites[i];
                if (sprite == null) continue;
                Color rgb = tinted ? unparryableTint : tintBaseColors[i];
                sprite.color = new Color(rgb.r, rgb.g, rgb.b, sprite.color.a);
            }
        }

        private void ClearShake()
        {
            transform.localPosition -= shakeOffset;
            shakeOffset = Vector3.zero;
        }

        private void OnDisable()
        {
            playing = false;
            unparryable = false;
            ClearShake();
            ApplyTint(false);
            if (rootScale != Vector3.zero) transform.localScale = rootScale;
            if (closingRing != null && ringScale != Vector3.zero) closingRing.localScale = ringScale;
            for (int i = 0; i < pulseAlpha?.Length; i++)
            {
                if (pulseSprites[i] == null) continue;
                Color c = pulseSprites[i].color;
                c.a = pulseAlpha[i];
                pulseSprites[i].color = c;
            }
        }
    }
}
