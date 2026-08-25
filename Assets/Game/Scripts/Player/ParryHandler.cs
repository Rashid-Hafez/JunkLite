using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    [RequireComponent(typeof(PlayerState), typeof(PlayerCharacter))]
    public class ParryHandler : MonoBehaviour
    {
        [Header("Parry Settings")]
        [SerializeField] private float parryDuration = 0.25f;
        [SerializeField] private float parryCooldown = 0.2f;
        [SerializeField, Tooltip("How long player input remains locked after successful parry (usually matches Perry_2 length)")]
        private float parryLockDuration = 0.3f;
        [SerializeField] private float parryRadius = 3f;
        [SerializeField] private LayerMask enemyLayer;
        [SerializeField] private GameObject parryVFXPrefab;
        [SerializeField] private GameObject VisualEffectParry;
        [SerializeField] private float pushForce = 20.5f;
        [SerializeField] private float pushDuration = 0.25f; // short push window; independent from parry stun duration
        [SerializeField] private float stunDuration = 0.3f;

        [SerializeField] private float parryStunDuration = 0.5f; // how long enemies are stunned when hit by a parry (primary attacker gets full duration, others get half)

        [Header("Feedback")]
        [SerializeField] private float hitstopDuration = 0.1f;

        // remembered attacker for stage-2 special logic
        private GameObject primaryAttacker;

        CameraManager camManager; // for screen shake on successful parry

        // internal state
        private PlayerState playerState;
        private PlayerCharacter playerChar;
        private Character2D5Controller controller;
        private AudioManager audioManager;

        private bool parryActive;
        private int parryStage; // 1 or 2
        private Coroutine parryRoutine;

        public bool IsParrying => parryActive;

        private void Awake()
        {
            playerState = GetComponent<PlayerState>();
            playerChar = GetComponent<PlayerCharacter>();
            controller = GetComponent<Character2D5Controller>();
            audioManager = AudioManager.Instance;

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

           //  Debug.Log("[Parry] Begin stage1");
            PlayParryStartSound();
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
          //  Debug.Log("[Parry] Stage1 timed out (whiff)");
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

              //  Debug.Log("[Parry] Hit detected during stage1 -> entering stage2");
                playerState?.ApplyInvulnerability(parryDuration);
                // immediately switch to the hit animation before anything else
                playerState?.RequestAttackAnimation("Perry_2");

                if (parryVFXPrefab != null)
                    Instantiate(parryVFXPrefab, transform.position, Quaternion.identity);

                PlayParrySuccessSound();

                // feedback effects
               // FeedbackManager.Instance?.DoHitstop(hitstopDuration);
                FeedbackManager.Instance?.DoCameraShake(); // uses default impulse internally

                // delay slow‑motion/zoom slightly so animation plays first
                StartCoroutine(DelayedCameraEffect());

                VisualEffectParry.SetActive(true);
                parryVFXPrefab.SetActive(true);
            }

            // always block damage while active
            return true;
        }

        private IEnumerator DelayedCameraEffect()
        {
            yield return new WaitForSeconds(0.2f);
                PushEnemies();

                parryRoutine = StartCoroutine(ParryStage2Coroutine());
            camManager?.DoParryCameraEffect();
        }

        private void PlayParryStartSound()
        {
            if (audioManager == null) audioManager = AudioManager.Instance;
            var profile = playerChar != null ? playerChar.SoundProfile : null;
            if (audioManager == null || profile == null) return;

            audioManager.PlaySpatialAtPosition(profile.parryStart, transform.position);
        }

        private void PlayParrySuccessSound()
        {
            if (audioManager == null) audioManager = AudioManager.Instance;
            var profile = playerChar != null ? playerChar.SoundProfile : null;
            if (audioManager == null || profile == null) return;

            audioManager.PlaySpatialAtPosition(profile.parrySuccess, transform.position);
        }

        //cooldown
        private IEnumerator ParryStage2Coroutine()
        {
            yield return new WaitForSeconds(parryDuration);
            parryActive = false;
            playerState?.SetParrying(false);
            VisualEffectParry.SetActive(false);
            parryVFXPrefab.SetActive(false);

          //  Debug.Log("[Parry] Stage2 complete, holding input lock until animation ends");
            if (playerState != null)
            {
                playerState.SetInputLocked(true);
                // hold lock for the duration of the parry hit animation (tunable)
                yield return new WaitForSeconds(parryLockDuration);
                playerState.SetInputLocked(false);
            }
        }

        private void PushEnemies()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, parryRadius, enemyLayer);
            var processedEnemies = new HashSet<EnemyCharacter>();

            foreach (var c in hits)
            {
                if (c == null) continue;

                var enemyChar = c.GetComponent<EnemyCharacter>() ?? c.GetComponentInParent<EnemyCharacter>();
                if (enemyChar == null) continue;
                if (!processedEnemies.Add(enemyChar)) continue; // avoid duplicate pushes/stuns from multiple colliders

                var state = enemyChar.GetComponent<CharacterState>() ?? enemyChar.GetComponentInParent<CharacterState>();
                bool isPrimary = false;
                if (primaryAttacker != null)
                {
                    isPrimary = enemyChar.gameObject == primaryAttacker
                             || enemyChar.transform.IsChildOf(primaryAttacker.transform)
                             || primaryAttacker.transform.IsChildOf(enemyChar.transform);
                }

                // Primary attacker: full parryStunDuration lock via OnParryStunned
                if (isPrimary)
                {
                    // Retaliatory parry damage — use isTickDamage to skip hitstun
                    // so the FSM doesn't bounce through HurtState and double-trigger
                    // the Hurt animation while the enemy should be parry-stunned.
                    DamageReceiverUtility.Receive(enemyChar, new DamageRequest(
                        5f,
                        gameObject,
                        isTickDamage: true));
                    enemyChar.OnParryStunned(parryStunDuration);
                }
                // Bystanders in radius: half duration stun
                else
                {
                    if (state != null)
                        state.ApplyStun(parryStunDuration * 0.5f);

                    enemyChar.OnParryStunned(parryStunDuration * 0.5f);
                }

                // Apply parry push through the enemy's axis-aware movement system
                Vector3 dir = (enemyChar.transform.position - transform.position).normalized;
                enemyChar.ApplyParryPush(dir, pushForce, 1f, pushDuration);
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
