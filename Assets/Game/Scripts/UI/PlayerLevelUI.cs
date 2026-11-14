using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.VFX;

namespace junklite
{
    /// <summary>Presentation only: binds without replaying rewards, and releases all transient feedback on hide.</summary>
    public sealed class PlayerLevelUI : MonoBehaviour
    {
        private readonly Image[] edges = new Image[4];
        private TMP_Text badge;
        private TMP_Text gain;
        private TMP_Text notification;
        private TMP_Text notificationTitle;
        private CanvasGroup notificationGroup;
        private RectTransform feedbackRoot;
        private RectTransform portrait;
        private PlayerProgression progression;
        private PlayerCharacter player;
        private Coroutine progressAnimation;
        private GameObject ripple;
        private float rippleRemaining;
        private AudioSource audioSource;
        private float displayedProgress;
        private float feedbackRemaining;
        private float gainRemaining;
        private long recentGain;
        private int celebrationFromLevel;

        public static PlayerLevelUI Create(RectTransform portrait, RectTransform hud)
        {
            var ui = portrait.gameObject.AddComponent<PlayerLevelUI>();
            ui.portrait = portrait;
            for (int i = 0; i < ui.edges.Length; i++)
            {
                Image track = MakeImage("XP Track " + i, portrait, new Color(0.18f, 0.27f, 0.38f, 1f));
                SetEdge(track.rectTransform, i, 1f, portrait.rect.width, portrait.rect.height);
            }
            // Keep every fill above the tracks, including their overlapping corner pixels.
            for (int i = 0; i < ui.edges.Length; i++)
            {
                ui.edges[i] = MakeImage("XP Edge " + i, portrait, Color.cyan);
            }
            Image badgeBackground = MakeImage("Level Badge", portrait, new Color(0.025f, 0.035f, 0.07f, 0.98f));
            // Keep the number below the perimeter so it never hides the lower edge.
            Place(badgeBackground.rectTransform, 19f, 68f, 28f, 20f);
            ui.badge = MakeText("Level", badgeBackground.transform, 13f);
            Stretch(ui.badge.rectTransform);
            ui.badge.alignment = TextAlignmentOptions.Center;
            ui.gain = MakeText("XP Gain", portrait, 12f);
            Place(ui.gain.rectTransform, -7f, -22f, 84f, 20f);
            ui.gain.alignment = TextAlignmentOptions.Center;
            ui.gain.text = "";

            Canvas canvas = hud.GetComponentInParent<Canvas>();
            Transform feedbackParent = canvas != null ? canvas.rootCanvas.transform : hud;
            var feedback = new GameObject("Level Up Feedback", typeof(RectTransform), typeof(Canvas));
            feedback.layer = hud.gameObject.layer;
            ui.feedbackRoot = (RectTransform)feedback.transform;
            ui.feedbackRoot.SetParent(feedbackParent, false);
            Stretch(ui.feedbackRoot);
            Canvas feedbackCanvas = feedback.GetComponent<Canvas>();
            feedbackCanvas.overrideSorting = true;
            feedbackCanvas.sortingLayerID = canvas != null ? canvas.rootCanvas.sortingLayerID : 0;
            feedbackCanvas.sortingOrder = canvas != null ? canvas.rootCanvas.sortingOrder + 10 : 10;
            var toast = new GameObject("Level Up Notification", typeof(RectTransform), typeof(CanvasGroup));
            toast.layer = hud.gameObject.layer;
            toast.transform.SetParent(ui.feedbackRoot, false);
            var rect = (RectTransform)toast.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.83f);
            rect.sizeDelta = new Vector2(560f, 112f);
            ui.notificationGroup = toast.GetComponent<CanvasGroup>();
            ui.notificationGroup.alpha = 0f;
            ui.notificationGroup.blocksRaycasts = false;
            ui.notificationGroup.interactable = false;
            Image background = MakeImage("Background", rect, new Color(0.025f, 0.035f, 0.07f, 0.9f));
            Stretch(background.rectTransform);
            ui.notificationTitle = MakeText("Level Up Title", rect, 30f);
            UIFonts.ApplyHudTitle(ui.notificationTitle);
            Place(ui.notificationTitle.rectTransform, 12, 6, 536, 38);
            ui.notificationTitle.alignment = TextAlignmentOptions.Center;
            ui.notification = MakeText("Rewards", rect, 18f);
            Place(ui.notification.rectTransform, 12, 45, 536, 61);
            ui.notification.alignment = TextAlignmentOptions.Center;
            ui.notification.textWrappingMode = TextWrappingModes.Normal;
            ui.audioSource = ui.gameObject.AddComponent<AudioSource>();
            ui.audioSource.playOnAwake = false;
            ui.audioSource.spatialBlend = 0f;
            return ui;
        }

