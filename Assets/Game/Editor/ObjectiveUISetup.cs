using junklite;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-time authoring tool: builds the Objective Tracker HUD and Missions Page prefabs and wires them
/// into New Player UI 2. After this, the prefabs are normal assets; edit their look in the Inspector.
/// Rerunning only creates missing prefabs, it never overwrites edited ones.
/// </summary>
public static class ObjectiveUISetup
{
    private const string Folder = "Assets/Game/New UI/Prefabs/New";
    private const string TrackerPath = Folder + "/Objective Tracker HUD.prefab";
    private const string MissionsPath = Folder + "/Missions Page.prefab";
    private const string PlayerUIPath = Folder + "/New Player UI 2.prefab";

    private static readonly Color Accent = new(1f, 0.42f, 0.1f, 1f);
    private static readonly Color Complete = new(0.25f, 0.85f, 0.78f, 1f);
    private static readonly Color Panel = new(0.03f, 0.045f, 0.05f, 0.82f);
    private static readonly Color Label = new(0.88f, 0.92f, 0.92f, 1f);
    private static readonly Color Muted = new(0.62f, 0.68f, 0.7f, 1f);

    [MenuItem("Tools/JunkLite/UI/Build Objective HUD + Missions Prefabs")]
    public static void Build()
    {
        GameObject tracker = AssetDatabase.LoadAssetAtPath<GameObject>(TrackerPath) ?? BuildTracker();
        GameObject missions = AssetDatabase.LoadAssetAtPath<GameObject>(MissionsPath) ?? BuildMissions();
        WirePlayerUI(tracker, missions);
        AssetDatabase.SaveAssets();
        Debug.Log("[ObjectiveUISetup] Objective Tracker HUD and Missions Page are ready and wired into New Player UI 2.");
    }

