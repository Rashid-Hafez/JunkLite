using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Push: the SpecialAttack input shoves the enemies in front of the player away. No damage. It moves
    /// them with the same enemy API as a parry's push, so canBeKnockedBack and per-enemy overrides apply.
    /// Optional: a player without this component simply has no push.
    /// </summary>
    [RequireComponent(typeof(PlayerCharacter))]
    public class PlayerPush : MonoBehaviour
    {
        // Same small lift a parry's push gives.
        private const float PushUpwardForce = 1f;

        [Tooltip("How far in front of her the push reaches (m).")]
        [SerializeField, Min(0f)] private float pushRange = 2.5f;
        [Tooltip("Radius of the push volume around that line (m).")]
        [SerializeField, Min(0.01f)] private float pushRadius = 1.5f;
        [Tooltip("Speed the enemy reaches by the end of the push (m/s).")]
        [SerializeField, Min(0f)] private float pushForce = 14f;
        [Tooltip("Seconds the enemy is pushed for.")]
        [SerializeField, Min(0.01f)] private float pushDuration = 0.2f;
        [SerializeField, Min(0f)] private float pushCooldown = 1.5f;
        [Tooltip("Attack clip played on a push. A rig without it plays nothing; the push still happens.")]
        [SerializeField] private string pushAnimation = "Attack_Punch_Combo_1";
        [Tooltip("Enemy layers. Empty uses the WeaponManager's enemy layers.")]
        [SerializeField] private LayerMask enemyLayer;

        private readonly Collider[] overlapBuffer = new Collider[64];
        private readonly HashSet<EnemyCharacter> pushedEnemies = new();

        private PlayerCharacter player;
        private PlayerState playerState;
        private Character2D5Controller controller;
        private Collider body;
        private GameInputManager input;
        private float nextPushTime;

        private void Awake()
        {
            player = GetComponent<PlayerCharacter>();
            playerState = GetComponent<PlayerState>();
            controller = GetComponent<Character2D5Controller>();
            TryGetComponent(out body);

            if (enemyLayer.value == 0 && TryGetComponent(out WeaponManager weapons))
                enemyLayer = weapons.EnemyLayer;
        }

        private void OnEnable() => Subscribe();

        // The input manager lives on the Game Root, which can come up after a scene-placed player.
        private void Start() => Subscribe();

        private void OnDisable()
        {
            if (input != null) input.OnSpecialAttack -= TryPush;
            input = null;
        }

        private void Subscribe()
        {
            if (input != null || GameInputManager.Instance == null) return;
            input = GameInputManager.Instance;
            input.OnSpecialAttack += TryPush;
        }

        private bool CanPush =>
            Time.time >= nextPushTime && player != null && player.IsActive && !player.IsGrabbed &&
            playerState != null && playerState.CanAttack && !playerState.IsInputLocked && !playerState.IsDashing;

        private void TryPush()
        {
            if (!CanPush || controller == null) return;

            nextPushTime = Time.time + pushCooldown;
            playerState.RequestAttackAnimation(pushAnimation);
            PushEnemies();
        }

        private void PushEnemies()
        {
            Vector3 facing = controller.MovementAxis * controller.FacingSign;
            Vector3 origin = body != null ? body.bounds.center : transform.position;
            int count = Physics.OverlapCapsuleNonAlloc(origin, origin + facing * pushRange, pushRadius,
                overlapBuffer, enemyLayer, QueryTriggerInteraction.Ignore);

            pushedEnemies.Clear();
            for (int i = 0; i < count; i++)
            {
                var enemy = overlapBuffer[i].GetComponentInParent<EnemyCharacter>();
                if (enemy == null || !enemy.IsAlive || !pushedEnemies.Add(enemy))
                    continue;
                if (IsInFront(transform.position, facing, enemy.transform.position))
                    enemy.ApplyParryPush(facing, pushForce, PushUpwardForce, pushDuration);
            }
        }

        /// <summary>True when the target is on the side the player faces. The volume's round end reaches behind her.</summary>
        public static bool IsInFront(Vector3 from, Vector3 facing, Vector3 target) =>
            Vector3.Dot(target - from, facing) >= 0f;

        private void OnDrawGizmosSelected()
        {
            Character2D5Controller c = controller != null ? controller : GetComponent<Character2D5Controller>();
            if (c == null) return;

            Gizmos.color = Color.yellow;
            Vector3 start = transform.position;
            Gizmos.DrawWireSphere(start, pushRadius);
            Gizmos.DrawWireSphere(start + transform.right * (c.FacingSign * pushRange), pushRadius);
        }
    }
}
