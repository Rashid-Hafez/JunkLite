using System.Collections;
using UnityEngine;
using TMPro;
using System;
using junklite;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager instance;

    [Header("UI")]
    public GameObject dialogueBox;
    public TMP_Text speakerText;
    public TMP_Text dialogueText;
    public GameObject continueIndicator;

    [Header("Settings")]
    public float typeSpeed = 0.03f;

    private DialogueSequence currentSequence;
    private int currentIndex;

    private Coroutine typingCoroutine;
    private bool isTyping;
    private bool waitingForInput;

    // Dialogue events
    public event Action OnDialogueContinue = delegate { };

    private void Start()
    {
        OnDialogueContinue += NextLine;

        if (instance == null)
            instance = this;
        else if (instance != this)
            Destroy(gameObject);

        dialogueBox.SetActive(false);
        if (continueIndicator) continueIndicator.SetActive(false);


        GameInputManager.Instance.controls.Player.DialogueContinue.performed += _ =>
        {
            OnDialogueContinue();
        };

        GameInputManager.Instance.controls.UI.DialogueContinue.performed += _ =>
        {
            OnDialogueContinue();
        };
    }

    // ENTRY POINT (used by everything)
    public void StartDialogue(DialogueSequence sequence)
    {
        currentSequence = sequence;
        currentIndex = 0;
        dialogueBox.SetActive(true);
        ShowLine();
    }

    void ShowLine()
    {
        var line = currentSequence.dialogueLines[currentIndex];

        speakerText.text = line.speakerName;

        // 🔒 Apply player control
        if (line.lockPlayerMovement)
        {
            GameInputManager.Instance.SetGameplayInputEnabled(false);
        }
        else
        {
            GameInputManager.Instance.SetGameplayInputEnabled(true);
        }

        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);

        typingCoroutine = StartCoroutine(TypeLine(line));
    }

    IEnumerator TypeLine(DialogueLine line)
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

    public void NextLine()
    {
        if (currentSequence == null) return;

        var line = currentSequence.dialogueLines[currentIndex];

        // Skip typing
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

        // Block if not ready
        if (line.requiresPlayerInput && !waitingForInput)
            return;

        waitingForInput = false;
        if (continueIndicator) continueIndicator.SetActive(false);

        currentIndex++;

        if (currentIndex >= currentSequence.dialogueLines.Length)
        {
            EndDialogue();
        }
        else
        {
            ShowLine();
        }
    }

    void EndDialogue()
    {
        dialogueBox.SetActive(false);
        currentSequence = null;

        // Restore player control
        GameInputManager.Instance.SetGameplayInputEnabled(true);

        // 🔗 Chain next sequence
        if (currentSequence != null && currentSequence.nextSequence != null)
        {
            StartDialogue(currentSequence.nextSequence);
        }
    }

    // 🎬 Timeline hook
    public void PlayDialogue(DialogueSequence sequence)
    {
        StartDialogue(sequence);
    }
}