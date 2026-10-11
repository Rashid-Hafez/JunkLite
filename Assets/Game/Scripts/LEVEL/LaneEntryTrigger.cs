using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Entry point from a free-move room (safe room / explore room) onto a LanePath.
    /// While the player stands in this trigger box an "[E] to proceed" prompt shows on screen;
    /// pressing Interact snaps the player onto the closest point of the lane with the lane's rotation.
    /// The optional gate door stays locked until the player has snapped, and locks again on every (re)spawn.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class LaneEntryTrigger : MonoBehaviour
    {
        [SerializeField] private LanePath lanePath;
        [Tooltip("Door out of the free-move room. Locked until the player presses Interact here.")]
        [SerializeField] private SlidingDoor gateDoor;
        [SerializeField] private string promptSuffix = "to proceed";
        [SerializeField] private float promptFadeSpeed = 8f;

        private Character2D5Controller playerInside;
        private GameInputManager subscribedInput;
        private PlayerLifecycle subscribedLifecycle;
        private CanvasGroup promptGroup;
        private TextMeshProUGUI promptText;
        private float promptTarget;
        private bool playerSnapped;

        private void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            BuildPrompt();
            SetGateLocked(true);
        }

        private void OnEnable()
        {
            Rebind();
            HidePrompt(instant: true);
        }

        private void Start() => Rebind();

        private void OnDisable()
        {
            Unbind();
            playerInside = null;
            HidePrompt(instant: true);
        }

        private void Update()
        {
            if (promptGroup == null || Mathf.Approximately(promptGroup.alpha, promptTarget)) return;
            promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, promptTarget, promptFadeSpeed * Time.unscaledDeltaTime);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;
            var controller = other.GetComponent<Character2D5Controller>();
            if (controller == null || playerSnapped) return;

            playerInside = controller;
            Rebind();
            RefreshPromptText();
            promptTarget = 1f;
        }

        private void OnTriggerExit(Collider other)
        {
            if (playerInside == null || other.gameObject != playerInside.gameObject) return;
            playerInside = null;
            HidePrompt(instant: false);
        }

        private void HandleInteract()
        {
            if (playerInside == null || lanePath == null) return;
            if (!lanePath.TryGetClosestPoint(playerInside.transform.position, out Vector3 point, out float yRotation)) return;

            playerInside.SnapToLane(point, yRotation);
            playerInside = null;
            playerSnapped = true;
            SetGateLocked(false);
            HidePrompt(instant: false);
        }

        private void HandlePlayerDied(PlayerCharacter _)
        {
            playerInside = null;
            HidePrompt(instant: true);
        }

        private void HandlePlayerSpawned(PlayerCharacter _)
        {
            playerSnapped = false;
            SetGateLocked(true);
        }

        private void SetGateLocked(bool locked)
        {
            if (gateDoor != null) gateDoor.Locked = locked;
        }

        #region Input / lifecycle binding

        private void Rebind()
        {
            GameInputManager input = GameInputManager.Instance;
            if (input != subscribedInput)
            {
                if (subscribedInput != null)
                {
                    subscribedInput.OnInteract -= HandleInteract;
                    subscribedInput.OnInputDeviceChanged -= HandleInputDeviceChanged;
                }
                subscribedInput = input;
                if (subscribedInput != null)
                {
                    subscribedInput.OnInteract += HandleInteract;
                    subscribedInput.OnInputDeviceChanged += HandleInputDeviceChanged;
                }
            }

            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (lifecycle != subscribedLifecycle)
            {
                if (subscribedLifecycle != null)
                {
                    subscribedLifecycle.PlayerDied -= HandlePlayerDied;
                    subscribedLifecycle.PlayerSpawned -= HandlePlayerSpawned;
                }
                subscribedLifecycle = lifecycle;
                if (subscribedLifecycle != null)
                {
                    subscribedLifecycle.PlayerDied += HandlePlayerDied;
                    subscribedLifecycle.PlayerSpawned += HandlePlayerSpawned;
                }
            }
        }

        private void Unbind()
        {
            if (subscribedInput != null)
            {
                subscribedInput.OnInteract -= HandleInteract;
                subscribedInput.OnInputDeviceChanged -= HandleInputDeviceChanged;
                subscribedInput = null;
            }

            if (subscribedLifecycle != null)
            {
                subscribedLifecycle.PlayerDied -= HandlePlayerDied;
                subscribedLifecycle.PlayerSpawned -= HandlePlayerSpawned;
                subscribedLifecycle = null;
            }
        }

        private void HandleInputDeviceChanged(bool _) => RefreshPromptText();

        #endregion

        #region Prompt

        private void BuildPrompt()
        {
            var canvasGo = new GameObject("LaneEntryPrompt", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            promptGroup = canvasGo.AddComponent<CanvasGroup>();
            promptGroup.interactable = false;
            promptGroup.blocksRaycasts = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(canvasGo.transform, false);
            var rect = (RectTransform)textGo.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 220f);
            rect.sizeDelta = new Vector2(600f, 60f);

            promptText = textGo.AddComponent<TextMeshProUGUI>();
            UIFonts.ApplyHudBody(promptText);
            promptText.fontSize = 30f;
            promptText.alignment = TextAlignmentOptions.Center;
            promptText.raycastTarget = false;
        }

        private void RefreshPromptText()
        {
            if (promptText == null) return;
            string key = subscribedInput != null ? subscribedInput.GetBindingHint("Player/Interact") : string.Empty;
            if (string.IsNullOrEmpty(key)) key = "E";
            promptText.text = $"{key} {promptSuffix}";
        }

        private void HidePrompt(bool instant)
        {
            promptTarget = 0f;
            if (instant && promptGroup != null) promptGroup.alpha = 0f;
        }

        #endregion

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.color = new Color(0.2f, 1f, 0.6f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
        }
    }
}
