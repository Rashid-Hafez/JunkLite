using System;
using UnityEngine;

[System.Serializable]
public class DialogueLine
{
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
    public bool lockPlayerCombat = false;
}
