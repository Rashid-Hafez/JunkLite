using System;
using UnityEditor;
using UnityEngine;

namespace junklite.Editor
{
    public static class EnemyArchitectureValidation
    {
        private static readonly string[] MeleeEnemyPrefabs =
        {
            "Assets/Game/Prefabs/Enemies/Grunt Enemy.prefab",
            "Assets/Game/Prefabs/Enemies/Hyena.prefab",
            "Assets/Game/Prefabs/Enemies/Hyena EASY.prefab",
            "Assets/Game/Prefabs/Enemies/Hyena Blue.prefab",
            "Assets/Game/Prefabs/Enemies/Hyena Green.prefab"
        };

        private const string RobotPrefab = "Assets/Game/Prefabs/Enemies/Robot Enemy.prefab";
        private const string FlyingPrefab = "Assets/Game/Prefabs/Enemies/Flying Dummy.prefab";
        private const string PatrolPrefab = "Assets/Game/Prefabs/Enemies/Patrol Dummy.prefab";
        private const string DummyPrefab = "Assets/Game/Prefabs/Enemies/Dummy.prefab";

        [MenuItem("Tools/JunkLite/Systems/Validate Enemies")]
        public static void ValidateMigratedEnemyPrefabs()
        {
            int errors = 0;

            foreach (string path in MeleeEnemyPrefabs)
                errors += ValidateMeleeEnemy(path);

            errors += ValidateRobot();
            errors += ValidateFlyingDummy();
            errors += ValidatePassiveDummy(PatrolPrefab, true);
            errors += ValidatePassiveDummy(DummyPrefab, false);

            int count = MeleeEnemyPrefabs.Length + 4;
            if (errors == 0)
                Debug.Log($"[EnemyValidation] Passed for {count} migrated enemy prefabs.");
            else
                Debug.LogError($"[EnemyValidation] Failed with {errors} error(s).");
        }

        private static int ValidateMeleeEnemy(string path)
        {
            if (!TryLoad(path, out GameObject prefab, out EnemyCharacter enemy))
                return 1;

            int errors = ValidateCore(path, prefab, enemy, true);
            bool isHyena = path.Contains("Hyena", StringComparison.Ordinal);
            EnemyBrain brain = prefab.GetComponent<EnemyBrain>();

            if ((isHyena && brain is not HyenaBrain)
                || (!isHyena && brain?.GetType() != typeof(MeleeChaserBrain)))
            {
                errors += Error(path, "has the wrong brain type", prefab);
            }

            errors += ValidateOwnedConfiguration(path, brain, prefab);
            errors += ValidateObjectReference(path, brain, "melee", "hitbox", prefab);
            if (isHyena)
                errors += ValidateObjectReference(path, brain, "dash", "dashHitbox", prefab);

            if (enemy.GetCapability<IChaser>() == null
                || enemy.GetCapability<IMeleeAttacker>() == null
                || enemy.GetCapability<IStunnable>() == null
                || (isHyena && (enemy.GetCapability<IPatroller>() == null
                    || enemy.GetCapability<IDodger>() == null
                    || enemy.GetCapability<ICharger>() == null
                    || enemy.GetCapability<IDasher>() == null)))
            {
                errors += Error(path, "has missing composed capabilities", prefab);
            }

            return errors;
        }

        private static int ValidateRobot()
        {
            if (!TryLoad(RobotPrefab, out GameObject prefab, out EnemyCharacter enemy))
                return 1;

            int errors = ValidateCore(RobotPrefab, prefab, enemy, true);
            RobotBrain brain = prefab.GetComponent<RobotBrain>();
            if (brain == null)
                errors += Error(RobotPrefab, "is missing RobotBrain", prefab);
            else
            {
                errors += ValidateOwnedConfiguration(RobotPrefab, brain, prefab);
                errors += ValidateObjectReference(RobotPrefab, brain, "dash", "dashHitbox", prefab);
            }

            return errors;
        }

