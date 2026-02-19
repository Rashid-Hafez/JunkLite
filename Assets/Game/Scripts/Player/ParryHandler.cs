using System.Collections;
using UnityEngine;

namespace junklite
{
    [RequireComponent(typeof(PlayerState), typeof(PlayerCharacter))]
    public class ParryHandler : MonoBehaviour
    {
        [Header("Parry Settings")]
        [SerializeField] private float parryDuration = 0.25f;
        [SerializeField] private float parryCooldown = 0.2f;
        [SerializeField] private float parryRadius = 3f;
        [SerializeField] private LayerMask enemyLayer;
        [SerializeField] private GameObject parryVFXPrefab;
        [SerializeField] private float pushForce = 12f;
        [SerializeField] private float stunDuration = 0.3f;

        [Header("Feedback")]
        [SerializeField] private float hitstopDuration = 0.1f;

        // remembered attacker for stage-2 special logic
        private GameObject primaryAttacker;

        CameraManager camManager; // for screen shake on successful parry

        // internal state
        private PlayerState playerState;
        private PlayerCharacter playerChar;
        private Character2D5Controller controller;

        private bool parryActive;
        private int parryStage; // 1 or 2
        private Coroutine parryRoutine;

        public bool IsParrying => parryActive;

        private void Awake()
        {
            playerState = GetComponent<PlayerState>();
            playerChar = GetComponent<PlayerCharacter>();
            controller = GetComponent<Character2D5Controller>();

            if (GameInputManager.Instance != null)
                GameInputManager.Instance.OnParry += BeginParry;

            camManager = CameraManager.Instance;

        }

        private void OnDestroy()
        {
            if (GameInputManager.Instance != null)
                GameInputManager.Instance.OnParry -= BeginParry;
        }

        public void BeginParry()
        {
            // cannot parry while already in parry or when state disallows it
            if (parryActive) return;
            if (playerState != null && !playerState.CanParry)
                return;

            // kill any residual momentum so the player stops immediately
            if (controller != null)
            {
                var rb = controller.GetComponent<Rigidbody>();
                if (rb != null)
                    rb.linearVelocity = Vector3.zero;
            }

            Debug.Log("[Parry] Begin stage1");
            parryRoutine = StartCoroutine(ParryCoroutine());
        }

        private IEnumerator ParryCoroutine()
        {
            parryActive = true;
            parryStage = 1;
            playerState?.SetParrying(true);
            playerState?.ApplyInvulnerability(parryDuration);
            playerState?.RequestAttackAnimation("Perry_1");

            yield return new WaitForSeconds(parryDuration);

            // whiff
            Debug.Log("[Parry] Stage1 timed out (whiff)");
            EndParryWithWhiff();
        }

        private void EndParryWithWhiff()
        {
            parryActive = false;
            playerState?.SetParrying(false);
            // lock input briefly
            if (playerState != null)
            {
                playerState.SetInputLocked(true);
                StartCoroutine(CooldownCoroutine());
            }
        }

        private IEnumerator CooldownCoroutine()
        {
            yield return new WaitForSeconds(parryCooldown);
            playerState?.SetInputLocked(false);
        }

        /// <summary>
        /// Called from PlayerCharacter when damage is incoming.  Returns true if the hit
        /// was blocked by a parry (in which case no further processing should occur).
        /// </summary>
        public bool HandleIncomingHit(GameObject attacker)
        {
            if (!parryActive) return false;

            if (parryStage == 1)
            {
                // remember who hit us so we can treat it specially later
                primaryAttacker = attacker;

                // transition to stage two
                parryStage = 2;
                if (parryRoutine != null) StopCoroutine(parryRoutine);

                Debug.Log("[Parry] Hit detected during stage1 -> entering stage2");
                playerState?.ApplyInvulnerability(parryDuration);
                playerState?.RequestAttackAnimation("Perry_2");

                if (parryVFXPrefab != null)
                    Instantiate(parryVFXPrefab, transform.position, Quaternion.identity);

                // feedback effects
                FeedbackManager.Instance?.DoHitstop(hitstopDuration);
                FeedbackManager.Instance?.DoCameraShake(); // uses default impulse internally
                // camera manager could also dolly/zoom if you add a method there (implement DoParryCameraEffect in CameraManager)
                camManager?.DoParryCameraEffect();

                PushEnemies();

                parryRoutine = StartCoroutine(ParryStage2Coroutine());
            }

            // always block damage while active
            return true;
        }

        private IEnumerator ParryStage2Coroutine()
        {
            yield return new WaitForSeconds(parryDuration);
            parryActive = false;
            playerState?.SetParrying(false);

            Debug.Log("[Parry] Stage2 complete, applying post-parry cooldown");
            // lock input briefly after successful parry, then run cooldown just like a whiff
            if (playerState != null)
            {
                playerState.SetInputLocked(true);
                StartCoroutine(CooldownCoroutine());
            }
        }

        private void PushEnemies()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, parryRadius, enemyLayer);
            foreach (var c in hits)
            {
                if (c == null) continue;

                bool isPrimary = (primaryAttacker != null && c.gameObject == primaryAttacker);

                // try to apply stun/knockback on enemy
                var state = c.GetComponent<CharacterState>() ?? c.GetComponentInParent<CharacterState>();
                if (state != null)
                    state.ApplyStun(isPrimary ? stunDuration : stunDuration * 0.5f);

                if (isPrimary)
                {
                    // custom logic for the attacker
                    var dmg = c.GetComponent<Damageable>() ?? c.GetComponentInParent<Damageable>();
                    if (dmg != null)
                    {
                        // example: deal a little retaliatory damage
                        dmg.TakeDamage(new DamageInfo(5f, gameObject));
                    }
                    // TODO: call any other special handling you want for this enemy
                }

                // apply physical push if it has a rigidbody
                var rb = c.GetComponent<Rigidbody>() ?? c.GetComponentInParent<Rigidbody>();
                if (rb != null)
                {
                    Vector3 dir = (c.transform.position - transform.position);
                    dir.z = 0f; // flatten to 2.5D plane
                    dir.Normalize();
                    rb.AddForce(dir * pushForce, ForceMode.Impulse);
                }
            }

            // clear remembered attacker once we're done
            primaryAttacker = null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, parryRadius);
        }
    }
}
