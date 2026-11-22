using System.Collections.Generic;
using Junklite.HUD;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Draws the level results in the Junklite look (Junklite.HUD.HudResults, UI Toolkit) in place of
    /// LevelResultsScreen's own canvas. The game's screen still runs everything: the delay, the pause, the menu input
    /// and what Retry and Continue do. When its canvas opens, this hides it and shows the same stats from
    /// LevelStatsTracker.LastStats. The game's Retry and Continue buttons stay selected and pressed through the
    /// EventSystem as before (keyboard and controller); this follows their selection, and the mouse picks them
    /// through the new screen. When the game's screen closes, this one goes too.
    /// If there are no stats to show (the Editor's F8 preview), the game's own screen shows instead.
    /// UI Toolkit draws under uGUI's Screen Space - Overlay canvases, so while the screen is up every other overlay
    /// canvas is switched off: at the end of Service Floor the level's black fade (FaderCanvas) would otherwise cover it.
    /// They come back as soon as the screen closes.
    /// Lives on the Junklite HUD, next to JunkliteHudBridge.
    /// </summary>
    public class JunkliteResultsBridge : MonoBehaviour
    {
        [Tooltip("The game's confirm action, whose key the selected button shows.")]
        [SerializeField] private string submitAction = "UI/Submit";

        private LevelResultsScreen screen;
        private GameObject canvasObject;
        private Canvas canvas;
        private Button retryButton;
        private Button continueButton;
        private HudResults view;
        private readonly List<Canvas> coveredCanvases = new List<Canvas>();
        private GameInputManager input;
        private bool shown;
        private bool skipped;
        private float nextSearch;

        private void OnEnable() => SceneManager.sceneLoaded += HandleSceneLoaded;

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (input != null) input.OnInputDeviceChanged -= HandleDeviceChanged;
            input = null;
            Close();
        }

        private void OnDestroy() => SceneManager.sceneLoaded -= HandleSceneLoaded;

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            screen = null;
            canvasObject = null;
            nextSearch = 0f;
        }

        private void Update()
        {
            if (!FindScreen())
            {
                if (shown) Close();
                return;
            }

            bool open = canvasObject.activeInHierarchy;
            if (!open)
            {
                skipped = false;
                if (shown) Close();
                return;
            }

            if (!shown && !skipped) Open();
            if (shown) FollowSelection();
        }

        // The game's results screen in this scene and its canvas, which it builds in Awake and keeps inactive until
        // the level is done. Searched for once a second until found.
        private bool FindScreen()
        {
            if (screen != null && canvasObject != null) return true;
            if (Time.unscaledTime < nextSearch) return false;
            nextSearch = Time.unscaledTime + 1f;

            screen = FindAnyObjectByType<LevelResultsScreen>();
            if (screen == null) return false;
            Transform built = screen.transform.Find("ResultsCanvas");
            if (built == null) return false;
            canvasObject = built.gameObject;
            canvas = built.GetComponent<Canvas>();
            retryButton = built.Find("Root/Content/Buttons/Retry")?.GetComponent<Button>();
            continueButton = built.Find("Root/Content/Buttons/Continue")?.GetComponent<Button>();
            return true;
        }

        private void Open()
        {
            LevelStatsTracker tracker = LevelStatsTracker.Instance;
            if (tracker == null || !tracker.LastStats.HasValue || canvas == null || retryButton == null || continueButton == null)
            {
                skipped = true;
                return;
            }

            LevelStats stats = tracker.LastStats.Value;
            var kills = new List<KeyValuePair<string, int>>();
            if (stats.KillsByType != null)
                foreach (KeyValuePair<EnemyType, int> pair in stats.KillsByType)
                    kills.Add(new KeyValuePair<string, int>(EnemyName(pair.Key), pair.Value));

            if (view == null)
            {
                view = HudResults.Create(transform);
                view.Hovered += HandleHovered;
                view.Clicked += HandleClicked;
            }

            SubscribeInput();
            ShowConfirm();
            view.Show(stats.CompletionTime, stats.TotalKills, kills, stats.Grade.ToString());
            canvas.enabled = false;
            HideOverlayCanvases();
            shown = true;
        }

        private void Close()
        {
            if (view != null) view.Hide();
            if (canvas != null) canvas.enabled = true;
            ShowOverlayCanvases();
            shown = false;
        }

        // Every other uGUI overlay canvas would draw over this UI Toolkit screen (the level's black fade, the HUD's
        // leftovers), so they're switched off while it's up. Only the ones that were showing; the same ones come back.
        private void HideOverlayCanvases()
        {
            coveredCanvases.Clear();
            foreach (Canvas other in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (other == canvas || !other.isRootCanvas || !other.enabled || !other.gameObject.activeInHierarchy) continue;
                if (other.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                other.enabled = false;
                coveredCanvases.Add(other);
            }
        }

        private void ShowOverlayCanvases()
        {
            foreach (Canvas other in coveredCanvases)
                if (other != null) other.enabled = true;
            coveredCanvases.Clear();
        }

        // Keyboard and controller move the game's own selection; the new screen shows it. If something else took
        // the selection (a click on the new screen), it goes back to the button that was showing.
        private void FollowSelection()
        {
            EventSystem events = EventSystem.current;
            if (events == null) return;
            GameObject selected = events.currentSelectedGameObject;
            if (selected == retryButton.gameObject) view.Select(HudResults.Retry);
            else if (selected == continueButton.gameObject) view.Select(HudResults.Continue);
            else events.SetSelectedGameObject(ButtonFor(view.Selected).gameObject);
        }

        private void HandleHovered(int index)
        {
            if (!shown || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(ButtonFor(index).gameObject);
            view.Select(index);
        }

        private void HandleClicked(int index)
        {
            if (!shown) return;
            Button button = ButtonFor(index);
            if (button.IsInteractable()) button.onClick.Invoke();
        }

        private Button ButtonFor(int index) => index == HudResults.Retry ? retryButton : continueButton;

        private void SubscribeInput()
        {
            GameInputManager next = GameInputManager.Instance;
            if (next == input) return;
            if (input != null) input.OnInputDeviceChanged -= HandleDeviceChanged;
            input = next;
            if (input != null) input.OnInputDeviceChanged += HandleDeviceChanged;
        }

        private void HandleDeviceChanged(bool _) => ShowConfirm();

        // The confirm key on the selected button: the key's name, or the face button's glyph on a controller.
        private void ShowConfirm()
        {
            if (view == null) return;
            bool pad = input != null && input.IsUsingGamepad;
            string key = input != null ? input.GetBindingHint(submitAction) : string.Empty;
            view.ShowConfirm(key, pad ? HudInput.PadButtonFromName(key) : PadButton.None, pad);
        }

        // The same names as LevelResultsScreen's kill rows.
        private static string EnemyName(EnemyType type) => type switch
        {
            EnemyType.FlyingDummy => "Flying Dummy",
            _ => type.ToString(),
        };
    }
}
