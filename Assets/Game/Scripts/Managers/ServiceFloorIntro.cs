using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Service Floor opening: Cassidy comes out of the simulation with her system failing.
    /// The screen and HUD glitch (New UI art tearing across an overlay canvas), she takes a few hits
    /// through the normal damage pipeline (hurt anim, SFX, shake, health bar), the glitch calms,
    /// then Crow's transmission dialogue plays. Runs once per scene load.
    /// The glitch uses its own runtime Volume, so no shared profile can be left glitched.
    /// </summary>
    public class ServiceFloorIntro : MonoBehaviour
    {
        // Must outrank the CinemachineVolumeSettings volumes (~998), same as CombatModeScreenGlitch.
        private const float VolumePriority = 10000f;
        private const int TearCount = 10;

        [SerializeField] private DialogueSequence transmissionDialogue;

        [Header("Timing (unscaled seconds)")]
        [SerializeField] private float glitchDuration = 3.6f;
        [SerializeField] private float calmDuration = 1.6f;
        [SerializeField] private float holdAfterCalm = 0.4f;
        [SerializeField] private float[] hitTimes = { 1.4f, 2.3f, 3.2f }; // after the 1.25 s spawn invulnerability
        [SerializeField] private float hitDamage = 12f;

        [Header("HUD glitch art (New UI)")]
        [SerializeField] private Sprite tearSprite;
        [SerializeField] private Sprite bandSprite;
        [SerializeField] private Sprite diagnosticSprite;
        [SerializeField] private string warningText = "// C.M.A. OPTIC  -  SYSTEM INTEGRITY CRITICAL";
        [SerializeField] private Color hurtColor = new Color(1f, 0.2f, 0.28f, 1f);
        [SerializeField] private Color cyan = new Color(0.24f, 0.91f, 0.98f, 1f);
        [SerializeField] private Color magenta = new Color(0.86f, 0.25f, 0.89f, 1f);
        [SerializeField] private float flickerInterval = 0.05f;

        private Volume volume;
        private VolumeProfile profile;
        private GameObject overlay;
        private Image[] tears;
        private Image diagnostic;
        private TextMeshProUGUI warning;
        private RectTransform hud;
        private CanvasGroup hudGroup;
        private Vector2 hudRestPosition;
        private float hudRestAlpha = 1f;
        private IDisposable inputLock;
        private bool dialogueFinished;

        private void Start() => StartCoroutine(Run());

        private void OnDestroy()
        {
            Cleanup();
            if (DialogueManager.instance != null)
                DialogueManager.instance.OnDialogueEnded -= OnDialogueEnded;
        }

        private IEnumerator Run()
        {
            PlayerCharacter player;
            while ((player = PlayerLifecycle.Instance != null ? PlayerLifecycle.Instance.Player : null) == null)
                yield return null;

            inputLock = player.State != null ? player.State.AcquireInputLock() : null;
            player.Controller?.StopAllVelocity();
            BuildGlitch();

            int nextHit = 0;
            for (float t = 0f; t < glitchDuration + calmDuration; t += Time.unscaledDeltaTime)
            {
                if (nextHit < hitTimes.Length && t >= hitTimes[nextHit])
                {
                    nextHit++;
                    DamageReceiverUtility.Receive(player, new DamageRequest(hitDamage, gameObject));
                }

                // Full strength with hard spikes, then a smooth calm-down.
                float strength = t < glitchDuration
                    ? Mathf.Lerp(0.55f, 1f, Mathf.PerlinNoise(t * 6f, 0.3f)) * (UnityEngine.Random.value < 0.08f ? 0.15f : 1f)
                    : 1f - Mathf.SmoothStep(0f, 1f, (t - glitchDuration) / calmDuration);
                ApplyGlitch(strength, t);
                yield return null;
            }

            Cleanup();
            yield return new WaitForSecondsRealtime(holdAfterCalm);

            if (transmissionDialogue != null && DialogueManager.instance != null)
            {
                dialogueFinished = false;
                DialogueManager.instance.OnDialogueEnded += OnDialogueEnded;
                DialogueManager.instance.StartDialogue(transmissionDialogue);
                while (!dialogueFinished)
                    yield return null;
                DialogueManager.instance.OnDialogueEnded -= OnDialogueEnded;
            }
        }

        private void OnDialogueEnded() => dialogueFinished = true;

        #region Glitch

        private void BuildGlitch()
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "ServiceFloorIntroGlitch (runtime)";
            var analog = profile.Add<URPGlitch.AnalogGlitchVolume>(overrides: true);
            var digital = profile.Add<URPGlitch.DigitalGlitchVolume>(overrides: true);
            analog.scanLineJitter.value = 0.6f;
            analog.verticalJump.value = 0.08f;
            analog.horizontalShake.value = 0.25f;
            analog.colorDrift.value = 0.55f;
            digital.intensity.value = 0.35f;

            var volumeGo = new GameObject("ServiceFloorIntroGlitchVolume");
            volumeGo.transform.SetParent(transform, false);
            volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = VolumePriority;
            volume.sharedProfile = profile;

            overlay = new GameObject("ServiceFloorIntroGlitchHUD", typeof(RectTransform));
            overlay.transform.SetParent(transform, false);
            var canvas = overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 70;
            var scaler = overlay.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            diagnostic = NewImage("Diagnostic", diagnosticSprite, hurtColor);
            diagnostic.preserveAspect = true;
            RectTransform d = diagnostic.rectTransform;
            d.anchorMin = d.anchorMax = d.pivot = new Vector2(0f, 0.5f);
            d.anchoredPosition = new Vector2(70f, -20f);
            d.sizeDelta = new Vector2(220f, 600f);

            tears = new Image[TearCount];
            for (int i = 0; i < TearCount; i++)
                tears[i] = NewImage("Tear", i % 3 == 0 ? bandSprite : tearSprite, Color.white);

            warning = new GameObject("Warning", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            warning.transform.SetParent(overlay.transform, false);
            UIFonts.ApplyHudBody(warning);
            warning.text = warningText;
            warning.fontSize = 30f;
            warning.color = hurtColor;
            warning.alignment = TextAlignmentOptions.Center;
            warning.raycastTarget = false;
            RectTransform w = warning.rectTransform;
            w.anchorMin = w.anchorMax = w.pivot = new Vector2(0.5f, 1f);
            w.anchoredPosition = new Vector2(0f, -150f);
            w.sizeDelta = new Vector2(1400f, 50f);

            // The live HUD shudders with the screen.
            PlayerUI playerHud = GameUIManager.Instance != null ? GameUIManager.Instance.PlayerHUD : null;
            if (playerHud != null)
            {
                hud = playerHud.transform as RectTransform;
                hudRestPosition = hud.anchoredPosition;
                hudGroup = playerHud.GetComponent<CanvasGroup>();
                if (hudGroup == null) hudGroup = playerHud.gameObject.AddComponent<CanvasGroup>();
                hudRestAlpha = hudGroup.alpha;
            }
        }

        private float nextFlicker;

        private void ApplyGlitch(float strength, float t)
        {
            volume.weight = strength;
            if (t < nextFlicker) return;
            nextFlicker = t + flickerInterval;

            for (int i = 0; i < tears.Length; i++)
            {
                Image tear = tears[i];
                bool on = UnityEngine.Random.value < strength * 0.7f;
                tear.enabled = on;
                if (!on) continue;
                RectTransform r = tear.rectTransform;
                r.anchoredPosition = new Vector2(UnityEngine.Random.Range(-500f, 500f), UnityEngine.Random.Range(-520f, 520f));
                r.sizeDelta = new Vector2(UnityEngine.Random.Range(300f, 1920f), UnityEngine.Random.Range(3f, i % 3 == 0 ? 60f : 14f));
                Color c = (i % 3) switch { 0 => cyan, 1 => magenta, _ => hurtColor };
                c.a = UnityEngine.Random.Range(0.35f, 0.9f) * strength;
                tear.color = c;
            }

            bool flash = UnityEngine.Random.value < strength;
            diagnostic.enabled = flash;
            diagnostic.rectTransform.anchoredPosition = new Vector2(70f + UnityEngine.Random.Range(-14f, 14f) * strength, -20f);
            warning.enabled = flash;
            warning.rectTransform.anchoredPosition = new Vector2(UnityEngine.Random.Range(-24f, 24f) * strength, -150f);

            if (hud != null)
            {
                hud.anchoredPosition = hudRestPosition + UnityEngine.Random.insideUnitCircle * (18f * strength);
                hudGroup.alpha = hudRestAlpha * (UnityEngine.Random.value < strength * 0.35f ? 0.25f : 1f);
            }
        }

        private Image NewImage(string name, Sprite sprite, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            image.transform.SetParent(overlay.transform, false);
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }

        private void Cleanup()
        {
            inputLock?.Dispose();
            inputLock = null;

            if (hud != null)
            {
                hud.anchoredPosition = hudRestPosition;
                if (hudGroup != null) hudGroup.alpha = hudRestAlpha;
                hud = null;
            }

            if (volume != null) Destroy(volume.gameObject);
            if (profile != null) Destroy(profile);
            if (overlay != null) Destroy(overlay);
            volume = null;
            profile = null;
            overlay = null;
        }

        #endregion
    }
}
