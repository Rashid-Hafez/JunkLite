using System;
using UnityEngine;

namespace junklite
{
    [RequireComponent(typeof(Collider))]
    public class SequenceTrigger : MonoBehaviour
    {
        [SerializeField] private string playerTag = "Player";

        public event Action OnTriggered;

        private bool fired;

        private void OnTriggerEnter(Collider other)
        {
            if (fired) return;
            if (!other.CompareTag(playerTag) &&
                (other.attachedRigidbody == null || !other.attachedRigidbody.CompareTag(playerTag)))
                return;

            fired = true;
            OnTriggered?.Invoke();
        }

        public void ResetTrigger()
        {
            fired = false;
        }
    }
}
