using UnityEngine;

namespace junklite
{
    /// <summary>
    /// The brawler launch: a launching hit throws the enemy up and back, it tilts
    /// toward horizontal, lands, bounces once, lies down for a beat and gets up.
    /// Mid-air hits keep it up (juggle, capped); ground hits keep it down (capped);
    /// the get-up is the punish window (a hit there is a normal flinch).
    ///
    /// Physics stays dynamic the whole time (EnemyMovement.Launch); the state reacts to
    /// landings and sets the gravity of the arc (LaunchArc) each physics step.
    /// Presentation goes through the animation presenter.
    /// Entered through EnemyCharacter.TryBeginLaunch, which stores the launch velocity.
    /// </summary>
    public sealed class LaunchedState : EnemyStateBase
    {
        private enum Phase
        {
            Air,
            Downed,
            GetUp
        }

        private const float MinBounceTime = 0.04f;
        // EnemyMovement uses the hang gravity while the body is slower than the hang speed.
        // This speed makes that always true, so the scale set each step is the gravity in play.
        private const float AlwaysHang = float.MaxValue;

        private float startY;
        private float apexY;
        // Flying the arc: off once a spike or bounce sets its own gravity.
        private bool followsArc;
        private LaunchArc.Shape arc;
        private float ceilingY;
        private float fallHangLeft;
        private float liftTarget;
        private float liftBlendLeft;

        private void Log(string message)
        {
            if (settings != null && settings.DebugLog)
                Debug.Log($"[Launch] {enemy.name} t={Time.time:0.00}: {message}", enemy);
        }

        private EnemyMovement movement;
        private EnemyLaunchSettings settings;
        private Phase phase;
        private float timer;
        private float airTime;
        private float downedTotal;
        private int juggleCount;
        private bool bounced;
        private bool spiked;
        private bool finished;

        public LaunchedState(EnemyCharacter enemy) : base(enemy) { }

        public override bool CanTakeDamage => true;

        /// <summary>
        /// The launch owns every hit from take-off to standing: juggles in the air,
        /// staying down on the ground, and being knocked back down while rising.
        /// A normal flinch here would snap the body upright.
        /// </summary>
        public override bool CanBeInterrupted => false;

        public bool IsAirborne => phase == Phase.Air;
        public bool IsDowned => phase == Phase.Downed;

        public override void Enter()
        {
            movement = enemy.Movement;
            settings = enemy.LaunchSettings;
            finished = false;
            bounced = false;
            spiked = false;
            followsArc = false;
            juggleCount = 0;
            airTime = 0f;
            downedTotal = 0f;
            fallHangLeft = 0f;
            liftBlendLeft = 0f;

            if (movement == null || settings == null
                || !enemy.TryConsumePendingLaunch(out Vector3 velocity, out GameObject source))
            {
                Debug.LogWarning($"{enemy.name}: LaunchedState entered without a pending launch.", enemy);
                Finish();
                return;
            }

            // Leaving the line: no lease, and the next grunt is promoted at once.
            EnemyEngagementDirector.ReleaseAttack(enemy);
            EnemyEngagementDirector.SetYielding(enemy, true);

            if (source != null)
                movement.FaceTarget(source.transform.position);

            if (settings.PassThroughEnemies)
                enemy.SetEnemyBodyPassThrough(true);

            // The drawn body is tilted and offset from the upright capsule: hits test it.
            enemy.AnimationPresenter?.SetVisualHurtboxActive(true);

            arc = settings.Arc;
            velocity.y = settings.LaunchUpSpeed(velocity.y);
            movement.Launch(velocity, settings.GravityScale);
            movement.SetLaunchHang(AlwaysHang, LaunchArc.GravityScaleAt(velocity.y, arc));
            followsArc = true;
            startY = Transform.position.y;
            apexY = startY;
            ceilingY = startY + settings.ApexRise(velocity.y) + settings.JuggleApexHeadroom;
            Log($"launched: velocity {velocity.x:0.0}, {velocity.y:0.0} u/s, predicted {LaunchArc.TimeToApex(velocity.y, arc):0.00} s to the top");
            BeginAir();
        }

        /// <summary>Flies the arc: eases in a juggle lift and sets this step's gravity.</summary>
        public override void FixedUpdate()
        {
            if (finished || phase != Phase.Air || !followsArc)
                return;

            float dt = Time.fixedDeltaTime;
            Vector3 velocity = movement.Velocity;

            if (liftBlendLeft > 0f)
            {
                velocity.y = Mathf.Lerp(velocity.y, liftTarget, dt / liftBlendLeft);
                liftBlendLeft -= dt;
                movement.SetLaunchVelocity(velocity);
            }

            float scale = LaunchArc.GravityScaleAt(velocity.y, arc);
            if (fallHangLeft > 0f && liftBlendLeft <= 0f && velocity.y <= 0f)
            {
                scale = LaunchArc.FallHangScale(scale, settings.AirHitFallGravityScale, fallHangLeft, settings.AirHitHangDuration);
                fallHangLeft -= dt;
            }

            movement.SetLaunchHang(AlwaysHang, scale);
        }

