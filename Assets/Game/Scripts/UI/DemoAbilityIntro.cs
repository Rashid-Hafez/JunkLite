using UnityEngine;
using UnityEngine.InputSystem;

namespace junklite
{
    /// <summary>
    /// Opening demo note: freezes the world, shows a gradient overlay and the shared tutorial prompt,
    /// and resumes on Interact (E or the controller button). Does not use GameManager pause, so the
    /// pause menu stays closed. Overlay and prompt are authored or referenced in the Inspector.
    /// </summary>
    public class DemoAbilityIntro : MonoBehaviour
    {
        [Tooltip("Shared tutorial prompt. Shows the message with an Interact chip.")]
        [SerializeField] private TutorialPromptHUD prompt;
        [Tooltip("Authored black gradient overlay (CanvasGroup on a canvas prefab). Shown while the intro runs.")]
        [SerializeField] private GameObject overlay;
        [SerializeField, TextArea] private string message =
            "For the best user experience we have provided you with Dash and Double jump, but in the game you would have to unlock them first";
        [Tooltip("Input action that continues. Player/Interact covers keyboard and controller.")]
        [SerializeField] private string continueAction = "Player/Interact";

        private InputAction continueInput;
        private bool running;

        private void Start()
        {
            if (prompt == null)
            {
                Debug.LogWarning("[DemoAbilityIntro] No TutorialPromptHUD assigned. Intro skipped.", this);
                return;
            }
            GameInputManager input = GameInputManager.Instance;
            if (input == null || input.controls == null) return;

            continueInput = input.controls.FindAction(continueAction);
            if (continueInput == null)
            {
                Debug.LogWarning($"[DemoAbilityIntro] Action '{continueAction}' not found. Intro skipped.", this);
                return;
            }

            running = true;
            Time.timeScale = 0f;
            input.SetGameplayInputEnabled(false);
            if (overlay != null) overlay.SetActive(true);
            prompt.Show(new TutorialPromptHUD.Entry(continueAction, message));
            continueInput.performed += OnContinue;
        }

        private void OnContinue(InputAction.CallbackContext _) => Finish();

        private void Finish()
        {
            if (!running) return;
            running = false;
            continueInput.performed -= OnContinue;

            prompt.Hide();
            if (overlay != null) overlay.SetActive(false);
            Time.timeScale = 1f;
            GameInputManager.Instance?.SetGameplayInputEnabled(true);
        }

        private void OnDisable()
        {
            // Never leave the world frozen if this object goes away mid-intro.
            if (!running) return;
            if (continueInput != null) continueInput.performed -= OnContinue;
            running = false;
            Time.timeScale = 1f;
        }
    }
}
