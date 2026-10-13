using DG.Tweening;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// "The CyberBox": the first Interact lifts the lid (Cassidy's effort SFX + the box's open SFX),
    /// then reveals the item inside. The item is any pickup (e.g. a weapon pickup) that owns the
    /// second Interact itself, so this component never touches inventory or loadout.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class CyberBoxInteractable : MonoBehaviour
    {
        [SerializeField] private Transform lid;
        [Tooltip("Inactive until the lid is open. Keep it outside the scaled box mesh hierarchy.")]
        [SerializeField] private GameObject item;
        [SerializeField] private string promptLabel = "Open The CyberBox";
        [Tooltip("Optional. The player needs this keyring item (e.g. Grapple Hook) to open the box.")]
        [SerializeField] private string requiredKeyTag;

        [Header("Lid (local space of the lid's parent)")]
        [SerializeField] private Vector3 lidOpenOffset = new Vector3(0f, 1.6f, 2.2f);
        [SerializeField] private Vector3 lidOpenEuler = new Vector3(-70f, 0f, 0f);
        [SerializeField] private float openDuration = 0.7f;

        [Header("Audio")]
        [SerializeField] private SoundEntry playerSound;
        [SerializeField] private SoundEntry openSound;

        private GameInputManager subscribedInput;
        private Character2D5Controller playerInside;
        private bool opened;

        private void Awake()
        {
            GetComponent<SphereCollider>().isTrigger = true;
            if (item != null) item.SetActive(false);
        }

        private void OnEnable() => Rebind();
        private void Start() => Rebind();

        private void OnDisable()
        {
            if (subscribedInput != null) subscribedInput.OnInteract -= HandleInteract;
            subscribedInput = null;
            playerInside = null;
            TutorialPromptHUD.HideInteract(this, instant: true);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (opened || !other.CompareTag("Player")) return;
            var controller = other.GetComponent<Character2D5Controller>();
            if (controller == null || !HasRequiredKey(controller)) return;

            playerInside = controller;
            Rebind();
            TutorialPromptHUD.ShowInteract(this, promptLabel);
        }

        private void OnTriggerExit(Collider other)
        {
            if (playerInside == null || other.gameObject != playerInside.gameObject) return;
            playerInside = null;
            TutorialPromptHUD.HideInteract(this);
        }

        private bool HasRequiredKey(Character2D5Controller controller)
        {
            if (string.IsNullOrEmpty(requiredKeyTag)) return true;
            PlayerKeyring keyring = controller.GetComponentInParent<PlayerKeyring>();
            return keyring != null && keyring.Has(requiredKeyTag);
        }

        private void HandleInteract()
        {
            if (playerInside == null || opened) return;
            opened = true;
            Vector3 playerPosition = playerInside.transform.position;
            playerInside = null;
            TutorialPromptHUD.HideInteract(this, completed: true);

            AudioManager audio = AudioManager.Instance;
            if (audio != null)
            {
                audio.PlaySpatialAtPosition(playerSound, playerPosition);
                audio.PlaySpatialAtPosition(openSound, transform.position);
            }

            if (lid == null)
            {
                RevealItem();
                return;
            }

            DOTween.Sequence()
                .Join(lid.DOLocalMove(lid.localPosition + lidOpenOffset, openDuration).SetEase(Ease.OutCubic))
                .Join(lid.DOLocalRotate(lid.localEulerAngles + lidOpenEuler, openDuration).SetEase(Ease.OutBack))
                .SetLink(lid.gameObject)
                .OnComplete(RevealItem);
        }

        private void RevealItem()
        {
            if (item != null) item.SetActive(true);
        }

        private void Rebind()
        {
            GameInputManager input = GameInputManager.Instance;
            if (input == subscribedInput) return;
            if (subscribedInput != null) subscribedInput.OnInteract -= HandleInteract;
            subscribedInput = input;
            if (subscribedInput != null) subscribedInput.OnInteract += HandleInteract;
        }
    }
}
