using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using junklite;

[InitializeOnLoad]
internal static class DevUiSmokeCheck
{
    static DevUiSmokeCheck()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
                EditorApplication.delayCall += Check;
        };
    }

    private static void Check()
    {
        if (!EditorApplication.isPlaying)
            return;
        var input = GameInputManager.Instance;
        Debug.Log($"[Dev UI check] manager={GameManager.Instance != null}; input={input != null}; " +
            $"playerMap={input?.controls?.Player.enabled}; uiMap={input?.controls?.UI.enabled}; " +
            $"pauseAction={input?.controls?.Player.Pause.enabled}; keyboard={Keyboard.current?.enabled}; " +
            $"focus={Application.isFocused}; uiReady={GameUIManager.Instance?.IsPauseMenuReady}");
        GameManager.Instance?.PauseGame();
    }
}
