using System;
using System.Collections.Generic;
using Junklite.Inventory;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Junklite.HUD
{
    public enum ModPressResult
    {
        Used,
        Offline,
        Empty,
        Cooling,
    }

    /// <summary>
    /// The mod bar (HUD/mods, bare in the chosen design): the four active slots, Z X C V on keyboard or hold L2 +
    /// square, triangle, circle, cross on a pad, and the two passive slots. The loadout comes from the scene's
    /// <see cref="ModInventory"/>, so equipping in the inventory screen changes the bar.
    /// Using a mod drops its button and flashes it cyan, then the slot cools down (a shade drains and the seconds
    /// count, in game time) and flashes cyan when it's ready again. A press that does nothing (mod state off,
    /// empty slot, still cooling) flashes the button red and rattles the slot.
    /// Q (L3) turns the mod state off and on; off, the slots drop to half strength.
    /// With Game Driven on, the game owns the mods instead: a bridge calls the Show methods (what's in each slot,
    /// cooldowns, uses, ready, refused presses, unlocked and broken slots), and the bar reads no keys and runs no
    /// cooldowns of its own.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HudModBar : MonoBehaviour
    {
        const int ActiveSlots = 4;
        const int PassiveSlots = 2;

        [Tooltip("Where the loadout comes from. Found in the scene if left empty.")]
        [SerializeField] ModInventory inventory;

        [Tooltip("Used only when there's no ModInventory: Z, X, C, V, Passive 1, Passive 2.")]
        [SerializeField] ModDefinition[] fallbackLoadout = new ModDefinition[ActiveSlots + PassiveSlots];

        [Tooltip("The mod state (L3 in the GDD). Off, the slots dim and presses are refused.")]
        [SerializeField] bool modStateOn = true;

        [Tooltip("Buttons only: no panel, strip or words, as in the chosen design.")]
        [SerializeField] bool bare = true;

        [Tooltip("The game owns the mods: a bridge calls the Show methods, and the bar doesn't read keys, run cooldowns or refuse presses itself.")]
        [SerializeField] bool gameDriven;

        [Tooltip("Cooldowns run this many times faster. For reviewing; 1 in the game.")]
        [Min(0.1f)] [SerializeField] float cooldownSpeed = 1f;

        [Tooltip("Passes the slot used: 0 to 3 for Z X C V.")]
        [SerializeField] UnityEvent<int> onModUsed = new UnityEvent<int>();

        [SerializeField] UnityEvent<bool> onModStateChanged = new UnityEvent<bool>();

        VisualElement _root;
        VisualElement _bound;
        VisualElement _head;
        VisualElement _divider;
        VisualElement _readout;
        Label _stateChip;
        Label _toggleLabel;
        Label _readoutName;
        Label _readoutDetail;
        KeyPrompt _toggleKey;
        KeyPrompt _readoutKey;
        readonly List<Slot> _active = new List<Slot>();
        readonly List<Slot> _passive = new List<Slot>();
        HudFx.Timer _readoutTimer;
        ModInventory _listening;
        readonly ModDefinition[] _gameLoadout = new ModDefinition[ActiveSlots + PassiveSlots];

        /// <summary>A slot was used. The game plays the mod's effect here.</summary>
        public event Action<ModSlot, ModDefinition> ModUsed;

        /// <summary>A press did nothing, and why.</summary>
        public event Action<ModSlot, ModPressResult> PressRefused;

        public event Action<bool> ModStateChanged;

        public bool ModStateOn => modStateOn;
        public VisualElement Root => _root;

        public bool Bare
        {
            get => bare;
            set
            {
                bare = value;
                _root?.EnableInClassList("mods--bare", value);
            }
        }

        public float CooldownSpeed
        {
            get => cooldownSpeed;
            set => cooldownSpeed = Mathf.Max(0.1f, value);
        }

        public ModInventory Inventory => inventory;

        /// <summary>On: the game drives the bar through the Show methods. Switching it restarts the bar.</summary>
        public bool GameDriven
        {
            get => gameDriven;
            set
            {
                if (gameDriven == value) return;
                bool live = isActiveAndEnabled;
                if (live) OnDisable();
                gameDriven = value;
                if (live) OnEnable();
            }
        }

        sealed class Slot
        {
            public int Index;
            public bool Passive;
            public VisualElement Element;
            public VisualElement Icon;
            public VisualElement Shade;
            public VisualElement Rarity;
            public Label Time;
            public KeyPrompt Key;
            public VisualElement Wear;
            public int Uses = -1;
            public int UsesLeft = -1;
            public HudFx.Timer Break;
            public ModDefinition Mod;
            public float Remaining;
            public float Total;
            public bool Cooling => Remaining > 0f;
            public HudFx.Timer Ready;
            public HudFx.Timer Fire;
            public HudFx.Timer Refuse;
            public HudFx.Timer Paint;
        }

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>().rootVisualElement;
            _root = doc?.Q("mods");
            if (_root == null)
            {
                Debug.LogError("Mod bar UI is missing. Check that the UI Document's Source Asset is Hud.uxml.", this);
                return;
            }

            if (_root != _bound)
            {
                _bound = _root;
                Bind();
            }

            if (!gameDriven)
            {
                if (inventory == null) inventory = FindAnyObjectByType<ModInventory>();
                if (inventory != null)
                {
                    _listening = inventory;
                    inventory.Changed += OnInventoryChanged;
                }
                HudInput.Pressed += OnPressed;
            }

            HudInput.DeviceChanged += OnDeviceChanged;

            foreach (var slot in _active) ResetCooldown(slot);
            SyncLoadout(false);
            Bare = bare;
            OnDeviceChanged(HudInput.Device);
            SetModState(modStateOn, true);
        }

        void OnDisable()
        {
            if (_listening != null) _listening.Changed -= OnInventoryChanged;
            _listening = null;
            HudInput.Pressed -= OnPressed;
            HudInput.DeviceChanged -= OnDeviceChanged;
            _readoutTimer?.Cancel();
            _toggleKey?.Attention(false);
        }

        void Update()
        {
            if (_root == null || gameDriven) return;
            float dt = Time.deltaTime * cooldownSpeed;
            foreach (var slot in _active)
            {
                if (!slot.Cooling) continue;
                slot.Remaining = Mathf.Max(0f, slot.Remaining - dt);
                PaintCooldown(slot);
                if (slot.Remaining <= 0f) CameReady(slot);
            }
        }

        void Bind()
        {
            _head = _root.Q("mods-head");
            _readout = _root.Q("mods-readout");
            _stateChip = _root.Q<Label>("mods-state");
            _toggleLabel = _root.Q<Label>("mods-toggle-label");
            _readoutName = _root.Q<Label>("mods-readout-name");
            _readoutDetail = _root.Q<Label>("mods-readout-detail");
            _toggleKey = _root.Q<KeyPrompt>("mods-toggle-key");
            _readoutKey = _root.Q<KeyPrompt>("mods-readout-key");

            var row = _root.Q("mods-row");
            row.Clear();
            _active.Clear();
            _passive.Clear();
            for (int i = 0; i < ActiveSlots; i++) _active.Add(MakeSlot(row, i, false, i == ActiveSlots - 1));
            _divider = new VisualElement { pickingMode = PickingMode.Ignore };
            _divider.AddToClassList("mods-div");
            row.Add(_divider);
            for (int i = 0; i < PassiveSlots; i++) _passive.Add(MakeSlot(row, i, true, i == PassiveSlots - 1));

            _root.Query<Label>().ForEach(l => l.enableRichText = false);
        }

        static Slot MakeSlot(VisualElement row, int index, bool passive, bool end)
        {
            VisualElement Part(VisualElement parent, string className)
            {
                var e = new VisualElement { pickingMode = PickingMode.Ignore };
                e.AddToClassList(className);
                parent.Add(e);
                return e;
            }

            Label Text(VisualElement parent, string className, string text = "")
            {
                var l = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = false };
                l.AddToClassList(className);
                parent.Add(l);
                return l;
            }

            var slot = new Slot { Index = index, Passive = passive };
            var el = Part(row, "slot");
            el.AddToClassList(passive ? "slot--passive" : "slot--active");
            if (end) el.AddToClassList("slot--end");
            slot.Element = el;

            var frame = Part(el, "slot__frame");
            slot.Icon = Part(frame, "slot__icon");
            slot.Shade = Part(frame, "slot__shade");
            slot.Rarity = Part(frame, "slot__rarity");
            Part(frame, "slot__flash");
            slot.Time = Text(frame, "slot__time");
            Text(frame, "slot__empty", "EMPTY");
            foreach (var corner in new[] { "slot__corner--tl", "slot__corner--tr", "slot__corner--bl", "slot__corner--br" })
            {
                Part(frame, "slot__corner").AddToClassList(corner);
            }

            // the uses bar (ShowWear); hidden until the game says how many uses the mod has
            slot.Wear = Part(el, "slot__wear");
            el.AddToClassList("slot--no-wear");

            if (passive)
            {
                Text(el, "slot__tag", "PSV");
            }
            else
            {
                slot.Key = new KeyPrompt(HudInput.SlotAction(index), PromptSize.Small);
                slot.Key.AddToClassList("slot__key");
                el.Add(slot.Key);
            }

            // shown in place of the button (or the PSV tag) while the mod is broken; the game's own word for it
            Text(el, "slot__broken", "BROKEN");
            return slot;
        }

        // ---------- public calls ----------

        /// <summary>Uses the mod in a slot (0 to 3 for Z X C V), or refuses the press and says why.</summary>
        public ModPressResult Press(int index)
        {
            if (_root == null || index < 0 || index >= ActiveSlots) return ModPressResult.Empty;
            var slot = _active[index];
            slot.Key.Press();

            if (!modStateOn)
            {
                Refuse(slot);
                HudFx.Shake(_stateChip, 5f);
                HudFx.Shake(slot.Element, 4f);
                _toggleKey.Ping();
                Say("MOD STATE OFFLINE", "PRESS", true, HudAction.ModState, true);
                return Refused(slot, ModPressResult.Offline);
            }

            if (slot.Mod == null)
            {
                Refuse(slot);
                HudFx.Shake(slot.Element, 5f);
                Say("EMPTY SLOT", "NO MOD EQUIPPED", true);
                return Refused(slot, ModPressResult.Empty);
            }

            if (slot.Cooling)
            {
                Refuse(slot);
                HudFx.Shake(slot.Element, 4f);
                slot.Element.AddToClassList("slot--refuse");
                HudFx.After(0.32f, () => slot.Element.RemoveFromClassList("slot--refuse"));
                Say(Name(slot.Mod), $"{Seconds(slot.Remaining)}S", true);
                return Refused(slot, ModPressResult.Cooling);
            }

            var mod = slot.Mod;
            PlayFire(slot);

            if (mod.HasCooldown)
            {
                slot.Total = mod.Cooldown;
                slot.Remaining = mod.Cooldown;
                slot.Element.AddToClassList("slot--cooling");
                PaintCooldown(slot);
                Say(Name(mod), $"{Mathf.RoundToInt(mod.Cooldown)}S COOLDOWN");
            }
            else
            {
                Say(Name(mod), "ACTIVE");
            }

            ModUsed?.Invoke((ModSlot)index, mod);
            onModUsed?.Invoke(index);
            return ModPressResult.Used;
        }

        public ModPressResult Press(ModSlot slot) => Press((int)slot);

        public void ToggleModState()
        {
            _toggleKey?.Press();
            SetModState(!modStateOn);
        }

        /// <summary>Turns the mod state on or off. Off, the slots drop to half strength.</summary>
        public void SetModState(bool on, bool quiet = false)
        {
            bool changed = on != modStateOn;
            modStateOn = on;
            if (_root == null) return;

            _root.EnableInClassList("mods--online", on);
            // offline, the toggle is the thing to press: hot and pinging
            _toggleKey.Hot = !on;
            _toggleKey.Attention(!on);
            HudFx.SetText(_toggleLabel, on ? "TOGGLE" : "ACTIVATE");
            _stateChip.EnableInClassList("chip--action", on);
            _stateChip.EnableInClassList("chip--ghost", !on);

            if (quiet)
            {
                HudFx.SetText(_stateChip, on ? "ONLINE" : "OFFLINE");
            }
            else
            {
                HudFx.Scramble(_stateChip, 240, on ? "ONLINE" : "OFFLINE");
                var all = AllSlots();
                for (int i = 0; i < all.Count; i++)
                {
                    var el = all[i].Element;
                    el.style.transitionDelay = new List<TimeValue> { new TimeValue(i * 40, TimeUnit.Millisecond) };
                    HudFx.After(0.6f, () => el.style.transitionDelay = StyleKeyword.Null);
                }
                var rows = new List<VisualElement> { _head };
                foreach (var s in all) rows.Add(s.Element);
                HudFx.Tear(rows, new TearOptions { frames = 5, amp = 14, hot = 0f, cold = on ? 0.5f : 0f });
            }

            if (changed)
            {
                ModStateChanged?.Invoke(on);
                onModStateChanged?.Invoke(on);
            }
        }

        /// <summary>
        /// Puts a mod in a slot, with the equip glitch. With a ModInventory this goes through it (the mod must be
        /// one Cas owns; null unequips), so the inventory screen agrees.
        /// </summary>
        public void Equip(int index, ModDefinition mod)
        {
            if (index < 0 || index >= ActiveSlots) return;
            if (gameDriven)
            {
                ShowSlot(index, mod);
                return;
            }
            if (inventory != null)
            {
                if (mod == null) inventory.Unequip((ModSlot)index);
                else inventory.Equip(mod, (ModSlot)index);
                return;
            }

            fallbackLoadout[index] = mod;
            SyncLoadout(true);
        }

        /// <summary>Ends every running cooldown at once.</summary>
        public void FinishCooldowns()
        {
            foreach (var slot in _active)
            {
                if (!slot.Cooling) continue;
                slot.Remaining = 0f;
                PaintCooldown(slot);
                CameReady(slot);
            }
        }

        public bool IsCooling(int index) => index >= 0 && index < _active.Count && _active[index].Cooling;

        public float CooldownRemaining(int index) => index >= 0 && index < _active.Count ? _active[index].Remaining : 0f;

        public ModDefinition ModIn(int index)
        {
            if (index < 0) return null;
            if (index < ActiveSlots) return index < _active.Count ? _active[index].Mod : null;
            index -= ActiveSlots;
            return index < _passive.Count ? _passive[index].Mod : null;
        }

        public KeyPrompt KeyFor(int index) => index >= 0 && index < _active.Count ? _active[index].Key : null;

        // ---------- game-driven calls (Game Driven on) ----------

        /// <summary>What's in a slot: 0 to 3 are the active slots (Z X C V), 4 and 5 the passive ones. A change plays the equip glitch.</summary>
        public void ShowSlot(int index, ModDefinition mod, bool animate = true)
        {
            if (index < 0 || index >= _gameLoadout.Length || _gameLoadout[index] == mod) return;
            _gameLoadout[index] = mod;
            SyncLoadout(animate);
        }

        /// <summary>How many active and passive slots the player has unlocked. The others are hidden.</summary>
        public void ShowUnlocked(int active, int passive)
        {
            if (_root == null) return;
            for (int i = 0; i < _active.Count; i++)
            {
                _active[i].Element.EnableInClassList("slot--locked", i >= active);
                _active[i].Element.EnableInClassList("slot--end", i == Mathf.Min(active, ActiveSlots) - 1);
            }
            for (int i = 0; i < _passive.Count; i++)
            {
                _passive[i].Element.EnableInClassList("slot--locked", i >= passive);
                _passive[i].Element.EnableInClassList("slot--end", i == Mathf.Min(passive, PassiveSlots) - 1);
            }
            _divider.EnableInClassList("mods-div--hidden", passive <= 0 || active <= 0);
        }

        /// <summary>A slot's cooldown: the shade and the seconds. 0 clears it; <see cref="ShowReady"/> plays the flash.</summary>
        public void ShowCooldown(int index, float remaining, float total)
        {
            if (_root == null || index < 0 || index >= ActiveSlots) return;
            var slot = _active[index];
            if (remaining <= 0f)
            {
                if (slot.Element.ClassListContains("slot--cooling")) ResetCooldown(slot);
                return;
            }
            slot.Total = Mathf.Max(total, remaining);
            slot.Remaining = remaining;
            slot.Element.AddToClassList("slot--cooling");
            PaintCooldown(slot);
        }

        /// <summary>A mod was used: its button drops and lights, and the slot dips.</summary>
        public void ShowUsed(int index)
        {
            if (_root == null || index < 0 || index >= ActiveSlots) return;
            var slot = _active[index];
            slot.Key.Press();
            PlayFire(slot);
            if (slot.Mod != null) Say(Name(slot.Mod), "ACTIVE");
        }

        /// <summary>A slot's cooldown just finished: the flash, and its button pings.</summary>
        public void ShowReady(int index)
        {
            if (_root == null || index < 0 || index >= ActiveSlots) return;
            CameReady(_active[index]);
        }

        /// <summary>A press that did nothing: the button turns red and the slot rattles.</summary>
        public void ShowRefused(int index)
        {
            if (_root == null || index < 0 || index >= ActiveSlots) return;
            var slot = _active[index];
            slot.Key.Press();
            Refuse(slot);
            HudFx.Shake(slot.Element, 4f);
            // a cooling slot reddens its seconds; a broken one pops its BROKEN tag
            if (!slot.Cooling && !slot.Element.ClassListContains("slot--broken")) return;
            slot.Element.AddToClassList("slot--refuse");
            HudFx.After(0.32f, () => slot.Element.RemoveFromClassList("slot--refuse"));
        }

        /// <summary>
        /// A mod with no uses left: its art dims, its frame turns red and BROKEN shows in place of its button until it's
        /// taken out. <paramref name="animate"/>: it broke just now (not loaded or equipped broken), so a red flash and a
        /// jolt play and BROKEN pops in.
        /// </summary>
        public void ShowBroken(int index, bool broken, bool animate = false)
        {
            var slot = SlotAt(index);
            if (slot == null) return;
            bool was = slot.Element.ClassListContains("slot--broken");
            slot.Element.EnableInClassList("slot--broken", broken);
            if (!broken || was || !animate) return;

            slot.Element.AddToClassList("slot--shatter");
            slot.Break?.Cancel();
            slot.Break = HudFx.After(0.52f, () => slot.Element.RemoveFromClassList("slot--shatter"));
            HudFx.Shake(slot.Element, 7f);
            HudFx.Tear(new[] { slot.Element }, new TearOptions { frames = 5, amp = 18f, chance = 1f, hot = 0f, cold = 0f });
            if (slot.Mod != null) Say(Name(slot.Mod), "BROKEN", warn: true);
        }

        /// <summary>
        /// A slot's uses bar, right under it: one cell per use (the mod's durability), lit for the uses left, the last
        /// one red. The used cells stay as a faint trace, so the bar keeps its length. 0 uses hides it (a mod that
        /// doesn't wear). Over 20 uses, 20 cells share them out.
        /// </summary>
        public void ShowWear(int index, int usesLeft, int uses)
        {
            var slot = SlotAt(index);
            if (slot == null) return;
            uses = Mathf.Max(0, uses);
            usesLeft = Mathf.Clamp(usesLeft, 0, uses);
            if (slot.Uses == uses && slot.UsesLeft == usesLeft) return;
            slot.Uses = uses;
            slot.UsesLeft = usesLeft;

            int cells = Mathf.Min(uses, 20);
            int lit = uses <= 20 ? usesLeft : Mathf.CeilToInt(usesLeft * 20f / uses);
            if (slot.Wear.childCount != cells)
            {
                slot.Wear.Clear();
                for (int k = 0; k < cells; k++)
                {
                    var cell = new VisualElement { pickingMode = PickingMode.Ignore };
                    cell.AddToClassList("slot__use");
                    if (k == cells - 1) cell.AddToClassList("slot__use--end");
                    slot.Wear.Add(cell);
                }
            }
            for (int k = 0; k < cells; k++) slot.Wear[k].EnableInClassList("slot__use--on", k < lit);
            slot.Element.EnableInClassList("slot--no-wear", cells == 0);
            slot.Element.EnableInClassList("slot--worn", cells > 0 && usesLeft == 1);
        }

        // 0 to 3 are the active slots, 4 and 5 the passive ones
        Slot SlotAt(int index)
        {
            if (_root == null || index < 0) return null;
            if (index < ActiveSlots) return index < _active.Count ? _active[index] : null;
            return index - ActiveSlots < _passive.Count ? _passive[index - ActiveSlots] : null;
        }

        // ---------- internals ----------

        void OnPressed(HudAction action)
        {
            if (!isActiveAndEnabled) return;
            switch (action)
            {
                case HudAction.ModState: ToggleModState(); break;
                case HudAction.Slot0: Press(0); break;
                case HudAction.Slot1: Press(1); break;
                case HudAction.Slot2: Press(2); break;
                case HudAction.Slot3: Press(3); break;
            }
        }

        void PlayFire(Slot slot)
        {
            slot.Element.AddToClassList("slot--fire");
            slot.Key.Hot = true;
            slot.Fire?.Cancel();
            slot.Fire = HudFx.After(0.26f, () =>
            {
                slot.Element.RemoveFromClassList("slot--fire");
                slot.Key.Hot = false;
            });
            HudFx.RaiseActivity();
        }

        void OnDeviceChanged(HudDevice device) => _root?.EnableInClassList("mods--pad", device == HudDevice.Gamepad);

        void OnInventoryChanged() => SyncLoadout(true);

        ModDefinition LoadoutMod(int index)
        {
            if (gameDriven) return _gameLoadout[index];
            if (inventory != null) return inventory.GetEquipped((ModSlot)index);
            return fallbackLoadout != null && index < fallbackLoadout.Length ? fallbackLoadout[index] : null;
        }

        void SyncLoadout(bool animate)
        {
            if (_root == null) return;
            for (int i = 0; i < ActiveSlots; i++)
            {
                var mod = LoadoutMod(i);
                var slot = _active[i];
                if (!animate)
                {
                    slot.Mod = mod;
                    PaintSlot(slot);
                    continue;
                }
                if (mod == slot.Mod) continue;
                EquipAnimation(slot, mod);
            }

            for (int i = 0; i < PassiveSlots; i++)
            {
                var mod = LoadoutMod(ActiveSlots + i);
                var slot = _passive[i];
                if (mod == slot.Mod && animate) continue;
                slot.Mod = mod;
                if (animate)
                {
                    HudFx.Tear(new[] { slot.Element }, new TearOptions { frames = 5, amp = 16, chance = 1f, hot = 0.4f, cold = 0.3f });
                    slot.Paint?.Cancel();
                    slot.Paint = HudFx.After(0.14f, () => PaintSlot(slot));
                }
                else
                {
                    PaintSlot(slot);
                }
            }
        }

        void EquipAnimation(Slot slot, ModDefinition mod)
        {
            slot.Mod = mod;
            ResetCooldown(slot);
            slot.Element.RemoveFromClassList("slot--ready");
            HudFx.Tear(new[] { slot.Element }, new TearOptions { frames = 5, amp = 16, chance = 1f, hot = 0.4f, cold = 0.3f });
            slot.Paint?.Cancel();
            slot.Paint = HudFx.After(0.14f, () => PaintSlot(slot));
            if (mod != null) Say(Name(mod), "LINKED TO", false, HudInput.SlotAction(slot.Index));
        }

        static void PaintSlot(Slot slot)
        {
            var mod = slot.Mod;
            slot.Element.EnableInClassList("slot--empty", mod == null);
            slot.Icon.style.backgroundImage = mod != null && mod.Icon != null
                ? new StyleBackground(mod.Icon)
                : new StyleBackground(StyleKeyword.None);
            slot.Rarity.ClearClassList();
            slot.Rarity.AddToClassList("slot__rarity");
            if (mod != null) slot.Rarity.AddToClassList($"slot__rarity--{mod.Rarity.ToString().ToLowerInvariant()}");
        }

        void ResetCooldown(Slot slot)
        {
            slot.Remaining = 0f;
            slot.Total = 0f;
            slot.Element.RemoveFromClassList("slot--cooling");
            slot.Shade.style.height = Length.Percent(0f);
        }

        static void PaintCooldown(Slot slot)
        {
            float share = slot.Total > 0f ? slot.Remaining / slot.Total : 0f;
            slot.Shade.style.height = Length.Percent(share * 100f);
            slot.Time.text = Seconds(slot.Remaining);
        }

        void CameReady(Slot slot)
        {
            slot.Remaining = 0f;
            slot.Element.RemoveFromClassList("slot--cooling");
            slot.Element.AddToClassList("slot--ready");
            HudFx.Tear(new[] { slot.Element }, new TearOptions { frames = 3, amp = 8, chance = 1f, hot = 0.7f, cold = 0f });
            if (slot.Mod != null) Say(Name(slot.Mod), "READY");

            // its key lights and pings: it can be pressed again
            slot.Key.Hot = true;
            slot.Key.Ping();
            slot.Ready?.Cancel();
            slot.Ready = HudFx.After(0.9f, () =>
            {
                slot.Element.RemoveFromClassList("slot--ready");
                slot.Key.Hot = false;
            });
        }

        static void Refuse(Slot slot) => slot.Key.Refuse();

        ModPressResult Refused(Slot slot, ModPressResult result)
        {
            PressRefused?.Invoke((ModSlot)slot.Index, result);
            return result;
        }

        /// <summary>The readout over the panel (hidden when bare): last mod used, or why a press was refused.</summary>
        void Say(string name, string detail, bool warn = false, HudAction? prompt = null, bool hotPrompt = false)
        {
            if (_readout == null) return;
            _readoutTimer?.Cancel();
            HudFx.SetText(_readoutName, name);
            HudFx.Scramble(_readoutDetail, 200, detail);
            _readout.EnableInClassList("mods-readout--warn", warn);
            _readout.EnableInClassList("mods-readout--key", prompt.HasValue);
            if (prompt.HasValue)
            {
                _readoutKey.Action = prompt.Value;
                _readoutKey.Hot = hotPrompt;
            }
            _readout.AddToClassList("mods-readout--on");
            if (prompt.HasValue && hotPrompt) _readoutKey.Ping();
            _readoutTimer = HudFx.After(prompt.HasValue ? 2.6f : 1.8f, () => _readout.RemoveFromClassList("mods-readout--on"));
        }

        List<Slot> AllSlots()
        {
            var all = new List<Slot>(_active);
            all.AddRange(_passive);
            return all;
        }

        static string Name(ModDefinition mod) => mod.DisplayName.ToUpperInvariant();

        static string Seconds(float s) => s >= 10f ? Mathf.CeilToInt(s).ToString() : s.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
