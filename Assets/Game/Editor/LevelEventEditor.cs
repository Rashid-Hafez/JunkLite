using System;
using System.Collections.Generic;
using System.Linq;
using junklite;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Designer Inspector for <see cref="LevelEvent"/>: source fields that match the chosen source,
/// and a reorderable step list with an "Add Action" menu, one-line summaries and setup warnings.
/// </summary>
[CustomEditor(typeof(LevelEvent))]
public class LevelEventEditor : Editor
{
    private const float Pad = 4f;

    private SerializedProperty source, sourceObjective, sourceDoor, playOnce, waitUntilOutOfCombat, actions;
    private ReorderableList list;

    private void OnEnable()
    {
        source = serializedObject.FindProperty("source");
        sourceObjective = serializedObject.FindProperty("sourceObjective");
        sourceDoor = serializedObject.FindProperty("sourceDoor");
        playOnce = serializedObject.FindProperty("playOnce");
        waitUntilOutOfCombat = serializedObject.FindProperty("waitUntilOutOfCombat");
        actions = serializedObject.FindProperty("actions");

        list = new ReorderableList(serializedObject, actions, true, true, true, true)
        {
            drawHeaderCallback = r => EditorGUI.LabelField(r, "Actions (run top to bottom)"),
            elementHeightCallback = ElementHeight,
            drawElementCallback = DrawElement,
            onAddDropdownCallback = (r, l) => ShowAddMenu(),
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var evt = (LevelEvent)target;

        EditorGUILayout.PropertyField(source);
        var src = (LevelEventSource)source.enumValueIndex;
        if (src == LevelEventSource.ObjectiveStarted || src == LevelEventSource.ObjectiveCompleted)
        {
            EditorGUILayout.PropertyField(sourceObjective);
            if (sourceObjective.objectReferenceValue == null)
                EditorGUILayout.HelpBox("Pick the objective that fires this event.", MessageType.Warning);
        }
        if (src == LevelEventSource.DoorOpened)
        {
            EditorGUILayout.PropertyField(sourceDoor);
            if (sourceDoor.objectReferenceValue == null)
                EditorGUILayout.HelpBox("Pick the door that fires this event.", MessageType.Warning);
        }
        if (src == LevelEventSource.TriggerEnter && !evt.TryGetComponent(out Collider _))
        {
            EditorGUILayout.HelpBox("Trigger Enter needs a collider on this object.", MessageType.Warning);
            if (GUILayout.Button("Add Box Trigger"))
            {
                var box = Undo.AddComponent<BoxCollider>(evt.gameObject);
                box.isTrigger = true;
                box.size = new Vector3(2f, 4f, 6f);
                box.center = new Vector3(0f, 2f, 0f);
            }
        }

        EditorGUILayout.PropertyField(playOnce);
        EditorGUILayout.PropertyField(waitUntilOutOfCombat);
        EditorGUILayout.Space();
        list.DoLayoutList();

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(evt.IsRunning ? "Running..." : "Play Now")) evt.Play();
            if (GUILayout.Button("Abort")) evt.Abort();
            EditorGUILayout.EndHorizontal();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private float ElementHeight(int index)
    {
        SerializedProperty element = actions.GetArrayElementAtIndex(index);
        float h = EditorGUIUtility.singleLineHeight + Pad;
        if (!element.isExpanded) return h + Pad;
        ForEachChild(element, child => h += EditorGUI.GetPropertyHeight(child, true) + EditorGUIUtility.standardVerticalSpacing);
        if (Problem(element) != null) h += EditorGUIUtility.singleLineHeight * 1.6f;
        return h + Pad * 2f;
    }

    private void DrawElement(Rect rect, int index, bool active, bool focused)
    {
        SerializedProperty element = actions.GetArrayElementAtIndex(index);
        var action = element.managedReferenceValue as LevelEventAction;
        string problem = Problem(element);

        rect.y += Pad * 0.5f;
        var line = new Rect(rect.x + 10f, rect.y, rect.width - 10f, EditorGUIUtility.singleLineHeight);
        string title = action == null ? "(missing action type)" : $"{index + 1}. {action.Summary}";
        GUIContent label = problem != null
            ? new GUIContent(title, EditorGUIUtility.IconContent("console.warnicon.sml").image, problem)
            : new GUIContent(title);
        element.isExpanded = EditorGUI.Foldout(line, element.isExpanded, label, true);
        if (!element.isExpanded) return;

        float y = line.yMax + Pad;
        EditorGUI.indentLevel++;
        ForEachChild(element, child =>
        {
            float ch = EditorGUI.GetPropertyHeight(child, true);
            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, ch), child, true);
            y += ch + EditorGUIUtility.standardVerticalSpacing;
        });
        EditorGUI.indentLevel--;

