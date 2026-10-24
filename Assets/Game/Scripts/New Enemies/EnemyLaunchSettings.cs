using UnityEngine;

namespace junklite
{
    /// <summary>
    /// How an enemy flies, bounces, lies down and gets up after a launching hit.
    /// Authored on the enemy (EnemyCharacter), read by LaunchedState.
    /// </summary>
    [System.Serializable]
    public sealed class EnemyLaunchSettings
    {
        [Header("Flight")]
        [Tooltip("How high the launch goes, in units above take-off (the feet), apex hang included. The launch speed is worked out from this, Gravity Scale and the hang, so the height stays put when you change them. 0 = use the upward knockback of the hit instead. With jump and double jump force 14 the player's sword reaches roughly 10 to 11 u at the top of a double jump (about 5 u on a single jump).")]
        [SerializeField, Min(0f)] private float apexHeight = 6.5f;
        [Tooltip("Near the top, while the body moves slower than this vertically, it floats on Apex Hang Gravity Scale: the window for air attacks. 0 = no hang.")]
        [SerializeField, Min(0f)] private float apexHangSpeed = 4f;
        [Tooltip("Gravity multiplier during the apex hang. 1.5 with a hang speed of 4 floats the body for ~0.5 s at the top.")]
        [SerializeField, Min(0.05f)] private float apexHangGravityScale = 1.5f;
        [Tooltip("Gravity multiplier while launched. Lower = floatier, more time for air attacks. The sword finisher (0.5, 22) at 4 goes ~6.2 u up in ~0.56 s and is back down in ~1.1 s, ~0.6 u sideways.")]
        [SerializeField, Min(1f)] private float gravityScale = 4f;
        [Tooltip("Launched bodies fly through other enemies instead of stopping against the grunt behind (they still hit walls and the floor).")]
        [SerializeField] private bool passThroughEnemies = true;
        [Tooltip("Stop waiting for the ground after this long (launched off a ledge).")]
        [SerializeField, Min(0.5f)] private float maxAirTime = 4f;

        [Header("Bounce")]
        [Tooltip("Off = the enemy drops straight into the lying pose on landing, no bounce at all.")]
        [SerializeField] private bool bounceEnabled = true;
        [Tooltip("Landing speed needed for the one bounce. Below it the enemy just drops.")]
        [SerializeField, Min(0f)] private float bounceMinSpeed = 3f;
        [Tooltip("Fraction of the landing speed sent back up. 0.08 = a barely visible hop (~0.03 u). Turn on Debug Log to see the real height in the Console.")]
        [SerializeField, Range(0f, 1f)] private float bounceRestitution = 0.08f;
        [Tooltip("Fraction of the horizontal speed kept through the bounce.")]
        [SerializeField, Range(0f, 1f)] private float bounceHorizontalKeep = 0.2f;
        [Tooltip("Gravity multiplier during the bounce. Same as the flight by default: a tiny, quick bounce. Lower it for a floatier bounce.")]
        [SerializeField, Min(1f)] private float bounceGravityScale = 8f;

        [Header("Downed")]
        [Tooltip("Min/max seconds lying on the ground before getting up.")]
        [SerializeField] private Vector2 downedDurationRange = new(0.8f, 1.2f);
        [Tooltip("Each hit on the ground keeps the enemy down this much longer.")]
        [SerializeField, Min(0f)] private float downedHitExtend = 0.2f;
        [Tooltip("Hard cap on the total time down, so ground hits cannot loop forever.")]
        [SerializeField, Min(0.1f)] private float downedMaxTotal = 2f;
        [Tooltip("Get-up: the body rises over this time. A hit here knocks it back down while Downed Max Total allows; after that the get-up has armor (damage, no reaction).")]
        [SerializeField, Min(0.05f)] private float getUpDuration = 0.45f;
        [Tooltip("How long a hit during the get-up puts the enemy back on the floor (counts toward Downed Max Total).")]
        [SerializeField, Min(0.05f)] private float knockDownAgainTime = 0.4f;

