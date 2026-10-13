using System;
using UnityEngine;

[System.Serializable]
public class DialogueLine
{
    [Tooltip("Bubble UI speaker. When empty, the legacy portrait/name below are used.")]
    public DialogueSpeaker speaker;
    public DialogueMood mood = DialogueMood.Calm;

    public Sprite speakerPortrait;

    public string speakerName;

    [TextArea(2,5)]
    public string dialogueText;

    [Header("Flow")]
    public bool requiresPlayerInput = true;
    public bool canSkip = true;
    public float autoAdvanceDelay = 2f;

    [Header("Gamplay Lock")]
    public bool lockPlayerMovement = false;
}