    private static GameObject BuildTracker()
    {
        var root = new GameObject("Objective Tracker HUD", typeof(RectTransform), typeof(CanvasGroup));
        var rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(40f, -40f);
        rect.sizeDelta = new Vector2(540f, 132f);
        var group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        Img("Panel", root.transform, Panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Img("Accent", root.transform, Accent, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(6f, 0f));

        TMP_Text header = Text("Header", root.transform, "OBJECTIVE", 20f, Accent, false, new Vector2(24f, -12f), new Vector2(300f, 26f));
        header.characterSpacing = 6f;
        TMP_Text newTag = Text("New Tag", root.transform, "NEW", 20f, Accent, false, new Vector2(420f, -12f), new Vector2(100f, 26f));
        newTag.alignment = TextAlignmentOptions.TopRight;
        TMP_Text title = Text("Title", root.transform, "GET TO THE ELEVATOR", 40f, Label, true, new Vector2(24f, -38f), new Vector2(496f, 46f));
        TMP_Text body = Text("Body", root.transform, "Objective description.", 20f, Muted, false, new Vector2(24f, -86f), new Vector2(496f, 40f));
        body.textWrappingMode = TextWrappingModes.Normal;
        TMP_Text done = Text("Complete Mark", root.transform, "COMPLETE", 20f, Complete, false, new Vector2(420f, -12f), new Vector2(100f, 26f));
        done.alignment = TextAlignmentOptions.TopRight;
        done.gameObject.SetActive(false);

        var view = root.AddComponent<ObjectiveTrackerView>();
        var so = new SerializedObject(view);
        so.FindProperty("group").objectReferenceValue = group;
        so.FindProperty("title").objectReferenceValue = title;
        so.FindProperty("body").objectReferenceValue = body;
        so.FindProperty("completeMark").objectReferenceValue = done.gameObject;
        so.FindProperty("newMark").objectReferenceValue = newTag.gameObject;
        so.ApplyModifiedPropertiesWithoutUndo();

        return Save(root, TrackerPath);
    }

    private static GameObject BuildMissions()
    {
        // Sized for the inventory frame's page slot (same rect InventoryUI uses for its pages).
        var root = new GameObject("Missions Page", typeof(RectTransform));
        var rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(40f, -170f);
        rect.sizeDelta = new Vector2(1360f, 568f);

        TMP_Text header = Text("Header", root.transform, "MISSIONS", 56f, Label, true, new Vector2(0f, 0f), new Vector2(800f, 64f));
        header.characterSpacing = 4f;

        var list = new GameObject("Rows", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
        list.SetParent(root.transform, false);
        list.anchorMin = list.anchorMax = list.pivot = new Vector2(0f, 1f);
        list.anchoredPosition = new Vector2(0f, -84f);
        list.sizeDelta = new Vector2(1360f, 484f);
        var layout = list.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 12f;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        // Row template (inactive; MissionsPageView copies it).
        var row = new GameObject("Mission Row", typeof(RectTransform));
        var rowRect = (RectTransform)row.transform;
        rowRect.SetParent(list, false);
        rowRect.sizeDelta = new Vector2(1360f, 96f);
        Img("Panel", row.transform, Panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Img("Accent", row.transform, Accent, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(6f, 0f));
        TMP_Text rowTitle = Text("Title", row.transform, "OBJECTIVE TITLE", 36f, Label, true, new Vector2(24f, -10f), new Vector2(1000f, 42f));
        TMP_Text rowBody = Text("Body", row.transform, "Objective description.", 20f, Muted, false, new Vector2(24f, -54f), new Vector2(1100f, 34f));
        TMP_Text main = Text("Primary Mark", row.transform, "MAIN", 20f, Accent, false, new Vector2(1180f, -14f), new Vector2(160f, 26f));
        main.alignment = TextAlignmentOptions.TopRight;
        TMP_Text rowDone = Text("Complete Mark", row.transform, "COMPLETE", 20f, Complete, false, new Vector2(1180f, -54f), new Vector2(160f, 26f));
        rowDone.alignment = TextAlignmentOptions.TopRight;

        var rowView = row.AddComponent<MissionRowView>();
        var rso = new SerializedObject(rowView);
        rso.FindProperty("title").objectReferenceValue = rowTitle;
        rso.FindProperty("body").objectReferenceValue = rowBody;
        rso.FindProperty("completeMark").objectReferenceValue = rowDone.gameObject;
        rso.FindProperty("primaryMark").objectReferenceValue = main.gameObject;
        rso.ApplyModifiedPropertiesWithoutUndo();
        row.SetActive(false);

        TMP_Text empty = Text("Empty Label", root.transform, "NO ACTIVE MISSIONS", 28f, Muted, false, new Vector2(0f, -120f), new Vector2(1360f, 40f));

        var page = root.AddComponent<MissionsPageView>();
        var pso = new SerializedObject(page);
        pso.FindProperty("rowTemplate").objectReferenceValue = rowView;
        pso.FindProperty("rowParent").objectReferenceValue = list;
        pso.FindProperty("emptyLabel").objectReferenceValue = empty;
        pso.ApplyModifiedPropertiesWithoutUndo();

        return Save(root, MissionsPath);
    }

    private static void WirePlayerUI(GameObject trackerPrefab, GameObject missionsPrefab)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerUIPath);
        try
        {
            if (root.GetComponentInChildren<ObjectiveTrackerView>(true) == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(trackerPrefab, root.transform);
                instance.transform.SetAsLastSibling();
            }

            var inventory = root.GetComponentInChildren<InventoryUI>(true);
            if (inventory != null)
            {
                var so = new SerializedObject(inventory);
                SerializedProperty screen = so.FindProperty("missionsScreen");
                var current = screen.objectReferenceValue as GameObject;
                if (current == null || current.GetComponent<MissionsPageView>() == null)
                {
                    // The prefab asset is instantiated by InventoryUI at runtime.
                    screen.objectReferenceValue = missionsPrefab;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            PrefabUtility.SaveAsPrefabAsset(root, PlayerUIPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Image Img(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text Text(string name, Transform parent, string value, float size, Color color, bool title, Vector2 pos, Vector2 box)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = box;
        var text = go.GetComponent<TextMeshProUGUI>();
        if (title) UIFonts.ApplyHudTitle(text);
        else UIFonts.ApplyHudBody(text);
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject Save(GameObject root, string path)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }
}
