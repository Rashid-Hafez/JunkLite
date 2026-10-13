using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Room trigger: once the player has entered, the next weapon they equip shows the
    /// "[Q] Combat mode" hint, which completes when they toggle combat mode. Runs once.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CombatModeHint : MonoBehaviour
    {
        [Tooltip("Scene TutorialPromptHUD used for the hint.")]
        [SerializeField] private TutorialPromptHUD prompt;
        [SerializeField] private string label = "Combat mode";

        private PlayerWeaponLoadout loadout;
        private GameInputManager input;
        private bool armed;
        private bool done;

        private void Awake() => GetComponent<Collider>().isTrigger = true;

        private void OnDisable()
        {
            Unsubscribe();
            if (armed && !done) armed = false; // re-arms on next entry
        }

        private void OnTriggerEnter(Collider other)
        {
            if (armed || done || !other.CompareTag("Player")) return;
            PlayerCharacter player = PlayerLifecycle.Instance != null ? PlayerLifecycle.Instance.Player : null;
            if (player == null) return;

            WeaponManager weapons = player.GetComponentInChildren<WeaponManager>(true);
            loadout = weapons != null ? weapons.Loadout : player.GetComponentInChildren<PlayerWeaponLoadout>(true);
            if (loadout == null) return;

            armed = true;
            loadout.WeaponChanged += HandleWeaponChanged;
        }

        private void HandleWeaponChanged()
        {
            loadout.WeaponChanged -= HandleWeaponChanged;
            input = GameInputManager.Instance;
            if (input != null) input.OnCombatModeToggle += HandleCombatModeToggle;
            if (prompt != null) prompt.Show(new TutorialPromptHUD.Entry("Player/CombatMode", label));
        }

        private void HandleCombatModeToggle()
        {
            done = true;
            Unsubscribe();
            if (prompt != null) prompt.Complete();
        }

        private void Unsubscribe()
        {
            if (loadout != null) loadout.WeaponChanged -= HandleWeaponChanged;
            if (input != null) input.OnCombatModeToggle -= HandleCombatModeToggle;
            input = null;
        }
    }
}
