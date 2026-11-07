using System.Collections.Generic;
using junklite;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// CMA dialogue presentation (UI_Art/Export/03_Portrait_Moods_Bubble_Kit): speaker portraits in the bottom
/// corners, name chips, and a short stack of chamfered speech bubbles. The newest bubble is outlined in the
/// speaker's accent and carries the continue chip; older lines dim out. DialogueManager drives the flow and
/// types into <see cref="NewestText"/>. Builds its own overlay canvas; add it next to the DialogueManager.
/// </summary>
public class DialogueBubbleView : MonoBehaviour
{
    private const int MaxBubbles = 3;
    private const int SpriteSize = 64;
    private const int Chamfer = 14;

    [Header("Style")]
    [SerializeField] private Color bubbleFill = new Color(0.035f, 0.05f, 0.06f, 0.94f);
    [SerializeField] private Color newestTextColor = new Color(0.95f, 0.96f, 0.96f, 1f);
    [SerializeField] private Color olderTextColor = new Color(0.5f, 0.53f, 0.55f, 1f);
    [SerializeField] private Color chipTextColor = new Color(0.04f, 0.05f, 0.06f, 1f);
    [SerializeField] private Color inactivePortraitTint = new Color(0.35f, 0.38f, 0.4f, 1f);
    [SerializeField] private float bubbleMaxWidth = 760f;
    [SerializeField] private float newestFontSize = 36f;
    [SerializeField] private float olderFontSize = 30f;
    [SerializeField] private float portraitHeight = 600f;
    [SerializeField] private float pixelPortraitSize = 220f;
    [Tooltip("Name chip height from the bottom edge; keeps the left chip above the health HUD.")]
    [SerializeField] private float chipHeight = 175f;
    [SerializeField] private int sortingOrder = 70;
    [Tooltip("Bubble stack height from the bottom edge; clears the weapon action bar.")]
    [SerializeField] private float stackBottom = 185f;

    private static Sprite fillSprite, outlineSprite, tailSprite, gradientSprite;

