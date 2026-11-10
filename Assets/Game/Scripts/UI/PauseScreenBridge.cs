using Junklite.Pause;
using UnityEngine;
using UnityEngine.InputSystem;

namespace junklite
{
    /// <summary>
    /// Runs the UI Toolkit pause screen (Assets/Junklite/Pause) from the game's pause state.
    /// GameManager stays the owner of pausing (time scale, state): this opens the screen on Paused and
    /// closes it on any other state, and turns the screen's choices into GameManager calls
    /// (RESUME → ResumeGame, QUIT TO TITLE → RestartGame, i.e. the title scene). The package's own
    /// Esc/Start handling and time scale are switched off on the prefab. Resume also on Start, Esc
    /// (UI Cancel) and the gamepad's B. The "Current build" strip shows the player's real mods.
    /// Instantiated by GameUIManager as the pause menu prefab.
    /// </summary>
    [RequireComponent(typeof(PauseMenu))]
    public sealed class PauseScreenBridge : MonoBehaviour
    {
        private PauseMenu menu;
        private GameManager subscribedGame;
        private GameInputManager subscribedInput;
        private int openedFrame = -1;

        private void Awake() => menu = GetComponent<PauseMenu>();

        private void OnEnable()
        {
            menu.CommandExecuted += HandleCommand;
            Rebind();
        }

        private void OnDisable()
        {
            menu.CommandExecuted -= HandleCommand;
            if (subscribedGame != null) subscribedGame.OnGameStateChanged -= HandleState;
            subscribedGame = null;
            BindInput(false);
        }

        private void Update()
        {
            Rebind();
            if (!menu.IsOpen || Time.frameCount == openedFrame) return;
            Gamepad pad = Gamepad.current;
            if (pad != null && pad.buttonEast.wasPressedThisFrame) Resume();
        }

        private void Rebind()
        {
            GameManager game = GameManager.Instance;
            if (game == subscribedGame) return;
            if (subscribedGame != null) subscribedGame.OnGameStateChanged -= HandleState;
            subscribedGame = game;
            if (subscribedGame != null) subscribedGame.OnGameStateChanged += HandleState;
        }

        private void HandleState(GameManager.GameState state)
        {
            if (state == GameManager.GameState.Paused) Open();
            else Close();
        }

        private void Open()
        {
            if (menu.IsOpen) return;
            PlayerCharacter player = PlayerLifecycle.Instance != null ? PlayerLifecycle.Instance.Player : null;
            var inventory = FindFirstObjectByType<InventoryScreenBridge>(FindObjectsInactive.Include);
            if (inventory != null && player != null) menu.SetInventory(inventory.SyncForDisplay(player));

            openedFrame = Time.frameCount;
            menu.Open();
            GameInputManager.Instance?.SwitchToUIActionMap();
            BindInput(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void Close()
        {
            BindInput(false);
            if (!menu.IsOpen) return;
            menu.Close();
            GameInputManager.Instance?.SwitchToPlayerActionMap();
        }

        private void HandleCommand(PauseCommand command)
        {
            switch (command)
            {
                case PauseCommand.Resume:
                    Resume();
                    break;
                case PauseCommand.QuitToTitle:
                    BindInput(false);
                    GameManager.Instance?.RestartGame();
                    break;
            }
        }

        private void Resume()
        {
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.Paused)
                GameManager.Instance.ResumeGame();
            else
                Close();
        }

        // Esc and Start are the UI map's Cancel while paused (the Player map's Pause is off then).
        private void HandleCancel()
        {
            if (Time.frameCount != openedFrame) Resume();
        }

        private void BindInput(bool bind)
        {
            if (subscribedInput != null) subscribedInput.OnUICancel -= HandleCancel;
            subscribedInput = bind ? GameInputManager.Instance : null;
            if (subscribedInput != null) subscribedInput.OnUICancel += HandleCancel;
        }
    }
}
