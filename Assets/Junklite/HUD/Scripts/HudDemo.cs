using System;
using System.Collections.Generic;
using Junklite.Inventory;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;
#if JUNKLITE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Junklite.HUD
{
    /// <summary>
    /// Drives the HUD in the demo scene: a screenshot of the game behind it, and keys that trigger every state
    /// (listed under "In-game HUD" in PIPELINE.md). Not needed in the game; leave it out of real scenes.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HudDemo : MonoBehaviour
    {
        [Tooltip("Equipped in slot Z by the P key. It has to be in the ModInventory's storage.")]
        [SerializeField] ModDefinition phantomStrike;

        [Tooltip("Fires random hits, heals, mods and steps every 2.6 seconds, to judge the HUD while it reacts.")]
        [SerializeField] bool fightLoop;

        HudVitals _vitals;
        HudObjective _objective;
        HudModBar _mods;
        HudDialogue _dialogue;
        HudLayout _layout;
        ModInventory _inventory;
        float _nextFight;

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) return;
            root.StretchToParentSize();
            root.pickingMode = PickingMode.Ignore;
        }

        void Start()
        {
            _vitals = FindAnyObjectByType<HudVitals>();
            _objective = FindAnyObjectByType<HudObjective>();
            _mods = FindAnyObjectByType<HudModBar>();
            _dialogue = FindAnyObjectByType<HudDialogue>();
            _layout = FindAnyObjectByType<HudLayout>();
            _inventory = FindAnyObjectByType<ModInventory>();
        }

        void Update()
        {
            foreach (var key in DemoKeys())
            {
                if (Pressed(key)) Run(key);
            }

            if (fightLoop && Time.unscaledTime >= _nextFight)
            {
                _nextFight = Time.unscaledTime + 2.6f;
                FightTick();
            }
        }

        void Run(string key)
        {
            switch (key)
            {
                case "H": _vitals?.Damage(22); break;
                case "J": _vitals?.Heal(40); break;
                case "B": _vitals?.HitArmor(); break;
                case "N": _vitals?.RepairArmor(); break;
                case "M": _vitals?.CycleCrow(); break;
                case "0": _vitals?.GoDown(); break;
                case "9": _vitals?.Full(); break;
                case "K": _objective?.Progress(); break;
                case "L": _objective?.Complete(); break;
                case "O": _objective?.SetCompact(); break;
                case "I": _objective?.Toggle(); break;
                case "Y": _objective?.Refresh(); break;
                case "P": EquipPhantomStrike(); break;
                case "U": _mods?.Equip(0, null); break;
                case "T":
                    if (_mods != null) _mods.CooldownSpeed = _mods.CooldownSpeed > 1f ? 1f : 4f;
                    break;
                case "R": _mods?.FinishCooldowns(); break;
                case "G": HudInput.ToggleDevice(); break;
                case "S": _layout?.CycleSize(); break;
                case "F":
                    fightLoop = !fightLoop;
                    _nextFight = Time.unscaledTime;
                    break;
            }
        }

        void EquipPhantomStrike()
        {
            if (_mods == null || phantomStrike == null) return;
            if (_inventory != null && !_inventory.Contains(phantomStrike)) _inventory.Add(phantomStrike);
            _mods.Equip(0, phantomStrike);
        }

        void FightTick()
        {
            if (_vitals == null) return;
            float r = Random.value;
            if (_vitals.Health < 55) _vitals.Heal(40);
            else if (r < 0.35f) _vitals.Damage(6 + Mathf.RoundToInt(Random.value * 14f));
            else if (r < 0.65f) _mods?.Press(1 + Random.Range(0, 3));
            else if (r < 0.75f) _vitals.HitArmor();
            else if (r < 0.85f) _objective?.Progress();
        }

        static IEnumerable<string> DemoKeys()
        {
            yield return "H"; yield return "J"; yield return "B"; yield return "N"; yield return "M";
            yield return "K"; yield return "L"; yield return "O"; yield return "I"; yield return "Y";
            yield return "P"; yield return "U"; yield return "T"; yield return "R";
            yield return "G"; yield return "F"; yield return "S";
            yield return "0"; yield return "9";
        }

        static bool Pressed(string key)
        {
#if JUNKLITE_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            bool digit = key.Length == 1 && char.IsDigit(key[0]);
            var k = (Key)Enum.Parse(typeof(Key), digit ? "Digit" + key : key);
            if (keyboard[k].wasPressedThisFrame) return true;
            return digit && keyboard[(Key)Enum.Parse(typeof(Key), "Numpad" + key)].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            KeyCode code = key.Length == 1 && char.IsDigit(key[0])
                ? (KeyCode)((int)KeyCode.Alpha0 + (key[0] - '0'))
                : (KeyCode)Enum.Parse(typeof(KeyCode), key);
            return Input.GetKeyDown(code);
#else
            return false;
#endif
        }
    }
}