        public override void Update()
        {
            if (finished)
                return;

            switch (phase)
            {
                case Phase.Air:
                    airTime += Time.deltaTime;
                    apexY = Mathf.Max(apexY, Transform.position.y);
                    if (movement.TryConsumeLanding(out float impactSpeed))
                    {
                        Log($"landed: impact {impactSpeed:0.0} u/s, apex {apexY - startY:0.00} u above take-off, bounced before {bounced}, spiked {spiked}");
                        if (spiked)
                        {
                            // Spiked: no bounce, hit the floor and skid on along it.
                            Vector3 slide = movement.Velocity;
                            slide.y = 0f;
                            BeginDowned();
                            if (slide.sqrMagnitude > 0.01f && settings.SpikeSkidKeep > 0f)
                                movement.ApplyKnockback(slide * settings.SpikeSkidKeep);
                        }
                        else if (settings.BounceEnabled && !bounced && impactSpeed >= settings.BounceMinSpeed)
                            Bounce(impactSpeed);
                        else
                            BeginDowned();
                    }
                    else if (bounced && airTime >= MinBounceTime && movement.IsGrounded && movement.Velocity.y <= 0.01f)
                    {
                        // A bounce lower than the ground check's reach never leaves the
                        // ground as far as it can tell, so no second landing would come.
                        BeginDowned();
                    }
                    else if (airTime >= settings.MaxAirTime)
                    {
                        BeginDowned();
                    }
                    break;

                case Phase.Downed:
                    timer -= Time.deltaTime;
                    if (timer <= 0f)
                        BeginGetUp();
                    break;

                case Phase.GetUp:
                    timer -= Time.deltaTime;
                    if (timer <= 0f)
                        Finish();
                    break;
            }
        }

        public override void Exit()
        {
            finished = true;
            EnemyEngagementDirector.SetYielding(enemy, false);
            enemy.AnimationPresenter?.SetVisualHurtboxActive(false);

            if (enemy.IsAlive)
            {
                enemy.SetEnemyBodyPassThrough(false);
                movement?.EndLaunch();
                enemy.AnimationPresenter?.SetBodyTilt(0f, false, settings != null ? settings.TiltBlendSeconds : 0.12f);
            }
            else
            {
                // Killed mid-flight: keep flying, the death sequence waits for the ground.
                // Nothing sets the gravity from here on, so hand back one that needs no steering.
                if (followsArc)
                    movement?.SetLaunchHang(settings.ApexHangSpeed, settings.ApexHangGravityScale);
                movement?.ReleaseLaunchToPhysics();
            }
        }

        /// <summary>
        /// An applied hit while airborne or downed. Called by EnemyCharacter instead
        /// of the normal knockback/hitstun path (which would end the launch).
        /// </summary>
        public void OnHitWhileLaunched(DamageRequest request, Vector3 launchVelocity)
        {
            if (finished || movement == null)
                return;

            switch (phase)
            {
                case Phase.Air:
                {
                    // Back on the floor (bouncing): a follow-up hit keeps it down instead
                    // of throwing it up again, which read as a huge bounce.
                    if (bounced)
                    {
                        BeginDowned();
                        break;
                    }

                    if (request.HitReaction.IsSpike)
                    {
                        // Air kick: knocked a little sideways and driven down fast, whether
                        // it was still rising or already falling. Ends the juggle.
                        Vector3 side = launchVelocity;
                        side.y = 0f;
                        float down = Mathf.Abs(request.KnockbackForce.y);
                        float fall = Mathf.Min(movement.Velocity.y, -down);
                        LeaveArc();
                        movement.SetLaunchGravityScale(settings.SpikeGravityScale);
                        movement.SetLaunchVelocity(side + Vector3.up * fall);
                        juggleCount = settings.MaxJuggleHits;
                        spiked = true;
                        Log($"spiked: sideways {side.magnitude:0.0} u/s, down {-fall:0.0} u/s");
                        break;
                    }

                    Vector3 horizontal = launchVelocity;
                    horizontal.y = 0f;
                    horizontal *= settings.JuggleHorizontalScale;
                    float currentUp = movement.Velocity.y;
                    Log($"hit in the air from {(request.Source != null ? request.Source.name : "?")}: rising at {currentUp:0.0} u/s, juggle {juggleCount + 1}/{settings.MaxJuggleHits}");

                    if (juggleCount < settings.MaxJuggleHits)
                    {
                        juggleCount++;
                        // A juggle only ever adds lift: a hit while the body is still
                        // climbing faster than the juggle lift must not slow the climb
                        // (it used to replace the launch speed and cut the arc short).
                        // The lift stops at the ceiling, so hits at the top keep it in reach.
                        liftTarget = LaunchArc.JuggleLiftSpeed(currentUp, settings.JuggleLift, ceilingY - Transform.position.y, arc);
                        liftBlendLeft = settings.JuggleLiftBlendSeconds;
                        movement.SetLaunchVelocity(horizontal + Vector3.up * (liftBlendLeft > 0f ? currentUp : liftTarget));
                        // The reward for hitting it in the air: it comes down slowly for a moment.
                        fallHangLeft = settings.AirHitHangDuration;
                        bounced = false;
                        airTime = 0f;
                    }
                    else
                    {
                        // Over the cap: the hit still shoves sideways; the arc is untouched.
                        movement.SetLaunchVelocity(horizontal + Vector3.up * currentUp);
                    }
                    break;
                }

                case Phase.Downed:
                {
                    float room = settings.DownedMaxTotal - downedTotal;
                    float extend = Mathf.Clamp(settings.DownedHitExtend, 0f, Mathf.Max(0f, room));
                    timer += extend;
                    downedTotal += extend;
                    break;
                }

                case Phase.GetUp:
                {
                    // Rising: knocked back down while the downed budget lasts. Once it
                    // is spent the get-up has armor (damage only), so nobody can be
                    // pinned to the floor forever.
                    float room = settings.DownedMaxTotal - downedTotal;
                    if (room <= 0.05f)
                        break;

                    float down = Mathf.Min(Mathf.Max(settings.DownedHitExtend, settings.KnockDownAgainTime), room);
                    downedTotal += down;
                    phase = Phase.Downed;
                    timer = down;
                    EnterLyingPose();
                    break;
                }
            }
        }

