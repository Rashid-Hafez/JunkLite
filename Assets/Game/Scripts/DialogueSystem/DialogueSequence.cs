using UnityEngine;


[CreateAssetMenu(fileName ="NewDialogueSequence",menuName = "Dialogue/Sequence")]
public class DialogueSequence : ScriptableObject
{
    public DialogueLine[] dialogueLines;

    [Header("Chaining")]
    public DialogueSequence nextSequence;
}
