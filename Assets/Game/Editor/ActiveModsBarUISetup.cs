using junklite;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace junklite.Editor
{
    /// <summary>
    /// One-time authoring tool: builds the Active Mods Bar prefab (the gameplay HUD's mod slots) and wires
    /// it into New Player UI 2 in place of the legacy code-built bar. Afterwards the prefab is a normal
    /// asset: edit sprites, colours, sizes and fonts in the Inspector. Rerunning only creates the prefab
    /// when it is missing; it never overwrites an edited one.
    ///
    /// Layout (1920x1080 reference): a low 560x112 strip at the bottom centre, drawn with the lead's
    /// action-bar gradient and pink accent line, holding up to four 84x84 slots. Each slot is the lead's
    /// MOD HOLDER frame with the mod icon filling it, a thin durability bar under the icon and a small
    /// key chip at the top-right corner. No card, no borders around the whole bar: the slots are the bar.
    /// </summary>
    public static class ActiveModsBarUISetup
    {
        private const string Folder = "Assets/Game/New UI/Prefabs/New";
        public const string BarPath = Folder + "/Active Mods Bar.prefab";
        private const string PlayerUIPath = Folder + "/New Player UI 2.prefab";

        private const string GradientSprite = "Assets/Game/New UI/Layout/Action Bar/Action Bar Gradient.png";
        private const string AccentSprite = "Assets/Game/New UI/Layout/Action Bar/HUD PINK LINE.png";
        private const string HolderSprite = "Assets/Game/New UI/Mods/MOD HOLDER.png";

        public const float BarWidth = 560f;
        public const float BarHeight = 112f;
        public const float BottomMargin = 22f;
        public const float SlotSize = 84f;
        public const float SlotSpacing = 14f;
        public const float IconSize = 58f;

        private static readonly Color Backdrop = new(0.03f, 0.045f, 0.09f, 0.92f);
        private static readonly Color Well = new(0.025f, 0.035f, 0.07f, 0.95f);
        private static readonly Color Cyan = new(0.24f, 0.91f, 0.98f, 1f);
        private static readonly Color Magenta = new(0.86f, 0.25f, 0.89f, 1f);
        private static readonly Color Paper = new(0.9f, 0.94f, 1f, 1f);
        private static readonly Color Muted = new(0.51f, 0.61f, 0.74f, 1f);
        private static readonly Color Line = new(0.18f, 0.27f, 0.38f, 0.75f);
        private static readonly Color FrameTint = new(0.82f, 0.88f, 1f, 0.95f);

        [MenuItem("Tools/JunkLite/UI/Build Active Mods Bar Prefab")]
        public static void Build()
        {
            GameObject bar = AssetDatabase.LoadAssetAtPath<GameObject>(BarPath);
            if (bar == null)
            {
                GameObject root = BuildBarObject();
                bar = PrefabUtility.SaveAsPrefabAsset(root, BarPath);
                Object.DestroyImmediate(root);
            }
            WirePlayerUI(bar);
            AssetDatabase.SaveAssets();
            Debug.Log("[ActiveModsBarUISetup] Active Mods Bar is ready and wired into New Player UI 2 (PlayerUI.modCombatUI). The legacy 'Mod Combat UI' object was disabled, not deleted.");
        }

        [MenuItem("Tools/JunkLite/UI/Rebuild Active Mods Bar Prefab")]
        public static void Rebuild()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BarPath) != null &&
                !EditorUtility.DisplayDialog("Rebuild Active Mods Bar", "Replace the Active Mods Bar prefab with a freshly built one? Inspector edits to it are lost.", "Rebuild", "Cancel"))
                return;
            AssetDatabase.DeleteAsset(BarPath);
            Build();
        }

        /// <summary>Builds the bar hierarchy in memory. Tests use this directly; Build saves it as the prefab.</summary>
        public static GameObject BuildBarObject()
        {
            Sprite gradient = AssetDatabase.LoadAssetAtPath<Sprite>(GradientSprite);
            Sprite accent = AssetDatabase.LoadAssetAtPath<Sprite>(AccentSprite);
            Sprite holder = AssetDatabase.LoadAssetAtPath<Sprite>(HolderSprite);

            var root = new GameObject("Active Mods Bar", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, BottomMargin);
            rect.sizeDelta = new Vector2(BarWidth, BarHeight);

            // Panel: the one object ModCombatUI shows and hides.
            var panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(root.transform, false);
            Stretch(panelRect);

            // Backdrop: the lead's gradient, tinted navy, with a hairline on top and the pink accent along the bottom.
            Image backdrop = Image("Backdrop", panel.transform, Backdrop, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            backdrop.sprite = gradient;
            backdrop.type = UnityEngine.UI.Image.Type.Simple;
            Image("Top Hairline", panel.transform, Line, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -0.5f), new Vector2(0f, 1f));
            Image bottomAccent = Image("Bottom Accent", panel.transform, Magenta, Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 1.5f), new Vector2(-40f, 3f));
            bottomAccent.sprite = accent;
            bottomAccent.type = UnityEngine.UI.Image.Type.Simple;

            TMP_Text label = Text("Label", panel.transform, "MODS", 11f, Muted, new Vector2(18f, -10f), new Vector2(80f, 16f));
            label.fontStyle = FontStyles.Bold;
            label.characterSpacing = 6f;

            // Slot row: centred, one copy of the template per unlocked slot.
            var slots = new GameObject("Slots", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var slotsRect = (RectTransform)slots.transform;
            slotsRect.SetParent(panel.transform, false);
            Stretch(slotsRect);
            slotsRect.offsetMin = new Vector2(20f, (BarHeight - SlotSize) * 0.5f);
            slotsRect.offsetMax = new Vector2(-20f, -(BarHeight - SlotSize) * 0.5f);
            var layout = slots.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = SlotSpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            CombatModSlotUI template = BuildSlotTemplate(slotsRect, holder);

            var bar = root.AddComponent<ModCombatUI>();
            var so = new SerializedObject(bar);
            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("activeModParent").objectReferenceValue = slotsRect;
            so.FindProperty("slotTemplate").objectReferenceValue = template;
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        /// <summary>
        /// One slot: the holder frame, the icon inside it, a durability bar under the icon, a key chip on
        /// the top-right corner and an inactive flash used when a bought upgrade adds the slot. The radial
        /// cooldown is still drawn over the icon by ModCooldownUI at runtime.
        /// </summary>
        private static CombatModSlotUI BuildSlotTemplate(Transform parent, Sprite holder)
        {
            var slot = new GameObject("Slot Template", typeof(RectTransform), typeof(LayoutElement));
            var rect = (RectTransform)slot.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(SlotSize, SlotSize);
            var element = slot.GetComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = SlotSize;
            element.minHeight = element.preferredHeight = SlotSize;

            Image frame = Image("Frame", slot.transform, FrameTint, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            frame.sprite = holder;
            frame.preserveAspect = true;

            Image icon = Image("Icon", slot.transform, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 2f), new Vector2(IconSize, IconSize));
            icon.preserveAspect = true;

            Image track = Image("Durability Track", slot.transform, Line, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 9f), new Vector2(IconSize - 4f, 3f));
            Image fill = Image("Fill", track.transform, Cyan, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            fill.type = UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;

            // Key chip: sits on the frame's top-right corner so it never covers the icon.
            Image chip = Image("Key Chip", slot.transform, Well, Vector2.one, Vector2.one, new Vector2(4f, 4f), new Vector2(34f, 18f));
            chip.rectTransform.pivot = new Vector2(1f, 1f);
            Border(chip.rectTransform, new Color(Cyan.r, Cyan.g, Cyan.b, 0.6f));
            TMP_Text key = Text("Key", chip.transform, "1", 11f, Cyan, Vector2.zero, Vector2.zero);
            Stretch(key.rectTransform);
            key.alignment = TextAlignmentOptions.Center;
            key.fontStyle = FontStyles.Bold;
            key.enableAutoSizing = true;
            key.fontSizeMin = 7f;
            key.fontSizeMax = 11f;

            Image flash = Image("Unlock Flash", slot.transform, new Color(Magenta.r, Magenta.g, Magenta.b, 0f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            flash.gameObject.SetActive(false);

            var view = slot.AddComponent<CombatModSlotUI>();
            var so = new SerializedObject(view);
            so.FindProperty("iconImage").objectReferenceValue = icon;
            so.FindProperty("durabilityFill").objectReferenceValue = fill;
            so.FindProperty("inputHintText").objectReferenceValue = key;
            so.FindProperty("unlockFlash").objectReferenceValue = flash;
            so.ApplyModifiedPropertiesWithoutUndo();
            slot.SetActive(false);
            return view;
        }

        /// <summary>
        /// Nests the bar in New Player UI 2, points PlayerUI.modCombatUI at it and disables the legacy
        /// 'Mod Combat UI' object (whose authored children the old code destroyed at runtime anyway).
        /// </summary>
        private static void WirePlayerUI(GameObject barPrefab)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerUIPath) == null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerUIPath);
            try
            {
                ModCombatUI authored = null;
                foreach (ModCombatUI candidate in root.GetComponentsInChildren<ModCombatUI>(true))
                {
                    if (PrefabUtility.GetCorrespondingObjectFromSource(candidate.gameObject) != null &&
                        AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(candidate.gameObject)) == BarPath)
                    {
                        authored = candidate;
                        continue;
                    }

                    // The legacy bar: keep the object for the lead to delete, but it must not bind.
                    if (candidate.gameObject.activeSelf)
                    {
                        candidate.gameObject.SetActive(false);
                        candidate.gameObject.name = "Mod Combat UI (legacy, unused)";
                    }
                }

                if (authored == null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(barPrefab, root.transform);
                    instance.transform.SetAsLastSibling();
                    authored = instance.GetComponent<ModCombatUI>();
                }

                var playerUI = root.GetComponentInChildren<PlayerUI>(true);
                if (playerUI != null)
                {
                    var so = new SerializedObject(playerUI);
                    so.FindProperty("modCombatUI").objectReferenceValue = authored;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(root, PlayerUIPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---- authoring helpers

        private static Image Image(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text Text(string name, Transform parent, string value, float size, Color color, Vector2 position, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = box;
            var text = go.GetComponent<TextMeshProUGUI>();
            UIFonts.ApplyHudBody(text);
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
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
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
