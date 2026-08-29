using System.Collections;
using UnityEngine;
using TMPro;
using System;
using junklite;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager instance;

    [Header("UI")]
    public Image portraitImage;
    public GameObject dialogueBox;
    public TMP_Text speakerText;
    public TMP_Text dialogueText;
    public GameObject continueIndicator;

    [Header("Settings")]
    public float typeSpeed = 0.03f;

    private DialogueSequence currentSequence;
    private int currentIndex;

    public bool IsInDialogue => currentSequence != null;
    public bool IsContinueInputSuppressed { get; set; }

    private Coroutine typingCoroutine;
    private bool isTyping;
    private bool waitingForInput;
    private bool revealedCurrentLine;
    private int lastShownIndex = -1;
    private float lastContinueTime = -10f;
    private float continueDebounce = 0.12f; // seconds, uses unscaled time so it works during freezes


    public event Action OnDialogueContinue = delegate { };
    public event Action OnDialogueEnded = delegate { };

    // Stored so we can unsubscribe on destroy
    private Action<InputAction.CallbackContext> playerContinueCallback;
    private Action<InputAction.CallbackContext> uiContinueCallback;
    private Action parryCallback;

    #region Lifecycle

    private void Awake()
    {
        if (instance == null)
            instance = this;
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

        private void Start()
    {
        // Do not subscribe NextLine to the OnDialogueContinue event to avoid
        // re-entrancy/duplicate calls when input triggers the event. Input is
        // handled explicitly by ProcessContinueInput which calls NextLine()
        // directly.

        dialogueBox.SetActive(false);
        if (continueIndicator) continueIndicator.SetActive(false);
            // Route continue input through a single handler that:
            // - ignores input when globally suppressed
            // - ignores input for unskippable lines
            // - when typing, reveals the full line on first press
            // - only advances when the line is revealed and ready to advance
            playerContinueCallback = _ => ProcessContinueInput();
            uiContinueCallback = _ => ProcessContinueInput();

            GameInputManager.Instance.controls.Player.DialogueContinue.performed += playerContinueCallback;
            GameInputManager.Instance.controls.UI.DialogueContinue.performed += uiContinueCallback;

            // Listen for parry input so that when the player parries during a
            // freeze (timeScale == 0) we can advance dialogue appropriately.
            if (GameInputManager.Instance != null)
            {
                parryCallback = () => { if (IsInDialogue) ParryAdvance(); };
                GameInputManager.Instance.OnParry += parryCallback;
            }
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;


        //if (GameInputManager.Instance == null) return;
        GameInputManager.Instance.controls.Player.DialogueContinue.performed -= playerContinueCallback;
        GameInputManager.Instance.controls.UI.DialogueContinue.performed -= uiContinueCallback;
        if (GameInputManager.Instance != null && parryCallback != null)
            GameInputManager.Instance.OnParry -= parryCallback;
    }

    #endregion

    #region Public API

    public void StartDialogue(DialogueSequence sequence)
    {
        // Ignore duplicate requests to start the same sequence while it's
        // already active to avoid restarting the currently shown line.
        if (currentSequence != null && currentSequence == sequence)
        {
            return;
        }

        currentSequence = sequence;
        currentIndex = 0;
        dialogueBox.SetActive(true);
        // Reset per-line guards/state so re-starting a sequence shows lines cleanly.
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }
        isTyping = false;
        revealedCurrentLine = false;
        lastShownIndex = -1;

        ShowLine();

    }

    // Timeline hook
    public void PlayDialogue(DialogueSequence sequence) => StartDialogue(sequence);

    public void TurnOffInput()
    { 
        GameInputManager.Instance.SetGameplayInputEnabled(false);
    }

    public void TurnOnInput()
    {
        GameInputManager.Instance.SetGameplayInputEnabled(true);
    }
    public void NextLine()
    {
        if (currentSequence == null) return;
        var line = currentSequence.dialogueLines[currentIndex];
        if (line.canSkip)
        { 
            IsContinueInputSuppressed = false;
        }else
        {
            IsContinueInputSuppressed = true;
        }

        // Skip typing — reveal full line immediately
        if (isTyping)
        {
            StopCoroutine(typingCoroutine);
            dialogueText.text = line.dialogueText;
            isTyping = false;

            if (line.requiresPlayerInput)
            {
                waitingForInput = true;
                if (continueIndicator) continueIndicator.SetActive(true);
            }

            return;
        }

        if (line.requiresPlayerInput && !waitingForInput) return;

        waitingForInput = false;
        if (continueIndicator) continueIndicator.SetActive(false);

        currentIndex++;

        if (currentIndex >= currentSequence.dialogueLines.Length)
            EndDialogue();
        else
            ShowLine();
    }

    #endregion

    #region Internal

    private void ShowLine()
    {
        // Prevent restarting the same line if it's already being shown/typed.
        if (lastShownIndex == currentIndex && (isTyping || revealedCurrentLine))
            return;
        

        var line = currentSequence.dialogueLines[currentIndex];

        speakerText.text = line.speakerName;
        if (portraitImage)
        {
            portraitImage.sprite = line.speakerPortrait;
            portraitImage.color = line.speakerPortrait != null ? Color.white : Color.clear;
        }
        GameInputManager.Instance.SetGameplayInputEnabled(!line.lockPlayerMovement);

        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);

        // reset reveal state for the new line
        revealedCurrentLine = false;
        lastShownIndex = currentIndex;
        typingCoroutine = StartCoroutine(TypeLine(line));
    }

    private IEnumerator TypeLine(DialogueLine line)
    {
        isTyping = true;
        waitingForInput = false;
        dialogueText.text = "";

        foreach (char c in line.dialogueText)
        {
            dialogueText.text += c;
            yield return new WaitForSeconds(typeSpeed);
        }

        isTyping = false;
        revealedCurrentLine = true;

        if (line.requiresPlayerInput)
        {
            waitingForInput = true;
            if (continueIndicator) continueIndicator.SetActive(true);
        }
        else
        {
            yield return new WaitForSeconds(line.autoAdvanceDelay);
            NextLine();
        }
    }

    private void ProcessContinueInput()
    {
        // debounce rapid/duplicate input (use unscaled time so it works during pausing/freezes)
        if (Time.unscaledTime - lastContinueTime < continueDebounce) return;

        if (IsContinueInputSuppressed) return;
        if (currentSequence == null) return;
        if (currentIndex < 0 || currentIndex >= currentSequence.dialogueLines.Length) return;

        var line = currentSequence.dialogueLines[currentIndex];

        // absolutely ignore input for unskippable lines
        if (!line.canSkip) return;

        // If we're still typing, reveal the rest of the line on first press
        if (isTyping && !revealedCurrentLine)
        {
            if (typingCoroutine != null)
                StopCoroutine(typingCoroutine);
            dialogueText.text = line.dialogueText;
            isTyping = false;
            revealedCurrentLine = true;

            if (line.requiresPlayerInput)
            {
                waitingForInput = true;
                if (continueIndicator) continueIndicator.SetActive(true);
            }

            return;
        }

        // If this line requires player input, don't advance until the player is expected to continue
        if (line.requiresPlayerInput && !waitingForInput) return;

        // Safe to advance: call NextLine directly to avoid event-based re-entrancy.
        lastContinueTime = Time.unscaledTime;
        NextLine();
        // Notify external subscribers that a continue occurred (do not subscribe
        // NextLine to this event to avoid duplicate calls).
        OnDialogueContinue?.Invoke();
    }

    /// <summary>
    /// Advance dialogue in response to a parry input. This bypasses normal
    /// input suppression and debouncing so it will work while the game is
    /// frozen (timeScale == 0). It will reveal the current line if typing
    /// and then advance.
    /// </summary>
    public void ParryAdvance()
    {
        if (currentSequence == null) return;

        var line = currentSequence.dialogueLines[currentIndex];

        // Reveal if still typing
        if (isTyping)
        {
            if (typingCoroutine != null)
                StopCoroutine(typingCoroutine);
            dialogueText.text = line.dialogueText;
            isTyping = false;
            revealedCurrentLine = true;
            // If this line requires explicit player input, mark it satisfied so NextLine can proceed.
            if (line.requiresPlayerInput)
                waitingForInput = true;
        }

        // Force advancement regardless of canSkip or suppression
        lastContinueTime = Time.unscaledTime;
        NextLine();
        OnDialogueContinue?.Invoke();
        // If the game is frozen, coroutines using scaled time won't progress.
        // Reveal the newly shown line immediately so the player sees it during the freeze.
        if (currentSequence != null && currentIndex >= 0 && currentIndex < currentSequence.dialogueLines.Length && Time.timeScale == 0f)
        {
            var newLine = currentSequence.dialogueLines[currentIndex];
            if (typingCoroutine != null)
                StopCoroutine(typingCoroutine);
            dialogueText.text = newLine.dialogueText;
            isTyping = false;
            revealedCurrentLine = true;
            waitingForInput = newLine.requiresPlayerInput;
            if (waitingForInput && continueIndicator) continueIndicator.SetActive(true);
        }
    }

    private void EndDialogue()
    {
        var finished = currentSequence;
        dialogueBox.SetActive(false);
        currentSequence = null;

        GameInputManager.Instance.SetGameplayInputEnabled(true);

        if (finished != null && finished.nextSequence != null)
            StartDialogue(finished.nextSequence);
        else
            OnDialogueEnded();
    }

    #endregion
}
