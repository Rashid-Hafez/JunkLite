using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace junklite.Tests
{
    public sealed class EnemyArchitectureTests
    {
        private const string GruntPrefabPath = "Assets/Game/ENEMIES/Grunt/Grunt Enemy.prefab";
        private const string RobotPrefabPath = "Assets/Game/Prefabs/Enemies/Robot Enemy.prefab";
        private const string FlyingPrefabPath = "Assets/Game/Prefabs/Enemies/Flying Dummy.prefab";
        private const string PatrolPrefabPath = "Assets/Game/Prefabs/Enemies/Patrol Dummy.prefab";
        private const string DummyPrefabPath = "Assets/Game/Prefabs/Enemies/Dummy.prefab";

        private static readonly string[] HyenaPrefabPaths =
        {
            "Assets/Game/Prefabs/Enemies/Hyena.prefab",
            "Assets/Game/Prefabs/Enemies/Hyena EASY.prefab",
            "Assets/Game/Prefabs/Enemies/Hyena Blue.prefab",
            "Assets/Game/Prefabs/Enemies/Hyena Green.prefab"
        };

        [Test]
        public void GruntPrefabUsesReusableMeleeChaserBrain()
        {
            GameObject prefab = LoadPrefab(GruntPrefabPath);
            EnemyCharacter enemy = prefab.GetComponent<EnemyCharacter>();
            MeleeChaserBrain brain = prefab.GetComponent<MeleeChaserBrain>();

            Assert.That(enemy, Is.Not.Null);
            Assert.That(brain, Is.Not.Null);
            Assert.That(prefab.GetComponent<HyenaBrain>(), Is.Null);
            AssertCommonEnemyComposition(prefab, enemy);
            AssertConfiguredAttack(brain, "melee", "hitbox");
        }

        [TestCaseSource(nameof(HyenaPrefabPaths))]
        public void HyenaPrefabsUseFocusedHyenaBrain(string prefabPath)
        {
            GameObject prefab = LoadPrefab(prefabPath);
            EnemyCharacter enemy = prefab.GetComponent<EnemyCharacter>();
            HyenaBrain brain = prefab.GetComponent<HyenaBrain>();

            Assert.That(enemy, Is.Not.Null, prefabPath);
            Assert.That(brain, Is.Not.Null, prefabPath);
            AssertCommonEnemyComposition(prefab, enemy);
            AssertConfiguredAttack(brain, "melee", "hitbox");
            AssertConfiguredAttack(brain, "dash", "dashHitbox");
        }

        [Test]
        public void MigratedPrefabsExposeTheirComposedCapabilities()
        {
            EnemyCharacter grunt = LoadPrefab(GruntPrefabPath).GetComponent<EnemyCharacter>();
            Assert.That(grunt.GetCapability<IChaser>(), Is.Not.Null);
            Assert.That(grunt.GetCapability<IMeleeAttacker>(), Is.Not.Null);
            Assert.That(grunt.GetCapability<IStunnable>(), Is.Not.Null);
            Assert.That(grunt.GetCapability<IDasher>(), Is.Null);

            foreach (string prefabPath in HyenaPrefabPaths)
            {
                EnemyCharacter hyena = LoadPrefab(prefabPath).GetComponent<EnemyCharacter>();
                Assert.That(hyena.GetCapability<IPatroller>(), Is.Not.Null, prefabPath);
                Assert.That(hyena.GetCapability<IChaser>(), Is.Not.Null, prefabPath);
                Assert.That(hyena.GetCapability<IMeleeAttacker>(), Is.Not.Null, prefabPath);
                Assert.That(hyena.GetCapability<IDodger>(), Is.Not.Null, prefabPath);
                Assert.That(hyena.GetCapability<ICharger>(), Is.Not.Null, prefabPath);
                Assert.That(hyena.GetCapability<IDasher>(), Is.Not.Null, prefabPath);
                Assert.That(hyena.GetCapability<IStunnable>(), Is.Not.Null, prefabPath);
            }
        }

        [Test]
        public void RobotPrefabUsesFocusedBrainAndRuntimeAttackCapability()
        {
            GameObject prefab = LoadPrefab(RobotPrefabPath);
            EnemyCharacter enemy = prefab.GetComponent<EnemyCharacter>();
            RobotBrain brain = prefab.GetComponent<RobotBrain>();

            Assert.That(brain, Is.Not.Null);
            AssertCommonEnemyComposition(prefab, enemy);
            AssertConfiguredAttack(brain, "dash", "dashHitbox");

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                EnemyCharacter runtimeEnemy = instance.GetComponent<EnemyCharacter>();
                Assert.That(runtimeEnemy.GetCapability<IPatroller>(), Is.Not.Null);
                Assert.That(runtimeEnemy.GetCapability<ICharger>(), Is.Not.Null);
                Assert.That(runtimeEnemy.GetCapability<IDasher>(), Is.Not.Null);
                Assert.That(runtimeEnemy.GetCapability<IGrabber>(), Is.Not.Null);
                Assert.That(runtimeEnemy.GetCapability<IRecoverer>(), Is.Not.Null);
                Assert.That(runtimeEnemy.GetCapability<IStunnable>(), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FlyingDummySeparatesFollowingFromHoverPhysics()
        {
            GameObject prefab = LoadPrefab(FlyingPrefabPath);
            EnemyCharacter enemy = prefab.GetComponent<EnemyCharacter>();

            Assert.That(prefab.GetComponent<FlyingFollowerBrain>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<FlyingHoverController>(), Is.Not.Null);
            AssertCommonEnemyComposition(prefab, enemy);
            AssertOwnsSerializedConfiguration(prefab.GetComponent<FlyingHoverController>());
            Assert.That(enemy.GetCapability<IPatroller>(), Is.Not.Null);
            Assert.That(enemy.GetCapability<IChaser>(), Is.Not.Null);
            Assert.That(enemy.GetCapability<IStunnable>(), Is.Not.Null);
        }

        [TestCase(PatrolPrefabPath, true)]
        [TestCase(DummyPrefabPath, false)]
        public void PassiveDummiesUsePassiveBrain(string prefabPath, bool patrols)
        {
            GameObject prefab = LoadPrefab(prefabPath);
            EnemyCharacter enemy = prefab.GetComponent<EnemyCharacter>();
            PassiveEnemyBrain brain = prefab.GetComponent<PassiveEnemyBrain>();

            Assert.That(brain, Is.Not.Null);
            AssertCommonEnemyComposition(prefab, enemy, false);

            SerializedProperty patrolFlag = new SerializedObject(brain)
                .FindProperty("patrolWhenPassive");
            Assert.That(patrolFlag, Is.Not.Null);
            Assert.That(patrolFlag.boolValue, Is.EqualTo(patrols));
            Assert.That(enemy.GetCapability<IStunnable>(), Is.Not.Null);
        }

        [TestCase("Assets/Game/Scripts/New Enemies/Robot Enemy/RobotEnemy.cs")]
        [TestCase("Assets/Game/Scripts/New Enemies/Dummy Enemy/FlyingDummy.cs")]
        [TestCase("Assets/Game/Scripts/New Enemies/Dummy Enemy/PatrolDummy.cs")]
        public void MigratedEnemyIdentityClassesDoNotOwnDecisionStateMachines(string sourcePath)
        {
            string source = File.ReadAllText(sourcePath);
            StringAssert.DoesNotContain("InitializeStateMachine", source);
            StringAssert.DoesNotContain("OnPlayerSpotted", source);
            StringAssert.DoesNotContain("ChangeState<", source);
        }

        [Test]
        public void EnemyDeathEventFiresExactlyOnce()
        {
            GameObject prefab = LoadPrefab(HyenaPrefabPaths[0]);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);

            try
            {
                EnemyCharacter enemy = instance.GetComponent<EnemyCharacter>();
                StateMachine machine = instance.GetComponent<StateMachine>();
                machine.RegisterState(new DeadState(enemy));

                int deathCount = 0;
                enemy.Died += _ => deathCount++;

                MethodInfo handleDeath = typeof(EnemyCharacter).GetMethod(
                    "HandleDeath",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(handleDeath, Is.Not.Null);
                handleDeath.Invoke(enemy, null);
                handleDeath.Invoke(enemy, null);

                Assert.That(deathCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void DeadGruntLandsOnGroundWithoutBeingPushedByEnemies()
        {
            Scene scene = SceneManager.CreateScene(
                "Enemy Corpse Collision Test",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            GameObject corpse = null;
            GameObject ground = null;
            GameObject otherEnemy = null;

            try
            {
                corpse = UnityEngine.Object.Instantiate(LoadPrefab(GruntPrefabPath));
                SceneManager.MoveGameObjectToScene(corpse, scene);
                corpse.transform.SetPositionAndRotation(new Vector3(0f, 4f, 0f), Quaternion.identity);
                corpse.transform.localScale = Vector3.one;

                ground = new GameObject("Ground", typeof(BoxCollider));
                ground.layer = LayerMask.NameToLayer("Ground");
                SceneManager.MoveGameObjectToScene(ground, scene);
                ground.transform.position = Vector3.down * 0.5f;
                ground.GetComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f);

                EnemyCharacter enemy = corpse.GetComponent<EnemyCharacter>();
                corpse.GetComponent<StateMachine>().RegisterState(new DeadState(enemy));
                MethodInfo handleDeath = typeof(EnemyCharacter).GetMethod(
                    "HandleDeath",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(handleDeath, Is.Not.Null);
                handleDeath.Invoke(enemy, null);

                Rigidbody body = corpse.GetComponent<Rigidbody>();
                CapsuleCollider capsule = corpse.GetComponent<CapsuleCollider>();
                Assert.That(capsule.enabled, Is.True);
                Assert.That(capsule.isTrigger, Is.False);
                Assert.That(body.isKinematic, Is.False);
                Assert.That(body.useGravity, Is.True);

                PhysicsScene physicsScene = scene.GetPhysicsScene();
                Physics.SyncTransforms();
                for (int tick = 0; tick < 150; tick++)
                    physicsScene.Simulate(0.02f);

                Assert.That(body.position.y, Is.LessThan(3f), "The corpse should fall under gravity.");
                Assert.That(capsule.bounds.min.y, Is.InRange(-0.05f, 0.1f),
                    "The corpse should land on the ground instead of falling through it.");

                otherEnemy = new GameObject("Moving Enemy", typeof(BoxCollider), typeof(Rigidbody));
                otherEnemy.layer = LayerMask.NameToLayer("Enemies");
                SceneManager.MoveGameObjectToScene(otherEnemy, scene);
                Rigidbody otherBody = otherEnemy.GetComponent<Rigidbody>();
                otherBody.isKinematic = true;
                otherBody.position = new Vector3(-3f, body.position.y, 0f);
                Physics.SyncTransforms();

                Vector3 settledPosition = body.position;
                for (int tick = 1; tick <= 100; tick++)
                {
                    otherBody.MovePosition(new Vector3(-3f + tick * 0.06f, settledPosition.y, 0f));
                    physicsScene.Simulate(0.02f);
                }

                Assert.That(Mathf.Abs(body.position.x - settledPosition.x), Is.LessThan(0.05f),
                    "An enemy moving through the corpse should not push it.");
                Assert.That(capsule.bounds.min.y, Is.InRange(-0.05f, 0.1f));
            }
            finally
            {
                if (otherEnemy != null) UnityEngine.Object.DestroyImmediate(otherEnemy);
                if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
                if (corpse != null) UnityEngine.Object.DestroyImmediate(corpse);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void MovementAndLevelSequenceDoNotInspectEnemyStates()
        {
            string movementSource = File.ReadAllText(
                "Assets/Game/Scripts/New Enemies/Robot Enemy/EnemyMovement.cs");
            string levelSequenceSource = File.ReadAllText(
                "Assets/Game/Scripts/Managers/Level 0 Sequence Manager.cs");

            StringAssert.DoesNotContain("StateMachine", movementSource);
            StringAssert.DoesNotContain("StunnedState", movementSource);
            StringAssert.DoesNotContain("DeadState", levelSequenceSource);
            StringAssert.Contains("encounter.EnemyDied += OnEncounterEnemyDied", levelSequenceSource);
        }

        private static GameObject LoadPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            return prefab;
        }

        private static void AssertCommonEnemyComposition(
            GameObject prefab,
            EnemyCharacter enemy,
            bool requiresPerception = true)
        {
            Assert.That(enemy, Is.Not.Null);
            Assert.That(prefab.GetComponent<StateMachine>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<EnemyMovement>(), Is.Not.Null);
            if (requiresPerception)
                Assert.That(enemy.Perception, Is.Not.Null);
            Assert.That(prefab.GetComponent<EnemyBrain>(), Is.Not.Null);

            AssertOwnsSerializedConfiguration(prefab.GetComponent<EnemyBrain>());
        }

        private static void AssertOwnsSerializedConfiguration(MonoBehaviour component)
        {
            SerializedObject serializedComponent = new(component);
            Assert.That(
                serializedComponent.FindProperty("ownsSerializedConfiguration").boolValue,
                Is.True,
                $"{component.name} should own its serialized tuning.");
        }

        private static void AssertConfiguredAttack(
            EnemyBrain brain,
            string behaviorPropertyName,
            string hitboxPropertyName)
        {
            SerializedObject serializedBrain = new(brain);
            SerializedProperty behavior = serializedBrain.FindProperty(behaviorPropertyName);
            Assert.That(behavior, Is.Not.Null, behaviorPropertyName);

            SerializedProperty hitbox = behavior.FindPropertyRelative(hitboxPropertyName);
            Assert.That(hitbox, Is.Not.Null, hitboxPropertyName);
            Assert.That(hitbox.objectReferenceValue, Is.Not.Null, $"{brain.name} {hitboxPropertyName}");
        }
    }
}
