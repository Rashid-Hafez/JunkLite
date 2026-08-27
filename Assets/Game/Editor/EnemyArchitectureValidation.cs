using UnityEditor;
using UnityEngine;

namespace junklite.Editor
{
    public static class EnemyArchitectureValidation
    {
        private static readonly string[] MigratedEnemyPrefabs =
        {
            "Assets/Game/Prefabs/Enemies/Grunt Enemy.prefab",
            "Assets/Game/Prefabs/Enemies/Hyena.prefab",
            "Assets/Game/Prefabs/Enemies/Hyena EASY.prefab",
            "Assets/Game/Prefabs/Enemies/Hyena Blue.prefab",
            "Assets/Game/Prefabs/Enemies/Hyena Green.prefab"
        };

        [MenuItem("Tools/JunkLite/Systems/Validate Enemies")]
        public static void ValidateMigratedEnemyPrefabs()
        {
            int errors = 0;

            foreach (string path in MigratedEnemyPrefabs)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogError($"[EnemyValidation] Missing prefab: {path}");
                    errors++;
                    continue;
                }

                EnemyCharacter enemy = prefab.GetComponent<EnemyCharacter>();
                EnemyBrain brain = prefab.GetComponent<EnemyBrain>();
                EnemyPerception perception = prefab.GetComponentInChildren<EnemyPerception>(true);

                if (enemy == null || brain == null || perception == null
                    || prefab.GetComponent<StateMachine>() == null
                    || prefab.GetComponent<EnemyMovement>() == null)
                {
                    Debug.LogError(
                        $"[EnemyValidation] {path} is missing character, brain, perception, FSM, or movement.",
                        prefab);
                    errors++;
                    continue;
                }

                if (enemy.Perception != perception)
                {
                    Debug.LogError(
                        $"[EnemyValidation] {path} does not reference its perception component.",
                        prefab);
                    errors++;
                }

                bool isHyenaPrefab = path.Contains("Hyena");
                if ((isHyenaPrefab && brain is not HyenaBrain)
                    || (!isHyenaPrefab && brain.GetType() != typeof(MeleeChaserBrain)))
                {
                    Debug.LogError($"[EnemyValidation] {path} has the wrong brain type.", prefab);
                    errors++;
                }

                SerializedObject serializedBrain = new(brain);
                SerializedProperty ownsConfiguration =
                    serializedBrain.FindProperty("ownsSerializedConfiguration");
                SerializedProperty melee = serializedBrain.FindProperty("melee");
                SerializedProperty meleeHitbox = melee?.FindPropertyRelative("hitbox");

                if (ownsConfiguration == null || !ownsConfiguration.boolValue
                    || meleeHitbox == null || meleeHitbox.objectReferenceValue == null)
                {
                    Debug.LogError(
                        $"[EnemyValidation] {path} has incomplete brain configuration.",
                        prefab);
                    errors++;
                }

                if (isHyenaPrefab)
                {
                    SerializedProperty dash = serializedBrain.FindProperty("dash");
                    SerializedProperty dashHitbox = dash?.FindPropertyRelative("dashHitbox");
                    if (dashHitbox == null || dashHitbox.objectReferenceValue == null)
                    {
                        Debug.LogError($"[EnemyValidation] {path} has no dash hitbox.", prefab);
                        errors++;
                    }
                }

                if (enemy.GetCapability<IChaser>() == null
                    || enemy.GetCapability<IMeleeAttacker>() == null
                    || enemy.GetCapability<IStunnable>() == null
                    || (isHyenaPrefab && (enemy.GetCapability<IPatroller>() == null
                        || enemy.GetCapability<IDodger>() == null
                        || enemy.GetCapability<ICharger>() == null
                        || enemy.GetCapability<IDasher>() == null)))
                {
                    Debug.LogError($"[EnemyValidation] {path} has missing composed capabilities.", prefab);
                    errors++;
                }
            }

            if (errors == 0)
                Debug.Log($"[EnemyValidation] Passed for {MigratedEnemyPrefabs.Length} migrated enemy prefabs.");
            else
                Debug.LogError($"[EnemyValidation] Failed with {errors} error(s).");
        }
    }
}