        public void Bind(PlayerCharacter target)
        {
            Unbind();
            player = target;
            progression = target != null ? target.RuntimeStats?.Progression : null;
            if (progression == null) return;
            progression.Changed += OnChanged;
            SetProgress(progression.Progress);
            badge.text = progression.Level.ToString();
            badge.color = progression.Config.xpColor;
        }

        public void Unbind()
        {
            if (progression != null) progression.Changed -= OnChanged;
            progression = null;
            player = null;
            if (progressAnimation != null) StopCoroutine(progressAnimation);
            progressAnimation = null;
            feedbackRemaining = gainRemaining = 0f;
            recentGain = 0;
            if (notificationGroup != null) notificationGroup.alpha = 0f;
            if (gain != null) gain.text = "";
            if (audioSource != null) audioSource.Stop();
            ClearRipple();
        }

        private void OnChanged(ProgressionChange change)
        {
            if (!isActiveAndEnabled || progression == null) return;
            if (progressAnimation != null) StopCoroutine(progressAnimation);
            if (!change.Earned)
            {
                SetProgress(progression.Progress);
                badge.text = progression.Level.ToString();
                return;
            }
            recentGain = gainRemaining > 0 ? recentGain + change.ExperienceGained : change.ExperienceGained;
            gainRemaining = 1.4f;
            gain.text = $"+{recentGain} XP";
            gain.color = progression.Config.xpColor;
            if (player != null && change.ExperienceGained > 0)
                DamagePopupManager.Instance?.SpawnExperiencePopup(player.VFXCenter, change.ExperienceGained, progression.Config);
            progressAnimation = StartCoroutine(AnimateProgress(change.LeveledUp));
            if (change.LeveledUp) Celebrate(change);
        }

        private IEnumerator AnimateProgress(bool leveledUp)
        {
            float duration = Mathf.Max(0.05f, progression.Config.xpAnimationDuration);
            if (leveledUp)
            {
                yield return AnimateTo(1f, duration);
                SetProgress(0f);
            }
            badge.text = progression.Level.ToString();
            yield return AnimateTo(progression.Progress, duration);
            progressAnimation = null;
        }

        private IEnumerator AnimateTo(float target, float duration)
        {
            float start = displayedProgress;
            for (float time = 0; time < duration; time += Time.unscaledDeltaTime)
            {
                SetProgress(Mathf.Lerp(start, target, Mathf.SmoothStep(0, 1, time / duration)));
                yield return null;
            }
            SetProgress(target);
        }