        private static int ValidateFlyingDummy()
        {
            if (!TryLoad(FlyingPrefab, out GameObject prefab, out EnemyCharacter enemy))
                return 1;

            int errors = ValidateCore(FlyingPrefab, prefab, enemy, true);
            FlyingFollowerBrain brain = prefab.GetComponent<FlyingFollowerBrain>();
            FlyingHoverController hover = prefab.GetComponent<FlyingHoverController>();
            if (brain == null || hover == null)
                return errors + Error(FlyingPrefab, "is missing its brain or hover controller", prefab);

            errors += ValidateOwnedConfiguration(FlyingPrefab, brain, prefab);
            errors += ValidateOwnedConfiguration(FlyingPrefab, hover, prefab);
            if (enemy.GetCapability<IPatroller>() == null
                || enemy.GetCapability<IChaser>() == null
                || enemy.GetCapability<IStunnable>() == null)
            {
                errors += Error(FlyingPrefab, "has missing patrol, chase, or interrupt capability", prefab);
            }

            return errors;
        }

        private static int ValidatePassiveDummy(string path, bool expectsPatrol)
        {
            if (!TryLoad(path, out GameObject prefab, out EnemyCharacter enemy))
                return 1;

            int errors = ValidateCore(path, prefab, enemy, false);
            PassiveEnemyBrain brain = prefab.GetComponent<PassiveEnemyBrain>();
            if (brain == null)
                return errors + Error(path, "is missing PassiveEnemyBrain", prefab);

            errors += ValidateOwnedConfiguration(path, brain, prefab);
            SerializedProperty patrolFlag = new SerializedObject(brain).FindProperty("patrolWhenPassive");
            if (patrolFlag == null || patrolFlag.boolValue != expectsPatrol)
                errors += Error(path, "has the wrong passive patrol setting", prefab);
            if (enemy.GetCapability<IStunnable>() == null)
                errors += Error(path, "has no interrupt recovery capability", prefab);

            return errors;
        }

        private static int ValidateCore(
            string path,
            GameObject prefab,
            EnemyCharacter enemy,
            bool requiresPerception)
        {
            if (enemy == null
                || prefab.GetComponent<StateMachine>() == null
                || prefab.GetComponent<EnemyMovement>() == null
                || prefab.GetComponent<EnemyBrain>() == null)
            {
                return Error(path, "is missing character, brain, FSM, or movement", prefab);
            }

            if (!requiresPerception)
                return 0;

            EnemyPerception perception = prefab.GetComponentInChildren<EnemyPerception>(true);
            return perception == null || enemy.Perception != perception
                ? Error(path, "has a missing or unreferenced perception component", prefab)
                : 0;
        }

        private static int ValidateOwnedConfiguration(
            string path,
            MonoBehaviour component,
            UnityEngine.Object context)
        {
            if (component == null)
                return Error(path, "is missing its configuration owner", context);

            SerializedProperty property = new SerializedObject(component)
                .FindProperty("ownsSerializedConfiguration");
            return property == null || !property.boolValue
                ? Error(path, $"{component.GetType().Name} does not own its serialized tuning", context)
                : 0;
        }

        private static int ValidateObjectReference(
            string path,
            EnemyBrain brain,
            string behaviorName,
            string referenceName,
            UnityEngine.Object context)
        {
            if (brain == null)
                return Error(path, "is missing its configured brain", context);

            SerializedProperty behavior = new SerializedObject(brain).FindProperty(behaviorName);
            SerializedProperty reference = behavior?.FindPropertyRelative(referenceName);
            return reference == null || reference.objectReferenceValue == null
                ? Error(path, $"has no configured {referenceName}", context)
                : 0;
        }

        private static bool TryLoad(
            string path,
            out GameObject prefab,
            out EnemyCharacter enemy)
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            enemy = prefab != null ? prefab.GetComponent<EnemyCharacter>() : null;
            if (prefab != null)
                return true;

            Debug.LogError($"[EnemyValidation] Missing prefab: {path}");
            return false;
        }

        private static int Error(string path, string message, UnityEngine.Object context)
        {
            Debug.LogError($"[EnemyValidation] {path} {message}.", context);
            return 1;
        }
    }
}
