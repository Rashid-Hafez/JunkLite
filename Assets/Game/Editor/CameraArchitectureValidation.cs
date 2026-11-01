using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace junklite.Editor
{
    public static class CameraArchitectureValidation
    {
        [MenuItem("Tools/JunkLite/Systems/Validate Cameras")]
        public static void ValidateOpenSceneCameras()
        {
            int issues = 0;
            void Report(Object context, string message)
            {
                issues++;
                Debug.LogWarning($"[CameraValidation] '{context.name}' {message}", context);
            }

            int occlusionLayer = LayerMask.NameToLayer(CameraOccluder.LayerName);
            if (occlusionLayer < 0)
                Debug.LogError($"[CameraValidation] Layer '{CameraOccluder.LayerName}' is missing from Tags and Layers.");

            CameraOcclusionFader[] faders = Find<CameraOcclusionFader>();
            CameraOccluder[] occluders = Find<CameraOccluder>();
            CameraZone[] zones = Find<CameraZone>();
            CameraTransition[] transitions = Find<CameraTransition>();

            if (faders.Length > 1)
                Report(faders[1], "is a second CameraOcclusionFader; keep exactly one, on the brain camera.");
            if (faders.Length == 0 && occluders.Length > 0)
                Debug.LogWarning("[CameraValidation] The scene has occluders but no CameraOcclusionFader on the main camera.");
            foreach (CameraOcclusionFader fader in faders)
                if (fader.GetComponent<CinemachineBrain>() == null)
                    Report(fader, "should sit on the CinemachineBrain camera.");

            foreach (CameraOccluder occluder in occluders)
            {
                if (occluder.Targets == null || occluder.Targets.Length == 0)
                    Report(occluder, "has no target renderers (assign Art Root).");

                Collider[] volumes = occluder.GetComponents<Collider>();
                if (volumes.Length == 0)
                    Report(occluder, "has no trigger volume.");
                foreach (Collider volume in volumes)
                {
                    if (volume.gameObject.layer != occlusionLayer)
                        Report(occluder, $"volume is not on the {CameraOccluder.LayerName} layer.");
                    if (!volume.isTrigger)
                        Report(occluder, "volume must be a trigger.");
                }
            }

            foreach (CameraZone zone in zones)
            {
                if (zone.RoomCamera == null)
                    Report(zone, "has no room camera.");
                else if (zone.RoomCamera.gameObject.activeSelf)
                    Report(zone, "room camera should start inactive (it would take over the view at load).");
            }

            foreach (CameraTransition transition in transitions)
            {
                if (transition.Shot == null)
                    Report(transition, "has no Shot camera.");
                else if (transition.Shot.gameObject.activeSelf)
                    Report(transition, "Shot should start inactive (it would take over the view at load).");
                if (transition.Arrival == null)
                    Report(transition, "has no Arrival box.");
            }

            string summary = $"{faders.Length} fader(s), {occluders.Length} occluder(s), " +
                             $"{zones.Length} zone(s), {transitions.Length} transition(s)";
            if (issues == 0)
                Debug.Log($"[CameraValidation] Passed for {summary} in the open scene.");
            else
                Debug.LogError($"[CameraValidation] Found {issues} issue(s) across {summary}.");
        }

        private static T[] Find<T>() where T : Object =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }
}
