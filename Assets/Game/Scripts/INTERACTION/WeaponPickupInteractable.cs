using UnityEngine;
using TMPro;
using UnityEngine.UI;
using DG.Tweening;

namespace junklite
{
    [RequireComponent(typeof(WorldWeaponPickup))]
    public class WeaponPickupInteractable : MonoBehaviour
    {
        #region Fields

        [Header("Proximity")]
        [SerializeField] private float interactRadius = 2.5f;
        [SerializeField] private string playerTag = "Player";

        [Header("Prompt (Child Object)")]
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private TMP_Text weaponNameText;
        [SerializeField] private TMP_Text interactHintText;
        [SerializeField] private string interactKeyLabel = "[E]";

        [Header("Animation")]
        [SerializeField] private float popDuration = 0.25f;
        [SerializeField] private Ease popInEase = Ease.OutBack;
        [SerializeField] private Ease popOutEase = Ease.InBack;

        private WorldWeaponPickup weaponPickup;
        private CanvasGroup canvasGroup;
        private Tween activeTween;
        private GameInputManager subscribedInputManager;
        private TMP_Text generatedKeyText;

        // Match the weapon assignment menu while using the world-space Play font.
        private static readonly Color PromptPanel = new(0.032f, 0.045f, 0.087f, 0.96f);
        private static readonly Color PromptLine = new(0.18f, 0.27f, 0.38f, 1f);
        private static readonly Color PromptCyan = new(0.24f, 0.91f, 0.98f, 1f);
        private static readonly Color PromptMagenta = new(0.86f, 0.25f, 0.89f, 1f);
        private static readonly Color PromptPaper = new(0.9f, 0.94f, 1f, 1f);

        #endregion

        #region Static

        public static WeaponPickupInteractable Current { get; private set; }

        public WorldWeaponPickup WeaponPickup => weaponPickup;

        #endregion

        #region Unity

        private void Awake()
        {
            weaponPickup = GetComponent<WorldWeaponPickup>();
            CreateProximityZone();

            if (promptRoot != null)
            {
                canvasGroup = promptRoot.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = promptRoot.AddComponent<CanvasGroup>();
            }

            EnsurePromptVisual();
            UIFonts.ApplyWorldTree(promptRoot != null ? promptRoot.transform : null);
            HideInstant();
        }

        private void OnEnable()
        {
            RebindInputManager();
            RefreshPromptText();
            HideInstant();
        }

        private void Start() => RebindInputManager();

        private void OnDisable()
        {
            UnbindInputManager();

            if (Current == this)
            {
                Current = null;
                KillTween();
                HideInstant();
            }
        }

        private void OnDestroy()
        {
            UnbindInputManager();
            KillTween();
        }

        private void LateUpdate()
        {
            if (promptRoot == null || !promptRoot.activeSelf) return;

            Camera cam = Camera.main;
            if (cam != null)
                promptRoot.transform.forward = cam.transform.forward;
        }

        #endregion

        #region Proximity Zone

        private void CreateProximityZone()
        {
            var zone = new GameObject("ProximityZone");
            zone.transform.SetParent(transform, false);
            zone.layer = gameObject.layer;

            var col = zone.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = interactRadius;

            var relay = zone.AddComponent<ProximityRelay>();
            relay.Init(this);
        }

        internal void OnPlayerEnter(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;

            // Bump previous interactable if there was one
            if (Current != null && Current != this)
                Current.Deactivate();

            Current = this;
            RefreshPromptText();
            PopIn();
        }

        internal void OnPlayerExit(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;
            if (Current != this) return;

            Deactivate();
        }

        private void Deactivate()
        {
            if (Current == this)
                Current = null;

            PopOut();
        }

        #endregion

        #region Public

        public void ReEnablePrompt()
        {
            if (Current != this) return;

            RefreshPromptText();
            PopIn();
        }

        #endregion

        #region Prompt

        private void RefreshPromptText()
        {
            if (weaponPickup == null) return;

            string weaponName = "Weapon";
            if (weaponPickup.weaponInstance != null &&
                weaponPickup.weaponInstance.weaponData != null)
            {
                var data = weaponPickup.weaponInstance.weaponData;
                if (!string.IsNullOrEmpty(data.displayName))
                    weaponName = data.displayName;
            }

            if (weaponNameText != null) weaponNameText.text = weaponName;
            string bindingHint = GetInteractBindingHint();
            if (interactHintText != null)
                interactHintText.text = $"{FormatKeyLabel(bindingHint)} Pick Up";
            if (generatedKeyText != null)
                generatedKeyText.text = bindingHint;
        }

        private string GetInteractBindingHint()
        {
            string hint = GameInputManager.Instance?.GetBindingHint("Player/Interact");
            if (!string.IsNullOrEmpty(hint))
                return hint;

            return string.IsNullOrEmpty(interactKeyLabel)
                ? "E"
                : interactKeyLabel.Trim('[', ']');
        }

        private static string FormatKeyLabel(string hint) => $"[{hint}]";

        private void RebindInputManager()
        {
            GameInputManager inputManager = GameInputManager.Instance;
            if (subscribedInputManager == inputManager)
                return;

            UnbindInputManager();
            subscribedInputManager = inputManager;
            if (subscribedInputManager != null)
                subscribedInputManager.OnInputDeviceChanged += HandleInputDeviceChanged;

            RefreshPromptText();
        }

