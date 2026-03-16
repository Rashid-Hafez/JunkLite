using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace junklite
{
    /// <summary>
    /// Interface for mod-specific UIs that live inside a CombatModSlotUI.
    /// Implement this on any prefab root that a mod wants to spawn in its slot.
    /// </summary>
    public interface IModSlotUI
    {
        void Bind(ModInstance mod, PlayerCharacter player);
        void Unbind();
    }

    public class CombatModSlotUI : MonoBehaviour
    {
        #region Fields

        [Header("References")]
        [SerializeField] private Image iconImage;
        [SerializeField] private Image durabilityFill;
        [SerializeField] private GameObject readyIndicator;
        [SerializeField] private TMP_Text inputHintText;

        [Header("Cooldown")]
        [SerializeField] private Image cooldownFill;

        [Header("Mod Custom UI")]
        [SerializeField] private Transform modUIContainer;

        private ModInstance boundMod;
        private PlayerCharacter boundPlayer;
        private GameObject spawnedModUI;

        #endregion

        #region Bind

        public void Bind(ModInstance mod, PlayerCharacter player, string inputHint = null)
        {
            Clear();

            boundMod = mod;
            boundPlayer = player;

            if (inputHintText != null)
                inputHintText.text = inputHint ?? "";

            SpawnModUI();
            Refresh();
        }

        public void Clear()
        {
            DestroyModUI();
            boundMod = null;
            boundPlayer = null;
            Refresh();
        }

        public void Refresh()
        {
            bool hasMod = boundMod != null && !boundMod.IsBroken;

            if (iconImage != null)
            {
                iconImage.enabled = hasMod && boundMod.Data.icon != null;
                if (iconImage.enabled)
                    iconImage.sprite = boundMod.Data.icon;
            }

            if (durabilityFill != null)
                durabilityFill.enabled = hasMod;

            if (readyIndicator != null)
                readyIndicator.SetActive(false);

            if (cooldownFill != null)
            {
                cooldownFill.fillAmount = 0f;
                cooldownFill.enabled = false;
            }
        }

        #endregion

        #region Mod UI Spawning

        private void SpawnModUI()
        {
            if (boundMod == null || modUIContainer == null) return;
            if (boundMod.Data is not ActiveModData activeMod) return;
            if (activeMod.modSlotUIPrefab == null) return;

            spawnedModUI = Instantiate(activeMod.modSlotUIPrefab, modUIContainer);

            var slotUI = spawnedModUI.GetComponent<IModSlotUI>();
            slotUI?.Bind(boundMod, boundPlayer);
        }

        private void DestroyModUI()
        {
            if (spawnedModUI == null) return;

            var slotUI = spawnedModUI.GetComponent<IModSlotUI>();
            slotUI?.Unbind();

            Destroy(spawnedModUI);
            spawnedModUI = null;
        }

        #endregion

        #region Update

        private void Update()
        {
            if (boundMod == null) return;

            if (durabilityFill != null && durabilityFill.enabled)
            {
                float max = boundMod.Data.maxDurability;
                durabilityFill.fillAmount = max > 0f ? boundMod.CurrentDurability / max : 0f;
            }

            if (readyIndicator != null && boundMod.Data is ActiveModData active)
                readyIndicator.SetActive(active.CanActivate(boundMod, null));

            // Cooldown overlay
            if (cooldownFill != null)
            {
                float normalized = boundMod.CooldownNormalized;
                bool onCooldown = normalized > 0f;
                cooldownFill.enabled = onCooldown;
                cooldownFill.fillAmount = normalized;
            }
        }

        #endregion
    }
}