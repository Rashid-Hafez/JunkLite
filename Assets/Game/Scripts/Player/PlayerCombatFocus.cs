using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Soft lock-on for melee. WeaponManager asks for a focus at the start of each side swing,
    /// toward the side the player is attacking (input, else facing). The focus sticks while it stays
    /// valid on that side, chains to the next enemy when it dies, and is dropped when the player
    /// attacks the other way. Never turns the player around. Owns only selection and the marker;
    /// the lunge itself is warped by the weapon code.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Character2D5Controller))]
    public sealed class PlayerCombatFocus : MonoBehaviour
    {
        [Header("Selection")]
        [Tooltip("Furthest gap (player centre to the enemy's near side) a swing will focus and close.")]
        [SerializeField, Min(0f)] private float range = 4.5f;
        [Tooltip("Furthest an enemy may stand off the lane line (into/out of the screen).")]
        [SerializeField, Min(0f)] private float laneTolerance = 1.25f;
        [SerializeField, Min(0f)] private float maxAbove = 2.5f;
        [SerializeField, Min(0f)] private float maxBelow = 1.5f;
        [Tooltip("Another enemy must be this much closer than the current focus to take it.")]
        [SerializeField, Min(0f)] private float switchMargin = 0.75f;
        [Tooltip("Enemies overlapping the player (centre up to this far behind) still count as in front.")]
        [SerializeField, Min(0f)] private float behindTolerance = 0.25f;
        [Tooltip("Walls on these layers block focus (no locking through walls).")]
        [SerializeField] private LayerMask lineOfSightBlockers = (1 << 11) | (1 << 12); // Ground, Wall

        [Header("Lock")]
        [Tooltip("Seconds the focus (and marker) stays after the last swing that used it.")]
        [SerializeField, Min(0f)] private float holdTime = 1.5f;

        [Header("Gap Close")]
        [Tooltip("Top lunge speed when a swing closes a gap longer than its authored lunge.")]
        [SerializeField, Min(1f)] private float gapCloseMaxSpeed = 24f;

        [Header("Marker")]
        [Tooltip("Optional. Authored marker prefab shown over the focus. Leave empty for no marker.")]
        [SerializeField] private FocusMarker markerPrefab;

        private Character2D5Controller controller;
        private Collider body;
        private PlayerState state;
        private FocusMarker marker;
        private EnemyCharacter current;
        private Collider currentCollider;
        private float currentSide;
        private float releaseTime;

        private readonly List<EnemyCharacter> candidates = new();
        private readonly List<Collider> candidateColliders = new();
        private float[] gaps = new float[16];

        public EnemyCharacter Current => current;
        public Collider CurrentCollider => currentCollider;
        public float GapCloseMaxSpeed => gapCloseMaxSpeed;

        private CombatFocusQuery Query => new(range, laneTolerance, maxAbove, maxBelow, switchMargin, behindTolerance);

        private void Awake()
        {
            controller = GetComponent<Character2D5Controller>();
            body = GetComponent<Collider>();
            state = GetComponent<PlayerState>();
        }

        private void OnEnable()
        {
            if (state != null) state.OnDeath += Clear;
        }

        private void OnDisable()
        {
            if (state != null) state.OnDeath -= Clear;
            Clear();
        }

        private void OnDestroy()
        {
            if (marker != null) Destroy(marker.gameObject);
        }

        /// <summary>
        /// Picks the focus for a swing toward <paramref name="side"/> (+1/-1 along the lane) and returns
        /// it with its current gap, or null. Refreshes the lock timer.
        /// </summary>
        public EnemyCharacter Acquire(float side, out float gap)
        {
            gap = float.MaxValue;
            side = Mathf.Sign(side);
            Vector3 origin = Origin;
            Vector3 axis = controller.MovementAxis;
            CombatFocusQuery query = Query;

            candidates.Clear();
            candidateColliders.Clear();
            int currentIndex = -1;
            IReadOnlyList<EnemyCharacter> enemies = EnemyCharacter.ActiveEnemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyCharacter enemy = enemies[i];
                if (!IsTargetable(enemy, out Collider enemyCollider)) continue;
                if (enemy == current && currentSide == side) currentIndex = candidates.Count;
                candidates.Add(enemy);
                candidateColliders.Add(enemyCollider);
            }

            if (gaps.Length < candidates.Count) gaps = new float[Mathf.NextPowerOfTwo(candidates.Count)];
            for (int i = 0; i < candidates.Count; i++)
            {
                Collider c = candidateColliders[i];
                gaps[i] = CombatFocusSelector.TryGetGap(origin, axis, side, c.bounds.center, HalfWidth(c, axis), query, out float g) &&
                          HasLineOfSight(origin, c)
                    ? g : float.MaxValue;
            }

            int chosen = CombatFocusSelector.Choose(gaps, candidates.Count, currentIndex, query);
            if (chosen < 0)
            {
                Clear();
                return null;
            }

            SetCurrent(candidates[chosen], candidateColliders[chosen], side);
            gap = gaps[chosen];
            return current;
        }

        /// <summary>Gap from the player to the current focus along <paramref name="side"/>, if it is still valid.</summary>
        public bool TryGetCurrentGap(float side, out float gap)
        {
            gap = float.MaxValue;
            if (current == null || currentCollider == null || !IsTargetable(current, out _)) return false;
            Vector3 axis = controller.MovementAxis;
            // The live check during a lunge ignores range: a target backing off is still the target.
            var query = new CombatFocusQuery(float.MaxValue, laneTolerance, maxAbove, maxBelow, 0f, behindTolerance);
            return CombatFocusSelector.TryGetGap(Origin, axis, Mathf.Sign(side), currentCollider.bounds.center,
                HalfWidth(currentCollider, axis), query, out gap);
        }

        public void Clear()
        {
            current = null;
            currentCollider = null;
            currentSide = 0f;
            if (marker != null) marker.Hide();
        }

        private void Update()
        {
            if (current == null) return;
            if (Time.time > releaseTime || !IsTargetable(current, out _) ||
                (state != null && !state.IsAlive))
                Clear();
        }

        private void SetCurrent(EnemyCharacter enemy, Collider enemyCollider, float side)
        {
            current = enemy;
            currentCollider = enemyCollider;
            currentSide = side;
            releaseTime = Time.time + holdTime;
            if (EnsureMarker()) marker.Show(enemyCollider);
        }

        private bool EnsureMarker()
        {
            if (marker != null) return true;
            if (markerPrefab == null) return false;
            marker = Instantiate(markerPrefab);
            marker.gameObject.name = markerPrefab.name;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(marker.gameObject, gameObject.scene);
            marker.Hide();
            return true;
        }

        private Vector3 Origin => body != null ? body.bounds.center : transform.position;

        private static bool IsTargetable(EnemyCharacter enemy, out Collider enemyCollider)
        {
            enemyCollider = null;
            if (enemy == null || !enemy.isActiveAndEnabled || !enemy.IsAlive) return false;
            if (!enemy.TryGetComponent(out enemyCollider) || !enemyCollider.enabled) return false;
            return true;
        }

        private static float HalfWidth(Collider c, Vector3 axis)
        {
            Vector3 e = c.bounds.extents;
            return Mathf.Abs(axis.x) * e.x + Mathf.Abs(axis.z) * e.z;
        }

        private bool HasLineOfSight(Vector3 origin, Collider target)
        {
            if (lineOfSightBlockers.value == 0) return true;
            // Level ray at the player's chest height: catches walls and ledges between the two
            // without the floor under an airborne target counting as a blocker.
            Vector3 to = target.bounds.center;
            to.y = origin.y;
            return !Physics.Linecast(origin, to, out RaycastHit hit, lineOfSightBlockers, QueryTriggerInteraction.Ignore) ||
                   hit.collider == target;
        }
    }
}
