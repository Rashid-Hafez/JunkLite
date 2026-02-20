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
        [SerializeField, Tooltip("How long player input remains locked after successful parry (usually matches Perry_2 length)")]
        private float parryLockDuration = 0.3f;
        [SerializeField] private float parryRadius = 3f;
        [SerializeField] private LayerMask enemyLayer;
        [SerializeField] private GameObject parryVFXPrefab;
        [SerializeField] private float pushForce = 20.5f;
        [SerializeField] private float pushDuration = 0.18f; // how long to apply push impulse over time
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
                // immediately switch to the hit animation before anything else
                playerState?.RequestAttackAnimation("Perry_2");

                if (parryVFXPrefab != null)
                    Instantiate(parryVFXPrefab, transform.position, Quaternion.identity);

                // feedback effects
               // FeedbackManager.Instance?.DoHitstop(hitstopDuration);
                FeedbackManager.Instance?.DoCameraShake(); // uses default impulse internally

                // delay slow‑motion/zoom slightly so animation plays first
                StartCoroutine(DelayedCameraEffect());

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

        //cooldown
        private IEnumerator ParryStage2Coroutine()
        {
            yield return new WaitForSeconds(parryDuration);
            parryActive = false;
            playerState?.SetParrying(false);

            Debug.Log("[Parry] Stage2 complete, holding input lock until animation ends");
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
                    var enemy = c.GetComponent<EnemyCharacter>();
                    if (enemy != null)
                    {
                        // example: deal a little retaliatory damage
                        enemy.TakeDamage(new DamageInfo(5f, gameObject));
                        enemy.ApplyStun(parryStunDuration);
                    }
                    // TODO: call any other special handling you want for this enemy

                }

                // schedule a timed stun so enemies stay locked down for the full parry duration
                {
                    float hold = isPrimary ? parryStunDuration : parryStunDuration * 0.5f;
                    if (state != null)
                        StartCoroutine(KeepEnemyStunned(state, hold));

                    // inform the enemy so it can play a looping stun animation if desired
                    var enemy = c.GetComponent<EnemyCharacter>();
                    if (enemy != null)
                        enemy.OnParryStunned(hold);
                }

                // apply physical push if it has a rigidbody
                var rb = c.GetComponent<Rigidbody>() ?? c.GetComponentInParent<Rigidbody>();
                if (rb != null)
                {
                    Vector3 dir = (c.transform.position - transform.position);
                    dir.Normalize();
                    dir.z = 0f; // flatten to 2.5D plane

                    // start a coroutine that applies acceleration over time (results in smoother push)
                    StartCoroutine(ApplyPushOverTime(rb, dir, pushForce, 1f, pushDuration));
                }
            }

            // clear remembered attacker once we're done
            primaryAttacker = null;
        }

    // coroutine used by PushEnemies to enforce a timed stun independent of the character's own logic
    private IEnumerator KeepEnemyStunned(CharacterState state, float duration)
    {
        if (state == null) yield break;

        state.SetStunned(true);
        yield return new WaitForSeconds(duration);
        if (state != null)
            state.SetStunned(false);
    }

        // Applies a directional push to a rigidbody over several fixed updates.
        // totalHorizontalImpulse: desired change in horizontal velocity (units per second)
        // totalUpwardImpulse: desired total upward velocity change (small, e.g. 1f)
        private IEnumerator ApplyPushOverTime(Rigidbody rb, Vector3 dir, float totalHorizontalImpulse, float totalUpwardImpulse, float duration)
        {
            if (rb == null || duration <= 0f) yield break;

            float elapsed = 0f;
            // acceleration needed to achieve total deltaV over time: a = deltaV / duration
            Vector3 horizAccel = new Vector3(dir.x, 0f, dir.z) * (totalHorizontalImpulse / Mathf.Max(0.0001f, duration));
            Vector3 upAccel = Vector3.up * (totalUpwardImpulse / Mathf.Max(0.0001f, duration));

            // run during physics steps
            while (elapsed < duration)
            {
                rb.AddForce(horizAccel, ForceMode.Acceleration);
                rb.AddForce(upAccel, ForceMode.Acceleration);
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;
            }
        }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, parryRadius);
    }
}
}