        private void UnbindInputManager()
        {
            if (subscribedInputManager != null)
                subscribedInputManager.OnInputDeviceChanged -= HandleInputDeviceChanged;
            subscribedInputManager = null;
        }

        private void HandleInputDeviceChanged(bool _) => RefreshPromptText();

        private void EnsurePromptVisual()
        {
            if (promptRoot == null || interactHintText != null || generatedKeyText != null)
                return;

            Image legacyKeyImage = null;
            Image legacyBackground = null;
            Image[] images = promptRoot.GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                if (image == null) continue;
                if (image.gameObject.name == "Letter")
                    legacyKeyImage = image;
                else if (image.gameObject.name == "BG")
                    legacyBackground = image;
            }

            if (legacyKeyImage == null)
                return;

            // Build once in the existing canvas so both pickup prefabs inherit the styling.
            legacyKeyImage.enabled = false;
            if (legacyBackground != null)
                legacyBackground.enabled = false;

            RectTransform card = CreatePromptRect("Weapon Pickup Prompt", legacyKeyImage.transform,
                0f, 0f, 3.15f, 0.94f);
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = new Vector2(0f, 0.85f);
            card.localScale = Vector3.one * 0.8f;
            Image border = card.gameObject.AddComponent<Image>();
            border.color = PromptLine;
            border.raycastTarget = false;

            PromptImage("Panel", card, PromptPanel, 0.012f, 0.012f, 3.126f, 0.916f);
            PromptImage("Cyan Accent", card, PromptCyan, 0f, 0f, 0.82f, 0.025f);
            PromptImage("Magenta Accent", card, PromptMagenta, 2.82f, 0.915f, 0.33f, 0.025f);
            PromptImage("Key Border", card, PromptCyan, 0.14f, 0.14f, 0.66f, 0.66f);
            PromptImage("Key Fill", card, PromptPanel, 0.154f, 0.154f, 0.632f, 0.632f);
            PromptImage("Divider", card, PromptLine, 0.96f, 0.17f, 0.012f, 0.6f);

            generatedKeyText = PromptText("Input Binding", card, "", PromptCyan,
                0.46f, 0.19f, 0.19f, 0.56f, 0.56f);
            generatedKeyText.alignment = TextAlignmentOptions.Midline;
            // Longer keyboard/controller labels shrink inside the badge.
            generatedKeyText.fontSizeMin = 0.05f;
            PromptText("Action", card, "PICK UP", PromptCyan,
                0.2f, 1.13f, 0.16f, 1.86f, 0.25f);
            weaponNameText = PromptText("Weapon Name", card, "Weapon", PromptPaper,
                0.31f, 1.13f, 0.43f, 1.86f, 0.34f);
        }

        private static RectTransform CreatePromptRect(
            string name, Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static void PromptImage(
            string name, Transform parent, Color color, float x, float y, float width, float height)
        {
            Image image = CreatePromptRect(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static TMP_Text PromptText(
            string name, Transform parent, string value, Color color, float size,
            float x, float y, float width, float height)
        {
            TMP_Text text = CreatePromptRect(name, parent, x, y, width, height)
                .gameObject.AddComponent<TextMeshProUGUI>();
            UIFonts.ApplyWorld(text);
            text.text = value;
            text.color = color;
            text.fontSize = text.fontSizeMax = size;
            text.fontSizeMin = size * 0.7f;
            text.enableAutoSizing = true;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        #endregion

        #region Animation

        private void PopIn()
        {
            if (promptRoot == null) return;

            KillTween();
            promptRoot.SetActive(true);
            promptRoot.transform.localScale = Vector3.one * 0.92f;
            if (canvasGroup != null) canvasGroup.alpha = 0f;

            activeTween = DOTween.Sequence()
                .Join(promptRoot.transform.DOScale(Vector3.one, popDuration).SetEase(popInEase))
                .Join(canvasGroup != null
                    ? canvasGroup.DOFade(1f, popDuration * 0.6f)
                    : DOTween.Sequence())
                .SetUpdate(true)
                .SetLink(promptRoot);
        }

        private void PopOut()
        {
            if (promptRoot == null || !promptRoot.activeSelf) return;

            KillTween();

            activeTween = DOTween.Sequence()
                .Join(promptRoot.transform.DOScale(Vector3.one * 0.96f, popDuration * 0.7f).SetEase(popOutEase))
                .Join(canvasGroup != null
                    ? canvasGroup.DOFade(0f, popDuration * 0.5f)
                    : DOTween.Sequence())
                .SetUpdate(true)
                .SetLink(promptRoot)
                .OnComplete(() => promptRoot.SetActive(false));
        }

        private void HideInstant()
        {
            if (promptRoot == null) return;

            KillTween();
            promptRoot.transform.localScale = Vector3.one;
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            promptRoot.SetActive(false);
        }

        private void KillTween()
        {
            if (activeTween != null && activeTween.IsActive())
                activeTween.Kill();
            activeTween = null;
        }

        #endregion
    }

    internal class ProximityRelay : MonoBehaviour
    {
        private WeaponPickupInteractable owner;

        public void Init(WeaponPickupInteractable parent) => owner = parent;
        private void OnTriggerEnter(Collider other) => owner?.OnPlayerEnter(other);
        private void OnTriggerExit(Collider other) => owner?.OnPlayerExit(other);
    }
}
