using System.Collections.Generic;
using junklite;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Level authoring tools for 2.5D lanes and collision (see MAIN_MECHANICS.md).
/// Every operation is a single Undo step.
/// </summary>
internal static class LevelLaneTools
{
    private const string Menu = "Tools/JunkLite/Level/";

    [MenuItem(Menu + "Replace Mesh Colliders With Boxes (Selection)")]
    private static void ReplaceMeshCollidersInSelection()
    {
        int replaced = ReplaceMeshColliders(SelectedRoots());
        Debug.Log($"[LevelLaneTools] Replaced {replaced} MeshColliders with fitted BoxColliders.");
    }

    [MenuItem(Menu + "Remove Mesh Colliders (Selection)")]
    private static void RemoveMeshCollidersInSelection()
    {
        int removed = RemoveMeshColliders(SelectedRoots());
        Debug.Log($"[LevelLaneTools] Removed {removed} MeshColliders.");
    }

    [MenuItem(Menu + "Snap Selected To Lane Path")]
    private static void SnapSelected()
    {
        int snapped = SnapToLane(Selection.transforms);
        Debug.Log($"[LevelLaneTools] Snapped {snapped} objects to the lane path.");
    }

    [MenuItem(Menu + "Snap All Enemies To Lane Path")]
    private static void SnapAllEnemies()
    {
        var targets = new List<Transform>();
        foreach (var enemy in Object.FindObjectsByType<EnemyBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            targets.Add(enemy.transform);
        int snapped = SnapToLane(targets);
        Debug.Log($"[LevelLaneTools] Snapped {snapped} enemies to the lane path.");
    }

    [MenuItem(Menu + "Replace Mesh Colliders With Boxes (Selection)", true)]
    [MenuItem(Menu + "Remove Mesh Colliders (Selection)", true)]
    [MenuItem(Menu + "Snap Selected To Lane Path", true)]
    private static bool HasSelection() => Selection.transforms.Length > 0;

    /// <summary>Swaps every MeshCollider under the roots for a BoxCollider fitted to the mesh bounds.</summary>
    public static int ReplaceMeshColliders(IEnumerable<Transform> roots)
    {
        int group = BeginUndo("Replace Mesh Colliders With Boxes");
        int count = 0;

        foreach (var meshCollider in CollectMeshColliders(roots))
        {
            GameObject go = meshCollider.gameObject;
            Mesh mesh = meshCollider.sharedMesh;
            bool isTrigger = meshCollider.isTrigger;
            PhysicsMaterial material = meshCollider.sharedMaterial;
            bool enabled = meshCollider.enabled;

            Undo.DestroyObjectImmediate(meshCollider);
            count++;

            if (mesh == null || go.GetComponent<BoxCollider>() != null) continue;

            var box = Undo.AddComponent<BoxCollider>(go);
            box.center = mesh.bounds.center;
            box.size = mesh.bounds.size;
            box.isTrigger = isTrigger;
            box.sharedMaterial = material;
            box.enabled = enabled;
        }

        Undo.CollapseUndoOperations(group);
        return count;
    }

    public static int RemoveMeshColliders(IEnumerable<Transform> roots)
    {
        int group = BeginUndo("Remove Mesh Colliders");
        int count = 0;
        foreach (var meshCollider in CollectMeshColliders(roots))
        {
            Undo.DestroyObjectImmediate(meshCollider);
            count++;
        }
        Undo.CollapseUndoOperations(group);
        return count;
    }

    /// <summary>Moves each target onto the closest lane point (keeping its height) and sets the lane Y rotation.</summary>
    public static int SnapToLane(IEnumerable<Transform> targets)
    {
        LanePath path = Object.FindAnyObjectByType<LanePath>();
        if (path == null)
        {
            Debug.LogWarning("[LevelLaneTools] No LanePath in the open scene.");
            return 0;
        }

        int group = BeginUndo("Snap To Lane Path");
        int count = 0;
        foreach (var t in targets)
        {
            if (t == null || t.GetComponentInParent<LanePath>() == path) continue;
            if (!path.TryGetClosestPoint(t.position, out Vector3 point, out float yRotation)) continue;

            Undo.RecordObject(t, "Snap To Lane Path");
            t.SetPositionAndRotation(point, Quaternion.Euler(0f, yRotation, 0f));
            count++;
        }
        Undo.CollapseUndoOperations(group);
        return count;
    }

    private static List<MeshCollider> CollectMeshColliders(IEnumerable<Transform> roots)
    {
        var result = new List<MeshCollider>();
        var seen = new HashSet<MeshCollider>();
        foreach (var root in roots)
        {
            if (root == null) continue;
            foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
                if (seen.Add(mc)) result.Add(mc);
        }
        return result;
    }

    private static IEnumerable<Transform> SelectedRoots() => Selection.GetTransforms(SelectionMode.TopLevel);

    private static int BeginUndo(string name)
    {
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(name);
        return Undo.GetCurrentGroup();
    }
}
