using junklite;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace junklite.Editor
{
    /// <summary>
    /// One-time authoring tool: builds the Upgrades Page prefab (the UPGRADES tab of the I menu) and wires it
    /// into New Player UI 2. Afterwards the prefab is a normal asset: edit colours, sizes and fonts in the
    /// Inspector. Rerunning only creates the prefab when it is missing; it never overwrites an edited one.
    /// Same palette as the inventory cards (navy, cyan, magenta).
    /// </summary>
    public static class UpgradesUISetup
    {
        private const string Folder = "Assets/Game/New UI/Prefabs/New";
        public const string PagePath = Folder + "/Upgrades Page.prefab";
        private const string PlayerUIPath = Folder + "/New Player UI 2.prefab";

        private static readonly Color Card = new(0.055f, 0.073f, 0.125f, 0.96f);
        private static readonly Color Well = new(0.025f, 0.035f, 0.07f, 0.9f);
        private static readonly Color Cyan = new(0.24f, 0.91f, 0.98f, 1f);
        private static readonly Color Magenta = new(0.86f, 0.25f, 0.89f, 1f);
        private static readonly Color Paper = new(0.9f, 0.94f, 1f, 1f);
        private static readonly Color Muted = new(0.51f, 0.61f, 0.74f, 1f);
        private static readonly Color Line = new(0.18f, 0.27f, 0.38f, 0.75f);
        private static readonly Color Dim = new(0f, 0f, 0f, 0.5f);

        [MenuItem("Tools/JunkLite/UI/Build Upgrades Page Prefab")]
        public static void Build()
        {
            GameObject page = AssetDatabase.LoadAssetAtPath<GameObject>(PagePath);
            if (page == null)
            {
                GameObject root = BuildPageObject();
                page = PrefabUtility.SaveAsPrefabAsset(root, PagePath);
                Object.DestroyImmediate(root);
            }
            WirePlayerUI(page);
            AssetDatabase.SaveAssets();
            Debug.Log("[UpgradesUISetup] Upgrades Page is ready and wired into New Player UI 2 (InventoryUI.upgradesScreen).");
        }

        [MenuItem("Tools/JunkLite/UI/Rebuild Upgrades Page Prefab")]
        public static void Rebuild()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PagePath) != null &&
                !EditorUtility.DisplayDialog("Rebuild Upgrades Page", "Replace the Upgrades Page prefab with a freshly built one? Inspector edits to it are lost.", "Rebuild", "Cancel"))
                return;
            AssetDatabase.DeleteAsset(PagePath);
            Build();
        }

        /// <summary>Builds the page hierarchy in memory. Tests use this directly; Build saves it as the prefab.</summary>
        public static GameObject BuildPageObject()
        {
            // Sized for the inventory frame's page slot (same rect InventoryUI uses for its pages).
            var root = new GameObject("Upgrades Page", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(40f, -170f);
            rect.sizeDelta = new Vector2(1360f, 568f);

            // No page title: the inventory frame's title names the tab. A full-width points bar takes the top row.
            Image pointsCard = Rect("Points Card", root.transform, Card, Vector2.zero, new Vector2(1360f, 64f));
            Border(pointsCard.rectTransform, Line);
            Rect("Points Accent", pointsCard.transform, Magenta, Vector2.zero, new Vector2(3f, 64f));
            Text("Points Label", pointsCard.transform, "UPGRADE POINTS AVAILABLE", 18f, Magenta, false, new Vector2(20f, -9f), new Vector2(420f, 26f));
            Text("Points Hint", pointsCard.transform, "ONE PER LEVEL  •  BOSSES DROP MORE", 13f, Muted, false, new Vector2(20f, -38f), new Vector2(600f, 20f));
            TMP_Text points = Text("Points Value", pointsCard.transform, "0", 48f, Paper, true, new Vector2(1100f, 0f), new Vector2(240f, 64f));
            points.alignment = TextAlignmentOptions.Right;
            // A big glyph is taller than its box: never let Ellipsis swallow the number.
            points.overflowMode = TextOverflowModes.Overflow;

            // Five branch columns under the points bar.
            var columns = new RectTransform[5];
            string[] names = { "BLADES", "VITALITY", "JUMP", "DASH", "MODS" };
            Color[] accents = { Cyan, Magenta, Cyan, Magenta, Cyan };
            UpgradeNodeView template = null;
            for (int i = 0; i < 5; i++)
            {
                Image column = Rect(names[i] + " Column", root.transform, Card, new Vector2(i * 202f, -74f), new Vector2(192f, 494f));
                Border(column.rectTransform, Line);
                Rect("Accent", column.transform, accents[i], Vector2.zero, new Vector2(3f, 494f));
                Text("Branch Label", column.transform, names[i], 13f, accents[i], false, new Vector2(14f, -10f), new Vector2(160f, 20f));
                var nodes = new GameObject("Nodes", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
                nodes.SetParent(column.transform, false);
                nodes.anchorMin = nodes.anchorMax = nodes.pivot = new Vector2(0f, 1f);
                nodes.anchoredPosition = new Vector2(8f, -40f);
                nodes.sizeDelta = new Vector2(176f, 446f);
                var layout = nodes.GetComponent<VerticalLayoutGroup>();
                layout.spacing = 12f;
                layout.childAlignment = TextAnchor.UpperCenter;
                layout.childControlWidth = false;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
                columns[i] = nodes;
                if (i == 0) template = BuildNodeTemplate(nodes);
            }

            // Details card.
            UpgradeDetailsView details = BuildDetails(root.transform);

            TMP_Text empty = Text("Empty Label", root.transform, "NO UPGRADE TREE  —  RUN TOOLS > JUNKLITE > PROGRESSION > INSTALL UPGRADE TREE", 14f, Muted, false,
                new Vector2(0f, -300f), new Vector2(1000f, 30f));
            empty.alignment = TextAlignmentOptions.Center;
            empty.gameObject.SetActive(false);

            var page = root.AddComponent<UpgradesPageView>();
            var so = new SerializedObject(page);
            so.FindProperty("nodeTemplate").objectReferenceValue = template;
            SerializedProperty columnsProperty = so.FindProperty("branchColumns");
            columnsProperty.arraySize = columns.Length;
            for (int i = 0; i < columns.Length; i++) columnsProperty.GetArrayElementAtIndex(i).objectReferenceValue = columns[i];
            so.FindProperty("pointsText").objectReferenceValue = points;
            so.FindProperty("details").objectReferenceValue = details;
            so.FindProperty("emptyLabel").objectReferenceValue = empty;
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        public const string IconFolder = "Assets/Game/New UI/Icons/Upgrades/";

        private static Sprite LoadIcon(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(IconFolder + name + ".png");

        /// <summary>
        /// One node card: a hexagon (fill + state-coloured frame) with the upgrade's icon inside and the
        /// cost under it. The name is not on the card; the details card shows it. Each state object holds
        /// its own frame colour so code only toggles objects.
        /// </summary>
        private static UpgradeNodeView BuildNodeTemplate(Transform parent)
        {
            Sprite hexFill = LoadIcon("hex-fill");
            Sprite hexFrame = LoadIcon("hex-frame");
            Sprite hexFrameBold = LoadIcon("hex-frame-bold");
            const float hex = 80f;

            var node = new GameObject("Upgrade Node", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)node.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(120f, 100f);
            // Transparent raycast target so the whole card is clickable; the hexagon is drawn by the children.
            var background = node.GetComponent<Image>();
            background.color = Color.clear;

            Image link = Rect("Link", node.transform, Line, new Vector2(59f, 10f), new Vector2(2f, 10f));
            Image fill = HexImage("Hex Fill", node.transform, hexFill, Well, hex);
            Image icon = Rect("Icon", node.transform, Color.white, new Vector2(60f - 22f, -(hex / 2f - 22f)), new Vector2(44f, 44f));
            icon.preserveAspect = true;
            icon.enabled = false;
            // Fallback when a node has no icon yet: its name inside the hexagon.
            TMP_Text nodeTitle = Text("Title", node.transform, "NAME", 9f, Paper, false, new Vector2(60f - 30f, -(hex / 2f - 16f)), new Vector2(60f, 32f));
            nodeTitle.fontStyle = FontStyles.Bold;
            nodeTitle.alignment = TextAlignmentOptions.Center;
            nodeTitle.textWrappingMode = TextWrappingModes.Normal;
            TMP_Text cost = Text("Cost", node.transform, "1 PT", 11f, Muted, false, new Vector2(10f, -(hex + 2f)), new Vector2(100f, 16f));
            cost.alignment = TextAlignmentOptions.Center;
            cost.fontStyle = FontStyles.Bold;

            GameObject locked = HexState("Locked", node.transform, hexFill, hexFrame, Dim, Line, hex);
            GameObject unaffordable = HexState("Unaffordable", node.transform, hexFill, hexFrame, new Color(0f, 0f, 0f, 0.3f), Muted, hex);
            GameObject available = HexState("Available", node.transform, null, hexFrame, Color.clear, Magenta, hex);
            GameObject ownedState = HexState("Owned", node.transform, hexFill, hexFrame, new Color(Cyan.r, Cyan.g, Cyan.b, 0.12f), Cyan, hex);
            // Selection: a slightly larger pale frame around the hexagon.
            var selection = new GameObject("Selection", typeof(RectTransform));
            var selectionRect = (RectTransform)selection.transform;
            selectionRect.SetParent(node.transform, false);
            selectionRect.anchorMin = selectionRect.anchorMax = selectionRect.pivot = new Vector2(0f, 1f);
            selectionRect.anchoredPosition = new Vector2(60f - (hex + 10f) / 2f, 5f);
            selectionRect.sizeDelta = new Vector2(hex + 10f, hex + 10f);
            var selectionImage = selection.AddComponent<Image>();
            selectionImage.sprite = hexFrameBold;
            selectionImage.color = Paper;
            selectionImage.preserveAspect = true;
            selectionImage.raycastTarget = false;
            selection.SetActive(false);

            var button = node.GetComponent<Button>();
            button.targetGraphic = fill;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.3f, 1.4f, 1f);
            colors.selectedColor = new Color(1.25f, 1.3f, 1.4f, 1f);
            colors.pressedColor = new Color(0.8f, 0.85f, 0.95f, 1f);
            button.colors = colors;

            var view = node.AddComponent<UpgradeNodeView>();
            var so = new SerializedObject(view);
            so.FindProperty("title").objectReferenceValue = nodeTitle;
            so.FindProperty("cost").objectReferenceValue = cost;
            so.FindProperty("icon").objectReferenceValue = icon;
            so.FindProperty("button").objectReferenceValue = button;
            so.FindProperty("lockedState").objectReferenceValue = locked;
            so.FindProperty("unaffordableState").objectReferenceValue = unaffordable;
            so.FindProperty("availableState").objectReferenceValue = available;
            so.FindProperty("ownedState").objectReferenceValue = ownedState;
            so.FindProperty("link").objectReferenceValue = link.gameObject;
            so.FindProperty("selection").objectReferenceValue = selection;
            so.ApplyModifiedPropertiesWithoutUndo();
            node.SetActive(false);
            return view;
        }

        /// <summary>A hexagon image centred horizontally at the top of a 120-wide card.</summary>
        private static Image HexImage(string name, Transform parent, Sprite sprite, Color color, float size)
        {
            Image image = Rect(name, parent, color, new Vector2(60f - size / 2f, 0f), new Vector2(size, size));
            image.sprite = sprite;
            image.preserveAspect = true;
            return image;
        }

        /// <summary>A state object: an optional tint over the hexagon plus the frame in the state's colour.</summary>
        private static GameObject HexState(string name, Transform parent, Sprite tintSprite, Sprite frameSprite, Color tint, Color frame, float size)
        {
            var state = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)state.transform;
            rect.SetParent(parent, false);
            Stretch(rect);
            if (tint.a > 0f && tintSprite != null) HexImage("Tint", state.transform, tintSprite, tint, size);
            HexImage("Frame", state.transform, frameSprite, frame, size);
            state.SetActive(false);
            return state;
        }

        private static UpgradeDetailsView BuildDetails(Transform parent)
        {
            Image card = Rect("Details", parent, Card, new Vector2(1020f, -74f), new Vector2(340f, 494f));
            Border(card.rectTransform, Line);
            Rect("Accent", card.transform, Magenta, Vector2.zero, new Vector2(3f, 494f));
            Text("Details Label", card.transform, "UPGRADE DETAILS", 14f, Magenta, false, new Vector2(16f, -12f), new Vector2(220f, 24f));

            var content = new GameObject("Content", typeof(RectTransform));
            var contentRect = (RectTransform)content.transform;
            contentRect.SetParent(card.transform, false);
            Stretch(contentRect);

            Image iconWell = Rect("Icon Well", content.transform, Well, new Vector2(20f, -46f), new Vector2(72f, 72f));
            Border(iconWell.rectTransform, new Color(Cyan.r, Cyan.g, Cyan.b, 0.25f));
            Image icon = Rect("Icon", iconWell.transform, Color.white, new Vector2(8f, -8f), new Vector2(56f, 56f));
            icon.preserveAspect = true;
            icon.enabled = false;
            TMP_Text title = Text("Title", content.transform, "NODE NAME", 22f, Paper, true, new Vector2(104f, -46f), new Vector2(220f, 44f));
            title.textWrappingMode = TextWrappingModes.Normal;
            TMP_Text meta = Text("Meta", content.transform, "BRANCH  •  TIER 1", 12f, Muted, false, new Vector2(104f, -94f), new Vector2(220f, 20f));
            TMP_Text description = Text("Description", content.transform, "", 14f, Muted, false, new Vector2(20f, -132f), new Vector2(300f, 84f));
            description.textWrappingMode = TextWrappingModes.Normal;
            Rect("Rule", content.transform, Line, new Vector2(20f, -224f), new Vector2(300f, 1f));
            TMP_Text effects = Text("Effects", content.transform, "", 13f, Paper, false, new Vector2(20f, -236f), new Vector2(300f, 80f));
            effects.textWrappingMode = TextWrappingModes.Normal;
            effects.fontStyle = FontStyles.Bold;
            TMP_Text status = Text("Status", content.transform, "", 12f, Cyan, false, new Vector2(20f, -376f), new Vector2(300f, 20f));
            status.fontStyle = FontStyles.Bold;

            var buy = new GameObject("Buy Button", typeof(RectTransform), typeof(Image), typeof(Button));
            var buyRect = (RectTransform)buy.transform;
            buyRect.SetParent(content.transform, false);
            buyRect.anchorMin = buyRect.anchorMax = buyRect.pivot = new Vector2(0f, 1f);
            buyRect.anchoredPosition = new Vector2(20f, -410f);
            buyRect.sizeDelta = new Vector2(300f, 48f);
            var buyImage = buy.GetComponent<Image>();
            buyImage.color = new Color(0.08f, 0.21f, 0.29f, 1f);
            Border(buyRect, Cyan);
            TMP_Text buyLabel = Text("Label", buy.transform, "BUY  —  1 POINT", 14f, Paper, false, Vector2.zero, new Vector2(300f, 48f));
            buyLabel.alignment = TextAlignmentOptions.Center;
            buyLabel.fontStyle = FontStyles.Bold;
            var buyButton = buy.GetComponent<Button>();
            buyButton.targetGraphic = buyImage;
            ColorBlock colors = buyButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            buyButton.colors = colors;

            TMP_Text empty = Text("Empty", card.transform, "SELECT AN UPGRADE", 14f, Muted, false, new Vector2(20f, -230f), new Vector2(300f, 32f));
            empty.alignment = TextAlignmentOptions.Center;
            empty.fontStyle = FontStyles.Bold;

            var view = card.gameObject.AddComponent<UpgradeDetailsView>();
            var so = new SerializedObject(view);
            so.FindProperty("title").objectReferenceValue = title;
            so.FindProperty("meta").objectReferenceValue = meta;
            so.FindProperty("description").objectReferenceValue = description;
            so.FindProperty("effects").objectReferenceValue = effects;
            so.FindProperty("status").objectReferenceValue = status;
            so.FindProperty("icon").objectReferenceValue = icon;
            so.FindProperty("buyButton").objectReferenceValue = buyButton;
            so.FindProperty("buyLabel").objectReferenceValue = buyLabel;
            so.FindProperty("emptyState").objectReferenceValue = empty.gameObject;
            so.FindProperty("content").objectReferenceValue = content;
            so.ApplyModifiedPropertiesWithoutUndo();
            content.SetActive(false);
            return view;
        }

        private static void WirePlayerUI(GameObject pagePrefab)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerUIPath) == null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerUIPath);
            try
            {
                var inventory = root.GetComponentInChildren<InventoryUI>(true);
                if (inventory != null)
                {
                    var so = new SerializedObject(inventory);
                    SerializedProperty screen = so.FindProperty("upgradesScreen");
                    var current = screen.objectReferenceValue as GameObject;
                    if (current == null || current.GetComponent<UpgradesPageView>() == null)
                    {
                        // The prefab asset is instantiated by InventoryUI at runtime, like the Missions page.
                        screen.objectReferenceValue = pagePrefab;
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

        // ---- authoring helpers (top-left anchored, like the inventory cards)

        private static Image Rect(string name, Transform parent, Color color, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text Text(string name, Transform parent, string value, float size, Color color, bool title, Vector2 position, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
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

        private static void Border(RectTransform parent, Color color)
        {
            Edge("Top Border", parent, color, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, 1f));
            Edge("Bottom Border", parent, color, Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 1f));
            Edge("Left Border", parent, color, Vector2.zero, new Vector2(0f, 1f), new Vector2(1f, 0f));
            Edge("Right Border", parent, color, new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0f));
        }

        private static void Edge(string name, RectTransform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
