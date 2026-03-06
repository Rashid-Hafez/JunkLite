using UnityEngine;
using TMPro;
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

            HideInstant();
        }

        private void OnEnable()
        {
            RefreshPromptText();
            HideInstant();
        }

        private void OnDisable()
        {
            if (Current == this)
            {
                Current = null;
                KillTween();
                HideInstant();
            }
        }

        private void OnDestroy() => KillTween();

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
            if (interactHintText != null) interactHintText.text = $"{interactKeyLabel} Pick Up";
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