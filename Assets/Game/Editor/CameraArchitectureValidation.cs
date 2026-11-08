using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
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

            // A Lock To Target binding re-latches to the player's yaw on every cut, so a respawn or teleport
            // on a lane facing +Z or -X puts the camera on the wrong side. Offsets are authored in world space.
            CinemachineFollow[] follows = Find<CinemachineFollow>();
            foreach (CinemachineFollow follow in follows)
            {
                BindingMode mode = follow.TrackerSettings.BindingMode;
                if (mode != BindingMode.WorldSpace)
                    Report(follow, $"Follow binding is {mode}; set it to World Space (checkpoint respawns and teleports re-latch the frame to the player's yaw).");
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

            // Door cinematics: the trigger plays the shot, and the door must be able to slide during the walk.
            foreach (LaneEntryTrigger entry in Find<LaneEntryTrigger>())
            {
                if (entry.Transition != null && entry.Transition.StartOn != CameraTransition.StartMode.Script)
                    Report(entry, "plays a Transition whose Start On is not Script (its Start box would also fire it).");
                if (entry.WalkThrough == null)
                    Report(entry, "has no Walk Through: E only snaps the player onto the lane inside the room instead of through the door.");
            }

            foreach (SlidingDoor door in Find<SlidingDoor>())
                foreach (Transform leaf in new[] { door.LeftLeaf, door.RightLeaf })
                    if (leaf != null && GameObjectUtility.AreStaticEditorFlagsSet(leaf.gameObject, StaticEditorFlags.BatchingStatic))
                        Report(door, $"leaf '{leaf.name}' is Batching Static, so it can't slide. Clear Batching Static on the leaves.");

            string summary = $"{follows.Length} follow camera(s), {faders.Length} fader(s), {occluders.Length} occluder(s), " +
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