        private void Celebrate(ProgressionChange change)
        {
            var config = progression.Config;
            celebrationFromLevel = feedbackRemaining > 0 ? Mathf.Min(celebrationFromLevel, change.PreviousLevel) : change.PreviousLevel;
            feedbackRemaining = Mathf.Max(config.notificationDuration, config.portraitHighlightDuration);
            notificationTitle.text = $"LEVEL UP  -  LEVEL {change.Level}";
            notificationTitle.color = config.levelUpColor;
            var message = new StringBuilder();
            int pointsPerLevel = PlayerUpgrades.Instance != null ? PlayerUpgrades.Instance.PointsPerLevel : 1;
            int points = Mathf.Max(0, change.Level - celebrationFromLevel) * pointsPerLevel;
            if (points > 0)
                message.Append($"+{points} UPGRADE POINT{(points > 1 ? "S" : "")}   ");
            AppendReward(message, config, PlayerStat.MaxHealth, "HEALTH", celebrationFromLevel, change.Level);
            AppendReward(message, config, PlayerStat.OutgoingDamage, "DAMAGE", celebrationFromLevel, change.Level);
            AppendReward(message, config, PlayerStat.Armor, "ARMOR", celebrationFromLevel, change.Level);
            if (points > 0) message.Append("\nOPEN THE UPGRADES TAB TO SPEND");
            notification.text = message.ToString();
            notification.color = config.levelUpColor;
            notificationGroup.alpha = 1f;
            UpdateHighlight(0f);
            ClearRipple();
            if (config.showLevelUpRipple && player != null && player.IsAlive)
            {
                GameObject template = config.levelUpRipplePrefab != null ? config.levelUpRipplePrefab
                    : player.GetComponent<ParryHandler>()?.ScreenPulseTemplate;
                if (template != null)
                {
                    // Clone the player's parry instance so its current graph and property overrides are preserved.
                    ripple = Instantiate(template, player.VFXCenter, template.transform.rotation);
                    ripple.name = "Level Up Parry Pulse";
                    ripple.transform.localScale = template.transform.lossyScale * config.rippleScale;
                    ripple.SetActive(true);
                    foreach (Renderer renderer in ripple.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
                    foreach (VisualEffect effect in ripple.GetComponentsInChildren<VisualEffect>(true))
                    {
                        effect.enabled = true;
                        effect.pause = false;
                        // Reinit sends the initial event, matching the parry's OnPlay, without a second burst.
                        effect.Reinit();
                    }
                    rippleRemaining = Mathf.Max(0.1f, config.rippleLifetime);
                }
            }
            if (config.levelUpSound != null) audioSource.PlayOneShot(config.levelUpSound, config.soundVolume);
        }

        private void ClearRipple()
        {
            if (ripple != null)
            {
                if (Application.isPlaying) Destroy(ripple);
                else DestroyImmediate(ripple);
            }
            ripple = null;
            rippleRemaining = 0f;
        }

        private static void AppendReward(StringBuilder message, PlayerProgressionConfig config,
            PlayerStat stat, string label, int previous, int level)
        {
            var before = config.BonusAt(stat, previous);
            var after = config.BonusAt(stat, level);
            float flat = after.flat - before.flat;
            float percent = after.percent - before.percent;
            if (flat > 0) message.Append($"+{flat:0.#} {label}   ");
            if (percent > 0) message.Append($"+{percent:0.#}% {label}   ");
        }

        private void Update()
        {
            if (progression == null) return;
            if (ripple != null)
            {
                rippleRemaining -= Time.unscaledDeltaTime;
                if (rippleRemaining <= 0f) ClearRipple();
            }
            if (gainRemaining > 0)
            {
                gainRemaining -= Time.unscaledDeltaTime;
                if (gainRemaining <= 0) gain.text = "";
            }
            if (feedbackRemaining <= 0) return;
            var config = progression.Config;
            feedbackRemaining = Mathf.Max(0, feedbackRemaining - Time.unscaledDeltaTime);
            float elapsed = Mathf.Max(config.notificationDuration, config.portraitHighlightDuration) - feedbackRemaining;
            UpdateHighlight(Mathf.Clamp01(elapsed / Mathf.Max(0.05f, config.portraitHighlightDuration)));
            notificationGroup.alpha = Mathf.Clamp01(feedbackRemaining / 0.35f);
        }

        private void UpdateHighlight(float pulse)
        {
            var config = progression.Config;
            badge.color = Color.Lerp(config.levelUpColor, config.xpColor, pulse);
            foreach (Image edge in edges) edge.color = badge.color;
        }

        private void SetProgress(float progress)
        {
            displayedProgress = Mathf.Clamp01(progress);
            float width = portrait.rect.width;
            float height = portrait.rect.height;
            float remaining = displayedProgress * (width + height) * 2f;
            for (int i = 0; i < edges.Length; i++)
            {
                float length = i == 0 || i == 2 ? height : width;
                SetEdge(edges[i].rectTransform, i, Mathf.Clamp01(remaining / Mathf.Max(1, length)), width, height);
                remaining -= length;
                edges[i].color = progression != null ? progression.Config.xpColor : Color.cyan;
            }
        }

        private static void SetEdge(RectTransform rect, int edge, float fraction, float width, float height)
        {
            const float thickness = 3f;
            switch (edge)
            {
                // Clockwise from the bottom-left corner: left, top, right, bottom.
                case 0: Place(rect, 0, height * (1 - fraction), thickness, height * fraction); break;
                case 1: Place(rect, 0, 0, width * fraction, thickness); break;
                case 2: Place(rect, width - thickness, 0, thickness, height * fraction); break;
                case 3: Place(rect, width * (1 - fraction), height - thickness, width * fraction, thickness); break;
            }
        }

        private static Image MakeImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
        private static TMP_Text MakeText(string name, Transform parent, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            UIFonts.ApplyHudBody(text);
            text.fontSize = size;
            text.color = Color.white;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private void OnDisable() => Unbind();
        private void OnDestroy()
        {
            Unbind();
            if (feedbackRoot == null) return;
            if (Application.isPlaying) Destroy(feedbackRoot.gameObject);
            else DestroyImmediate(feedbackRoot.gameObject);
        }
    }
}
