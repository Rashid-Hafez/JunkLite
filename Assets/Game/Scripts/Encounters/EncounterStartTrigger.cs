using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Starts one referenced encounter the first time the player enters this trigger box
    /// (e.g. the doorway of a boss room). Owns nothing else about the fight.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class EncounterStartTrigger : MonoBehaviour
    {
        [SerializeField] private EncounterController encounter;

        private void Awake() => GetComponent<Collider>().isTrigger = true;

        private void OnTriggerEnter(Collider other)
        {
            if (encounter == null || encounter.State != EncounterState.Idle || !other.CompareTag("Player")) return;
            encounter.StartEncounter();
        }
    }
}