        if (problem != null)
            EditorGUI.HelpBox(new Rect(rect.x + 10f, y, rect.width - 10f, EditorGUIUtility.singleLineHeight * 1.5f), problem, MessageType.Warning);
    }

    private static string Problem(SerializedProperty element) =>
        element.managedReferenceValue is LevelEventAction action ? action.Validate() : "Action type is missing.";

    private static void ForEachChild(SerializedProperty element, Action<SerializedProperty> draw)
    {
        SerializedProperty child = element.Copy();
        SerializedProperty end = element.GetEndProperty();
        if (!child.NextVisible(true)) return;
        while (!SerializedProperty.EqualContents(child, end))
        {
            draw(child);
            if (!child.NextVisible(false)) break;
        }
    }

    private void ShowAddMenu()
    {
        var menu = new GenericMenu();
        foreach ((Type type, string path) in ActionTypes())
        {
            menu.AddItem(new GUIContent(path), false, () =>
            {
                serializedObject.Update();
                int i = actions.arraySize;
                actions.InsertArrayElementAtIndex(i);
                SerializedProperty element = actions.GetArrayElementAtIndex(i);
                element.managedReferenceValue = Activator.CreateInstance(type);
                element.isExpanded = true;
                serializedObject.ApplyModifiedProperties();
            });
        }
        menu.ShowAsContext();
    }

    private static IEnumerable<(Type, string)> ActionTypes() =>
        TypeCache.GetTypesDerivedFrom<LevelEventAction>()
            // Public only: hides private test helpers that also derive from LevelEventAction.
            .Where(t => !t.IsAbstract && !t.IsGenericType && (t.IsPublic || t.IsNestedPublic) && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (t, t.GetCustomAttributes(typeof(LevelEventActionMenuAttribute), false)
                .Cast<LevelEventActionMenuAttribute>().FirstOrDefault()?.Path ?? ObjectNames.NicifyVariableName(t.Name)))
            .OrderBy(p => p.Item2);
}

/// <summary>Create and validate LevelEvents.</summary>
public static class LevelEventTools
{
    [MenuItem("GameObject/JunkLite/Level Event", false, 10)]
    private static void Create(MenuCommand command)
    {
        var go = new GameObject("LevelEvent");
        var parent = command.context as GameObject ?? GameObject.Find("[Gameplay]");
        GameObjectUtility.SetParentAndAlign(go, parent);
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(2f, 4f, 6f);
        box.center = new Vector3(0f, 2f, 0f);
        go.AddComponent<LevelEvent>();
        Undo.RegisterCreatedObjectUndo(go, "Create Level Event");
        Selection.activeObject = go;
    }

    [MenuItem("Tools/JunkLite/Level/Validate Level Events")]
    public static int Validate()
    {
        int problems = 0;
        ObjectiveTracker[] trackers = UnityEngine.Object.FindObjectsByType<ObjectiveTracker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (LevelEvent evt in UnityEngine.Object.FindObjectsByType<LevelEvent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ObjectiveTracker tracker = trackers.FirstOrDefault(t => t.gameObject.scene == evt.gameObject.scene);
            void Report(string message)
            {
                problems++;
                Debug.LogWarning($"[LevelEvent] {evt.name}: {message}", evt);
            }

            if (evt.Source == LevelEventSource.TriggerEnter && !evt.TryGetComponent(out Collider _)) Report("Trigger Enter source has no collider.");
            if ((evt.Source == LevelEventSource.ObjectiveStarted || evt.Source == LevelEventSource.ObjectiveCompleted) && evt.SourceObjective == null) Report("No source objective.");
            if (evt.Source == LevelEventSource.DoorOpened && evt.SourceDoor == null) Report("No source door.");
            if (evt.Actions.Count == 0) Report("No actions.");

            for (int i = 0; i < evt.Actions.Count; i++)
            {
                LevelEventAction action = evt.Actions[i];
                if (action == null) { Report($"Action {i + 1} is missing its type."); continue; }
                string problem = action.Validate();
                if (problem != null) Report($"Action {i + 1} ({action.Summary}): {problem}");

                ObjectiveDefinition objective = action is StartObjectiveAction s ? s.objective
                    : action is CompleteObjectiveAction c ? c.objective : null;
                if (objective != null && (tracker == null || !tracker.Objectives.Contains(objective)))
                    Report($"Objective {objective.name} is not in the scene ObjectiveTracker list (Missions tab will not show it).");
            }
        }
        Debug.Log(problems == 0 ? "[LevelEvent] All level events OK." : $"[LevelEvent] {problems} problem(s) found.");
        return problems;
    }
}
