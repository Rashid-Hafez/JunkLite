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
        [Tooltip("How high the launch goes, in units above take-off (the feet). The launch speed is worked out from this and the gravity curve, so the height stays put when you change them. 0 = use the upward knockback of the hit instead. With jump and double jump force 14 the player's sword reaches roughly 10 to 11 u at the top of a double jump (about 5 u on a single jump).")]
        [SerializeField, Min(0f)] private float apexHeight = 6.5f;
        [Tooltip("Gravity eases from Gravity Scale (at this vertical speed and above) to Apex Hang Gravity Scale (at the very top), rising and falling alike, so the arc is a smooth bell. Larger = a softer, longer top. 0 = constant Gravity Scale, a plain parabola.")]
        [SerializeField, Min(0f)] private float apexHangSpeed = 12f;
        [Tooltip("Gravity multiplier at the very top of the arc. Lower = the body lingers longer near the top.")]
        [SerializeField, Min(0.05f)] private float apexHangGravityScale = 1.2f;
        [Tooltip("Gravity multiplier while moving fast (Apex Hang Speed or faster). Lower = floatier, more time for air attacks. These defaults reach 6.5 u in ~0.86 s. 4 u with 9, hang 4 and hang speed 10 takes ~0.35 s.")]
        [SerializeField, Min(1f)] private float gravityScale = 3f;
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
        [Tooltip("Most upward speed a mid-air hit gives back before the first landing, less near the top (see Juggle Apex Headroom). Hits after the landing keep it down instead.")]
        [SerializeField, Min(0f)] private float juggleLift = 8f;
        [Tooltip("How far above Apex Height juggle hits can carry the body, in units. Hits near the top only add what is left of this, so several hits at the apex cannot stack the enemy out of reach. They still earn the slow fall.")]
        [SerializeField, Min(0f)] private float juggleApexHeadroom = 0.5f;
        [Tooltip("Seconds a mid-air hit takes to ease the body into its lift, so the hit does not snap the velocity. 0 = instant.")]
        [SerializeField, Min(0f)] private float juggleLiftBlendSeconds = 0.06f;
        [Tooltip("How much of the horizontal knockback of a mid-air hit is applied.")]
        [SerializeField, Range(0f, 1f)] private float juggleHorizontalScale = 0.15f;
        [Tooltip("After a mid-air hit the enemy falls slowly for this long once it starts to fall: the extra air time you earn by hitting it. The last half fades back into the arc. 0 = no slow fall. The player gets the same (Weapon Manager > Air Hit Hang).")]
        [SerializeField, Min(0f)] private float airHitHangDuration = 0.5f;
        [Tooltip("Gravity multiplier during the slow fall. 1.5 ≈ the player's air-hit hang (0.22 of its ~6.9 g fall), so both drift down together.")]
        [SerializeField, Min(0.05f)] private float airHitFallGravityScale = 1.5f;

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
        public LaunchArc.Shape Arc => new(Physics.gravity.magnitude, apexHangGravityScale, gravityScale, apexHangSpeed);

        /// <summary>
        /// Upward launch speed: exactly enough to reach <see cref="ApexHeight"/> on the
        /// launch arc, or the hit's own upward knockback when Apex Height is 0.
        /// </summary>
        public float LaunchUpSpeed(float requestedUp)
        {
            if (apexHeight <= 0f)
                return requestedUp;

            return LaunchArc.LaunchSpeedForHeight(apexHeight, Arc, Time.fixedDeltaTime);
        }

        /// <summary>Height above take-off that a launch at this upward speed tops out at.</summary>
        public float ApexRise(float launchUp) =>
            apexHeight > 0f ? apexHeight : LaunchArc.RiseFromSpeed(launchUp, Arc);

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
        public float JuggleApexHeadroom => juggleApexHeadroom;
        public float JuggleLiftBlendSeconds => juggleLiftBlendSeconds;
        public float JuggleHorizontalScale => juggleHorizontalScale;
        public float AirHitHangDuration => airHitHangDuration;
        public float AirHitFallGravityScale => airHitFallGravityScale;
        public float AirTilt => airTilt;
        public float DownedTilt => downedTilt;
        public float TiltBlendSeconds => tiltBlendSeconds;
    }
}
