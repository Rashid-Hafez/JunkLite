using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Persistent, duplicate-safe root for services shared by every gameplay level.
    /// The prefab may be dropped into every scene. LevelContext is a separate
    /// scene-local root and is never parented beneath this persistent object.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        [Header("Persistent UI")]
        [SerializeField] private Vector2 referenceResolution = new(1920f, 1080f);
        [SerializeField, Range(0f, 1f)] private float screenMatch = 0.5f;
        [SerializeField] private int uiSortingOrder = 10;

        public Transform GameplayUIRoot { get; private set; }

        private EventSystem ownedEventSystem;

        private void Awake()
        {
            DetachLegacyBundledLevelContexts();

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            DontDestroyOnLoad(gameObject);

            EnsureGameplayUIRoot();
            EnsureEventSystem();
            RemoveDuplicateEventSystems();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Instance != this) return;

            EnsureGameplayUIRoot();
            EnsureEventSystem();
            RemoveDuplicateEventSystems();
        }

        /// <summary>
        /// Compatibility for scenes created by the first GameRoot prototype.
        /// Newly rebuilt scenes use a standalone LevelContext and skip this path.
        /// </summary>
        private void DetachLegacyBundledLevelContexts()
        {
            LevelContext[] contexts = GetComponentsInChildren<LevelContext>(true);
            for (int i = 0; i < contexts.Length; i++)
            {
                LevelContext context = contexts[i];
                if (context != null && context.transform.IsChildOf(transform))
                    context.transform.SetParent(null, true);
            }
        }

        private void EnsureGameplayUIRoot()
        {
            if (GameplayUIRoot != null) return;

            var uiObject = new GameObject(
                "Persistent Gameplay UI",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            uiObject.layer = LayerMask.NameToLayer("UI");
            uiObject.transform.SetParent(transform, false);

            var canvas = uiObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = uiSortingOrder;

            var scaler = uiObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = screenMatch;

            GameplayUIRoot = uiObject.transform;
        }

        private void EnsureEventSystem()
        {
            if (ownedEventSystem != null) return;

            var eventObject = new GameObject(
                "Persistent Event System",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));

            eventObject.transform.SetParent(transform, false);
            ownedEventSystem = eventObject.GetComponent<EventSystem>();
        }

        private void RemoveDuplicateEventSystems()
        {
            var eventSystems = FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (EventSystem eventSystem in eventSystems)
            {
                if (eventSystem == null || eventSystem == ownedEventSystem) continue;

                eventSystem.enabled = false;
                foreach (BaseInputModule inputModule in eventSystem.GetComponents<BaseInputModule>())
                    inputModule.enabled = false;
            }
        }
    }
}
