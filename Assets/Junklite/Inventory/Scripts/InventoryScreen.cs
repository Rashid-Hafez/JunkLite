using System.Collections;
using System.Collections.Generic;
using Junklite.Shared;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Junklite.Inventory
{
    public enum ModFilter
    {
        All,
        Active,
        Passive,
    }

    /// <summary>
    /// Draws a <see cref="ModInventory"/> as the inventory screen and lets the player rearrange it.
    /// Mouse: click a mod then a slot, drag between storage and slots, double-click to equip.
    /// Keyboard: arrows to move, Enter to equip, Z X C V to equip into that slot, Delete to unequip,
    /// Esc to drop a picked-up mod.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class InventoryScreen : MonoBehaviour, IBackHandler
    {
        const float CalloutSeconds = 2.6f;
        const float DragThreshold = 8f;
        const int Columns = 5;
        const float CellStep = 108f;
        const float SlotBorderAlpha = 0.65f;

        const string HiddenClass = "inventory--hidden";
        const string DimmedClass = "inventory--dimmed";

        static readonly string[] SlotClasses = { "socket--z", "socket--x", "socket--c", "socket--v", "socket--passive1", "socket--passive2" };

        static readonly Color[] RarityColors =
        {
            new Color32(0xC9, 0xD1, 0xE6, 0xFF),
            new Color32(0x86, 0xE6, 0x8C, 0xFF),
            new Color32(0x6F, 0xD0, 0xFF, 0xFF),
            new Color32(0xFF, 0x5F, 0x74, 0xFF),
        };

        static readonly Color Pop = new Color32(0xE8, 0x5C, 0x19, 0xFF);

        [SerializeField] ModInventory inventory;

        [Tooltip("The callout asks the player to attach this mod until it's in a slot. Leave empty for no prompt.")]
        [SerializeField] ModDefinition tutorialMod;

        [Header("Missions tab")]
        [SerializeField] List<InfoEntry> missions = new List<InfoEntry>();

        [Tooltip("Fires with the tab's name when the player switches tabs.")]
        [SerializeField] UnityEvent<string> onTabChanged = new UnityEvent<string>();

        class SocketView
        {
            public ModSlot Slot;
            public VisualElement Root;
            public VisualElement Image;
            public VisualElement Ring;
            public Label Name;
            public Label Meta;
        }

        class TileView
        {
            public VisualElement Root;
            public VisualElement Image;
            public VisualElement Bar;
            public VisualElement NewDot;
            public Label Cooldown;
            public ModDefinition Mod;
        }

        VisualElement _screen;
        VisualElement _boundScreen;
        VisualElement _grid;
        VisualElement _tabLine;
        VisualElement _filterGlide;
        VisualElement _callout;
        Label _calloutMessage;
        VisualElement _casStats;
        VisualElement _detail;
        VisualElement _reticle;
        VisualElement _page;
        VisualElement _upgrades;
        readonly List<VisualElement> _modViews = new List<VisualElement>();
        readonly Dictionary<InventoryTab, Button> _tabButtons = new Dictionary<InventoryTab, Button>();
        Label _storageCount;
        Label _healthValue;
        Label _armorValue;
        Label _statsHealth;
        Label _statsArmor;
        VisualElement _healthFill;
        Button _activeTab;
        readonly Dictionary<ModFilter, Button> _filterButtons = new Dictionary<ModFilter, Button>();
        readonly List<SocketView> _sockets = new List<SocketView>();
        readonly List<TileView> _tiles = new List<TileView>();
        readonly VisualElement[] _corners = new VisualElement[4];
        readonly Vector2[] _cornerPositions = new Vector2[4];
        readonly Vector2[] _cornerTargets = new Vector2[4];

        readonly HashSet<ModDefinition> _seen = new HashSet<ModDefinition>();
        ModDefinition _picked;
        ModDefinition _currentMod;
        ModSlot? _currentSlot;
        VisualElement _lastFocused;
        bool _tutorialDone;
        bool _dimmed;
        bool _reticleShown;
        int _backHandledFrame = -1;
        Coroutine _calloutReset;

        // a press on a tile or slot that might turn into a drag
        ModDefinition _pressMod;
        ModSlot? _pressSlot;
        VisualElement _pressElement;
        Vector2 _pressPosition;
        bool _dragging;
        VisualElement _dragGhost;

        bool Ignoring => !isActiveAndEnabled || _dimmed;

        public ModInventory Inventory => inventory;

        /// <summary>JunkLite: raised every time a tab is shown, so game code can draw the Upgrades page.</summary>
        public event System.Action<InventoryTab> TabShown;

        /// <summary>JunkLite: empty container for the Upgrades tab (null if the UXML has none).</summary>
        public VisualElement UpgradesRoot => _upgrades;
        public ModFilter Filter { get; private set; }
        public InventoryTab Tab { get; private set; }
        public ModDefinition PickedMod => _picked;
        public ModDefinition HighlightedMod => _currentSlot.HasValue ? inventory.GetEquipped(_currentSlot.Value) : _currentMod;
        public ModSlot? HighlightedSlot => _currentSlot;
        public string CalloutText => _calloutMessage?.text;
        public bool IsCasStatsOpen => _casStats != null && _casStats.ClassListContains("cas-stats--open");

        /// <summary>Set by the pause menu: pushes the screen back and ignores input while it's open.</summary>
        public bool Dimmed
        {
            get => _dimmed;
            set
            {
                if (_screen == null || _dimmed == value) return;
                _dimmed = value;
                _screen.EnableInClassList(DimmedClass, value);
                if (value)
                {
                    _lastFocused = _screen.focusController?.focusedElement as VisualElement;
                    ShowReticle(false);
                }
                else
                {
                    (_lastFocused ?? FirstTile())?.Focus();
                    ShowReticle(CurrentElement() != null);
                }
            }
        }

        void OnEnable()
        {
            // UIDocument rebuilds its tree and re-enables this component whenever the UXML is reimported,
            // so everything is looked up again here rather than once in Awake.
            var root = GetComponent<UIDocument>().rootVisualElement;
            _screen = root?.Q("inventory");
            if (inventory == null) inventory = FindAnyObjectByType<ModInventory>();
            if (_screen == null || inventory == null)
            {
                Debug.LogError(_screen == null
                    ? "Inventory UI is missing. Check that the UI Document's Source Asset is Inventory.uxml."
                    : "No ModInventory to show. Assign one to the Inventory Screen.", this);
                _screen = null;
                return;
            }

            root.StretchToParentSize();
            _dimmed = false;
            inventory.Changed += Refresh;
            _picked = null;

            if (_screen != _boundScreen)
            {
                _boundScreen = _screen;
                Bind();
            }

            Refresh();
            ShowBaseCallout();
            _screen.AddToClassList(HiddenClass);
            StartCoroutine(Enter());
            StartCoroutine(PulseTargets());
        }

        void Bind()
        {
            _sockets.Clear();
            _tiles.Clear();
            _filterButtons.Clear();

            _grid = _screen.Q("grid");
            _tabLine = _screen.Q("tab-line");
            _filterGlide = _screen.Q("filter-glide");
            _callout = _screen.Q("callout");
            _calloutMessage = _screen.Q<Label>("callout-message");
            _casStats = _screen.Q("cas-stats");
            _detail = _screen.Q("detail");
            _reticle = _screen.Q("reticle");
            _storageCount = _screen.Q<Label>("storage-count");
            _healthValue = _screen.Q<Label>("health-value");
            _armorValue = _screen.Q<Label>("armor-value");
            _statsHealth = _screen.Q<Label>("stats-health");
            _statsArmor = _screen.Q<Label>("stats-armor");
            _healthFill = _screen.Q("health-fill");
            _grid.Clear();
            for (int i = 0; i < 4; i++) _corners[i] = _reticle[i];
            _page = _screen.Q("page");
            _upgrades = _screen.Q("upgrades");
            _modViews.Clear();
            foreach (var name in new[] { "sockets", "storage-head", "filter", "grid", "detail", "callout-row", "legend" })
                _modViews.Add(_screen.Q(name));

            BindTabs();
            BindFilter();
            BindCas();
            BuildSockets();

            _screen.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _screen.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _screen.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _screen.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            _screen.RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit, TrickleDown.TrickleDown);
            _screen.RegisterCallback<NavigationCancelEvent>(_ => HandleBack());
        }

        void OnDisable()
        {
            StopAllCoroutines();
            if (inventory != null) inventory.Changed -= Refresh;
        }

        void Update()
        {
            if (_screen == null || !_reticleShown || Tab != InventoryTab.Inventory) return;
            var target = CurrentElement();
            if (target == null) return;

            var rect = target.ChangeCoordinatesTo(_screen, new Rect(Vector2.zero, target.layout.size));
            const float pad = 6f, size = 14f;
            _cornerTargets[0] = new Vector2(rect.xMin - pad, rect.yMin - pad);
            _cornerTargets[1] = new Vector2(rect.xMax + pad - size, rect.yMin - pad);
            _cornerTargets[2] = new Vector2(rect.xMin - pad, rect.yMax + pad - size);
            _cornerTargets[3] = new Vector2(rect.xMax + pad - size, rect.yMax + pad - size);

            for (int i = 0; i < 4; i++)
            {
                // Each corner follows at a slightly different speed, so the reticle stretches as it travels.
                float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime * (16f - i * 1.6f));
                _cornerPositions[i] = Vector2.Lerp(_cornerPositions[i], _cornerTargets[i], t);
                _corners[i].style.left = _cornerPositions[i].x;
                _corners[i].style.top = _cornerPositions[i].y;
            }
        }

        /// <summary>Health in the header and on Cas's stats card. Shows a dash until this is called.</summary>
        public void SetHealth(float current, float max)
        {
            if (_screen == null) return;
            float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            _healthValue.text = Mathf.RoundToInt(current).ToString();
            _statsHealth.text = $"{Mathf.RoundToInt(current)} / {Mathf.RoundToInt(max)}";
            _healthFill.style.scale = new Scale(new Vector2(fraction, 1f));
        }

        public void SetArmor(int armor)
        {
            if (_screen == null) return;
            _armorValue.text = armor.ToString();
            _statsArmor.text = armor.ToString();
        }

        public void ClearVitals()
        {
            if (_screen == null) return;
            _healthValue.text = _armorValue.text = _statsHealth.text = _statsArmor.text = "—";
            _healthFill.style.scale = new Scale(Vector2.one);
        }

        public void SetFilter(ModFilter filter)
        {
            Filter = filter;
            foreach (var pair in _filterButtons) pair.Value.EnableInClassList("filter__button--active", pair.Key == filter);
            MoveGlide(_filterGlide, _filterButtons[filter]);
            RenderGrid();
            MarkCurrent();
        }

        /// <summary>Esc or the gamepad's back button: drops a picked-up mod. Returns false if there was nothing to drop.</summary>
        public bool HandleBack()
        {
            if (_backHandledFrame == Time.frameCount) return true;
            if (_screen != null && !Ignoring && Tab != InventoryTab.Inventory)
            {
                _backHandledFrame = Time.frameCount;
                ShowTab(InventoryTab.Inventory);
                return true;
            }
            if (_screen == null || Ignoring || _picked == null) return false;

            _backHandledFrame = Time.frameCount;
            _picked = null;
            MarkPicked();
            ShowBaseCallout();
            return true;
        }

        IEnumerator Enter()
        {
            // The fade needs one laid-out frame to start from, and the glides need button sizes.
            yield return null;
            yield return null;
            _screen.RemoveFromClassList(HiddenClass);
            MoveGlide(_tabLine, _activeTab, true);
            MoveGlide(_filterGlide, _filterButtons[Filter], true);

            if (Tab == InventoryTab.Inventory)
            {
                var start = TileFor(tutorialMod) ?? FirstTile();
                start?.Focus();
            }
        }

        // ---------- drawing ----------

        void Refresh()
        {
            if (_screen == null) return;
            RenderSockets();
            RenderGrid();
            RenderDetail(false);
            MarkCurrent();
            MarkTargets();
        }

        void BuildSockets()
        {
            var parent = _screen.Q("sockets");
            parent.Clear();
            for (int i = 0; i < ModInventory.SlotCount; i++)
            {
                var slot = (ModSlot)i;
                var view = new SocketView { Slot = slot, Root = new VisualElement { name = "slot-" + SlotClasses[i].Substring(8), focusable = true } };
                view.Root.AddToClassList("socket");
                view.Root.AddToClassList(SlotClasses[i]);
                view.Root.AddToClassList(ModInventory.SlotType(slot) == ModType.Active ? "socket--active" : "socket--passive");

                var art = Child(view.Root, "socket__art");
                view.Image = Child(art, "socket__image");
                var plus = Child(view.Root, "socket__plus");
                Child(plus, "socket__plus-h");
                Child(plus, "socket__plus-v");
                Child(view.Root, "socket__dash");
                view.Ring = Child(view.Root, "socket__ring");

                var label = Child(view.Root, "socket__label");
                view.Name = new Label { pickingMode = PickingMode.Ignore };
                view.Name.AddToClassList("socket__name");
                view.Meta = new Label { pickingMode = PickingMode.Ignore };
                view.Meta.AddToClassList("socket__meta");
                label.Add(view.Name);
                label.Add(view.Meta);

                view.Root.RegisterCallback<PointerEnterEvent>(_ => SetCurrent(null, slot));
                view.Root.RegisterCallback<FocusInEvent>(_ => SetCurrent(null, slot));
                view.Root.RegisterCallback<PointerDownEvent>(evt => OnPress(evt, inventory.GetEquipped(slot), slot, view.Root));
                view.Root.RegisterCallback<ClickEvent>(_ => OnSocketClicked(slot));

                parent.Add(view.Root);
                _sockets.Add(view);
            }
        }

        void RenderSockets()
        {
            foreach (var view in _sockets)
            {
                var mod = inventory.GetEquipped(view.Slot);
                bool passive = ModInventory.SlotType(view.Slot) == ModType.Passive;
                bool locked = inventory.IsLocked(view.Slot);
                view.Root.EnableInClassList("socket--empty", mod == null);
                view.Root.EnableInClassList("socket--locked", locked);
                view.Image.style.backgroundImage = mod != null ? mod.Icon : null;
                view.Root.style.borderTopColor = view.Root.style.borderRightColor =
                    view.Root.style.borderBottomColor = view.Root.style.borderLeftColor = mod != null
                        ? new StyleColor(WithAlpha(RarityColor(mod), SlotBorderAlpha))
                        : new StyleColor(StyleKeyword.Null);

                view.Name.text = mod != null ? mod.DisplayName.ToUpperInvariant() : locked ? "LOCKED" : "EMPTY";
                view.Meta.text = locked ? "UPGRADE TO UNLOCK" : passive ? "PASSIVE" : mod != null && mod.HasCooldown ? $"{FormatSeconds(mod.Cooldown).ToUpperInvariant()} COOLDOWN" : "ACTIVE";
            }
        }

        void RenderGrid()
        {
            while (_tiles.Count < inventory.Capacity) _tiles.Add(BuildTile(_tiles.Count));
            while (_tiles.Count > inventory.Capacity)
            {
                _tiles[_tiles.Count - 1].Root.RemoveFromHierarchy();
                _tiles.RemoveAt(_tiles.Count - 1);
            }
            _grid.style.height = Mathf.CeilToInt(inventory.Capacity / (float)Columns) * CellStep - (CellStep - 96f);

            var visible = new List<ModDefinition>();
            foreach (var mod in inventory.Stored)
            {
                if (Filter == ModFilter.All || (Filter == ModFilter.Active) == (mod.Type == ModType.Active)) visible.Add(mod);
            }

            for (int i = 0; i < _tiles.Count; i++)
            {
                var tile = _tiles[i];
                tile.Mod = i < visible.Count ? visible[i] : null;
                tile.Root.EnableInClassList("tile--empty", tile.Mod == null);
                tile.Root.focusable = tile.Mod != null;
                if (tile.Mod == null) continue;

                tile.Image.style.backgroundImage = tile.Mod.Icon;
                tile.Bar.style.backgroundColor = RarityColor(tile.Mod);
                tile.Cooldown.text = tile.Mod.HasCooldown ? FormatSeconds(tile.Mod.Cooldown) : "PSV";
                bool isNew = tile.Mod.MarkAsNew && !_seen.Contains(tile.Mod);
                tile.NewDot.style.display = tile.Mod.MarkAsNew ? DisplayStyle.Flex : DisplayStyle.None;
                tile.NewDot.EnableInClassList("tile__new--seen", !isNew);
            }

            _storageCount.text = $"{inventory.StoredCount} / {inventory.Capacity}";
            MarkPicked();
        }

        TileView BuildTile(int index)
        {
            var tile = new TileView { Root = new VisualElement { name = "tile-" + index } };
            tile.Root.AddToClassList("tile");
            tile.Root.style.left = index % Columns * CellStep;
            tile.Root.style.top = index / Columns * CellStep;

            tile.Image = Child(tile.Root, "tile__image");
            tile.Bar = Child(tile.Root, "tile__bar");
            tile.Cooldown = new Label { pickingMode = PickingMode.Ignore };
            tile.Cooldown.AddToClassList("tile__cooldown");
            tile.Root.Add(tile.Cooldown);
            tile.NewDot = Child(tile.Root, "tile__new");
            Child(tile.Root, "tile__picked");
            Child(tile.Root, "tile__dash");

            tile.Root.RegisterCallback<PointerEnterEvent>(_ => { if (tile.Mod != null) SetCurrent(tile.Mod, null); });
            tile.Root.RegisterCallback<FocusInEvent>(_ => { if (tile.Mod != null) SetCurrent(tile.Mod, null); });
            tile.Root.RegisterCallback<PointerDownEvent>(evt => { if (tile.Mod != null) OnPress(evt, tile.Mod, null, tile.Root); });
            tile.Root.RegisterCallback<ClickEvent>(evt => OnTileClicked(tile, evt.clickCount));

            _grid.Add(tile.Root);
            return tile;
        }

        void RenderDetail(bool animate)
        {
            var mod = HighlightedMod;
            var modView = _detail.Q("detail-mod");
            var emptyView = _detail.Q("detail-empty");
            modView.EnableInClassList("hidden", mod == null);
            emptyView.EnableInClassList("hidden", mod != null);

            if (mod == null)
            {
                bool slot = _currentSlot.HasValue;
                bool passive = slot && ModInventory.SlotType(_currentSlot.Value) == ModType.Passive;
                _detail.Q<Label>("detail-empty-title").text = !slot ? "SELECT A MOD" : passive ? "EMPTY PASSIVE SLOT" : "EMPTY ACTIVE SLOT";
                _detail.Q<Label>("detail-empty-hint").text = !slot ? "HOVER OR USE THE ARROW KEYS" : passive ? "PASSIVE MODS GO HERE" : "ACTIVE MODS GO HERE";
            }
            else
            {
                var rarity = RarityColor(mod);
                _detail.Q("detail-image").style.backgroundImage = mod.Icon;
                _detail.Q("detail-tint").style.unityBackgroundImageTintColor = WithAlpha(rarity, 0.129f);
                _detail.Q("detail-dot").style.backgroundColor = rarity;
                _detail.Q<Label>("detail-rarity").text = $"{mod.Rarity.ToString().ToUpperInvariant()} · TIER {mod.Tier}";
                _detail.Q<Label>("detail-name").text = mod.DisplayName.ToUpperInvariant();
                _detail.Q<Label>("detail-type").text = $"{mod.TypeLabel.ToUpperInvariant()} MOD";
                _detail.Q<Label>("detail-cooldown").text = mod.HasCooldown ? FormatSeconds(mod.Cooldown) : "None";
                _detail.Q<Label>("detail-damage").text = string.IsNullOrEmpty(mod.Damage) ? "None" : mod.Damage;
                _detail.Q<Label>("detail-damage-label").text = mod.DamageOnlyWhenActive ? "DAMAGE · ACTIVE" : "DAMAGE";
                _detail.Q<Label>("detail-tier").text = mod.Tier.ToString();
                _detail.Q<Label>("detail-description").text = mod.Description;

                bool linked = inventory.IsEquipped(mod);
                _detail.Q("detail-status").EnableInClassList("detail__status--linked", linked);
                _detail.Q<Label>("detail-status-label").text = linked ? "LINKED TO A MOD SLOT" : "IN STORAGE";
            }

            if (!animate) return;
            // a short fade so the swap reads as one change
            _detail.AddToClassList("detail--swap");
            _detail.schedule.Execute(() => _detail.RemoveFromClassList("detail--swap")).ExecuteLater(16);
        }

        void SetCurrent(ModDefinition mod, ModSlot? slot)
        {
            if (Ignoring || _dragging) return;
            if (_currentMod == mod && _currentSlot == slot) return;

            _currentMod = mod;
            _currentSlot = slot;
            MarkCurrent();
            RenderDetail(true);

            if (mod != null && mod.MarkAsNew && _seen.Add(mod))
            {
                TileFor(mod)?.Q(className: "tile__new").AddToClassList("tile__new--seen");
            }
        }

        void MarkCurrent()
        {
            var current = CurrentElement();
            foreach (var view in _sockets) view.Root.EnableInClassList("socket--current", view.Root == current);
            foreach (var tile in _tiles) tile.Root.EnableInClassList("tile--current", tile.Root == current);
            ShowReticle(current != null && !_dimmed && Tab == InventoryTab.Inventory);
        }

        void MarkPicked()
        {
            foreach (var tile in _tiles) tile.Root.EnableInClassList("tile--picked", tile.Mod != null && tile.Mod == _picked);
            MarkTargets();
        }

        void MarkTargets()
        {
            foreach (var view in _sockets)
            {
                bool target = _picked != null && _picked.Type == ModInventory.SlotType(view.Slot) && !inventory.IsLocked(view.Slot);
                view.Root.EnableInClassList("socket--target", target);
                if (target) SetBorder(view.Root, Pop);
            }
            if (_picked == null) RenderSockets();
        }

        VisualElement CurrentElement()
        {
            if (_currentSlot.HasValue) return _sockets.Count > 0 ? _sockets[(int)_currentSlot.Value].Root : null;
            return TileFor(_currentMod);
        }

        VisualElement TileFor(ModDefinition mod)
        {
            if (mod == null) return null;
            foreach (var tile in _tiles) if (tile.Mod == mod) return tile.Root;
            return null;
        }

        VisualElement FirstTile() => _tiles.Count > 0 && _tiles[0].Mod != null ? _tiles[0].Root : _sockets.Count > 0 ? _sockets[0].Root : null;

        void ShowReticle(bool show)
        {
            if (_reticle == null) return;
            if (show && !_reticleShown)
            {
                // appear in place, then follow
                var target = CurrentElement();
                if (target != null)
                {
                    var rect = target.ChangeCoordinatesTo(_screen, new Rect(Vector2.zero, target.layout.size));
                    _cornerPositions[0] = new Vector2(rect.xMin - 6f, rect.yMin - 6f);
                    _cornerPositions[1] = new Vector2(rect.xMax - 8f, rect.yMin - 6f);
                    _cornerPositions[2] = new Vector2(rect.xMin - 6f, rect.yMax - 8f);
                    _cornerPositions[3] = new Vector2(rect.xMax - 8f, rect.yMax - 8f);
                }
            }
            _reticleShown = show;
            _reticle.EnableInClassList("reticle--on", show);
        }

        // ---------- actions ----------

        void OnTileClicked(TileView tile, int clickCount)
        {
            if (Ignoring || tile.Mod == null) return;
            if (clickCount >= 2)
            {
                QuickEquip(tile.Mod);
                return;
            }

            _picked = _picked == tile.Mod ? null : tile.Mod;
            MarkPicked();
            if (_picked != null)
                ShowCallout(false, $"{_picked.DisplayName.ToUpperInvariant()} PICKED UP. CHOOSE A{(_picked.Type == ModType.Active ? "N ACTIVE" : " PASSIVE")} SLOT.", false);
            else
                ShowBaseCallout();
        }

        void OnSocketClicked(ModSlot slot)
        {
            if (Ignoring) return;
            if (_picked != null) TryEquip(_picked, slot);
            else SetCurrent(null, slot);
        }

        void QuickEquip(ModDefinition mod)
        {
            ModSlot? target = null;
            for (int i = 0; i < ModInventory.SlotCount; i++)
            {
                var slot = (ModSlot)i;
                if (ModInventory.SlotType(slot) != mod.Type || inventory.IsLocked(slot)) continue;
                target ??= slot;
                if (inventory.GetEquipped(slot) == null)
                {
                    target = slot;
                    break;
                }
            }
            if (target.HasValue) TryEquip(mod, target.Value);
            else ShowCallout(true, $"NO UNLOCKED {(mod.Type == ModType.Active ? "ACTIVE" : "PASSIVE")} SLOT");
        }

        void TryEquip(ModDefinition mod, ModSlot slot)
        {
            var previous = inventory.GetEquipped(slot);
            bool fromSlot = inventory.IsEquipped(mod);
            var result = inventory.Equip(mod, slot);

            if (result == EquipResult.WrongSlotType)
            {
                Shake(_sockets[(int)slot].Root);
                string kind = mod.Type == ModType.Active ? "ACTIVE" : "PASSIVE";
                ShowCallout(true, $"{mod.DisplayName.ToUpperInvariant()} IS {kind}. IT NEEDS A{(kind == "ACTIVE" ? "N" : "")} {kind} SLOT.");
                return;
            }
            if (result == EquipResult.SlotLocked)
            {
                Shake(_sockets[(int)slot].Root);
                ShowCallout(true, "SLOT LOCKED. UNLOCK IT IN UPGRADES");
                return;
            }
            if (result != EquipResult.Equipped) return;

            _picked = null;
            _currentMod = null;
            _currentSlot = slot;
            Refresh();
            Flash(_sockets[(int)slot].Image);
            _sockets[(int)slot].Root.Focus();

            if (mod == tutorialMod && !_tutorialDone)
            {
                _tutorialDone = true;
                ShowBaseCallout();
            }
            else
            {
                string stored = previous != null && previous != mod && !fromSlot ? $". {previous.DisplayName.ToUpperInvariant()} STORED" : "";
                ShowCallout(false, $"{mod.DisplayName.ToUpperInvariant()} ATTACHED{stored}");
            }
        }

        void TryUnequip(ModSlot slot)
        {
            var mod = inventory.GetEquipped(slot);
            if (mod == null) return;
            if (!inventory.Unequip(slot))
            {
                ShowCallout(true, inventory.IsLocked(slot) ? "SLOT LOCKED" : "STORAGE IS FULL");
                return;
            }
            _currentMod = null;
            _currentSlot = slot;
            Refresh();
            ShowCallout(false, $"{mod.DisplayName.ToUpperInvariant()} RETURNED TO STORAGE");
        }

        // ---------- callout ----------

        void ShowBaseCallout()
        {
            if (tutorialMod == null)
            {
                ShowCallout(false, "PICK A MOD, THEN A SLOT", false);
                return;
            }
            if (inventory.IsEquipped(tutorialMod)) _tutorialDone = true;
            string name = tutorialMod.DisplayName.ToUpperInvariant();
            ShowCallout(false, _tutorialDone ? $"{name} ATTACHED" : $"ATTACH {name} TO A MOD SLOT", false);
        }

        void ShowCallout(bool warning, string message, bool temporary = true)
        {
            _callout.EnableInClassList("callout--warn", warning);
            _calloutMessage.text = message;
            _calloutMessage.AddToClassList("callout__message--swap");
            _calloutMessage.schedule.Execute(() => _calloutMessage.RemoveFromClassList("callout__message--swap")).ExecuteLater(16);

            if (_calloutReset != null) StopCoroutine(_calloutReset);
            _calloutReset = temporary ? StartCoroutine(ResetCallout()) : null;
        }

        IEnumerator ResetCallout()
        {
            yield return new WaitForSecondsRealtime(CalloutSeconds);
            _calloutReset = null;
            if (_picked == null) ShowBaseCallout();
        }

        // ---------- header, filter, Cas ----------

        void BindTabs()
        {
            _tabButtons.Clear();
            _tabButtons[InventoryTab.Inventory] = _screen.Q<Button>("tab-inventory");
            _tabButtons[InventoryTab.Missions] = _screen.Q<Button>("tab-missions");
            var upgradesTab = _screen.Q<Button>("tab-upgrades");
            if (upgradesTab != null && _upgrades != null) _tabButtons[InventoryTab.Upgrades] = upgradesTab;
            _activeTab = _tabButtons[InventoryTab.Inventory];
            Tab = InventoryTab.Inventory;

            foreach (var pair in _tabButtons)
            {
                var tab = pair.Key;
                var button = pair.Value;
                button.RegisterCallback<PointerEnterEvent>(_ => MoveGlide(_tabLine, button));
                button.RegisterCallback<PointerLeaveEvent>(_ => MoveGlide(_tabLine, _activeTab));
                button.clicked += () => { if (!Ignoring) ShowTab(tab); };
            }
        }

        /// <summary>Switches between the mods and the Missions text.</summary>
        public void ShowTab(InventoryTab tab)
        {
            if (_screen == null || !_tabButtons.ContainsKey(tab)) return;
            bool changed = Tab != tab;
            Tab = tab;
            _activeTab = _tabButtons[tab];
            foreach (var pair in _tabButtons) pair.Value.EnableInClassList("tab--active", pair.Key == tab);
            MoveGlide(_tabLine, _activeTab);

            bool page = tab != InventoryTab.Inventory;
            foreach (var view in _modViews) view?.EnableInClassList("hidden", page);
            _page.EnableInClassList("hidden", tab != InventoryTab.Missions);
            _upgrades?.EnableInClassList("hidden", tab != InventoryTab.Upgrades);
            _casStats.RemoveFromClassList("cas-stats--open");

            if (page)
            {
                _picked = null;
                MarkPicked();
                if (tab == InventoryTab.Missions) RenderPage();
                ShowReticle(false);
                TabShown?.Invoke(tab);
                // Focus the page's first button (Upgrades nodes) so keys and gamepad work at once.
                Focusable first = tab == InventoryTab.Upgrades ? _upgrades.Query(className: "upgrade-node").First() : null;
                (first ?? _activeTab).Focus();
            }
            else
            {
                MarkCurrent();
                (CurrentElement() ?? FirstTile())?.Focus();
            }
            if (changed) onTabChanged?.Invoke(_activeTab.text);
        }

        /// <summary>Replaces the cards on the Missions tab.</summary>
        public void SetMissions(IEnumerable<InfoEntry> entries)
        {
            missions.Clear();
            missions.AddRange(entries);
            if (Tab == InventoryTab.Missions) RenderPage();
        }

        void RenderPage()
        {
            _page.Clear();
            var entries = missions;
            if (entries.Count == 0)
            {
                _page.Add(TextLabel("page__empty", "NOTHING HERE YET"));
                return;
            }

            foreach (var entry in entries)
            {
                var card = new VisualElement { pickingMode = PickingMode.Ignore };
                card.AddToClassList("page-card");
                if (!string.IsNullOrEmpty(entry.kicker)) card.Add(TextLabel("page-card__kicker", entry.kicker.ToUpperInvariant()));
                card.Add(TextLabel("page-card__title", (entry.title ?? "").ToUpperInvariant()));
                if (!string.IsNullOrEmpty(entry.body)) card.Add(TextLabel("page-card__body", entry.body));
                foreach (var point in entry.points) card.Add(TextLabel("page-card__point", "· " + point.ToUpperInvariant()));
                _page.Add(card);
            }
        }

        static Label TextLabel(string className, string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }

        void BindFilter()
        {
            _filterButtons[ModFilter.All] = _screen.Q<Button>("filter-all");
            _filterButtons[ModFilter.Active] = _screen.Q<Button>("filter-active");
            _filterButtons[ModFilter.Passive] = _screen.Q<Button>("filter-passive");
            foreach (var pair in _filterButtons)
            {
                var filter = pair.Key;
                pair.Value.clicked += () => { if (!Ignoring) SetFilter(filter); };
            }
            Filter = ModFilter.All;
        }

        void BindCas()
        {
            var hit = _screen.Q("cas-hit");
            hit.RegisterCallback<PointerEnterEvent>(_ => _casStats.AddToClassList("cas-stats--open"));
            hit.RegisterCallback<PointerLeaveEvent>(_ => _casStats.EnableInClassList("cas-stats--open", hit.focusController?.focusedElement == hit));
            hit.RegisterCallback<FocusInEvent>(_ => _casStats.AddToClassList("cas-stats--open"));
            hit.RegisterCallback<FocusOutEvent>(_ => _casStats.RemoveFromClassList("cas-stats--open"));
        }

        static void MoveGlide(VisualElement glide, VisualElement target, bool instant = false)
        {
            if (glide == null || target == null) return;
            if (instant) glide.style.transitionDuration = new List<TimeValue> { new TimeValue(0) };
            glide.style.left = target.layout.x;
            glide.style.width = target.layout.width;
            if (instant) glide.schedule.Execute(() => glide.style.transitionDuration = StyleKeyword.Null).ExecuteLater(16);
        }

        // ---------- input ----------

        void OnKeyDown(KeyDownEvent evt)
        {
            if (Ignoring || Tab != InventoryTab.Inventory) return;
            switch (evt.keyCode)
            {
                case KeyCode.Z: EquipToKey(ModSlot.Z); break;
                case KeyCode.X: EquipToKey(ModSlot.X); break;
                case KeyCode.C: EquipToKey(ModSlot.C); break;
                case KeyCode.V: EquipToKey(ModSlot.V); break;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    if (_currentSlot.HasValue) TryUnequip(_currentSlot.Value);
                    break;
                default: return;
            }
            evt.StopPropagation();
        }

        // An unlabelled shortcut: puts the highlighted mod straight into that slot.
        void EquipToKey(ModSlot slot)
        {
            var mod = _picked ?? _currentMod ?? (_currentSlot.HasValue ? inventory.GetEquipped(_currentSlot.Value) : null);
            if (mod != null && _currentSlot != slot) TryEquip(mod, slot);
        }

        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (Ignoring) return;
            var from = evt.target as VisualElement;
            if (from == null || (!from.ClassListContains("tile") && !from.ClassListContains("socket"))) return;

            Vector2 dir = evt.direction switch
            {
                NavigationMoveEvent.Direction.Left => Vector2.left,
                NavigationMoveEvent.Direction.Right => Vector2.right,
                NavigationMoveEvent.Direction.Up => Vector2.down,
                NavigationMoveEvent.Direction.Down => Vector2.up,
                _ => Vector2.zero,
            };
            if (dir == Vector2.zero) return;

            evt.StopImmediatePropagation();
            _screen.focusController?.IgnoreEvent(evt);
            Nearest(from, dir)?.Focus();
        }

        // The nearest slot or mod in the pressed direction, favouring ones in line with the current one.
        VisualElement Nearest(VisualElement from, Vector2 dir)
        {
            var origin = from.worldBound.center;
            VisualElement best = null;
            float bestScore = float.MaxValue;

            void Consider(VisualElement candidate)
            {
                if (candidate == from || !candidate.focusable) return;
                var delta = candidate.worldBound.center - origin;
                float along = Vector2.Dot(delta, dir);
                if (along <= 2f) return;
                float across = Mathf.Abs(dir.x != 0f ? delta.y : delta.x);
                float score = along + across * 2.2f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            foreach (var view in _sockets) Consider(view.Root);
            foreach (var tile in _tiles) if (tile.Mod != null) Consider(tile.Root);
            return best;
        }

        void OnNavigationSubmit(NavigationSubmitEvent evt)
        {
            if (Ignoring) return;
            var target = evt.target as VisualElement;
            var tile = _tiles.Find(t => t.Root == target);
            if (tile?.Mod != null)
            {
                QuickEquip(tile.Mod);
                evt.StopPropagation();
                return;
            }

            var socket = _sockets.Find(s => s.Root == target);
            if (socket != null && _picked != null)
            {
                TryEquip(_picked, socket.Slot);
                evt.StopPropagation();
            }
        }

        void OnPress(PointerDownEvent evt, ModDefinition mod, ModSlot? slot, VisualElement element)
        {
            if (Ignoring || evt.button != 0 || mod == null) return;
            _pressMod = mod;
            _pressSlot = slot;
            _pressElement = element;
            _pressPosition = evt.position;
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (_pressMod == null) return;
            if (!_dragging)
            {
                if (((Vector2)evt.position - _pressPosition).sqrMagnitude < DragThreshold * DragThreshold) return;
                BeginDrag(evt.pointerId);
            }

            var local = _screen.WorldToLocal(evt.position);
            _dragGhost.style.left = local.x - 36f;
            _dragGhost.style.top = local.y - 36f;
        }

        void BeginDrag(int pointerId)
        {
            _dragging = true;
            _picked = _pressMod;
            _pressElement.AddToClassList(_pressSlot.HasValue ? "socket--dragging" : "tile--dragging");
            _grid.EnableInClassList("grid--drop", _pressSlot.HasValue);
            MarkPicked();

            _dragGhost = new VisualElement { pickingMode = PickingMode.Ignore };
            _dragGhost.AddToClassList("drag-ghost");
            _dragGhost.style.backgroundImage = _pressMod.Icon;
            _screen.Add(_dragGhost);
            _screen.CapturePointer(pointerId);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_dragging)
            {
                _pressMod = null;
                return;
            }

            _screen.ReleasePointer(evt.pointerId);
            var mod = _pressMod;
            var from = _pressSlot;
            EndDrag();

            var hit = _screen.panel.Pick(evt.position);
            var socket = _sockets.Find(s => hit != null && (s.Root == hit || s.Root.Contains(hit)));
            bool overGrid = hit != null && (hit == _grid || _grid.Contains(hit));

            if (socket != null && socket.Slot != from) TryEquip(mod, socket.Slot);
            else if (socket == null && overGrid && from.HasValue) TryUnequip(from.Value);
            else
            {
                _picked = null;
                MarkPicked();
            }
        }

        void EndDrag()
        {
            _dragging = false;
            _pressElement?.RemoveFromClassList("socket--dragging");
            _pressElement?.RemoveFromClassList("tile--dragging");
            _grid.RemoveFromClassList("grid--drop");
            _dragGhost?.RemoveFromHierarchy();
            _dragGhost = null;
            _pressMod = null;
            _pressSlot = null;
            _pressElement = null;
        }

        // ---------- small effects ----------

        IEnumerator PulseTargets()
        {
            var wait = new WaitForSecondsRealtime(1.2f);
            while (true)
            {
                foreach (var view in _sockets) view.Ring.RemoveFromClassList("socket__ring--out");
                yield return null;
                yield return null;
                foreach (var view in _sockets) view.Ring.AddToClassList("socket__ring--out");
                yield return wait;
            }
        }

        void Shake(VisualElement element) => StartCoroutine(ShakeRoutine(element));

        static IEnumerator ShakeRoutine(VisualElement element)
        {
            var wait = new WaitForSecondsRealtime(0.06f);
            foreach (float x in new[] { -6f, 5f, -2f, 0f })
            {
                element.style.translate = new Translate(x, 0);
                yield return wait;
            }
            element.style.translate = StyleKeyword.Null;
        }

        // equip feedback: the art lands in the slot a little large and settles
        static void Flash(VisualElement image)
        {
            image.style.transitionDuration = new List<TimeValue> { new TimeValue(0) };
            image.style.scale = new Scale(new Vector2(1.16f, 1.16f));
            image.schedule.Execute(() =>
            {
                image.style.transitionDuration = StyleKeyword.Null;
                image.style.scale = StyleKeyword.Null;
            }).ExecuteLater(16);
        }

        // ---------- helpers ----------

        static VisualElement Child(VisualElement parent, string className)
        {
            var child = new VisualElement { pickingMode = PickingMode.Ignore };
            child.AddToClassList(className);
            parent.Add(child);
            return child;
        }

        static Color RarityColor(ModDefinition mod) => RarityColors[Mathf.Clamp(mod.Tier, 0, RarityColors.Length - 1)];

        static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        static void SetBorder(VisualElement element, Color color)
        {
            element.style.borderTopColor = element.style.borderRightColor =
                element.style.borderBottomColor = element.style.borderLeftColor = color;
        }

        static string FormatSeconds(float seconds) =>
            Mathf.Approximately(seconds, Mathf.Round(seconds)) ? $"{seconds:0}s" : $"{seconds:0.#}s";
    }
}
