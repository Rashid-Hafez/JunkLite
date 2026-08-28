using UnityEditor;
using UnityEngine;

namespace junklite.Editor
{
    public static class EncounterArchitectureValidation
    {
        [MenuItem("Tools/JunkLite/Systems/Validate Encounters")]
        public static void ValidateOpenSceneEncounters()
        {
            EncounterController[] encounters = Object.FindObjectsByType<EncounterController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            int issues = 0;
            foreach (EncounterController encounter in encounters)
            {
                if (encounter != null)
                    issues += encounter.ValidateConfiguration();
            }

            if (encounters.Length == 0)
            {
                Debug.LogWarning("[EncounterValidation] The open scene contains no encounters.");
                return;
            }

            if (issues == 0)
            {
                Debug.Log(
                    $"[EncounterValidation] Passed for {encounters.Length} encounter(s) in the open scene.");
            }
            else
            {
                Debug.LogError(
                    $"[EncounterValidation] Found {issues} configuration issue(s) " +
                    $"across {encounters.Length} encounter(s).");
            }
        }
    }
}