        [Header("Spike (air kick)")]
        [Tooltip("Gravity multiplier after a spike, so the drop is clearly faster than a normal fall.")]
        [SerializeField, Min(1f)] private float spikeGravityScale = 7f;
        [Tooltip("A spiked enemy does not bounce: it hits the floor and skids on with this fraction of its sideways speed (0 = stops dead).")]
        [SerializeField, Range(0f, 2f)] private float spikeSkidKeep = 1.5f;

        [Header("Juggle")]
        [Tooltip("Mid-air hits that keep the enemy up, per launch. Later hits only push it sideways.")]
        [SerializeField, Min(0)] private int maxJuggleHits = 3;
        [Tooltip("Upward speed a mid-air hit gives back before the first landing (~0.8 u and ~0.4 s more air at the default gravity). Hits after the landing keep it down instead.")]
        [SerializeField, Min(0f)] private float juggleLift = 8f;
        [Tooltip("How much of the horizontal knockback of a mid-air hit is applied.")]
        [SerializeField, Range(0f, 1f)] private float juggleHorizontalScale = 0.15f;

        [Header("Pose")]
        [Tooltip("Body tilt toward horizontal while airborne, in degrees (feet toward the attacker).")]
        [SerializeField, Range(0f, 90f)] private float airTilt = 75f;
        [SerializeField, Range(0f, 90f)] private float downedTilt = 90f;
        [Tooltip("Seconds to blend into the air and downed tilts.")]
        [SerializeField, Min(0.01f)] private float tiltBlendSeconds = 0.12f;

        [Header("Debug")]
        [Tooltip("Logs each launch phase (velocity, apex height, landing speed, bounce height) to the Console.")]
        [SerializeField] private bool debugLog = true;

        public float GravityScale => gravityScale;
        public float SpikeGravityScale => spikeGravityScale;
        public float SpikeSkidKeep => spikeSkidKeep;
        public float ApexHeight => apexHeight;
        public float ApexHangSpeed => apexHangSpeed;
        public float ApexHangGravityScale => apexHangGravityScale;

        /// <summary>
        /// Upward launch speed: exactly enough to reach <see cref="ApexHeight"/> under the
        /// launch gravity, or the hit's own upward knockback when Apex Height is 0.
        /// </summary>
        public float LaunchUpSpeed(float requestedUp)
        {
            if (apexHeight <= 0f)
                return requestedUp;

            float gravity = Physics.gravity.magnitude * Mathf.Max(1f, gravityScale);
            if (apexHangSpeed <= 0f)
                return Mathf.Sqrt(2f * gravity * apexHeight);

            // The last part of the climb (below the hang speed) uses the lighter hang
            // gravity, which adds height: take that out of the fast part.
            float hangGravity = Physics.gravity.magnitude * Mathf.Max(0.05f, apexHangGravityScale);
            float hangRise = apexHangSpeed * apexHangSpeed / (2f * hangGravity);
            float fastRise = Mathf.Max(0f, apexHeight - hangRise);
            return Mathf.Sqrt(2f * gravity * fastRise + apexHangSpeed * apexHangSpeed);
        }
        public bool BounceEnabled => bounceEnabled;
        public bool DebugLog => debugLog;
        public bool PassThroughEnemies => passThroughEnemies;
        public float BounceGravityScale => bounceGravityScale;
        public float MaxAirTime => maxAirTime;
        public float BounceMinSpeed => bounceMinSpeed;
        public float BounceRestitution => bounceRestitution;
        public float BounceHorizontalKeep => bounceHorizontalKeep;
        public Vector2 DownedDurationRange => downedDurationRange;
        public float DownedHitExtend => downedHitExtend;
        public float DownedMaxTotal => downedMaxTotal;
        public float GetUpDuration => getUpDuration;
        public float KnockDownAgainTime => knockDownAgainTime;
        public int MaxJuggleHits => maxJuggleHits;
        public float JuggleLift => juggleLift;
        public float JuggleHorizontalScale => juggleHorizontalScale;
        public float AirTilt => airTilt;
        public float DownedTilt => downedTilt;
        public float TiltBlendSeconds => tiltBlendSeconds;
    }
}
