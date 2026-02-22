using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

namespace junklite
{
    public class PhantomStrikeUI : MonoBehaviour
    {
        #region Fields

        [Header("Panel")]
        [SerializeField] private GameObject panel;

        [Header("Hit Count")]
        [SerializeField] private TMP_Text hitCountText;

        [Header("Ready State")]
        [SerializeField] private TMP_Text readyText;
        [SerializeField] private GameObject specialReadyEffect;

        [Header("Strike Icons")]
        [SerializeField] private Image[] strikeIcons;
        [SerializeField] private float fillDuration = 0.3f;

        [Header("Colors")]
        [SerializeField] private Color readyColor = Color.yellow;

        private PlayerCharacter player;
        private PhantomStrikeTracker tracker;
        private Dictionary<Image, Coroutine> activeCoroutines = new();
        private int lastHitCount;

        #endregion

        #region Unity

        private void Start()
        {
            if (panel != null)
                panel.SetActive(false);

            if (readyText != null)
                readyText.gameObject.SetActive(false);
        }

        private void OnDestroy() => Unbind();

        #endregion

        #region Bind / Unbind

        public void Bind(PlayerCharacter targetPlayer)
        {
            Unbind();
            player = targetPlayer;

            if (player == null)
            {
                Hide();
                return;
            }

            RefreshTracker();
        }

        public void Unbind()
        {
            UnsubscribeFromTracker();
            player = null;
            tracker = null;
            Hide();
        }

        public void RefreshTracker()
        {
            if (player == null)
            {
                Hide();
                return;
            }

            // Look for tracker under "Mod Trackers" child
            var modTrackers = player.transform.Find("Mod Trackers");
            var newTracker = modTrackers != null
                ? modTrackers.GetComponentInChildren<PhantomStrikeTracker>()
                : null;

            if (newTracker == null || !newTracker.IsActive)
            {
                UnsubscribeFromTracker();
                tracker = null;
                Hide();
                return;
            }

            if (newTracker == tracker) return;

            UnsubscribeFromTracker();
            tracker = newTracker;
            SubscribeToTracker();
            Show();
        }

        #endregion

        #region Tracker Events

        private void SubscribeToTracker()
        {
            if (tracker == null) return;

            tracker.OnChargesChanged += UpdateDisplay;
            tracker.OnSpecialReady += ShowSpecialReady;
            tracker.OnSpecialUsed += OnSpecialUsed;
            tracker.OnChargesReset += OnReset;
        }

        private void UnsubscribeFromTracker()
        {
            if (tracker == null) return;

            tracker.OnChargesChanged -= UpdateDisplay;
            tracker.OnSpecialReady -= ShowSpecialReady;
            tracker.OnSpecialUsed -= OnSpecialUsed;
            tracker.OnChargesReset -= OnReset;
        }

        #endregion

        #region Display

        private void Show()
        {
            if (panel != null)
                panel.SetActive(true);

            lastHitCount = 0;
            SetAllIconFills(0f);
        }

        private void Hide()
        {
            if (panel != null)
                panel.SetActive(false);

            ClearCoroutines();
            lastHitCount = 0;
        }

        private void UpdateDisplay(int current, int required)
        {
            bool isReady = current >= required;

            if (hitCountText != null)
            {
                hitCountText.gameObject.SetActive(!isReady);
                if (!isReady) hitCountText.text = $"{current}";
            }

            if (strikeIcons != null && current > lastHitCount && current <= strikeIcons.Length)
                AnimateIcon(strikeIcons[current - 1], 0f, 1f);

            lastHitCount = current;
        }

        private void ShowSpecialReady()
        {
            if (hitCountText != null)
                hitCountText.gameObject.SetActive(false);

            if (readyText != null)
            {
                readyText.gameObject.SetActive(true);
                readyText.faceColor = readyColor;
                readyText.ForceMeshUpdate();
            }

            if (specialReadyEffect != null)
                specialReadyEffect.SetActive(true);
        }

        private void HideSpecialReady()
        {
            if (hitCountText != null)
            {
                hitCountText.gameObject.SetActive(true);
                hitCountText.text = "0";
            }

            if (readyText != null)
            {
                readyText.faceColor = Color.white;
                readyText.gameObject.SetActive(false);
            }

            if (specialReadyEffect != null)
                specialReadyEffect.SetActive(false);
        }

        private void OnReset()
        {
            HideSpecialReady();
            AnimateAllIconsToZero();
            lastHitCount = 0;
        }

        private void OnSpecialUsed()
        {
            HideSpecialReady();
            AnimateAllIconsToZero();
            lastHitCount = 0;
        }

        #endregion

        #region Icon Animation

        private void AnimateIcon(Image icon, float from, float to)
        {
            if (icon == null) return;

            if (activeCoroutines.TryGetValue(icon, out var existing) && existing != null)
                StopCoroutine(existing);

            activeCoroutines[icon] = StartCoroutine(FillRoutine(icon, from, to));
        }

        private IEnumerator FillRoutine(Image icon, float from, float to)
        {
            float elapsed = 0f;
            while (elapsed < fillDuration)
            {
                elapsed += Time.deltaTime;
                icon.fillAmount = Mathf.Lerp(from, to, elapsed / fillDuration);
                yield return null;
            }
            icon.fillAmount = to;
            activeCoroutines.Remove(icon);
        }

        private void AnimateAllIconsToZero()
        {
            if (strikeIcons == null) return;
            foreach (var icon in strikeIcons)
                if (icon != null && icon.fillAmount > 0f)
                    AnimateIcon(icon, icon.fillAmount, 0f);
        }

        private void SetAllIconFills(float value)
        {
            if (strikeIcons == null) return;
            foreach (var icon in strikeIcons)
                if (icon != null) icon.fillAmount = value;
        }

        private void ClearCoroutines()
        {
            foreach (var co in activeCoroutines.Values)
                if (co != null) StopCoroutine(co);
            activeCoroutines.Clear();
        }

        #endregion
    }
}