using UnityEditor;
using UnityEngine;

namespace junklite.Editor
{
    /// <summary>Explicit asset creation. Does not open/save scenes or rewrite enemy prefabs.</summary>
    public static class BurnVisualSetup
    {
        public const string ProfilePath = "Assets/Game/Resources/Burn Visual Profile.asset";
        private const string Folder = "Assets/Game/VFX/Burn";

        [MenuItem("Tools/JunkLite/VFX/Install Burn Visual Assets")]
        public static void Install()
        {
            EnsureFolder("Assets/Game/Resources");
            EnsureFolder(Folder);
            Texture2D noise = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Burn Noise.asset");
            if (noise == null)
            {
                // Small seamless noise: four translated samples blended across both wrap boundaries.
                const int size = 64;
                noise = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
                { name = "Burn Noise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float value = TileNoise(u, v, 5f) * 0.75f + TileNoise(u, v, 11f) * 0.25f;
                    byte gray = (byte)Mathf.RoundToInt(Mathf.Clamp01((value - 0.25f) * 2f) * 255);
                    pixels[y * size + x] = new Color32(gray, gray, gray, 255);
                }
                noise.SetPixels32(pixels);
                noise.Apply(false, true);
                AssetDatabase.CreateAsset(noise, Folder + "/Burn Noise.asset");
            }

            Material heat = CreateMaterial("Burn Heat", "JunkLite/VFX/Burn Heat", noise);
            Material flames = CreateMaterial("Burn Flames", "JunkLite/VFX/Burn Flames", noise);
            BurnVisualProfile profile = AssetDatabase.LoadAssetAtPath<BurnVisualProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<BurnVisualProfile>();
                profile.heatMaterial = heat;
                profile.flameMaterial = flames;
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            else
            {
                if (profile.heatMaterial == null) profile.heatMaterial = heat;
                if (profile.flameMaterial == null) profile.flameMaterial = flames;
                EditorUtility.SetDirty(profile);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Burn visual assets installed. EnemyCharacter binds the presenter automatically; no scene wiring required.");
        }

        private static float TileNoise(float u, float v, float frequency)
        {
            float x = u * frequency + 17.3f, y = v * frequency + 9.1f;
            return Mathf.Lerp(Mathf.Lerp(Mathf.PerlinNoise(x, y), Mathf.PerlinNoise(x - frequency, y), u),
                Mathf.Lerp(Mathf.PerlinNoise(x, y - frequency), Mathf.PerlinNoise(x - frequency, y - frequency), u), v);
        }

        private static Material CreateMaterial(string name, string shaderName, Texture2D noise)
        {
            string path = Folder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException("Missing shader: " + shaderName);
            material = new Material(shader) { name = name };
            material.SetTexture("_NoiseTex", noise);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
