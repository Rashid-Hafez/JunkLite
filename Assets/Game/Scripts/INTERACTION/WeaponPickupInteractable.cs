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
        private Image legacyKeyImage;
        private TMP_Text generatedKeyText;

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

            EnsureDynamicKeyVisual();
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

        private void EnsureDynamicKeyVisual()
        {
            if (promptRoot == null || interactHintText != null || generatedKeyText != null)
                return;

            Image[] images = promptRoot.GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                if (image != null && image.gameObject.name == "Letter")
                {
                    legacyKeyImage = image;
                    break;
                }
            }

            if (legacyKeyImage == null)
                return;

            legacyKeyImage.enabled = false;
            GameObject textObject = new("Dynamic Input Hint", typeof(RectTransform));
            textObject.layer = legacyKeyImage.gameObject.layer;
            RectTransform rect = (RectTransform)textObject.transform;
            rect.SetParent(legacyKeyImage.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            generatedKeyText = textObject.AddComponent<TextMeshProUGUI>();
            generatedKeyText.font = TMP_Settings.defaultFontAsset;
            generatedKeyText.alignment = TextAlignmentOptions.Center;
            generatedKeyText.enableAutoSizing = true;
            generatedKeyText.fontSizeMin = 0.05f;
            generatedKeyText.fontSizeMax = 1f;
            generatedKeyText.color = Color.white;
            generatedKeyText.raycastTarget = false;
        }

        #endregion

        #region Animation

        private void PopIn()
        {
            if (promptRoot == null) return;

            KillTween();
            promptRoot.SetActive(true);
            promptRoot.transform.localScale = Vector3.zero;
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
                .Join(promptRoot.transform.DOScale(Vector3.zero, popDuration * 0.7f).SetEase(popOutEase))
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
            promptRoot.transform.localScale = Vector3.zero;
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
