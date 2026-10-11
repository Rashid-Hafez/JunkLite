using UnityEngine;
using TMPro;

namespace junklite
{
    public class DamagePopup : MonoBehaviour
    {
        private TextMeshPro textMesh;
        private Color textColor;
        private float elapsed;
        private float moveSpeed;
        private float lifetime;
        private Vector3 originalScale;
        private Vector3 restingScale;
        private bool experiencePopup;

        private void Awake()
        {
            textMesh = GetComponent<TextMeshPro>();
            UIFonts.ApplyWorld(textMesh);
            originalScale = transform.localScale;
        }

        public void Setup(float damageAmount)
        {
            var manager = DamagePopupManager.Instance;

            // Get settings from manager
            moveSpeed = manager != null ? manager.MoveSpeed : 1f;
            lifetime = manager != null ? manager.Lifetime : 1f;

            // Set text
            textMesh.text = Mathf.RoundToInt(damageAmount).ToString();

            // Set color based on damage amount
            if (manager != null)
            {
                if (damageAmount >= manager.HighDamageThreshold)
                    textColor = manager.HighDamageColor;
                else if (damageAmount >= manager.MediumDamageThreshold)
                    textColor = manager.MediumDamageColor;
                else
                    textColor = manager.LowDamageColor;
            }
            else
            {
                textColor = Color.white;
            }

            Begin(false, 1f);
        }

        public void SetupExperience(long amount, PlayerProgressionConfig config)
        {
            textMesh.text = $"+{amount} XP";
            textColor = config.xpColor;
            moveSpeed = config.xpPopupRiseSpeed;
            lifetime = config.xpPopupLifetime;
            Begin(true, config.xpPopupScale);
        }

        private void Begin(bool isExperience, float scale)
        {
            experiencePopup = isExperience;
            restingScale = originalScale * scale;
            transform.localScale = restingScale * (isExperience ? 0.65f : 1f);
            textMesh.color = textColor;
            elapsed = 0f;
        }

        private void Update()
        {
            // Move up
            Vector3 direction = experiencePopup ? transform.up : Vector3.up;
            transform.position += direction * moveSpeed * Time.deltaTime;

            // Fade out
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, lifetime));
            Color fadedColor = textColor;
            // Give rewards a short readable hold before fading, plus a quick pop/settle.
            fadedColor.a *= 1f - (experiencePopup ? Mathf.InverseLerp(0.35f, 1f, t) : t);
            textMesh.color = fadedColor;
            if (experiencePopup)
            {
                float pop = Mathf.Clamp01(t / 0.2f);
                float scale = pop < 0.5f
                    ? Mathf.Lerp(0.65f, 1.18f, Mathf.SmoothStep(0f, 1f, pop * 2f))
                    : Mathf.Lerp(1.18f, 1f, Mathf.SmoothStep(0f, 1f, (pop - 0.5f) * 2f));
                transform.localScale = restingScale * scale;
            }

            // Return to pool when fully faded
            if (t >= 1f)
                DamagePopupManager.Instance?.ReturnPopup(this);
        }
    }
}