        // ------------------------------------------------------------

        private void BeginAir()
        {
            phase = Phase.Air;
            // Rest the tilted body on the capsule's feet even in the air (onGround),
            // so the sprite touches the floor at the same moment the physics body
            // does and the bounce happens on visual contact, not above the floor.
            enemy.AnimationPresenter?.PlayLaunched();
            enemy.AnimationPresenter?.SetBodyTilt(settings.AirTilt, true, settings.TiltBlendSeconds);
        }

        /// <summary>A spike or bounce takes over the gravity: plain launch gravity from here on.</summary>
        private void LeaveArc()
        {
            followsArc = false;
            fallHangLeft = 0f;
            liftBlendLeft = 0f;
            movement.SetLaunchHang(0f, 1f);
        }

        private void Bounce(float impactSpeed)
        {
            bounced = true;
            airTime = 0f;

            Vector3 velocity = movement.Velocity;
            velocity.y = 0f;
            velocity *= settings.BounceHorizontalKeep;
            velocity.y = impactSpeed * settings.BounceRestitution;
            LeaveArc();
            movement.SetLaunchGravityScale(settings.BounceGravityScale);
            movement.SetLaunchVelocity(velocity);
            apexY = Transform.position.y;
            startY = apexY;
            Log($"bounce: up {velocity.y:0.00} u/s (expected height {velocity.y * velocity.y / (2f * Physics.gravity.magnitude * settings.BounceGravityScale):0.000} u)");

            enemy.AnimationPresenter?.PlayLaunchBounce();
        }

        private void BeginDowned()
        {
            Log($"downed (bounce rose {apexY - startY:0.00} u)");
            phase = Phase.Downed;
            LeaveArc();
            movement.EndLaunch();
            movement.Stop();

            Vector2 range = settings.DownedDurationRange;
            timer = Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
            timer = Mathf.Min(timer, settings.DownedMaxTotal);
            downedTotal = timer;

            EnterLyingPose();
        }

        /// <summary>
        /// Lays the body down. Presenters with an authored fall (the grunt's Death
        /// clip, held before the dead face) rest exactly on the floor by themselves,
        /// so the flight's tilt hands over to it as the fall plays; otherwise the
        /// body is tilted flat.
        /// </summary>
        private void EnterLyingPose()
        {
            EnemyAnimationPresenter presenter = enemy.AnimationPresenter;
            if (presenter == null)
                return;

            if (presenter.PlayDowned())
                presenter.SetBodyTilt(0f, true, presenter.DownedFallSeconds);
            else
                presenter.SetBodyTilt(settings.DownedTilt, true, settings.TiltBlendSeconds);
        }

        private void BeginGetUp()
        {
            Log("get up");
            phase = Phase.GetUp;
            timer = settings.GetUpDuration;

            // The body rises over the whole get-up, not in a snap, and keeps facing the
            // way it fell; it turns to the player once it is standing.
            enemy.AnimationPresenter?.PlayGetUp(timer);
            enemy.AnimationPresenter?.SetBodyTilt(0f, true, timer);
        }

        private void Finish()
        {
            if (finished)
                return;

            finished = true;
            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);
            enemy.OnLaunchFinished();
        }
    }
}
