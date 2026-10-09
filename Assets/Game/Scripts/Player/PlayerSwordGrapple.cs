using System;
using UnityEngine;

namespace junklite
{
    public enum SwordGrapplePhase { Idle, Aiming, Flying, Pulling, Holding, ImpactDelay }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Character2D5Controller), typeof(CapsuleCollider))]
    public sealed class PlayerSwordGrapple : MonoBehaviour
    {
        [SerializeField] private SwordGrappleSettings settings;
        [Tooltip("Player-specific visual prefab. Falls back to the settings asset when unassigned.")]
        [SerializeField] private SwordGrappleVisuals visualsPrefab;
        private Character2D5Controller controller;
        private CapsuleCollider capsule;
        private Rigidbody body;
        private PlayerState state;
        private PlayerWeaponLoadout loadout;
        private WeaponManager weapons;
        private GameInputManager input;
        private Camera viewCamera;
        private SwordGrappleVisuals visuals;
        private WeaponInstance sword;
        private IDisposable movementLease;
        private IDisposable physicsLease;
        private SwordGrappleTarget target;
        private SwordGrappleTarget heldTarget;
        private Matrix4x4 targetMatrix;
        private Matrix4x4 heldMatrix;
        private Vector3 planeAxis;
        private Vector3 swordPosition;
        private Vector3 previousSwordPosition;
        private Vector3 heldPosition;
        private Vector3 aimDirection;
        private float deadline;
        private float impactDelayRemaining;
        private bool holding;
        private bool aimHeld;
        private int consumedFrame = -1;
        private bool reportedVisualError;

        public SwordGrapplePhase Phase { get; private set; }
        public bool IsActive => Phase != SwordGrapplePhase.Idle;
        public bool IsHolding => holding;
        public bool HasValidAim { get; private set; }
        public string AimFailure { get; private set; }
        public Collider AimHitCollider { get; private set; }
        public Vector3 PreviewEnd { get; private set; }
        public SwordGrappleSettings Settings => settings;
        public event Action<SwordGrapplePhase> PhaseChanged;

        private Vector3 VisualOrigin => transform.TransformPoint(capsule.center) +
                                       Vector3.up * settings.handHeightAboveCenter;
        private Vector3 Origin => VisualOrigin + body.position - transform.position;
        private int Blockers => settings.obstructionLayers | settings.attachableLayers;

        private void Awake()
        {
            controller = GetComponent<Character2D5Controller>();
            capsule = GetComponent<CapsuleCollider>();
            body = GetComponent<Rigidbody>();
            state = GetComponent<PlayerState>();
            loadout = GetComponent<PlayerWeaponLoadout>();
            weapons = GetComponent<WeaponManager>();
            controller.ConfigureWallTraversal(this);
        }

        private void OnEnable()
        {
            controller.ExternalRepositioned += Cancel;
            if (loadout != null) loadout.WeaponChanged += OnWeaponChanged;
            if (weapons != null) weapons.OnCombatModeChanged += OnWeaponChanged;
            if (state != null)
            {
                state.OnDeath += Cancel;
                state.OnStunnedChanged += OnStunned;
            }
            BindInput();
        }

        private void OnDisable()
        {
            UnbindInput();
            if (controller != null) controller.ExternalRepositioned -= Cancel;
            if (loadout != null) loadout.WeaponChanged -= OnWeaponChanged;
            if (weapons != null) weapons.OnCombatModeChanged -= OnWeaponChanged;
            if (state != null)
            {
                state.OnDeath -= Cancel;
                state.OnStunnedChanged -= OnStunned;
            }
            Cancel();
        }

        private void OnDestroy()
        {
            if (visuals != null) visuals.Dispose();
        }

        private void BindInput()
        {
            if (input == GameInputManager.Instance) return;
            UnbindInput();
            input = GameInputManager.Instance;
            if (input == null) return;
            input.OnGrappleAim += BeginAim;
            input.OnGrappleAimReleased += EndAim;
            input.PrimaryAttackConsumer = ConsumePrimaryAttack;
        }

        private void UnbindInput()
        {
            if (input == null) return;
            input.OnGrappleAim -= BeginAim;
            input.OnGrappleAimReleased -= EndAim;
            if (input.PrimaryAttackConsumer == ConsumePrimaryAttack) input.PrimaryAttackConsumer = null;
            input = null;
        }

        public bool HasUsableSword()
        {
            return weapons != null && weapons.IsModCombat &&
                   (IsEquippedSword(loadout != null ? loadout.WeaponSlot1 : null) ||
                    IsEquippedSword(loadout != null ? loadout.WeaponSlot2 : null));
        }

        private bool IsEquippedSword(WeaponInstance weapon) => loadout != null && weapon != null &&
            weapon.isActiveAndEnabled && weapon.transform.IsChildOf(transform) &&
            (loadout.WeaponSlot1 == weapon || loadout.WeaponSlot2 == weapon) &&
            weapon.weaponData is MeleeWeaponData && weapon.weaponData.enablesSwordGrapple && !weapon.IsBroken;

        private bool CanContinue()
        {
            return settings != null && state != null && state.IsAlive && !state.IsStunned &&
                   !state.IsInputLocked && !state.IsParrying && !state.IsAttacking && !state.IsRolling && !state.IsDashing &&
                   !state.IsActionBlocked(StatusActionBlock.Move) && controller.IsLocomotionEnabled &&
                   !body.isKinematic && controller.MovementLockCount <= (movementLease != null ? 1 : 0) &&
                   controller.PhysicsOverrideCount <= (physicsLease != null ? 1 : 0) &&
                   weapons != null && weapons.IsModCombat && IsEquippedSword(sword);
        }

        public void BeginAim()
        {
            if (settings == null || Time.timeScale <= 0f) return;
            aimHeld = true;
            if (Phase == SwordGrapplePhase.Aiming) return;
            if (Phase != SwordGrapplePhase.Idle && Phase != SwordGrapplePhase.Holding) return;
            if (!IsActive)
            {
                sword = IsEquippedSword(loadout?.WeaponSlot1) ? loadout.WeaponSlot1 : loadout?.WeaponSlot2;
                if (!CanContinue() || controller.IsDashing) { sword = null; return; }
                planeAxis = controller.MovementAxis;
            }
            viewCamera = Camera.main;
            if (viewCamera == null) { if (!holding) Cancel(); return; }
            if (!EnsureVisuals()) { Cancel(); return; }
            weapons?.CancelBufferedAttack();
            aimDirection = planeAxis * (controller.IsFacingRight ? 1f : -1f);
            SetPhase(SwordGrapplePhase.Aiming);
            RefreshAim();
        }

        private bool EnsureVisuals()
        {
            if (visuals == null || !visuals.HasRequiredRenderers)
            {
                if (visuals != null) visuals.Dispose();
                visuals = null;
                var prefab = visualsPrefab != null ? visualsPrefab : settings != null ? settings.visualsPrefab : null;
                if (prefab == null || !prefab.HasRequiredRenderers)
                {
                    if (!reportedVisualError)
                    {
                        Debug.LogError("[Sword Grapple] Assign a visual prefab with Aim, Target, Rope and Sword renderers on the player or its grapple settings. Grappling is blocked until presentation is configured.", this);
                        reportedVisualError = true;
                    }
                    return false;
                }
                visuals = Instantiate(prefab);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(visuals.gameObject, gameObject.scene);
                visuals.PrepareForUse();
                visuals.Hide();
                if (sword != null) visuals.SetSword(sword.weaponData.icon);
                reportedVisualError = false;
            }
            // Recover after a prefab reload or an external disable during play.
            if (!visuals.gameObject.activeSelf) visuals.gameObject.SetActive(true);
            return true;
        }

        public void EndAim()
        {
            aimHeld = false;
            visuals?.HideAim();
            // Releasing Ctrl hides the preview, but does not recall a fired sword
            // or drop the player from an established wall hold.
            if (Phase != SwordGrapplePhase.Aiming) return;
            HasValidAim = false;
            if (holding) SetPhase(SwordGrapplePhase.Holding);
            else Cancel();
        }

        // Both legacy LMB actions pass here. Consume even invalid clicks, without
        // allowing the same click to become a melee attack or a second throw.
        public bool ConsumePrimaryAttack()
        {
            if (consumedFrame == Time.frameCount) return true;
            if (!IsActive) return false;
            // Input callbacks can arrive before Update after reaching a wall.
            ResumeHeldAim();
            consumedFrame = Time.frameCount;
            if (Phase == SwordGrapplePhase.Aiming && aimHeld && Time.timeScale > 0f && CanContinue())
            {
                // Resolve the current pointer on the click. A cached Update preview
                // can be stale when input arrives before the next render/physics step.
                bool pointerValid = input == null || RefreshAim();
                if (pointerValid && ResolveAim(out var chosenTarget, out _))
                {
                    target = chosenTarget;
                    targetMatrix = target.Collider.transform.localToWorldMatrix;
                    previousSwordPosition = swordPosition = Origin;
                    deadline = Time.time + settings.maximumRange / Mathf.Max(1f, settings.throwSpeed) + 0.5f;
                    loadout.SetGrappleWeaponHidden(sword);
                    visuals?.SetSword(sword.weaponData.icon);
                    visuals?.HideAim();
                    SetPhase(SwordGrapplePhase.Flying);
                }
#if UNITY_EDITOR
                else
                {
                    string hitDescription = AimHitCollider != null
                        ? $" Hit '{AimHitCollider.name}' on layer '{LayerMask.LayerToName(AimHitCollider.gameObject.layer)}'."
                        : $" Maximum range: {settings.maximumRange:0.##}.";
                    Debug.LogWarning($"[Sword Grapple] Cannot throw: {AimFailure}{hitDescription}", this);
                }
#endif
            }
            return true;
        }

        public bool ReleaseFromJump()
        {
            if (!IsActive) return false;
            if (state == null || !state.CanJump || Time.timeScale <= 0f) return true;
            bool jumpFromWall = holding && CanContinue() && AnchorValid(heldTarget, heldMatrix);
            Vector3 normal = heldTarget.Normal;
            Cancel();
            // A held wall grants an actual push-off, even after the air jump was
            // spent. In-flight cancellation falls through to the normal jump.
            return jumpFromWall && controller.JumpFromWall(normal);
        }

        private bool PressingIntoWall(Vector3 normal)
        {
            float horizontal = input != null ? input.MoveDirection.x : controller.HorizontalInput;
            return horizontal * -Vector3.Dot(normal, planeAxis) > 0.1f;
        }

        private void ReleaseDirectionalHold()
        {
            // A fired sword can finish its flight while the old wall is released.
            if (Phase != SwordGrapplePhase.Flying && Phase != SwordGrapplePhase.ImpactDelay)
            { Cancel(); return; }
            holding = false;
            physicsLease?.Dispose();
            movementLease?.Dispose();
            physicsLease = movementLease = null;
            state.SetGrappleState(true, false);
        }

        private void ResumeHeldAim()
        {
            // A completed throw returns to Holding without another Ctrl press.
            // Read the action's current state instead of relying on a new edge
            // or the cached aim flag from the previous throw.
            if (Phase == SwordGrapplePhase.Holding && input != null && Time.timeScale > 0f &&
                input.IsGameplayInputEnabled && input.IsGrappleAimHeld &&
                input.controls.Player.GrappleAim.enabled && CanContinue() &&
                AnchorValid(heldTarget, heldMatrix) && PressingIntoWall(heldTarget.Normal))
                BeginAim();
        }

        private void Update()
        {
            BindInput();
            if (!IsActive) return;
            if (!CanContinue() || Mathf.Abs(Vector3.Dot(planeAxis, controller.MovementAxis)) < 0.999f)
            { Cancel(); return; }
            if (Time.timeScale <= 0f) return;
            if (input == null || !input.IsGameplayInputEnabled || !input.controls.Player.GrappleAim.enabled)
            { Cancel(); return; }
            if (holding && !PressingIntoWall(heldTarget.Normal)) ReleaseDirectionalHold();
            ResumeHeldAim();
            if (Phase == SwordGrapplePhase.Aiming)
            {
                if (!input.IsGrappleAimHeld) EndAim();
                else RefreshAim();
            }
        }

        private bool RefreshAim()
        {
            HasValidAim = false;
            PreviewEnd = Origin;
            AimHitCollider = null;
            AimFailure = "The aim camera or pointer is unavailable.";
            if (viewCamera == null || input == null) return false;
            Vector3 normal = Vector3.Cross(planeAxis, Vector3.up).normalized;
            Ray ray = viewCamera.ScreenPointToRay(input.GrapplePointerPosition);
            var plane = new Plane(normal, Origin);
            AimFailure = "The pointer does not intersect the player's movement plane.";
            if (!plane.Raycast(ray, out float distance)) return false;
            aimDirection = ray.GetPoint(distance) - Origin;
            HasValidAim = ResolveAim(out _, out Vector3 end);
            PreviewEnd = end;
            return true;
        }

        private bool ResolveAim(out SwordGrappleTarget result, out Vector3 end)
        {
            bool valid = SwordGrappleTargeting.TryResolve(capsule, planeAxis, aimDirection,
                settings, out result, out end, out string reason);
            AimHitCollider = result.Collider;
            if (valid && holding && Vector3.Distance(result.Point, heldTarget.Point) < settings.minimumChainDistance)
            {
                valid = false;
                reason = "Aim farther from the current wall anchor.";
            }
            AimFailure = reason;
            return valid;
        }

        private void FixedUpdate()
        {
            if (!IsActive || Time.timeScale <= 0f) return;
            if (!CanContinue()) { Cancel(); return; }
            if (holding && !AnchorValid(heldTarget, heldMatrix)) { Cancel(); return; }
            if (holding && !PressingIntoWall(heldTarget.Normal)) ReleaseDirectionalHold();
            if (!IsActive) return;

            if (Phase == SwordGrapplePhase.Flying) TickFlight();
            else if (Phase == SwordGrapplePhase.ImpactDelay) TickImpactDelay();
            if (Phase == SwordGrapplePhase.Pulling) TickPull();
            else if (holding) MoveSafely(heldPosition, settings.pullSpeed);
        }

        private void TickFlight()
        {
            if (!AnchorValid(target, targetMatrix) || Time.time > deadline)
            { ReturnFromFailedThrow(); return; }
            Vector3 delta = target.Point - swordPosition;
            float step = Mathf.Min(delta.magnitude, settings.throwSpeed * Time.fixedDeltaTime);
            if (SwordGrappleTargeting.TryRaycast(swordPosition, delta.normalized, step + 0.01f,
                    settings, out RaycastHit hit) && hit.collider != target.Collider)
            { ReturnFromFailedThrow(); return; }
            previousSwordPosition = swordPosition;
            swordPosition = Vector3.MoveTowards(swordPosition, target.Point, step);
            if (Vector3.Distance(swordPosition, target.Point) > 0.01f) return;

            previousSwordPosition = swordPosition = target.Point;
            impactDelayRemaining = Mathf.Max(0f, settings.impactToPullDelay);
            if (impactDelayRemaining <= 0f) BeginPull();
            else SetPhase(SwordGrapplePhase.ImpactDelay);
        }

        private void TickImpactDelay()
        {
            if (!AnchorValid(target, targetMatrix)) { ReturnFromFailedThrow(); return; }
            // Fixed time respects pause/time scale. Do not count the impact's
            // physics tick, so the configured delay starts after arrival.
            impactDelayRemaining -= Time.fixedDeltaTime;
            if (impactDelayRemaining <= 0.00001f) BeginPull();
        }

        private void BeginPull()
        {
            // Revalidate from the player's current position: they may have moved
            // during flight/the impact delay, or a blocker may have entered the route.
            aimDirection = target.Point - Origin;
            if (!SwordGrappleTargeting.TryResolve(capsule, planeAxis, aimDirection, settings,
                    out var refreshed, out _) || refreshed.Collider != target.Collider)
            { ReturnFromFailedThrow(); return; }

            target = refreshed;
            AcquireMotion();
            holding = false;
            controller.FaceForTraversal(Vector3.Dot(target.Point - Origin, planeAxis) >= 0f);
            // Facing can mirror an offset capsule. Preserve its world center and
            // translate the root destination by the same amount.
            aimDirection = target.Point - Origin;
            if (SwordGrappleTargeting.TryResolve(capsule, planeAxis, aimDirection, settings, out refreshed, out _) &&
                refreshed.Collider == target.Collider) target = refreshed;
            else { Cancel(); return; }
            deadline = Time.time + settings.maximumPullDuration;
            SetPhase(SwordGrapplePhase.Pulling);
        }

        private void TickPull()
        {
            if (!AnchorValid(target, targetMatrix) || Time.time > deadline)
            { Cancel(); return; }
            float distance = Vector3.Distance(body.position, target.PlayerPosition);
            if (distance <= settings.arrivalDistance)
            {
                controller.SetVelocity(Vector3.zero);
                if (!PressingIntoWall(target.Normal)) { Cancel(); return; }
                heldTarget = target;
                heldMatrix = targetMatrix;
                // Arrival tolerance starts the pose; it must not become a
                // permanent extra gap. Continue swept movement to the anchor.
                heldPosition = target.PlayerPosition;
                holding = true;
                loadout.SetGrappleWeaponHidden(null);
                SetPhase(SwordGrapplePhase.Holding);
                return;
            }
            MoveSafely(target.PlayerPosition, settings.pullSpeed);
        }

        private void MoveSafely(Vector3 destination, float speed)
        {
            Vector3 delta = destination - body.position;
            if (delta.sqrMagnitude < 0.000001f) { controller.SetVelocity(Vector3.zero); return; }
            // Full configured speed from the first step; only truncate the final
            // step to avoid overshooting, or shorten it for a real obstruction.
            Vector3 step = delta.normalized * Mathf.Min(speed * Time.fixedDeltaTime, delta.magnitude);
            SwordGrappleTargeting.GetCapsule(capsule, body.position, out var top, out var bottom, out float radius);
            if (Physics.CapsuleCast(top, bottom, radius, step.normalized, out RaycastHit hit,
                    step.magnitude + 0.01f, Blockers, QueryTriggerInteraction.Ignore))
            {
                step = step.normalized * Mathf.Max(0f, hit.distance - 0.01f);
                if (step.sqrMagnitude < 0.000001f) { Cancel(); return; }
            }
            controller.SetVelocity(step / Time.fixedDeltaTime);
        }

        private static bool AnchorValid(SwordGrappleTarget anchor, Matrix4x4 matrix) =>
            anchor.Collider != null && anchor.Collider.enabled && anchor.Collider.gameObject.activeInHierarchy &&
            anchor.Collider.transform.localToWorldMatrix == matrix;

        private void AcquireMotion()
        {
            if (physicsLease != null) return;
            movementLease = controller.AcquireMovementLock();
            physicsLease = controller.AcquirePhysicsOverride();
        }

        private void ReturnFromFailedThrow()
        {
            loadout?.SetGrappleWeaponHidden(null);
            if (holding && AnchorValid(heldTarget, heldMatrix)) SetPhase(SwordGrapplePhase.Holding);
            else Cancel();
        }

        private void SetPhase(SwordGrapplePhase phase)
        {
            Phase = phase;
            state?.SetGrappleState(IsActive, holding);
            PhaseChanged?.Invoke(phase);
        }

        private void OnStunned(bool stunned)
        {
            if (stunned) Cancel();
        }

        private void OnWeaponChanged()
        {
            if (IsActive && !CanContinue()) Cancel();
        }

        public void Cancel()
        {
            // Release only our leases. Never clear another ability's locks or
            // overwrite knockback when death/stun/grab interrupts this traversal.
            aimHeld = false;
            holding = false;
            impactDelayRemaining = 0f;
            HasValidAim = false;
            loadout?.SetGrappleWeaponHidden(null);
            physicsLease?.Dispose();
            movementLease?.Dispose();
            physicsLease = movementLease = null;
            sword = null;
            visuals?.Hide();
            if (Phase != SwordGrapplePhase.Idle) SetPhase(SwordGrapplePhase.Idle);
        }

        private void LateUpdate()
        {
            if (IsActive && !EnsureVisuals()) { Cancel(); return; }
            if (visuals == null) return;
            bool aiming = aimHeld && Phase == SwordGrapplePhase.Aiming && Time.timeScale > 0f;
            bool deployed = Phase == SwordGrapplePhase.Flying || Phase == SwordGrapplePhase.ImpactDelay ||
                            Phase == SwordGrapplePhase.Pulling;
            float interpolation = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            Vector3 displayedSword = Phase == SwordGrapplePhase.Flying
                ? Vector3.Lerp(previousSwordPosition, swordPosition, interpolation) : swordPosition;
            visuals.Render(VisualOrigin, PreviewEnd, planeAxis, HasValidAim, aiming,
                displayedSword, target.Point, deployed, viewCamera);
        }
    }
}
