using UnityEditor;
using UnityEngine;

namespace junklite.Editor
{
    /// <summary>Editor-facing switch and status window for the in-game developer panel.</summary>
    [InitializeOnLoad]
    public sealed class RuntimeDevToolsWindow : EditorWindow
    {
        private const string WindowMenuPath = "JunkLite/Dev Tools Console";
        private const string ToggleMenuPath = "JunkLite/Dev Tools/Enable In-Game Panel";

        static RuntimeDevToolsWindow()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            EditorApplication.delayCall += RefreshMenuCheckmark;
        }

        [MenuItem(WindowMenuPath)]
        private static void OpenWindow()
        {
            RuntimeDevToolsWindow window = GetWindow<RuntimeDevToolsWindow>();
            window.titleContent = new GUIContent("Dev Tools");
            window.minSize = new Vector2(360f, 220f);
            window.Show();
        }

        [MenuItem(ToggleMenuPath)]
        private static void ToggleFromMenu()
        {
            SetEnabled(!IsEnabled);
        }

        [MenuItem(ToggleMenuPath, true)]
        private static bool ValidateToggleMenu()
        {
            RefreshMenuCheckmark();
            return true;
        }

        private static bool IsEnabled => EditorRuntimeDevToolsPanel.IsFeatureEnabled;

        private void OnGUI()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.LabelField("Runtime Developer Tools", EditorStyles.boldLabel);

            EditorGUILayout.Space(12f);
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.ToggleLeft(
                "Enable in-game dev tools",
                IsEnabled,
                EditorStyles.boldLabel);
            if (EditorGUI.EndChangeCheck())
                SetEnabled(enabled);

            EditorGUILayout.Space(10f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string status = !EditorApplication.isPlaying
                    ? IsEnabled ? "Opens when Play Mode starts" : "Tools disabled"
                    : IsEnabled
                        ? EditorRuntimeDevToolsPanel.IsPanelVisible
                            ? "Play Mode: panel visible"
                            : "Play Mode: panel hidden"
                        : "Play Mode: tools disabled";
                EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("F10: hide / show during play", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(10f);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || !IsEnabled))
            {
                string buttonLabel = EditorRuntimeDevToolsPanel.IsPanelVisible
                    ? "Hide Game View Panel"
                    : "Show Game View Panel";
                if (GUILayout.Button(buttonLabel, GUILayout.Height(30f)))
                    EditorRuntimeDevToolsPanel.SetPanelVisible(!EditorRuntimeDevToolsPanel.IsPanelVisible);
            }
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private static void SetEnabled(bool enabled)
        {
            EditorRuntimeDevToolsPanel.SetFeatureEnabled(enabled);
            RefreshMenuCheckmark();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !IsEnabled)
                return;

            EditorApplication.delayCall += () =>
                EditorRuntimeDevToolsPanel.SetFeatureEnabled(true);
        }

        private static void RefreshMenuCheckmark()
        {
            Menu.SetChecked(ToggleMenuPath, IsEnabled);
        }
    }
}
