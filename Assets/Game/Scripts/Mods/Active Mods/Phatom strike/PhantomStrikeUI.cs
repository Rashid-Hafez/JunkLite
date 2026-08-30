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

        private ModInstance modInstance;
        private PhantomStrikeTracker tracker;
        private Dictionary<Image, Coroutine> activeCoroutines = new();
        private int lastHitCount;

        #endregion

        #region Unity

        private void OnDestroy() => Unbind();

        #endregion

        #region IModSlotUI

        public void Bind(ModInstance mod, PlayerCharacter player)
        {
            Unbind();

            modInstance = mod;

            if (player == null || modInstance == null)
                return;

            // Find the tracker
            var modTrackers = player.transform.Find("Mod Trackers");
            tracker = modTrackers != null
                ? modTrackers.GetComponentInChildren<PhantomStrikeTracker>()
                : null;

            if (tracker == null || !tracker.IsActive)
            {
                tracker = null;
                return;
            }

            SubscribeToTracker();
            SnapToCurrentState();
        }

        public void Unbind()
        {
            UnsubscribeFromTracker();
            ClearCoroutines();
            modInstance = null;
            tracker = null;
            lastHitCount = 0;
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

        /// <summary>
        /// Snap all visuals to match ModInstance's current charges instantly (no animation).
        /// Called on bind so quick-swapping shows the correct state immediately.
        /// </summary>
        private void SnapToCurrentState()
        {
            if (modInstance == null) return;

            int current = modInstance.CurrentCharges;
            int required = modInstance.Data is ActiveModData active ? active.chargesRequired : 0;
            bool isReady = required > 0 && current >= required;

            // Snap icons
            if (strikeIcons != null)
            {
                for (int i = 0; i < strikeIcons.Length; i++)
                {
                    if (strikeIcons[i] != null)
                        strikeIcons[i].fillAmount = i < current ? 1f : 0f;
                }
            }

            // Snap text
            if (isReady)
            {
                ShowSpecialReady();
            }
            else
            {
                HideSpecialReady();
                if (hitCountText != null)
                    hitCountText.text = $"{current}";
            }

            lastHitCount = current;
        }

        private void UpdateDisplay(int current, int required)
        {
            bool isReady = current >= required;

            if (hitCountText != null)
            {
                hitCountText.gameObject.SetActive(!isReady);
                if (!isReady) hitCountText.text = $"{current}";
            }

            // Only animate newly gained charges
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

        private void ClearCoroutines()
        {
            foreach (var co in activeCoroutines.Values)
                if (co != null) StopCoroutine(co);
            activeCoroutines.Clear();
        }

        #endregion
    }
}