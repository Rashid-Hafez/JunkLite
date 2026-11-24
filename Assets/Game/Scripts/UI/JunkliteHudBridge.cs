using System.Collections.Generic;
using Junklite.HUD;
using Junklite.Inventory;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Shows the game on the Junklite HUD (UI Toolkit, Assets/Junklite/HUD), the way <see cref="InventoryScreenBridge"/>
    /// does for the I menu. The game owns all the data; this only shows it:
    ///   vitals     the player's Health attribute (hits and heals animate), the armor stat, and the Pulse Barrier's
    ///              <see cref="DamageShield"/> (its points, read each frame)
    ///   mod bar    <see cref="ModManager"/>: what's in each slot, cooldowns, uses, ready, refused presses,
    ///              unlocked slots, the uses left (durability) and broken mods, active and passive
    ///   objective  the scene's <see cref="ObjectiveTracker"/>, under the level's display name
    ///   prompts    the mod bindings and the device from <see cref="GameInputManager"/>
    /// The HUD's own dialogue box isn't used: the game's dialogue bubbles stay.
    /// Sits on the HUD object, next to the HUD components, inside the Player UI prefab, so it shows and hides with it.
    /// Nothing in the game changes: it only listens and reads.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JunkliteHudBridge : MonoBehaviour
    {
        private const int ActiveSlots = 4;
        private const int PassiveSlots = 2;

        [SerializeField] private HudVitals vitals;
        [SerializeField] private HudModBar modBar;
        [SerializeField] private HudObjective objective;

        [Tooltip("Optional: mod icons come from the inventory screen's list, so the HUD and the I menu show the same art.")]
        [SerializeField] private InventoryScreenBridge inventoryScreen;

        private PlayerCharacter player;
        private Attribute health;
        private Attribute armor;
        private ModManager mods;
        private GameInputManager input;
        private ObjectiveTracker tracker;
        private ObjectiveDefinition shownObjective;
        private DamageShield shield;
        private int shownShield;
        private float shieldTimeLeft;
        private bool synced;

        private readonly bool[] pressed = new bool[ActiveSlots];
        private readonly bool[] used = new bool[ActiveSlots];
        private readonly bool[] wasCooling = new bool[ActiveSlots];
        private readonly ModInstance[] slotMods = new ModInstance[ActiveSlots];
        private readonly ModInstance[] wornMods = new ModInstance[ActiveSlots + PassiveSlots];
        private readonly bool[] shownBroken = new bool[ActiveSlots + PassiveSlots];
        private readonly Dictionary<ModData, ModDefinition> definitions = new();
        private readonly List<ObjectiveDefinition> objectives = new();

        private void Reset() => FindParts();

        private void Awake() => FindParts();

        private void FindParts()
        {
            if (vitals == null) vitals = GetComponent<HudVitals>();
            if (modBar == null) modBar = GetComponent<HudModBar>();
            if (objective == null) objective = GetComponent<HudObjective>();
        }

        // The HUD rebuilds its elements whenever it's switched on, so everything is pushed again on the next frame.
        private void OnEnable() => synced = false;

        private void OnDisable()
        {
            BindPlayer(null);
            BindInput(null);
            BindTracker(null);
        }

        private void OnDestroy()
        {
            foreach (ModDefinition definition in definitions.Values)
                if (definition != null) Destroy(definition);
            definitions.Clear();
        }

        private void LateUpdate()
        {
            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            PlayerCharacter current = lifecycle != null ? lifecycle.Player : null;
            if (current != player) BindPlayer(current);
            if (player != null && ArmorOf(player) != armor) BindArmor(ArmorOf(player));
            if (GameInputManager.Instance != input) BindInput(GameInputManager.Instance);
            if (ObjectiveTracker.Instance != tracker) BindTracker(ObjectiveTracker.Instance);

            if (!synced)
            {
                SyncAll();
                synced = true;
            }

            UpdateMods();
            UpdateObjective();
            UpdateShield();
        }

        // ---------- binding ----------

        private void BindPlayer(PlayerCharacter target)
        {
            if (health != null)
            {
                health.OnValueChanged -= HandleHealthChanged;
                health.OnMaxChanged -= HandleHealthMaxChanged;
            }
            if (mods != null)
            {
                mods.OnActiveModActivated -= HandleModUsed;
                mods.OnActiveModReady -= HandleModReady;
            }
            BindArmor(null);
            health = null;
            mods = null;
            shield = null;
            shownShield = 0;

            player = target;
            synced = false;
            if (player == null) return;

            var attributes = player.GetComponent<AttributeManager>();
            health = attributes != null ? attributes.Health : null;
            if (health != null)
            {
                health.OnValueChanged += HandleHealthChanged;
                health.OnMaxChanged += HandleHealthMaxChanged;
            }

            mods = player.GetComponent<ModManager>();
            if (mods != null)
            {
                mods.OnActiveModActivated += HandleModUsed;
                mods.OnActiveModReady += HandleModReady;
            }

            BindArmor(ArmorOf(player));
        }

        private static Attribute ArmorOf(PlayerCharacter target)
        {
            if (target.RuntimeStats != null && target.RuntimeStats.ArmorDisplay != null) return target.RuntimeStats.ArmorDisplay;
            var attributes = target.GetComponent<AttributeManager>();
            return attributes != null ? attributes.Get(AttributeType.Armor) : null;
        }

        private void BindArmor(Attribute target)
        {
            if (armor != null)
            {
                armor.OnValueChanged -= HandleArmorChanged;
                armor.OnMaxChanged -= HandleArmorChanged;
            }
            armor = target;
            if (armor != null)
            {
                armor.OnValueChanged += HandleArmorChanged;
                armor.OnMaxChanged += HandleArmorChanged;
            }
            if (synced) ShowArmor();
        }

        private void BindInput(GameInputManager target)
        {
            if (input != null)
            {
                input.OnModActivate1 -= HandlePress0;
                input.OnModActivate2 -= HandlePress1;
                input.OnModActivate3 -= HandlePress2;
                input.OnModActivate4 -= HandlePress3;
                input.OnInputDeviceChanged -= HandleDeviceChanged;
            }
            input = target;
            if (input == null) return;
            input.OnModActivate1 += HandlePress0;
            input.OnModActivate2 += HandlePress1;
            input.OnModActivate3 += HandlePress2;
            input.OnModActivate4 += HandlePress3;
            input.OnInputDeviceChanged += HandleDeviceChanged;
            if (synced) ShowBindings();
        }

        private void BindTracker(ObjectiveTracker target)
        {
            if (tracker != null) tracker.StateChanged -= HandleObjectiveStateChanged;
            tracker = target;
            shownObjective = null;
            objectives.Clear();
            if (tracker != null)
            {
                tracker.StateChanged += HandleObjectiveStateChanged;
                foreach (ObjectiveDefinition definition in tracker.Objectives)
                    if (definition != null) objectives.Add(definition);
            }
            LoadObjectives();
        }

        // ---------- pushing everything (after the HUD is switched on, or the player changes) ----------

        private void SyncAll()
        {
            if (vitals != null)
            {
                // JunkLite has no CROW revive and no lives
                vitals.ShowCrow = false;
                vitals.ShowLives = false;
                if (health != null) vitals.SetHealth(Mathf.RoundToInt(health.Current), Mathf.RoundToInt(health.Max));
                ShowArmor();
                shownShield = 0;
            }

            if (modBar != null && !modBar.GameDriven)
            {
                Debug.LogWarning("[JunkliteHudBridge] The HUD's mod bar should have Game Driven on; switching it on.", this);
                modBar.GameDriven = true;
            }
            for (int i = 0; i < ActiveSlots; i++)
            {
                pressed[i] = used[i] = wasCooling[i] = false;
                slotMods[i] = null;
            }
            for (int i = 0; i < wornMods.Length; i++)
            {
                wornMods[i] = null;
                shownBroken[i] = false;
            }

            if (objective != null) objective.AdvanceAfterComplete = false;
            LoadObjectives();
            ShowBindings();
        }

        private void ShowArmor()
        {
            if (vitals != null) vitals.ShowArmorStat(armor != null ? armor.Current : 0f);
        }

        // ---------- vitals ----------

        private void HandleHealthChanged(float value)
        {
            if (!synced || vitals == null) return;
            int next = Mathf.RoundToInt(value);
            int shown = vitals.Health;
            if (next < shown) vitals.Damage(shown - next);
            else if (next > shown) vitals.Heal(next - shown);
        }

        private void HandleHealthMaxChanged(float max)
        {
            if (!synced || vitals == null || health == null) return;
            vitals.SetHealth(Mathf.RoundToInt(health.Current), Mathf.RoundToInt(max));
        }

        private void HandleArmorChanged(float _)
        {
            if (synced) ShowArmor();
        }

        // The Pulse Barrier adds a DamageShield to the player the first time it's used. Its points are read each frame;
        // it soaks hits before health, so the health bar doesn't move while it's up.
        private void UpdateShield()
        {
            if (!synced || vitals == null || player == null) return;
            if (shield == null) shield = player.GetComponent<DamageShield>();
            bool up = shield != null && shield.IsActive;
            int points = up ? Mathf.CeilToInt(shield.CurrentHP) : 0;
            if (points != shownShield)
            {
                // it ran out if its time was nearly up; otherwise a hit broke it
                bool broken = points == 0 && shieldTimeLeft > 0.25f;
                vitals.SetShield(points, shield != null ? Mathf.RoundToInt(shield.MaxHP) : 0, broken);
                shownShield = points;
            }
            shieldTimeLeft = up ? shield.TimeRemaining : 0f;
        }

        // ---------- mod bar ----------

        private void HandlePress0() => pressed[0] = true;
        private void HandlePress1() => pressed[1] = true;
        private void HandlePress2() => pressed[2] = true;
        private void HandlePress3() => pressed[3] = true;

        private void HandleModUsed(int slot)
        {
            if (slot < 0 || slot >= ActiveSlots) return;
            used[slot] = true;
            if (synced && modBar != null) modBar.ShowUsed(slot);
        }

        // charge-based mods come ready through hits
        private void HandleModReady(int slot)
        {
            if (synced && modBar != null && slot >= 0 && slot < ActiveSlots) modBar.ShowReady(slot);
        }

        private void UpdateMods()
        {
            if (modBar == null) return;
            int unlockedActive = mods != null ? Mathf.Min(mods.UnlockedActiveSlots, ActiveSlots) : 0;
            int unlockedPassive = mods != null ? Mathf.Min(mods.UnlockedPassiveSlots, PassiveSlots) : 0;
            modBar.ShowUnlocked(unlockedActive, unlockedPassive);

            for (int i = 0; i < ActiveSlots; i++)
            {
                ModInstance mod = i < unlockedActive ? mods.GetActiveMod(i) : null;
                bool changed = mod != slotMods[i];
                slotMods[i] = mod;
                modBar.ShowSlot(i, DefinitionFor(mod));

                bool cooling = mod != null && mod.IsOnCooldown;
                if (cooling)
                {
                    float remaining = mod.CooldownRemaining;
                    float share = mod.CooldownNormalized;
                    modBar.ShowCooldown(i, remaining, share > 0f ? remaining / share : remaining);
                }
                else if (wasCooling[i] && !changed)
                {
                    modBar.ShowReady(i);   // the cooldown just ran out
                }
                else
                {
                    modBar.ShowCooldown(i, 0f, 0f);
                }
                wasCooling[i] = cooling;

                ShowWear(i, mod);
                if (pressed[i] && !used[i] && i < unlockedActive) modBar.ShowRefused(i);
                pressed[i] = used[i] = false;
            }

            for (int i = 0; i < PassiveSlots; i++)
            {
                ModInstance mod = i < unlockedPassive ? mods.GetPassiveMod(i) : null;
                modBar.ShowSlot(ActiveSlots + i, DefinitionFor(mod));
                ShowWear(ActiveSlots + i, mod);
            }
        }

        // The uses bar and the broken state for a slot (0 to 3 active, 4 and 5 passive). Uses come from the mod's
        // durability: max / per use, and the ones left from its current durability. The break plays only when the mod
        // that was already in the slot breaks, not when a broken one is loaded or equipped.
        private void ShowWear(int index, ModInstance mod)
        {
            bool broken = mod != null && mod.IsBroken;
            bool same = mod == wornMods[index];
            modBar.ShowBroken(index, broken, animate: broken && same && !shownBroken[index]);
            wornMods[index] = mod;
            shownBroken[index] = broken;

            int uses = 0, left = 0;
            if (mod != null && mod.Data != null && mod.Data.durabilityPerUse > 0f && mod.Data.maxDurability > 0f)
            {
                uses = Mathf.CeilToInt(mod.Data.maxDurability / mod.Data.durabilityPerUse - 0.001f);
                left = Mathf.CeilToInt(Mathf.Max(0f, mod.CurrentDurability) / mod.Data.durabilityPerUse - 0.001f);
            }
            modBar.ShowWear(index, left, uses);
        }

        private ModDefinition DefinitionFor(ModInstance mod)
        {
            if (mod == null || mod.Data == null) return null;
            ModData data = mod.Data;
            if (definitions.TryGetValue(data, out ModDefinition existing) && existing != null) return existing;

            float cooldown = data is ActiveModData active ? active.cooldown : 0f;
            Texture2D icon = inventoryScreen != null ? inventoryScreen.IconFor(data)
                : data.icon != null ? data.icon.texture : null;
            ModDefinition definition = ModDefinition.Create(
                id: data.name,
                displayName: string.IsNullOrEmpty(data.modName) ? data.name : data.modName,
                rarity: (ModRarity)Mathf.Clamp((int)data.rarity, 0, (int)ModRarity.Elite),
                type: mod.IsPassive ? ModType.Passive : ModType.Active,
                cooldown: cooldown,
                description: data.description,
                icon: icon);
            definition.hideFlags = HideFlags.DontSave;
            definitions[data] = definition;
            return definition;
        }

        // ---------- prompts ----------

        private void HandleDeviceChanged(bool _) => ShowBindings();

        private void ShowBindings()
        {
            if (input == null || !input.isActiveAndEnabled) return;
            bool pad = input.IsUsingGamepad;
            for (int i = 0; i < ActiveSlots; i++)
            {
                string hint = input.GetModActivateHint(i);
                if (string.IsNullOrEmpty(hint)) continue;
                HudAction action = HudInput.SlotAction(i);
                if (pad) HudInput.SetPadButton(action, PadButtonFor(hint));
                else HudInput.SetKey(action, hint);
            }
            HudInput.SetDevice(pad ? HudDevice.Gamepad : HudDevice.Keyboard);
        }

        // The Input System's display names: "D-Pad Up" and so on, or a face button's name.
        private static PadButton PadButtonFor(string hint)
        {
            string h = hint.Trim().ToLowerInvariant();
            if (h.Contains("up")) return PadButton.DpadUp;
            if (h.Contains("down")) return PadButton.DpadDown;
            if (h.Contains("left")) return PadButton.DpadLeft;
            if (h.Contains("right")) return PadButton.DpadRight;
            switch (h)
            {
                case "a": case "cross": return PadButton.Cross;
                case "b": case "circle": return PadButton.Circle;
                case "x": case "square": return PadButton.Square;
                case "y": case "triangle": return PadButton.Triangle;
                default: return PadButton.None;
            }
        }

        // ---------- objective ----------

        private void LoadObjectives()
        {
            if (objective == null) return;
            var list = new List<Directive>(objectives.Count);
            string level = LevelName();
            foreach (ObjectiveDefinition definition in objectives)
                list.Add(new Directive(level, (definition.Title ?? string.Empty).ToUpperInvariant(), definition.Body ?? string.Empty));
            objective.LoadDirectives(list);
            shownObjective = null;
        }

        private static string LevelName()
        {
            LevelContext context = FindAnyObjectByType<LevelContext>();
            return context != null && !string.IsNullOrEmpty(context.DisplayName) ? context.DisplayName.ToUpperInvariant() : string.Empty;
        }

        private void HandleObjectiveStateChanged(ObjectiveDefinition definition, ObjectiveState state)
        {
            if (!synced || objective == null) return;
            if (state == ObjectiveState.Complete && definition == shownObjective && objective.IsShown)
                objective.Complete();
        }

        // Shows the tracker's current objective once the card is free (not mid-completion).
        private void UpdateObjective()
        {
            if (objective == null || tracker == null || objective.IsBusy) return;
            ObjectiveDefinition current = tracker.Current;
            if (current == shownObjective) return;
            shownObjective = current;
            if (current == null) return;

            int index = objectives.IndexOf(current);
            if (index < 0)
            {
                objectives.Add(current);
                LoadObjectives();
                shownObjective = current;
                index = objectives.Count - 1;
            }
            objective.Show(index);
        }
    }
}
