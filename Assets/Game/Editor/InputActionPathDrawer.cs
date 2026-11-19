using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace junklite.Editor
{
    /// <summary>
    /// Dropdown for <see cref="InputActionPathAttribute"/> fields. Lists every action of the project's
    /// InputSystem_Actions asset as "Map/Action" (the path TutorialPromptHUD resolves), plus "(none)" for text only.
    /// </summary>
    [CustomPropertyDrawer(typeof(InputActionPathAttribute))]
    public sealed class InputActionPathDrawer : PropertyDrawer
    {
        private const string NoneLabel = "(none: text only)";

        private static List<string> paths;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            List<string> options = Options();
            string current = property.stringValue ?? string.Empty;
            // Keep a value that is no longer mapped visible instead of silently dropping it.
            bool missing = current.Length > 0 && !options.Contains(current);
            var shown = new List<string> { NoneLabel };
            shown.AddRange(options);
            if (missing) shown.Add(current + " (missing)");

            int index = current.Length == 0 ? 0 : missing ? shown.Count - 1 : options.IndexOf(current) + 1;
            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUI.Popup(position, label.text, index, shown.ToArray());
            if (EditorGUI.EndChangeCheck() && picked != index)
                property.stringValue = picked == 0 ? string.Empty : picked <= options.Count ? options[picked - 1] : current;
            EditorGUI.EndProperty();
        }

        private static List<string> Options()
        {
            if (paths != null && paths.Count > 0) return paths;

            paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("InputSystem_Actions t:InputActionAsset", new[] { "Assets" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                foreach (InputActionMap map in asset.actionMaps)
                    foreach (InputAction action in map.actions)
                        paths.Add(map.name + "/" + action.name);
                break;
            }
            return paths;
        }
    }
}