    // Enter Play Mode without domain reload keeps statics; rebuild the generated sprites each session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        fillSprite = outlineSprite = tailSprite = gradientSprite = null;
    }

    private readonly List<Bubble> bubbles = new List<Bubble>(MaxBubbles);
    private readonly Queue<Bubble> pool = new Queue<Bubble>(MaxBubbles);
    private SideSlot left, right;
    private GameObject canvasRoot;
    private RectTransform stack;
    private RectTransform continueChip;
    private TextMeshProUGUI continueText;
    private bool built;

    public GameObject Root { get { Build(); return canvasRoot; } }
    public GameObject ContinueIndicator { get { Build(); return continueChip.gameObject; } }
    public TMP_Text ContinueText { get { Build(); return continueText; } }
    public TMP_Text NewestText { get; private set; }

    private sealed class Bubble
    {
        public RectTransform row;
        public HorizontalLayoutGroup rowLayout;
        public RectTransform body;
        public Image outline;
        public Image tail;
        public RectTransform tailRect;
        public TextMeshProUGUI text;
        public LayoutElement textLayout;
        public Color accent;
    }

    private sealed class SideSlot
    {
        public RectTransform root;
        public Image portrait;
        public Image nameBg;
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI tagText;
        public GameObject tagChip;
        public DialogueSpeaker speaker;
    }

    private void Awake() => Build();

    #region Public API

    /// <summary>Clears bubbles and portraits; called when a conversation starts or ends.</summary>
    public void Clear()
    {
        Build();
        for (int i = 0; i < bubbles.Count; i++)
            Recycle(bubbles[i]);
        bubbles.Clear();
        NewestText = null;
        ResetSide(left);
        ResetSide(right);
        continueChip.SetParent(canvasRoot.transform, false);
        continueChip.gameObject.SetActive(false);
    }

    /// <summary>Pushes a new bubble for this line and returns the text component to type into.</summary>
    public TMP_Text ShowLine(DialogueLine line, string fullText)
    {
        Build();

        DialogueSpeaker speaker = line.speaker;
        DialogueSide side = speaker != null ? speaker.side : DialogueSide.Left;
        Color accent = speaker != null ? speaker.accent : new Color(1f, 0.42f, 0.1f, 1f);

        // Dim the previous newest bubble.
        if (bubbles.Count > 0)
            StyleBubble(bubbles[bubbles.Count - 1], newest: false);

        if (bubbles.Count >= MaxBubbles)
        {
            Recycle(bubbles[0]);
            bubbles.RemoveAt(0);
        }

        Bubble b = pool.Count > 0 ? pool.Dequeue() : CreateBubble();
        b.row.gameObject.SetActive(true);
        b.row.SetAsLastSibling();
        b.accent = accent;
        SetBubbleSide(b, side);
        StyleBubble(b, newest: true);
        // Fix the width from the full line so the bubble doesn't stretch while typing.
        float maxText = bubbleMaxWidth - 60f;
        b.textLayout.preferredWidth = Mathf.Min(maxText,
            b.text.GetPreferredValues(fullText ?? string.Empty, float.PositiveInfinity, 0f).x + 2f);
        b.text.text = string.Empty;
        bubbles.Add(b);

        continueChip.SetParent(b.body, false);
        continueChip.anchorMin = continueChip.anchorMax = new Vector2(side == DialogueSide.Right ? 1f : 0f, 0f);
        continueChip.pivot = new Vector2(side == DialogueSide.Right ? 1f : 0f, 0.5f);
        continueChip.anchoredPosition = new Vector2(side == DialogueSide.Right ? -24f : 24f, 0f);
        continueChip.GetComponent<Image>().color = accent;
        continueChip.gameObject.SetActive(false);

        UpdateSpeakers(line, side);

        NewestText = b.text;
        return b.text;
    }

    #endregion

    #region Speakers

    private void UpdateSpeakers(DialogueLine line, DialogueSide activeSide)
    {
        SideSlot active = activeSide == DialogueSide.Right ? right : left;
        SideSlot other = activeSide == DialogueSide.Right ? left : right;

        DialogueSpeaker speaker = line.speaker;
        Sprite portrait = speaker != null ? speaker.GetPortrait(line.mood) : line.speakerPortrait;
        string displayName = speaker != null ? speaker.displayName : line.speakerName;
        string tag = speaker != null && !string.IsNullOrEmpty(speaker.roleTag)
            ? speaker.roleTag
            : line.mood.ToString();

        active.speaker = speaker;
        active.root.gameObject.SetActive(true);
        active.portrait.sprite = portrait;
        active.portrait.enabled = portrait != null;
        active.portrait.color = Color.white;
        // Small pixel-art portraits (the drone) sit above the name chip instead of filling the corner.
        bool pixel = speaker != null && speaker.pixelArt;
        float h = pixel ? pixelPortraitSize : portraitHeight;
        RectTransform pr = active.portrait.rectTransform;
        pr.sizeDelta = new Vector2(pixel ? h : h * 0.95f, h);
        pr.anchoredPosition = new Vector2(pr.anchoredPosition.x, pixel ? chipHeight + 56f : 0f);
        active.nameBg.color = speaker != null ? speaker.accent : new Color(1f, 0.42f, 0.1f, 1f);
        active.nameText.text = string.IsNullOrEmpty(displayName) ? string.Empty : displayName.Trim().TrimEnd(':').ToUpperInvariant();
        active.tagText.text = "// " + tag.ToUpperInvariant();
        active.tagText.color = active.nameBg.color;
        active.tagChip.SetActive(true);

        if (other.root.gameObject.activeSelf)
        {
            other.portrait.color = inactivePortraitTint;
            other.tagChip.SetActive(false);
        }
    }

    private static void ResetSide(SideSlot slot)
    {
        slot.speaker = null;
        slot.root.gameObject.SetActive(false);
    }

    #endregion

    #region Bubbles

    private void StyleBubble(Bubble b, bool newest)
    {
        Color outline = newest ? b.accent : Color.Lerp(b.accent, bubbleFill, 0.55f);
        b.outline.color = outline;
        b.tail.color = outline;
        b.text.color = newest ? newestTextColor : olderTextColor;
        b.text.fontSize = newest ? newestFontSize : olderFontSize;
    }

    private void SetBubbleSide(Bubble b, DialogueSide side)
    {
        bool rightSide = side == DialogueSide.Right;
        b.rowLayout.childAlignment = rightSide ? TextAnchor.LowerRight : TextAnchor.LowerLeft;
        b.tailRect.anchorMin = b.tailRect.anchorMax = new Vector2(rightSide ? 1f : 0f, 0.5f);
        b.tailRect.pivot = new Vector2(0f, 0.5f);
        b.tailRect.anchoredPosition = new Vector2(rightSide ? -1f : 1f, 0f);
        b.tailRect.localScale = new Vector3(rightSide ? 1f : -1f, 1f, 1f);
    }

    private void Recycle(Bubble b)
    {
        if (continueChip.parent == b.body)
            continueChip.SetParent(canvasRoot.transform, false);
        b.row.gameObject.SetActive(false);
        pool.Enqueue(b);
    }

    private Bubble CreateBubble()
    {
        var b = new Bubble();
        b.row = NewRect("BubbleRow", stack);
        b.rowLayout = b.row.gameObject.AddComponent<HorizontalLayoutGroup>();
        b.rowLayout.childControlWidth = b.rowLayout.childControlHeight = true;
        b.rowLayout.childForceExpandWidth = b.rowLayout.childForceExpandHeight = false;

        b.body = NewRect("Bubble", b.row);
        var fill = b.body.gameObject.AddComponent<Image>();
        fill.sprite = fillSprite;
        fill.type = Image.Type.Sliced;
        fill.color = bubbleFill;
        fill.raycastTarget = false;
        var bodyLayout = b.body.gameObject.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.padding = new RectOffset(30, 30, 16, 20);
        bodyLayout.childControlWidth = bodyLayout.childControlHeight = true;
        bodyLayout.childForceExpandWidth = bodyLayout.childForceExpandHeight = false;

        b.outline = NewImage("Outline", b.body, outlineSprite);
        b.outline.type = Image.Type.Sliced;
        Stretch(b.outline.rectTransform);

        b.tail = NewImage("Tail", b.body, tailSprite);
        b.tailRect = b.tail.rectTransform;
        b.tailRect.sizeDelta = new Vector2(16f, 24f);

        b.text = NewText("Text", b.body);
        UIFonts.ApplyHudTitle(b.text);
        b.text.textWrappingMode = TextWrappingModes.Normal;
        b.text.characterSpacing = 4f;
        b.textLayout = b.text.gameObject.AddComponent<LayoutElement>();
        return b;
    }

    #endregion

    #region Build

    private void Build()
    {
        if (built) return;
        built = true;
        EnsureSprites();

        canvasRoot = new GameObject("DialogueBubbleCanvas", typeof(RectTransform));
        canvasRoot.transform.SetParent(transform, false);
        var canvas = canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var group = canvasRoot.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        // Bottom gradient keeps text readable over a lit level.
        Image shade = NewImage("Shade", canvasRoot.transform, gradientSprite);
        shade.color = new Color(0f, 0f, 0f, 0.8f);
        RectTransform shadeRect = shade.rectTransform;
        shadeRect.anchorMin = new Vector2(0f, 0f);
        shadeRect.anchorMax = new Vector2(1f, 0f);
        shadeRect.pivot = new Vector2(0.5f, 0f);
        shadeRect.sizeDelta = new Vector2(0f, 520f);

        left = BuildSide(DialogueSide.Left);
        right = BuildSide(DialogueSide.Right);

        stack = NewRect("Bubbles", canvasRoot.transform);
        stack.anchorMin = stack.anchorMax = new Vector2(0.5f, 0f);
        stack.pivot = new Vector2(0.5f, 0f);
        stack.anchoredPosition = new Vector2(0f, stackBottom);
        stack.sizeDelta = new Vector2(1060f, 0f);
        var stackLayout = stack.gameObject.AddComponent<VerticalLayoutGroup>();
        stackLayout.spacing = 18f;
        stackLayout.childAlignment = TextAnchor.LowerCenter;
        stackLayout.childControlWidth = stackLayout.childControlHeight = true;
        stackLayout.childForceExpandWidth = true;
        stackLayout.childForceExpandHeight = false;
        var stackFitter = stack.gameObject.AddComponent<ContentSizeFitter>();
        stackFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        continueChip = NewRect("Continue", canvasRoot.transform);
        var chipBg = continueChip.gameObject.AddComponent<Image>();
        chipBg.raycastTarget = false;
        var chipLayout = continueChip.gameObject.AddComponent<HorizontalLayoutGroup>();
        chipLayout.padding = new RectOffset(10, 10, 2, 2);
        chipLayout.childControlWidth = chipLayout.childControlHeight = true;
        var chipFitter = continueChip.gameObject.AddComponent<ContentSizeFitter>();
        chipFitter.horizontalFit = chipFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        continueChip.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        continueText = NewText("Text", continueChip);
        UIFonts.ApplyHudBody(continueText);
        continueText.fontSize = 18f;
        continueText.fontStyle = FontStyles.Bold;
        continueText.color = chipTextColor;
        continueText.text = "E >";
        continueChip.gameObject.SetActive(false);

        canvasRoot.SetActive(false);
    }

    private SideSlot BuildSide(DialogueSide side)
    {
        bool r = side == DialogueSide.Right;
        float x = r ? 1f : 0f;
        var slot = new SideSlot();

        slot.root = NewRect(r ? "Right" : "Left", canvasRoot.transform);
        Stretch(slot.root);

        slot.portrait = NewImage("Portrait", slot.root, null);
        slot.portrait.preserveAspect = true;
        RectTransform pr = slot.portrait.rectTransform;
        pr.anchorMin = pr.anchorMax = new Vector2(x, 0f);
        pr.pivot = new Vector2(x, 0f);
        pr.anchoredPosition = new Vector2(r ? -30f : 30f, 0f);
        pr.sizeDelta = new Vector2(portraitHeight * 0.95f, portraitHeight);

        RectTransform chips = NewRect("Chips", slot.root);
        chips.anchorMin = chips.anchorMax = new Vector2(x, 0f);
        chips.pivot = new Vector2(x, 0f);
        chips.anchoredPosition = new Vector2(r ? -32f : 32f, chipHeight);
        var chipsLayout = chips.gameObject.AddComponent<HorizontalLayoutGroup>();
        chipsLayout.childControlWidth = chipsLayout.childControlHeight = true;
        chipsLayout.childForceExpandWidth = chipsLayout.childForceExpandHeight = false;
        var chipsFitter = chips.gameObject.AddComponent<ContentSizeFitter>();
        chipsFitter.horizontalFit = chipsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform nameChip = NewRect("Name", chips);
        slot.nameBg = nameChip.gameObject.AddComponent<Image>();
        slot.nameBg.raycastTarget = false;
        var nameLayout = nameChip.gameObject.AddComponent<HorizontalLayoutGroup>();
        nameLayout.padding = new RectOffset(16, 16, 4, 4);
        nameLayout.childControlWidth = nameLayout.childControlHeight = true;
        slot.nameText = NewText("Text", nameChip);
        UIFonts.ApplyHudTitle(slot.nameText);
        slot.nameText.fontSize = 30f;
        slot.nameText.characterSpacing = 6f;
        slot.nameText.color = chipTextColor;

        RectTransform tagChip = NewRect("Tag", chips);
        var tagBg = tagChip.gameObject.AddComponent<Image>();
        tagBg.color = new Color(0.02f, 0.03f, 0.035f, 0.95f);
        tagBg.raycastTarget = false;
        var tagLayout = tagChip.gameObject.AddComponent<HorizontalLayoutGroup>();
        tagLayout.padding = new RectOffset(14, 14, 8, 8);
        tagLayout.childControlWidth = tagLayout.childControlHeight = true;
        slot.tagText = NewText("Text", tagChip);
        UIFonts.ApplyHudBody(slot.tagText);
        slot.tagText.fontSize = 16f;
        slot.tagText.characterSpacing = 14f;
        slot.tagChip = tagChip.gameObject;

        slot.root.gameObject.SetActive(false);
        return slot;
    }

    private static void EnsureSprites()
    {
        if (fillSprite != null) return;
        const float t = 2f;
        fillSprite = MakeSprite(SpriteSize, SpriteSize, (x, y) => Chamfered(x, y, 0f), border: 20);
        outlineSprite = MakeSprite(SpriteSize, SpriteSize,
            (x, y) => Chamfered(x, y, 0f) * (1f - Chamfered(x, y, t)), border: 20);
        tailSprite = MakeSprite(16, 24, (x, y) => (x <= 16f * (1f - Mathf.Abs(y - 12f) / 12f)) ? 1f : 0f, border: 0);

        var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
            tex.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Pow(1f - y / 63f, 1.6f)));
        tex.Apply();
        gradientSprite = Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f));
    }

    // 1 inside the chamfered rect (top-left and bottom-right corners cut), 0 outside.
    private static float Chamfered(float x, float y, float inset)
    {
        float s = SpriteSize;
        if (x < inset || y < inset || x > s - inset || y > s - inset) return 0f;
        if (x + (s - y) < Chamfer + inset * 1.4142f) return 0f;
        if ((s - x) + y < Chamfer + inset * 1.4142f) return 0f;
        return 1f;
    }

    private static Sprite MakeSprite(int w, int h, System.Func<float, float, float> coverage, int border)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // 4x supersampling for soft edges.
            float a = 0f;
            for (int sy = 0; sy < 2; sy++)
            for (int sx = 0; sx < 2; sx++)
                a += coverage(x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f);
            pixels[y * w + x] = new Color(1f, 1f, 1f, a * 0.25f);
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, Transform parent, Sprite sprite)
    {
        RectTransform rt = NewRect(name, parent);
        rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent)
    {
        RectTransform rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    #endregion
}
