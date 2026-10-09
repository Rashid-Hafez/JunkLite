using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    public sealed class SwordGrappleTests
    {
        private readonly List<Object> cleanup = new();
        private readonly Vector3 testOrigin = new(5000f, 5000f, 5000f);
        private SwordGrappleSettings settings;
        private CapsuleCollider capsule;
        private GameInputManager testInput;
        private PlayerSwordGrapple boundGrapple;
        private InputSettings previousInputSettings;
        private HideFlags previousInputSettingsFlags;
        private InputSettings temporaryInputSettings;
        private readonly List<InputDevice> devices = new();

        [SetUp]
        public void SetUp()
        {
            testInput = null;
            boundGrapple = null;
            previousInputSettings = temporaryInputSettings = null;
            settings = Track(ScriptableObject.CreateInstance<SwordGrappleSettings>());
            var player = CreateObject("Grapple test player", Vector3.zero);
            player.layer = 16;
            capsule = player.AddComponent<CapsuleCollider>();
            capsule.radius = 0.35f;
            capsule.height = 2f;
            capsule.center = Vector3.up;
            settings.visualsPrefab = CreateVisualsTemplate();
        }

        [TearDown]
        public void TearDown()
        {
            if (boundGrapple != null) Invoke(boundGrapple, "OnDisable");
            if (testInput != null)
            {
                Invoke(testInput, "OnDisable");
                Object.DestroyImmediate(testInput.controls.asset);
            }
            foreach (var device in devices) InputSystem.RemoveDevice(device);
            devices.Clear();
            if (previousInputSettings != null)
            {
                InputSystem.settings = previousInputSettings;
                previousInputSettings.hideFlags = previousInputSettingsFlags;
            }
            if (temporaryInputSettings != null) Object.DestroyImmediate(temporaryInputSettings);
            for (int i = cleanup.Count - 1; i >= 0; i--)
                if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            cleanup.Clear();
        }

        [Test]
        public void NearestSolidBlockerClipsPreviewAndPreventsThrowThroughIt()
        {
            CreateWall();
            BoxCollider blocker = CreateBox(new Vector3(2f, 2f, 0f), new Vector3(1f, 6f, 4f), 0);
            Assert.That(Resolve(Vector3.right, out _, out Vector3 preview), Is.False);
            Assert.That(preview.x - testOrigin.x, Is.EqualTo(1.5f).Within(0.01f));
            blocker.isTrigger = true;
            Assert.That(Resolve(Vector3.right, out _, out _), Is.True);
        }

        [Test]
        public void TriggerAnchorsIgnoreOtherTriggersButRespectSolidOcclusion()
        {
            BoxCollider wall = CreateWall();
            wall.isTrigger = true;
            var volume = CreateBox(new Vector3(2f, 2f, 0f), new Vector3(1f, 6f, 4f), 0);
            volume.isTrigger = true;
            Assert.That(Resolve(Vector3.right, out var target, out _), Is.True);
            Assert.That(target.Collider, Is.SameAs(wall));
            Assert.That(wall.isTrigger, Is.True);
            volume.isTrigger = false;
            Assert.That(Resolve(Vector3.right, out _, out var preview), Is.False);
            Assert.That(preview.x - testOrigin.x, Is.EqualTo(1.5f).Within(0.01f));
            volume.enabled = false;
            settings.allowTriggerAnchors = false;
            Assert.That(Resolve(Vector3.right, out _, out _), Is.False);
        }

        [Test]
        public void TriggerWallCanBeReachedHeldAndChainedWithoutChangingItsTriggerState()
        {
            var grapple = CreateGrapple();
            EquipSword(grapple, 1);
            var wall = CreateWall();
            wall.isTrigger = true;
            ReachWall(grapple);
            var firstPosition = grapple.GetComponent<Rigidbody>().position;
            ReachWall(grapple, new Vector3(0.45f, 2f, 0f));
            Assert.That(grapple.GetComponent<Rigidbody>().position.y, Is.GreaterThan(firstPosition.y + 1f));
            Assert.That(wall.isTrigger, Is.True);
            Assert.That(grapple.IsHolding, Is.True);
        }

        [Test]
        public void NewTriggerWallInterceptsSwordFlight()
        {
            var grapple = CreateGrapple();
            EquipSword(grapple, 1);
            CreateWall().isTrigger = true;
            grapple.BeginAim();
            Physics.SyncTransforms();
            grapple.ConsumePrimaryAttack();
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Flying));
            CreateBox(new Vector3(1f, 2f, 0f), new Vector3(0.2f, 6f, 4f), 12).isTrigger = true;
            Physics.SyncTransforms();
            for (int i = 0; i < 3; i++) Invoke(grapple, "FixedUpdate");
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Idle));
        }

        [Test]
        public void OutOfRangeWallLeavesPreviewAtMaximumRange()
        {
            CreateBox(new Vector3(12f, 2f, 0f), new Vector3(1f, 20f, 4f), 12);
            Assert.That(Resolve(Vector3.right, out _, out Vector3 preview), Is.False);
            Assert.That(preview.x - testOrigin.x, Is.EqualTo(settings.maximumRange).Within(0.01f));
        }

        [TestCase(0f)]
        [TestCase(90f)]
        public void TargetRespectsCurrentMovementPlaneAndLeavesCapsuleClear(float yaw)
        {
            capsule.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 axis = capsule.transform.right;
            BoxCollider wall = CreateBox(axis * 5f + Vector3.up * 2f,
                yaw == 0f ? new Vector3(1f, 20f, 4f) : new Vector3(4f, 20f, 1f), 12);
            Physics.SyncTransforms();
            Assert.That(SwordGrappleTargeting.TryResolve(capsule, axis, axis, settings,
                out var target, out _), Is.True);
            Assert.That(target.Collider, Is.EqualTo(wall));
            Vector3 planeNormal = Vector3.Cross(axis, Vector3.up);
            Assert.That(Vector3.Dot(target.PlayerPosition - testOrigin, planeNormal), Is.EqualTo(0f).Within(0.01f));
            SwordGrappleTargeting.GetCapsule(capsule, target.PlayerPosition, out var top, out var bottom, out float radius);
            Assert.That(Physics.CheckCapsule(top, bottom, radius, settings.obstructionLayers,
                QueryTriggerInteraction.Ignore), Is.False);
        }

        [TestCase(-15f)]
        [TestCase(15f)]
        public void SlopedWallMaintainsConfiguredCapsuleClearance(float tilt)
        {
            var wall = CreateWall();
            wall.transform.rotation = Quaternion.Euler(0f, 0f, tilt);
            Assert.That(Resolve(Vector3.right, out var target, out _), Is.True);
            SwordGrappleTargeting.GetCapsule(capsule, target.PlayerPosition,
                out var top, out var bottom, out float radius);
            float gap = Mathf.Min(Vector3.Dot(top - target.Point, target.Normal),
                Vector3.Dot(bottom - target.Point, target.Normal)) - radius;
            Assert.That(gap, Is.EqualTo(settings.wallClearance).Within(0.002f));
            Assert.That(Physics.CheckCapsule(top, bottom, radius, settings.obstructionLayers,
                QueryTriggerInteraction.Ignore), Is.False);
        }

        [TestCase(false, 1f)]
        [TestCase(true, 1f)]
        [TestCase(false, -1f)]
        [TestCase(true, -1f)]
        public void WallHoldSettlesToConfiguredGapWithScaledOffsetCapsule(bool trigger, float side)
        {
            capsule.transform.localScale = Vector3.one * 1.1f;
            capsule.center = new Vector3(-0.17522341f, 1.27f, -0.06704581f);
            capsule.height = 2.55f;
            var grapple = CreateGrapple();
            EquipSword(grapple, 1);
            var wall = CreateBox(new Vector3(side * 5f, 2f, 0f), new Vector3(1f, 20f, 4f), 12);
            wall.isTrigger = trigger;
            ReachWall(grapple, Vector3.right * side);
            AdvanceGrapple(grapple, 15);
            Assert.That(grapple.IsHolding, Is.True);
            var body = grapple.GetComponent<Rigidbody>();
            SwordGrappleTargeting.GetCapsule(capsule, body.position, out var top, out var bottom, out float radius);
            float capsuleEdge = ((top + bottom) * 0.5f).x + side * radius;
            float wallFace = side > 0f ? wall.bounds.min.x : wall.bounds.max.x;
            Assert.That(side * (wallFace - capsuleEdge), Is.EqualTo(settings.wallClearance).Within(0.002f),
                "Arrival tolerance must not leave a permanent gap outside the configured clearance.");
            Assert.That(Physics.CheckCapsule(top, bottom, radius, settings.obstructionLayers,
                QueryTriggerInteraction.Ignore), Is.False);
            Assert.That(wall.isTrigger, Is.EqualTo(trigger));
        }

        [Test]
        public void ClearSwordRayWithBlockedBodyRouteIsInvalid()
        {
            CreateWall();
            CreateBox(new Vector3(2f, 0.45f, 0f), new Vector3(0.5f, 0.6f, 4f), 0);
            Assert.That(Resolve(Vector3.right, out _, out Vector3 preview), Is.False);
            Assert.That(preview.x - testOrigin.x, Is.EqualTo(4.5f).Within(0.01f));
        }

        [Test]
        public void CeilingAtArrivalRejectsAnchorEvenWhenSwordRayIsClear()
        {
            CreateWall();
            CreateBox(new Vector3(4f, 2.2f, 0f), new Vector3(1f, 1f, 4f), 0);
            Assert.That(Resolve(Vector3.right, out _, out _), Is.False);
        }

        [Test]
        public void FloorAndMovingWallsCannotBeAnchors()
        {
            BoxCollider floor = CreateBox(new Vector3(0f, -2f, 0f), new Vector3(20f, 1f, 4f), 12);
            Assert.That(Resolve(Vector3.down, out _, out _), Is.False);
            floor.enabled = false;
            CreateWall().gameObject.AddComponent<Rigidbody>().isKinematic = true;
            Assert.That(Resolve(Vector3.right, out _, out _), Is.False);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void OnlyAnUnbrokenGrappleWeaponInEitherEquippedSlotEnablesTraversal(int slot)
        {
            PlayerSwordGrapple grapple = CreateGrapple();
            Assert.That(grapple.HasUsableSword(), Is.False);
            WeaponInstance weapon = EquipSword(grapple, slot);
            weapon.weaponData.enablesSwordGrapple = false;
            Assert.That(grapple.HasUsableSword(), Is.False);
            weapon.weaponData.enablesSwordGrapple = true;
            Assert.That(grapple.HasUsableSword(), Is.True);
            SetField(weapon, "currentDurability", 0f);
            Assert.That(grapple.HasUsableSword(), Is.False);
        }

        [Test]
        public void MissingHolsteredOrDetachedSwordCannotStartGrappling()
        {
            var grapple = CreateGrapple();
            grapple.BeginAim();
            Assert.That(grapple.IsActive, Is.False);
            Assert.That(grapple.ConsumePrimaryAttack(), Is.False);
            var weapon = EquipSword(grapple, 1, false);
            Assert.That(grapple.HasUsableSword(), Is.False);
            grapple.BeginAim();
            Assert.That(grapple.IsActive, Is.False);
            Assert.That(GetField<SwordGrappleVisuals>(grapple, "visuals"), Is.Null);

            Assert.That(grapple.GetComponent<WeaponManager>().TryToggleCombatMode(), Is.True);
            Assert.That(grapple.HasUsableSword(), Is.True);
            weapon.transform.SetParent(null, true);
            Assert.That(grapple.HasUsableSword(), Is.False);
            grapple.BeginAim();
            Assert.That(grapple.IsActive, Is.False);
            weapon.transform.SetParent(grapple.transform, false);
            weapon.gameObject.SetActive(false);
            Assert.That(grapple.HasUsableSword(), Is.False);
        }

        [Test]
        public void ForcedHolsteringCancelsWallHoldAndReleasesItsLocks()
        {
            var grapple = CreateGrapple();
            EquipSword(grapple, 1);
            boundGrapple = grapple;
            Invoke(grapple, "OnEnable");
            CreateWall();
            ReachWall(grapple);
            Invoke(grapple.GetComponent<WeaponManager>(), "ExitModCombat");
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Idle));
            Assert.That(grapple.HasUsableSword(), Is.False);
            Assert.That(grapple.GetComponent<Character2D5Controller>().IsPhysicsOverridden, Is.False);
        }

        [Test]
        public void ThrowPullHoldAndReleasePreserveWeaponAndDurability()
        {
            PlayerSwordGrapple grapple = CreateGrapple();
            WeaponInstance weapon = EquipSword(grapple, 2);
            float durability = weapon.CurrentDurability;
            CreateWall();
            ReachWall(grapple);
            Assert.That(grapple.IsHolding, Is.True);
            Assert.That(grapple.GetComponent<PlayerState>().IsWallAttached, Is.True);
            Assert.That(grapple.GetComponent<Character2D5Controller>().IsPhysicsOverridden, Is.True);
            Assert.That(grapple.GetComponent<PlayerWeaponLoadout>().TrySwapSlots(), Is.False);
            Assert.That(weapon.CurrentDurability, Is.EqualTo(durability));
            Assert.That(grapple.ReleaseFromJump(), Is.True);
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Idle));
            Assert.That(grapple.GetComponent<Character2D5Controller>().IsPhysicsOverridden, Is.False);
            Assert.That(grapple.GetComponent<PlayerWeaponLoadout>().WeaponSlot2, Is.SameAs(weapon));
            Assert.That(grapple.GetComponent<PlayerState>().CanAttack, Is.True);
        }

        [Test]
        public void ImpactDelayKeepsSwordAndRopeVisibleAndWaitsBeforePulling()
        {
            settings.impactToPullDelay = Time.fixedDeltaTime * 5f;
            var grapple = CreateGrapple();
            var weapon = EquipSword(grapple, 1);
            var texture = Track(new Texture2D(8, 16));
            weapon.weaponData.icon = Track(Sprite.Create(texture, new Rect(0, 0, 8, 16), Vector2.one * 0.5f));
            CreateWall();
            ThrowUntilImpact(grapple);
            var body = grapple.GetComponent<Rigidbody>();
            Vector3 beforePull = body.position;
            grapple.EndAim();
            Invoke(grapple, "LateUpdate");
            var visuals = GetField<SwordGrappleVisuals>(grapple, "visuals");
            Assert.That(GetField<LineRenderer>(visuals, "rope").enabled, Is.True);
            Assert.That(GetField<SpriteRenderer>(visuals, "sword").enabled, Is.True);
            Assert.That(GetField<LineRenderer>(visuals, "reticle").enabled, Is.False);
            Assert.That(GetField<LineRenderer>(visuals, "rope").GetPosition(1).x,
                Is.EqualTo(testOrigin.x + 4.5f).Within(0.001f));
            AdvanceGrapple(grapple, 4);
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.ImpactDelay));
            Assert.That(body.position, Is.EqualTo(beforePull));
            Assert.That(grapple.GetComponent<Character2D5Controller>().IsPhysicsOverridden, Is.False);

            float previousTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                AdvanceGrapple(grapple, 10);
                Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.ImpactDelay));
            }
            finally { Time.timeScale = previousTimeScale; }
            AdvanceGrapple(grapple);
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Pulling));
            Assert.That(body.position.x, Is.GreaterThan(beforePull.x));
        }

        [Test]
        public void ZeroImpactDelayStartsPullOnTheImpactTick()
        {
            settings.impactToPullDelay = 0f;
            var grapple = CreateGrapple();
            EquipSword(grapple, 1);
            CreateWall();
            ThrowUntilImpact(grapple, expected: SwordGrapplePhase.Pulling);
            Assert.That(grapple.GetComponent<Rigidbody>().linearVelocity.x, Is.GreaterThan(0f));
        }

        [Test]
        public void ObstacleEnteringDuringImpactDelayPreventsThePull()
        {
            var grapple = CreateGrapple();
            EquipSword(grapple, 1);
            CreateWall();
            ThrowUntilImpact(grapple);
            CreateBox(new Vector3(2f, 2f, 0f), new Vector3(0.5f, 6f, 4f), 0);
            Physics.SyncTransforms();
            AdvanceGrapple(grapple, 10);
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Idle));
            Assert.That(grapple.GetComponent<Character2D5Controller>().IsPhysicsOverridden, Is.False);
            Invoke(grapple, "LateUpdate");
            Assert.That(GetField<LineRenderer>(GetField<SwordGrappleVisuals>(grapple, "visuals"), "rope").enabled, Is.False);
        }

        [Test]
        public void ChainedImpactDelayKeepsOldHoldAndRecoversWhenNewAnchorDisappears()
        {
            var grapple = CreateGrapple();
            EquipSword(grapple, 1);
            CreateWall();
            var oppositeWall = CreateBox(new Vector3(-4f, 2f, 0f), new Vector3(1f, 20f, 4f), 12);
            ReachWall(grapple);
            AdvanceGrapple(grapple, 15);
            Vector3 held = grapple.GetComponent<Rigidbody>().position;
            ThrowUntilImpact(grapple, Vector3.left);
            AdvanceGrapple(grapple);
            Assert.That(grapple.IsHolding, Is.True);
            Assert.That(grapple.GetComponent<PlayerState>().IsWallAttached, Is.True);
            Assert.That(Vector3.Distance(grapple.GetComponent<Rigidbody>().position, held), Is.LessThan(0.002f));
            oppositeWall.enabled = false;
            AdvanceGrapple(grapple);
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Holding));
            Assert.That(grapple.IsHolding, Is.True);
        }

        [Test]
        public void ChainingCanReachHigherOnSameWallAndThenTheOppositeWall()
        {
            PlayerSwordGrapple grapple = CreateGrapple();
            EquipSword(grapple, 1);
            CreateWall();
            CreateBox(new Vector3(-4f, 2f, 0f), new Vector3(1f, 20f, 4f), 12);
            ReachWall(grapple);
            Rigidbody body = grapple.GetComponent<Rigidbody>();
            Vector3 first = body.position;
            ReachWall(grapple, new Vector3(0.45f, 2f, 0f));
            Assert.That(body.position.y, Is.GreaterThan(first.y + 1f));
            ReachWall(grapple, Vector3.left);
            Assert.That(body.position.x, Is.LessThan(testOrigin.x));
            Assert.That(grapple.IsHolding, Is.True);
        }

        [Test]
        public void CancellationReleasesOnlyGrappleLocksAndPreservesExternalImpulse()
        {
            PlayerSwordGrapple grapple = CreateGrapple();
            EquipSword(grapple, 1);
            CreateWall();
            ReachWall(grapple);
            var controller = grapple.GetComponent<Character2D5Controller>();
            using (controller.AcquirePhysicsOverride())
            using (controller.AcquireMovementLock(false))
            {
                controller.SetVelocity(new Vector3(-3f, 2f, 0f));
                grapple.Cancel();
                Assert.That(controller.PhysicsOverrideCount, Is.EqualTo(1));
                Assert.That(controller.MovementLockCount, Is.EqualTo(1));
                Assert.That(controller.Velocity, Is.EqualTo(new Vector3(-3f, 2f, 0f)));
            }
            Assert.That(controller.IsPhysicsOverridden, Is.False);
            Assert.That(controller.CanMove, Is.True);
        }

        [Test]
        public void GrapplingBlocksCombatWithoutGrantingDamageImmunity()
        {
            PlayerState state = capsule.gameObject.AddComponent<PlayerState>();
            state.SetGrounded(true);
            bool canTakeDamage = state.CanTakeDamage;
            state.SetGrappleState(true, true);
            Assert.That(state.CanAttack || state.CanDash || state.CanRoll || state.CanParry, Is.False);
            Assert.That(state.CanTakeDamage, Is.EqualTo(canTakeDamage));
            state.SetGrappleState(false, false);
            Assert.That(state.CanAttack, Is.True);
        }

        [Test]
        public void ReleasingCtrlCancelsPreviewButKeepsAnEstablishedWallHold()
        {
            PlayerSwordGrapple grapple = CreateGrapple();
            EquipSword(grapple, 1);
            grapple.BeginAim();
            Invoke(grapple, "LateUpdate");
            var visuals = GetField<object>(grapple, "visuals");
            Assert.That(GetField<LineRenderer>(visuals, "reticle").enabled, Is.True);
            grapple.EndAim();
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Idle));
            Assert.That(grapple.HasValidAim, Is.False);
            CreateWall();
            ReachWall(grapple);
            grapple.BeginAim();
            Invoke(grapple, "LateUpdate");
            grapple.EndAim();
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Holding));
            Assert.That(grapple.IsHolding, Is.True);
            Assert.That(GetField<LineRenderer>(visuals, "reticle").enabled, Is.False);
            Assert.That(GetField<LineRenderer>(visuals, "preview").enabled, Is.False);
        }

        [Test]
        public void SceneTeardownCanDestroyVisualsBeforePlayerCancellation()
        {
            PlayerSwordGrapple grapple = CreateGrapple();
            EquipSword(grapple, 1);
            CreateWall();
            ReachWall(grapple);
            var visuals = GetField<object>(grapple, "visuals");
            Object.DestroyImmediate(((SwordGrappleVisuals)visuals).gameObject);

            Assert.DoesNotThrow(() => grapple.EndAim());
            Assert.DoesNotThrow(() => Invoke(grapple, "OnDisable"));
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Idle));
            Assert.That(grapple.GetComponent<Character2D5Controller>().IsPhysicsOverridden, Is.False);
            Assert.That(grapple.GetComponent<PlayerState>().IsWallAttached, Is.False);
            Assert.DoesNotThrow(() => Invoke(grapple, "OnDestroy"));
        }

        [Test]
        public void WallAttachmentPlaysOnceHoldsFinalPoseAndRestartsForANewAttachment()
        {
            var grapple = CreateGrapple();
            var skeleton = CreateObject("Grapple animation skeleton", Vector3.zero)
                .AddComponent<Spine.Unity.SkeletonAnimation>();
            var data = new Spine.SkeletonData();
            data.Bones.Add(new Spine.BoneData(0, "root", null));
            foreach (string name in new[] { "idle", "Jump_2_Air" })
                data.Animations.Add(new Spine.Animation(name, new Spine.ExposedList<Spine.Timeline>(), 1f));
            var rotation = new Spine.RotateTimeline(2, 0, 0);
            rotation.SetFrame(0, 0f, 0f);
            rotation.SetFrame(1, 1f, 45f);
            var wallTimelines = new Spine.ExposedList<Spine.Timeline>();
            wallTimelines.Add(rotation);
            data.Animations.Add(new Spine.Animation("Jump_Wall", wallTimelines, 1f));
            skeleton.skeleton = new Spine.Skeleton(data);
            skeleton.state = new Spine.AnimationState(new Spine.AnimationStateData(data));
            skeleton.valid = true;
            var animation = capsule.gameObject.AddComponent<SpineAnimationController>();
            SetField(animation, "skeletonAnimation", skeleton);
            SetField(animation, "wallSlide", "Jump_Wall");
            Invoke(animation, "Awake");
            Invoke(animation, "Start");
            var player = grapple.GetComponent<PlayerState>();
            player.SetGrappleState(true, true);
            var entry = skeleton.state.GetCurrent(0);
            int completions = 0;
            entry.Complete += _ => completions++;
            for (int i = 0; i < 40; i++)
            {
                skeleton.state.Update(0.1f);
                skeleton.state.Apply(skeleton.skeleton);
                Invoke(animation, "Update");
            }
            player.SetGrappleState(true, true); // Re-aiming must retain the same pose.
            Assert.That(skeleton.state.GetCurrent(0), Is.SameAs(entry));
            Assert.That(entry.Loop, Is.False);
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(skeleton.skeleton.RootBone.Rotation, Is.EqualTo(45f).Within(0.001f));
            player.SetGrappleState(true, false); // Pulling towards the next anchor.
            player.SetGrappleState(true, true);
            Assert.That(skeleton.state.GetCurrent(0).Animation.Name, Is.EqualTo("Jump_Wall"));
            Assert.That(skeleton.state.GetCurrent(0).TrackTime, Is.Zero);
        }

        [Test]
        public void CtrlAndLmbUseCurrentPointerAndReleaseDoesNotCancelFiredSword()
        {
            var (keyboard, mouse) = CreateInputDevices();
            PlayerSwordGrapple grapple = CreateGrapple();
            boundGrapple = grapple;
            Invoke(grapple, "OnEnable");
            EquipSword(grapple, 1);
            CreateWall();
            Physics.SyncTransforms();
            var camera = Camera.main;
            camera.pixelRect = new Rect(0f, 0f, 800f, 600f);
            Vector2 pointer = camera.WorldToScreenPoint(testOrigin + new Vector3(4.5f, 1.5f, 0f));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
            InputSystem.Update();
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Aiming));
            Assert.That(grapple.HasValidAim, Is.True, grapple.AimFailure);

            // A stale preview flag must not suppress a valid input arriving before Update.
            SetField(grapple, "<HasValidAim>k__BackingField", false);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer }.WithButton(MouseButton.Left));
            InputSystem.Update();
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Flying), grapple.AimFailure);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Flying));
            Assert.That(GetField<bool>(grapple, "aimHeld"), Is.False);
        }

        [TestCase(Key.LeftCtrl, true, false)]
        [TestCase(Key.RightCtrl, true, false)]
        [TestCase(Key.LeftCtrl, true, true)]
        [TestCase(Key.LeftCtrl, false, false)]
        public void HeldCtrlResumesAimOnArrivalAndCanFireTheNextGrapple(Key ctrl, bool keepHeld, bool clickBeforeUpdate)
        {
            var (keyboard, mouse) = CreateInputDevices();
            var grapple = CreateGrapple();
            boundGrapple = grapple;
            Invoke(grapple, "OnEnable");
            EquipSword(grapple, 1);
            CreateWall();
            Physics.SyncTransforms();
            var camera = Camera.main;
            camera.pixelRect = new Rect(0f, 0f, 800f, 600f);
            Vector2 pointer = camera.WorldToScreenPoint(testOrigin + new Vector3(4.5f, 1.5f, 0f));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(ctrl, Key.D));
            InputSystem.Update();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer }.WithButton(MouseButton.Left));
            InputSystem.Update();
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Flying));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
            if (!keepHeld) InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            InputSystem.Update();
            for (int i = 0; i < 100 && !grapple.IsHolding; i++) AdvanceGrapple(grapple);
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Holding));
            pointer = camera.WorldToScreenPoint(testOrigin + new Vector3(4.5f, 4f, 0f));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
            InputSystem.Update();
            if (!clickBeforeUpdate)
            {
                Invoke(grapple, "Update");
                Invoke(grapple, "LateUpdate");
                var visuals = GetField<SwordGrappleVisuals>(grapple, "visuals");
                Assert.That(grapple.Phase, Is.EqualTo(keepHeld ? SwordGrapplePhase.Aiming : SwordGrapplePhase.Holding));
                Assert.That(GetField<LineRenderer>(visuals, "reticle").enabled, Is.EqualTo(keepHeld));
                if (!keepHeld) return;
                Assert.That(grapple.HasValidAim, Is.True, grapple.AimFailure);
            }

            // This manual EditMode simulation does not advance frameCount;
            // reset only the per-render-frame duplicate-click guards.
            SetField(grapple, "consumedFrame", -1);
            SetField(testInput, "primaryConsumedFrame", -1);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer }.WithButton(MouseButton.Left));
            InputSystem.Update();
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Flying), grapple.AimFailure);
            Assert.That(testInput.IsGrappleAimHeld, Is.True, "No Ctrl release/repress should be needed.");
        }

        [Test]
        public void VisualPrefabCustomizationSurvivesRenderingAndIsReused()
        {
            var sourceRope = GetField<LineRenderer>(settings.visualsPrefab, "rope");
            sourceRope.widthMultiplier = 0.17f;
            sourceRope.textureMode = LineTextureMode.Tile;
            sourceRope.textureScale = new Vector2(2.5f, 1f);
            sourceRope.startColor = sourceRope.endColor = Color.yellow;
            var grapple = CreateGrapple();
            EquipSword(grapple, 1);
            grapple.BeginAim();
            var visuals = GetField<SwordGrappleVisuals>(grapple, "visuals");
            Assert.That(visuals, Is.Not.SameAs(settings.visualsPrefab));
            visuals.Render(testOrigin, testOrigin + Vector3.right * 4f, Vector3.right, true, true,
                testOrigin + Vector3.right * 3f, testOrigin + Vector3.right * 4f, true, Camera.main);
            var rope = GetField<LineRenderer>(visuals, "rope");
            Assert.That(rope.widthMultiplier, Is.EqualTo(0.17f));
            Assert.That(rope.textureScale.x, Is.EqualTo(2.5f));
            Assert.That(rope.textureMode, Is.EqualTo(LineTextureMode.Tile));
            Assert.That(rope.startColor, Is.EqualTo(Color.yellow));
            Assert.That(rope.GetPosition(1), Is.EqualTo(testOrigin + Vector3.right * 3f));
            grapple.EndAim();
            Assert.That(rope.enabled, Is.False);
            grapple.BeginAim();
            Assert.That(GetField<SwordGrappleVisuals>(grapple, "visuals"), Is.SameAs(visuals));
        }

        [Test]
        public void PlayerPrefabReferenceAndVisualRecoveryKeepAimSwordAndRopeVisible()
        {
            var grapple = CreateGrapple();
            var weapon = EquipSword(grapple, 1);
            var texture = Track(new Texture2D(8, 16));
            weapon.weaponData.icon = Track(Sprite.Create(texture, new Rect(0, 0, 8, 16), Vector2.one * 0.5f));
            SetField(grapple, "visualsPrefab", settings.visualsPrefab);
            settings.visualsPrefab = null; // Direct prefab wiring is independently sufficient.
            CreateWall();
            Physics.SyncTransforms();
            grapple.BeginAim();
            Invoke(grapple, "LateUpdate");
            var visuals = GetField<SwordGrappleVisuals>(grapple, "visuals");
            Assert.That(GetField<LineRenderer>(visuals, "reticle").enabled, Is.True);
            Assert.That(GetField<LineRenderer>(visuals, "preview").enabled, Is.True);
            Object.DestroyImmediate(visuals.gameObject);
            Invoke(grapple, "LateUpdate");
            visuals = GetField<SwordGrappleVisuals>(grapple, "visuals");
            Assert.That(GetField<LineRenderer>(visuals, "reticle").enabled, Is.True);

            grapple.ConsumePrimaryAttack();
            Invoke(grapple, "LateUpdate");
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Flying));
            Assert.That(GetField<LineRenderer>(visuals, "rope").enabled, Is.True);
            Assert.That(GetField<SpriteRenderer>(visuals, "sword").enabled, Is.True);
            visuals.gameObject.SetActive(false);
            Invoke(grapple, "LateUpdate");
            Assert.That(visuals.gameObject.activeInHierarchy, Is.True);
            Assert.That(GetField<SpriteRenderer>(visuals, "sword").sprite, Is.SameAs(weapon.weaponData.icon));
            Assert.That(grapple.HasUsableSword(), Is.True, "Hiding the in-hand renderer during flight must not unequip the reserved sword.");
        }

        [Test]
        public void AimDetailsAreBoundedReusedAndHiddenOnReleaseOrFlight()
        {
            var visuals = settings.visualsPrefab;
            visuals.PrepareForUse();
            var details = GetField<MeshFilter>(visuals, "aimDetails");
            var mesh = details.sharedMesh;
            visuals.Render(testOrigin, testOrigin + Vector3.right * 1000f, Vector3.right, true, true,
                testOrigin, testOrigin, false, null);
            Assert.That(details.GetComponent<Renderer>().enabled, Is.True);
            Assert.That(mesh.vertexCount, Is.InRange(4, 512));
            visuals.HideAim();
            Assert.That(details.GetComponent<Renderer>().enabled, Is.False);
            visuals.Render(testOrigin, testOrigin + Vector3.right * 4f, Vector3.right, false, true,
                testOrigin, testOrigin, false, null);
            Assert.That(details.sharedMesh, Is.SameAs(mesh));
            Assert.That(details.GetComponent<Renderer>().enabled, Is.True);
            visuals.Render(testOrigin, testOrigin + Vector3.right * 4f, Vector3.right, true, false,
                testOrigin + Vector3.right, testOrigin + Vector3.right * 4f, true, null);
            Assert.That(details.GetComponent<Renderer>().enabled, Is.False);
            Assert.That(GetField<LineRenderer>(visuals, "rope").enabled, Is.True);
            visuals.Dispose();
            Assert.That(mesh == null, Is.True, "The generated mesh must be released with its visual owner.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AimMarkerKeepsScreenSizeAcrossCameraZoom(bool orthographic)
        {
            CreateGrapple();
            var camera = Camera.main;
            camera.orthographic = orthographic;
            camera.fieldOfView = 30f;
            camera.orthographicSize = 2f;
            camera.pixelRect = new Rect(0f, 0f, 800f, 600f);
            camera.transform.position = testOrigin + Vector3.back * 10f;
            var visuals = settings.visualsPrefab;
            SetField(visuals, "focusPulseAmount", 0f);
            visuals.PrepareForUse();
            var reticle = GetField<LineRenderer>(visuals, "reticle");
            Vector3 end = testOrigin + Vector3.right;
            visuals.Render(testOrigin, end, Vector3.right, true, true, testOrigin, end, false, camera);
            float before = Vector3.Distance(camera.WorldToScreenPoint(reticle.GetPosition(0)), camera.WorldToScreenPoint(end));
            camera.orthographicSize = 4f;
            if (!orthographic) camera.transform.position = testOrigin + Vector3.back * 20f;
            visuals.Render(testOrigin, end, Vector3.right, true, true, testOrigin, end, false, camera);
            float after = Vector3.Distance(camera.WorldToScreenPoint(reticle.GetPosition(0)), camera.WorldToScreenPoint(end));
            Assert.That(after, Is.EqualTo(before).Within(0.5f));
        }

        [Test]
        public void ProductionPlayerAndSwordHaveGrappleConfiguration()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/PLAYER/Player_2.2.prefab");
            var grapple = prefab.GetComponent<PlayerSwordGrapple>();
            Assert.That(grapple, Is.Not.Null);
            Assert.That(grapple.Settings, Is.Not.Null);
            Assert.That(grapple.Settings.visualsPrefab, Is.Not.Null);
            Assert.That(GetField<SwordGrappleVisuals>(grapple, "visualsPrefab"), Is.SameAs(grapple.Settings.visualsPrefab));
            Assert.That(grapple.Settings.visualsPrefab.HasRequiredRenderers, Is.True);
            Assert.That(GetField<MeshFilter>(grapple.Settings.visualsPrefab, "aimDetails"), Is.Not.Null);
            foreach (var renderer in grapple.Settings.visualsPrefab.GetComponentsInChildren<Renderer>(true))
            {
                Assert.That(renderer.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Player")));
                Assert.That(renderer.sortingLayerName, Is.EqualTo("Player"));
                Assert.That(renderer.allowOcclusionWhenDynamic, Is.False);
            }
            var rope = GetField<LineRenderer>(grapple.Settings.visualsPrefab, "rope");
            Assert.That(rope.sharedMaterial.shader.name, Is.EqualTo("Universal Render Pipeline/Particles/Unlit"));
            Assert.That(rope.sharedMaterial.GetTexture("_BaseMap"), Is.Not.Null);
            Assert.That(rope.textureMode, Is.EqualTo(LineTextureMode.Tile));
            Assert.That(GetField<bool>(prefab.GetComponentInChildren<SpineAnimationController>(), "wallSlideLoop"), Is.False);
            var sword = AssetDatabase.LoadAssetAtPath<MeleeWeaponData>("Assets/Game/Scripts/Weapons/Sword.asset");
            Assert.That(sword.enablesSwordGrapple, Is.True);
        }

        [TestCase(7f)]
        [TestCase(25f)]
        public void PullUsesConfiguredSpeedImmediatelyAndDoesNotEaseNearWall(float speed)
        {
            settings.pullSpeed = speed;
            settings.impactToPullDelay = 0f;
            var grapple = CreateGrapple();
            EquipSword(grapple, 1);
            CreateWall();
            ThrowUntilImpact(grapple, expected: SwordGrapplePhase.Pulling);
            var controller = grapple.GetComponent<Character2D5Controller>();
            Assert.That(controller.Velocity.magnitude, Is.EqualTo(speed).Within(0.001f));
            var target = GetField<SwordGrappleTarget>(grapple, "target");
            var body = grapple.GetComponent<Rigidbody>();
            body.position = target.PlayerPosition - Vector3.right * (speed * Time.fixedDeltaTime + 0.15f);
            Physics.SyncTransforms();
            Invoke(grapple, "FixedUpdate");
            Assert.That(controller.Velocity.magnitude, Is.EqualTo(speed).Within(0.001f));
        }

        [TestCase(-1f)]
        [TestCase(1f)]
        public void DirectionalHoldReleasesAndRepressOnlySlides(float side)
        {
            var grapple = CreateGrapple();
            EquipSword(grapple, 1);
            CreateBox(new Vector3(side * 5f, 2f, 0f), new Vector3(1f, 20f, 4f), 12).isTrigger = true;
            ReachWall(grapple, Vector3.right * side);
            AdvanceGrapple(grapple, 3);
            var controller = grapple.GetComponent<Character2D5Controller>();
            var state = grapple.GetComponent<PlayerState>();
            var position = grapple.GetComponent<Rigidbody>().position;
            AdvanceGrapple(grapple, 8);
            Assert.That(grapple.GetComponent<Rigidbody>().position, Is.EqualTo(position));
            Assert.That(state.IsWallSliding, Is.False);
            controller.SetMovementInput(0f);
            AdvanceGrapple(grapple);
            Assert.That(grapple.IsActive, Is.False);
            Assert.That(controller.IsPhysicsOverridden, Is.False);
            Assert.That(state.IsWallAttached, Is.False);
            controller.SetVelocity(Vector3.down * 8f);
            Invoke(controller, "FixedUpdate");
            Assert.That(state.IsWallSliding, Is.False);
            controller.SetMovementInput(side);
            Invoke(controller, "FixedUpdate");
            Assert.That(state.IsWallSliding, Is.True);
            Assert.That(grapple.IsHolding, Is.False);
            Assert.That(controller.Velocity.y, Is.EqualTo(-settings.wallSlideSpeed));
            Assert.That(controller.Velocity.x, Is.Zero);
            controller.SetMovementInput(-side);
            Invoke(controller, "FixedUpdate");
            Assert.That(state.IsWallSliding, Is.False);
        }

        [TestCase(-1f, 0f, true)]
        [TestCase(1f, 0f, false)]
        [TestCase(-1f, 90f, false)]
        [TestCase(1f, 90f, true)]
        public void OrdinaryWallContactSlidesWithoutASwordAndCannotAirGrab(float side, float yaw, bool trigger)
        {
            capsule.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var grapple = CreateGrapple();
            var controller = grapple.GetComponent<Character2D5Controller>();
            var state = grapple.GetComponent<PlayerState>();
            Vector3 axis = controller.MovementAxis;
            var wall = CreateBox(axis * side * 0.875f + Vector3.up * 2f,
                yaw == 0 ? new Vector3(1f, 20f, 4f) : new Vector3(4f, 20f, 1f), 12);
            wall.isTrigger = trigger;
            Physics.SyncTransforms();
            controller.SetMovementInput(side);
            controller.SetVelocity(Vector3.down * 9f);
            Invoke(controller, "FixedUpdate");
            Assert.That(state.IsWallSliding, Is.True);
            Assert.That(controller.Velocity.y, Is.EqualTo(-settings.wallSlideSpeed));
            Assert.That(grapple.IsActive, Is.False);
            Assert.That(grapple.HasUsableSword(), Is.False);
            wall.enabled = false;
            Invoke(controller, "FixedUpdate");
            Assert.That(state.IsWallSliding, Is.False);
        }

        [TestCase(-1f)]
        [TestCase(1f)]
        public void JumpFromHoldPushesAwayEvenWithSpentAirJumpAndPreservesLaunch(float side)
        {
            var grapple = CreateGrapple();
            EquipSword(grapple, 1);
            CreateBox(new Vector3(side * 5f, 2f, 0f), new Vector3(1f, 20f, 4f), 12);
            ReachWall(grapple, Vector3.right * side);
            var controller = grapple.GetComponent<Character2D5Controller>();
            SetField(controller, "airJumpCount", 1);
            int jumps = 0;
            controller.OnJumpStarted += () => jumps++;
            Assert.That(grapple.ReleaseFromJump(), Is.True);
            Assert.That(grapple.IsActive, Is.False);
            Assert.That(controller.Velocity.x, Is.EqualTo(-side * settings.wallJumpAwaySpeed));
            Assert.That(controller.Velocity.y, Is.EqualTo(settings.wallJumpUpSpeed));
            Assert.That(grapple.GetComponent<PlayerState>().IsJumping, Is.True);
            Assert.That(jumps, Is.EqualTo(1));
            controller.SetMovementInput(side);
            Invoke(controller, "FixedUpdate");
            Assert.That(controller.Velocity.x, Is.EqualTo(-side * settings.wallJumpAwaySpeed));
            Assert.That(grapple.GetComponent<PlayerState>().IsWallSliding, Is.False);
        }

        [Test]
        public void ReleasingOldWallDuringChainedFlightDoesNotCancelTheFiredSword()
        {
            var grapple = CreateGrapple();
            EquipSword(grapple, 1);
            CreateWall();
            CreateBox(new Vector3(-4f, 2f, 0f), new Vector3(1f, 20f, 4f), 12);
            ReachWall(grapple);
            StartThrow(grapple, Vector3.left);
            grapple.GetComponent<Character2D5Controller>().SetMovementInput(-1f);
            AdvanceGrapple(grapple);
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Flying));
            Assert.That(grapple.IsHolding, Is.False);
            for (int i = 0; i < 100 && grapple.Phase != SwordGrapplePhase.Holding; i++) AdvanceGrapple(grapple);
            Assert.That(grapple.IsHolding, Is.True);
            Assert.That(grapple.GetComponent<Rigidbody>().position.x, Is.LessThan(testOrigin.x));
        }

        [Test]
        public void WallPoseOffsetMovesOnlyArtworkAndRestoresOnReleaseAndDisable()
        {
            var grapple = CreateGrapple();
            var animation = grapple.gameObject.AddComponent<SpineAnimationController>();
            var child = CreateObject("Wall pose artwork", Vector3.zero);
            child.transform.SetParent(grapple.transform, false);
            child.transform.localPosition = new Vector3(-0.247f, 0f, 0f);
            var skeleton = child.AddComponent<Spine.Unity.SkeletonAnimation>();
            SetField(animation, "skeletonAnimation", skeleton);
            SetField(animation, "playerState", grapple.GetComponent<PlayerState>());
            SetField(animation, "wallPoseForwardOffset", 0.345f);
            var rest = child.transform.localPosition;
            var bodyPosition = grapple.GetComponent<Rigidbody>().position;
            grapple.GetComponent<PlayerState>().SetGrappleState(true, true);
            Invoke(animation, "LateUpdate");
            Invoke(animation, "LateUpdate");
            Assert.That(child.transform.localPosition.x, Is.EqualTo(rest.x + 0.345f).Within(0.0001f));
            Assert.That(grapple.GetComponent<Rigidbody>().position, Is.EqualTo(bodyPosition));
            grapple.GetComponent<PlayerState>().SetGrappleState(false, false);
            Invoke(animation, "LateUpdate");
            Assert.That(Vector3.Distance(child.transform.localPosition, rest), Is.LessThan(0.0001f));
            grapple.GetComponent<PlayerState>().SetWallSliding(true);
            Invoke(animation, "LateUpdate");
            Invoke(animation, "OnDisable");
            Assert.That(Vector3.Distance(child.transform.localPosition, rest), Is.LessThan(0.0001f));
        }

        private (Keyboard keyboard, Mouse mouse) CreateInputDevices()
        {
            previousInputSettings = InputSystem.settings;
            previousInputSettingsFlags = previousInputSettings.hideFlags;
            // Input System destroys HideAndDontSave defaults when replacing
            // settings. Retain the original instance so teardown can restore it.
            if (previousInputSettingsFlags == HideFlags.HideAndDontSave)
                previousInputSettings.hideFlags = HideFlags.None;
            temporaryInputSettings = Object.Instantiate(previousInputSettings);
            temporaryInputSettings.hideFlags = HideFlags.HideAndDontSave;
            InputSystem.settings = temporaryInputSettings;
            // Enable gameplay action updates only in this temporary EditMode settings copy.
            temporaryInputSettings.SetInternalFeatureFlag("RUN_PLAYER_UPDATES_IN_EDIT_MODE", true);
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            devices.Add(keyboard);
            devices.Add(mouse);
            testInput = CreateObject("Grapple Input", Vector3.zero).AddComponent<GameInputManager>();
            Invoke(testInput, "Awake");
            Invoke(testInput, "OnEnable");
            return (keyboard, mouse);
        }

        private void StartThrow(PlayerSwordGrapple grapple, Vector3? direction)
        {
            if (!grapple.IsHolding)
                grapple.GetComponent<Character2D5Controller>().SetMovementInput(Mathf.Sign(Vector3.Dot(direction ?? Vector3.right,
                    grapple.GetComponent<Character2D5Controller>().MovementAxis)));
            Physics.SyncTransforms();
            SetField(grapple, "consumedFrame", -1);
            grapple.BeginAim();
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Aiming));
            if (direction.HasValue) SetField(grapple, "aimDirection", direction.Value);
            Assert.That(grapple.ConsumePrimaryAttack(), Is.True);
            Assert.That(grapple.ConsumePrimaryAttack(), Is.True);
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Flying));
        }

        private void ThrowUntilImpact(PlayerSwordGrapple grapple, Vector3? direction = null,
            SwordGrapplePhase expected = SwordGrapplePhase.ImpactDelay)
        {
            StartThrow(grapple, direction);
            for (int i = 0; i < 300 && grapple.Phase == SwordGrapplePhase.Flying; i++)
                AdvanceGrapple(grapple);
            Assert.That(grapple.Phase, Is.EqualTo(expected));
        }

        private void ReachWall(PlayerSwordGrapple grapple, Vector3? direction = null)
        {
            StartThrow(grapple, direction);
            for (int i = 0; i < 300 && grapple.Phase != SwordGrapplePhase.Holding; i++)
            {
                if (grapple.Phase == SwordGrapplePhase.Pulling)
                {
                    var controller = grapple.GetComponent<Character2D5Controller>();
                    controller.SetMovementInput(Mathf.Sign(-Vector3.Dot(GetField<SwordGrappleTarget>(grapple, "target").Normal, controller.MovementAxis)));
                }
                AdvanceGrapple(grapple);
            }
            Assert.That(grapple.Phase, Is.EqualTo(SwordGrapplePhase.Holding));
        }

        private static void AdvanceGrapple(PlayerSwordGrapple grapple, int steps = 1)
        {
            var body = grapple.GetComponent<Rigidbody>();
            for (int i = 0; i < steps; i++)
            {
                Invoke(grapple, "FixedUpdate");
                body.position += body.linearVelocity * Time.fixedDeltaTime;
                Physics.SyncTransforms();
            }
        }

        private PlayerSwordGrapple CreateGrapple()
        {
            var player = capsule.gameObject;
            player.AddComponent<Rigidbody>().useGravity = false;
            player.AddComponent<PlayerState>();
            player.AddComponent<PlayerWeaponLoadout>();
            var controller = player.AddComponent<Character2D5Controller>();
            Invoke(controller, "Awake");
            var weapons = player.AddComponent<WeaponManager>();
            SetField(weapons, "fistWeaponData", Track(ScriptableObject.CreateInstance<MeleeWeaponData>()));
            Invoke(weapons, "Awake");
            var grapple = player.AddComponent<PlayerSwordGrapple>();
            SetField(grapple, "settings", settings);
            Invoke(grapple, "Awake");
            var camera = CreateObject("Grapple test camera", new Vector3(0f, 2f, -10f));
            camera.tag = "MainCamera";
            camera.AddComponent<Camera>();
            return grapple;
        }

        private SwordGrappleVisuals CreateVisualsTemplate()
        {
            var template = CreateObject("Grapple visual template", Vector3.zero).AddComponent<SwordGrappleVisuals>();
            foreach (string name in new[] { "preview", "rope", "reticle" })
            {
                var child = new GameObject(name);
                child.transform.SetParent(template.transform, false);
                var line = child.AddComponent<LineRenderer>();
                line.positionCount = name == "reticle" ? 24 : 2;
                line.enabled = false;
                SetField(template, name, line);
            }
            var sword = new GameObject("sword");
            sword.transform.SetParent(template.transform, false);
            SetField(template, "sword", sword.AddComponent<SpriteRenderer>());
            var details = new GameObject("Aim Details", typeof(MeshFilter), typeof(MeshRenderer));
            details.transform.SetParent(template.transform, false);
            SetField(template, "aimDetails", details.GetComponent<MeshFilter>());
            return template;
        }

        private WeaponInstance EquipSword(PlayerSwordGrapple grapple, int slot, bool draw = true)
        {
            var data = Track(ScriptableObject.CreateInstance<MeleeWeaponData>());
            data.enablesSwordGrapple = true;
            data.maxWeaponDurability = 100;
            var weapon = CreateObject("Equipped sword", Vector3.zero).AddComponent<WeaponInstance>();
            weapon.weaponData = data;
            weapon.transform.SetParent(grapple.transform, false);
            SetField(grapple.GetComponent<PlayerWeaponLoadout>(), "weaponSlot" + slot, weapon);
            if (draw && !grapple.GetComponent<WeaponManager>().IsModCombat)
                Assert.That(grapple.GetComponent<WeaponManager>().TryToggleCombatMode(), Is.True);
            return weapon;
        }

        private bool Resolve(Vector3 direction, out SwordGrappleTarget target, out Vector3 preview)
        {
            Physics.SyncTransforms();
            return SwordGrappleTargeting.TryResolve(capsule, Vector3.right, direction, settings, out target, out preview);
        }

        private BoxCollider CreateWall() => CreateBox(new Vector3(5f, 2f, 0f), new Vector3(1f, 20f, 4f), 12);

        private BoxCollider CreateBox(Vector3 position, Vector3 size, int layer)
        {
            GameObject obj = CreateObject("Grapple test surface", position);
            obj.layer = layer;
            var collider = obj.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private GameObject CreateObject(string name, Vector3 position)
        {
            var obj = Track(new GameObject(name));
            obj.transform.position = testOrigin + position;
            return obj;
        }

        private T Track<T>(T obj) where T : Object { cleanup.Add(obj); return obj; }
        private static void SetField(object obj, string name, object value) => obj.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value);
        private static T GetField<T>(object obj, string name) => (T)obj.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);
        private static void Invoke(object obj, string name) => obj.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, null);
    }
}
