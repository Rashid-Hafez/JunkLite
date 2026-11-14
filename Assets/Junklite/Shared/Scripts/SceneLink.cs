using UnityEngine;
using UnityEngine.SceneManagement;

namespace Junklite.Shared
{
    /// <summary>
    /// Loads a scene by name. Point a UnityEvent, such as a menu button's, at <see cref="Load()"/>.
    /// Disabling the component stops it loading anything.
    /// </summary>
    public class SceneLink : MonoBehaviour
    {
        [SerializeField] string sceneName;

        public string SceneName
        {
            get => sceneName;
            set => sceneName = value;
        }

        public void Load()
        {
            if (isActiveAndEnabled) Load(sceneName);
        }

        public static void Load(string name)
        {
            if (Application.CanStreamedLevelBeLoaded(name))
            {
                SceneManager.LoadScene(name);
                return;
            }

#if UNITY_EDITOR
            // Not in the build's scene list yet. That's fine in the Editor, but it has to be added before building.
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Scene " + name))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;

                Debug.LogWarning($"Scene '{name}' isn't in File > Build Profiles > Scene List, so builds won't find it.");
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
                return;
            }
#endif
            Debug.LogError($"Couldn't load scene '{name}'. Add it to File > Build Profiles > Scene List.");
        }
    }
}
