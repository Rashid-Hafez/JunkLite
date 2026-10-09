using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    public sealed class MovementAndCameraRegressionTests
    {
        private readonly List<GameObject> cleanupObjects = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = cleanupObjects.Count - 1; i >= 0; i--)
            {
                if (cleanupObjects[i] != null)
                    Object.DestroyImmediate(cleanupObjects[i]);
            }

            cleanupObjects.Clear();
        }

        [Test]
        public void DashUpdatesVelocityInThePhysicsStepWithoutRestartingItsTimer()
        {
            Character2D5Controller controller = CreateController();
            Rigidbody body = controller.GetComponent<Rigidbody>();
            controller.StartDash(Vector3.right);
            float deadline = GetField<float>(controller, "dashEndTime");

            Assert.That(body.linearVelocity.x, Is.Zero);
            Invoke(controller, "FixedUpdate");

            Assert.That(body.linearVelocity.x, Is.GreaterThan(0f));
            Assert.That(GetField<float>(controller, "dashEndTime"), Is.EqualTo(deadline));
            Assert.That(controller.IsDashing, Is.True);
        }

        [TestCase("movement")]
        [TestCase("physics")]
        [TestCase("kinematic")]
        public void TakingControlEndsDashOnceAndPreservesItsCooldown(string lockKind)
        {
            Character2D5Controller controller = CreateController();
            int endedCount = 0;
            controller.OnDashEnded += () => endedCount++;
            controller.StartDash(Vector3.right);
            float cooldown = GetField<float>(controller, "dashCooldownTimer");

            IDisposable controlLock = lockKind switch
            {
                "movement" => controller.AcquireMovementLock(),
                "physics" => controller.AcquirePhysicsOverride(),
                _ => controller.AcquireKinematicLock()
            };

            try
            {
                controller.StartDash(Vector3.right);
                Assert.That(controller.IsDashing, Is.False);
                Assert.That(controller.CanDash, Is.False);
                controller.InterruptSpecialMovement();
                Assert.That(endedCount, Is.EqualTo(1));
            }
            finally
            {
                controlLock.Dispose();
            }

            Assert.That(controller.IsDashing, Is.False);
            Assert.That(GetField<float>(controller, "dashCooldownTimer"), Is.EqualTo(cooldown));
        }

        [Test]
        public void OverlappingKinematicLocksRestoreTheBodyOnlyAfterTheFinalRelease()
        {
            Character2D5Controller controller = CreateController();
            Rigidbody body = controller.GetComponent<Rigidbody>();
            controller.StartDash(Vector3.right);

            IDisposable first = controller.AcquireKinematicLock();
            IDisposable second = controller.AcquireKinematicLock();
            first.Dispose();
            Assert.That(body.isKinematic, Is.True);
            Assert.That(controller.IsDashing, Is.False);

            second.Dispose();
            Assert.That(body.isKinematic, Is.False);
            Assert.That(controller.IsDashing, Is.False);
        }

        [Test]
        public void ExternalKinematicChangeCancelsDashWithoutVelocityWarnings()
        {
            Character2D5Controller controller = CreateController();
            Rigidbody body = controller.GetComponent<Rigidbody>();
            controller.StartDash(Vector3.right);
            body.isKinematic = true;

            Invoke(controller, "FixedUpdate");
            controller.TeleportTo(Vector3.up);
            controller.RotatePLayer(90f);
            controller.ApplyExternalImpulse(Vector3.right);

            Assert.That(controller.IsDashing, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DisablingLocomotionOrTheControllerEndsDashOnce(bool disableLocomotion)
        {
            Character2D5Controller controller = CreateController();
            int endedCount = 0;
            controller.OnDashEnded += () => endedCount++;
            controller.StartDash(Vector3.right);

            if (disableLocomotion)
                controller.SetLocomotionEnabled(false);
            else
            {
                controller.enabled = false;
                Invoke(controller, "OnDisable");
            }

            Assert.That(controller.IsDashing, Is.False);
            Assert.That(controller.CanDash, Is.False);
            Assert.That(endedCount, Is.EqualTo(1));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MissingBrainOrBlendRestoresTheAuthoredSpriteRotation(bool hasBrain)
        {
            CameraSwitchTrigger trigger = CreateTrigger();
            trigger.cinemachineBrain = hasBrain ? CreateBrain() : null;
            Transform sprite = CreateObject("Player Sprite").transform;
            Quaternion restRotation = Quaternion.Euler(0f, 12f, 5f);
            sprite.localRotation = restRotation;

            var rotation = trigger.BillboardRotate(sprite);
            Assert.That(rotation.MoveNext(), Is.True);
            Assert.That(Quaternion.Angle(sprite.localRotation, restRotation), Is.GreaterThan(1f));
            Assert.That(rotation.MoveNext(), Is.False);
            Assert.That(Quaternion.Angle(sprite.localRotation, restRotation), Is.LessThan(0.01f));
        }

        [TestCase("OnDisable")]
        [TestCase("ResetToDefaultState")]
        public void InterruptedCameraTransitionRestoresSpriteRotation(string interruption)
        {
            CameraSwitchTrigger trigger = CreateTrigger();
            Transform sprite = CreateObject("Player Sprite").transform;
            Quaternion restRotation = Quaternion.Euler(0f, 12f, 5f);
            sprite.localRotation = restRotation;

            var rotation = trigger.BillboardRotate(sprite);
            Assert.That(rotation.MoveNext(), Is.True);
            Invoke(trigger, interruption);
            Assert.That(Quaternion.Angle(sprite.localRotation, restRotation), Is.LessThan(0.01f));
            ((IDisposable)rotation).Dispose();
        }

        [Test]
        public void MissingSceneryReferencesDoNotBreakHidingOrDeathReset()
        {
            CameraSwitchTrigger trigger = CreateTrigger();
            GameObject scenery = CreateObject("Scenery");
            SetField(trigger, "objectsToHide", new[] { scenery, null });

            Invoke(trigger, "HideObjects");
            Assert.That(scenery.activeSelf, Is.False);
            Invoke(trigger, "ResetToDefaultState");
            Assert.That(scenery.activeSelf, Is.True);

            SetField(trigger, "objectsToHide", null);
            Assert.DoesNotThrow(() => Invoke(trigger, "HideObjects"));
        }

        private Character2D5Controller CreateController()
        {
            GameObject player = CreateObject("Movement Test Player");
            player.AddComponent<CapsuleCollider>();
            player.AddComponent<Rigidbody>().useGravity = false;
            Character2D5Controller controller = player.AddComponent<Character2D5Controller>();
            Invoke(controller, "Awake");
            return controller;
        }

        private CameraSwitchTrigger CreateTrigger()
        {
            GameObject triggerObject = CreateObject("Camera Test Trigger");
            triggerObject.AddComponent<BoxCollider>();
            CameraSwitchTrigger trigger = triggerObject.AddComponent<CameraSwitchTrigger>();
            Invoke(trigger, "Awake");
            return trigger;
        }

        private CinemachineBrain CreateBrain()
        {
            GameObject cameraObject = CreateObject("Test Camera");
            cameraObject.AddComponent<Camera>();
            return cameraObject.AddComponent<CinemachineBrain>();
        }

        private GameObject CreateObject(string name)
        {
            var instance = new GameObject(name);
            cleanupObjects.Add(instance);
            return instance;
        }

        private static void Invoke(object target, string methodName)
        {
            target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, null);
        }

        private static T GetField<T>(object target, string fieldName)
        {
            return (T)target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(target);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }
    }
}
