using UnityEngine;

public enum DialogueMood
{
    Calm,
    Shout,
    Angry,
    Profile
}

public enum DialogueSide
{
    Left,
    Right
}

/// <summary>
/// Read-only speaker identity for the bubble dialogue UI: name chip, side, accent colour and a portrait per mood.
/// </summary>
[CreateAssetMenu(fileName = "NewDialogueSpeaker", menuName = "Dialogue/Speaker")]
public class DialogueSpeaker : ScriptableObject
{
    public string displayName = "SPEAKER";
    [Tooltip("Shown after the name, e.g. FIXER. Empty uses the line's mood.")]
    public string roleTag;
    public DialogueSide side = DialogueSide.Left;
    public Color accent = new Color(0.25f, 0.85f, 0.78f, 1f);

    [Header("Portraits (Calm is the fallback)")]
    public Sprite calm;
    public Sprite shout;
    public Sprite angry;
    public Sprite profile;
    [Tooltip("Use point filtering friendly sizing for small pixel-art portraits.")]
    public bool pixelArt;

    public Sprite GetPortrait(DialogueMood mood)
    {
        Sprite s = mood switch
        {
            DialogueMood.Shout => shout,
            DialogueMood.Angry => angry,
            DialogueMood.Profile => profile,
            _ => calm
        };
        return s != null ? s : calm;
    }
}
