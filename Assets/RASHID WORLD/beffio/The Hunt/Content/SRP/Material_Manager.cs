using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using UnityEditor;
//using UnityEngine.Experimental.Rendering;
//using UnityEngine.Experimental.Rendering.HDPipeline;




    public class Material_Manager : EditorWindow
    {
    


        static List<MaterialUpgrader> GetHDUpgraders()
        {
            var upgraders = new List<MaterialUpgrader>();
      
            upgraders.Add(new StandardsToHDLitMaterialUpgrader("Standard", "HDRenderPipeline/Lit"));
            upgraders.Add(new StandardsToHDLitMaterialUpgrader("Standard (Specular setup)", "HDRenderPipeline/Lit"));
            upgraders.Add(new StandardsToHDLitMaterialUpgrader("Standard (Roughness setup)", "HDRenderPipeline/Lit"));
            //upgraders.Add(new UnityEditor.Experimental.Rendering.HDPipeline.UnlitsToHDUnlitUpgrader("Unlit/Color", "HDRenderPipeline/Unlit"));
            //upgraders.Add(new UnityEditor.Experimental.Rendering.HDPipeline.UnlitsToHDUnlitUpgrader("Unlit/Texture", "HDRenderPipeline/Unlit"));
            //upgraders.Add(new UnityEditor.Experimental.Rendering.HDPipeline.UnlitsToHDUnlitUpgrader("Unlit/Transparent", "HDRenderPipeline/Unlit"));
            //upgraders.Add(new UnityEditor.Experimental.Rendering.HDPipeline.UnlitsToHDUnlitUpgrader("Unlit/Transparent Cutout", "HDRenderPipeline/Unlit"));
      
        return upgraders;
        }
        
        [MenuItem("Tools/beffio/Material Manager")]
        public static void ShowWindow()
        {
            EditorWindow editorWindow = EditorWindow.GetWindow(typeof(Material_Manager));
            editorWindow.autoRepaintOnSceneChange = true;
            editorWindow.Show();
            editorWindow.titleContent.text = "Material Manager";
        }

        public static List<string> MatFiles = new List<string>();
        int selected_from = 0;
        int selected_to = 1;
        int range = 0;
        string[] options = { "Legacy", "HD SRP", "LW SRP" };
        string[] options_full = { "Standard", "HD", "LW" };
        string[] ranges = { "Project-Wide", "Selected", "Current Scene" };
        bool includestd = true;

        void OnGUI()
        {
            GUILayout.Label("Shader convertion");
            includestd = EditorGUILayout.Toggle("Standard materials", includestd);
            range = EditorGUILayout.Popup("Range:", range, ranges);
            selected_from = EditorGUILayout.Popup("From:", selected_from, options);
            selected_to = EditorGUILayout.Popup("To:", selected_to, options);
            if (GUILayout.Button("Convert"))
            {
                var obj = Selection.activeObject;
                var path = "";
                try { path = AssetDatabase.GetAssetPath(obj.GetInstanceID()); }
                catch { }
                if (range == 1) Convert_Directory(path);
                if (range == 0) Convert_Directory(Application.dataPath);
                if (range == 2) Convert_Scene();
            }

        }



        void Convert_Scene()
        {
          

            var renderers = FindObjectsOfType(typeof(Renderer)) as Renderer[];
            foreach (Renderer r in renderers)
            {
                try
                {
                    var materials = r.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        Shader shaderold = materials[i].shader;
                        string shadername = shaderold.name.ToString();
                        if (shadername == "Standard" && selected_to != 0)
                        {
                            //Changing standard to standard chosen
                            if (selected_to == 1)
                            {
                                //HD
                                // Shader shadernew = Shader.Find("HDRenderPipeline/Lit)");
                            }
                            if (selected_to == 2)
                            {
                                //LW
                                Shader shadernew = Shader.Find("LightweightPipeline/Standard (Physically Based)");
                            if (shadernew != null) materials[i].shader = shadernew;
                            }
                        }
                        if (shadername.IndexOf("/SRP/" + options_full[selected_from]) >= 0)
                        {
                            if (selected_to == 0)
                            {
                                //Changing custom to legacy
                                Shader shadernew = Shader.Find(shadername.Replace("/SRP/LW", "").Replace("/SRP/HD", ""));
                            //Debug.Log("changing shader " + shadername + " to: " + shadernew.name);
                            if (shadernew != null) materials[i].shader = shadernew;
                            }
                            else
                            {
                                //Changing custom SRP to custom SRP
                                try
                                {
                                    string newshadername = shadername.Replace(options_full[selected_from], options_full[selected_to]);
                                    //Debug.Log(options_full[selected_from]);
                                    Shader shadernew = Shader.Find(newshadername);
                                if (shadernew != null) materials[i].shader = shadernew;
                                    //Debug.Log("changing shader " + shadername + " to: " + shadernew.name);
                                }
                                catch
                                { }

                            }
                        }
                        if (shadername.IndexOf("beffio") >= 0 && selected_from == 0)
                        {
                            if (shadername.IndexOf("/SRP/") >= 0)
                            {

                            }
                            else
                            {
                                //Source custom not SRP
                                try
                                {
                                    string newshadername = shadername.Insert(shadername.LastIndexOf("/"), "/SRP/" + options_full[selected_to]);
                                    //Debug.Log(options_full[selected_from]);
                                    Shader shadernew = Shader.Find(newshadername);

                                    if(shadernew!=null) materials[i].shader = shadernew;
                                    //Debug.Log("changing shader " + shadername + " to: " + shadernew.name);
                                }
                                catch
                                { }
                            }
                        }
                    if (shadername.IndexOf("HDRenderPipeline/Lit") >= 0 && selected_to == 0 && selected_from == 1 && includestd)
                    {
                        //Standard HDRP to Standard
                        try
                        {
                            Shader shadernew = Shader.Find("Standard");
                            if (shadernew != null) materials[i].shader = shadernew;
                        }
                        catch { }
                    }
                    if (shadername.IndexOf("LightweightPipeline/Standard") >= 0 && selected_to == 0 && selected_from == 2 && includestd)
                    {
                        //Standard LWRP to Standard
                        try
                        {
                            Shader shadernew = Shader.Find("Standard");
                            if (shadernew != null) materials[i].shader = shadernew;
                        }
                        catch { }
                    }
                }
                }
                catch { }
            }
              if (selected_to == 1&&includestd)
            {
            MaterialUpgrader.UpgradeProjectFolder(GetHDUpgraders(), "Upgrade to HD Material");
            }
        }



        void Convert_Directory(string path)
        {
            
            if (selected_from != selected_to)
            {
                MatFiles.Clear();
                DirSearch(path, true);
                foreach (string mpath in MatFiles)
                {
                try
                {
                    string shortpath = mpath.Substring(mpath.IndexOf("/Asset") + 1);
                    Material m = (Material)AssetDatabase.LoadAssetAtPath(shortpath, typeof(Material));
                    Shader shaderold = m.shader;
                    string shadername = shaderold.name.ToString();

                    if (shadername == "Standard" && selected_to != 0)
                    {
                        //Changing standard to standard chosen
                        if (selected_to == 2)
                        {
                            //LW
                            Shader shadernew = Shader.Find("LightweightPipeline/Standard (Physically Based)");
                            if (shadernew != null) m.shader = shadernew;
                        }
                    }
                    if (shadername.IndexOf("/SRP/" + options_full[selected_from]) >= 0)
                    {
                        if (selected_to == 0)
                        {
                            //Changing custom to legacy
                            Shader shadernew = Shader.Find(shadername.Replace("/SRP/LW", "").Replace("/SRP/HD", ""));
                            //Debug.Log("changing shader " + shadername + " to: " + shadernew.name);
                            if (shadernew != null) m.shader = shadernew;
                        }
                        else
                        {
                            //Changing custom SRP to custom SRP
                            try
                            {
                                string newshadername = shadername.Replace(options_full[selected_from], options_full[selected_to]);
                                //Debug.Log(options_full[selected_from]);
                                Shader shadernew = Shader.Find(newshadername);
                                if (shadernew != null) m.shader = shadernew;
                                //Debug.Log("changing shader " + shadername + " to: " + shadernew.name);
                            }
                            catch
                            { }

                        }
                    }
                    if (shadername.IndexOf("beffio") >= 0 && selected_from == 0)
                    {
                        if (shadername.IndexOf("/SRP/") >= 0)
                        {

                        }
                        else
                        {
                            //Source custom not SRP
                            try
                            {
                                string newshadername = shadername.Insert(shadername.LastIndexOf("/"), "/SRP/" + options_full[selected_to]);
                                //Debug.Log(options_full[selected_from]);
                                Shader shadernew = Shader.Find(newshadername);
                                if (shadernew != null) m.shader = shadernew;
                                //Debug.Log("changing shader " + shadername + " to: " + shadernew.name);
                            }
                            catch
                            { }
                        }
                    }
                    if (shadername.IndexOf("HDRenderPipeline/Lit") >= 0 && selected_to == 0 && selected_from == 1 && includestd)
                    {
                        //Standard HDRP to Standard
                        try
                        {
                            Shader shadernew = Shader.Find("Standard");
                            if (shadernew != null) m.shader = shadernew;
                        }
                        catch { }
                    }
                    if (shadername.IndexOf("LightweightPipeline/Standard") >= 0 && selected_to == 0 && selected_from == 2 && includestd)
                    {
                        //Standard LWRP to Standard
                        try
                        {
                            Shader shadernew = Shader.Find("Standard");
                            if (shadernew != null) m.shader = shadernew;
                        }
                        catch { }
                    }
                }
                catch
                { }
                }
            }
            if (selected_to == 1 && includestd)
            {
            MaterialUpgrader.UpgradeProjectFolder(GetHDUpgraders(), "Upgrade to HD Material");
            }
        }

        static void DirSearch(string sDir, bool first)
        {
            if (sDir.IndexOf(".mat") >= 0)
            {
                string f = sDir;
                string pref = "";
                try { pref = f.Substring(f.Length - 4); }
                catch { }
                if (pref.IndexOf(".mat") >= 0)
                {
                    MatFiles.Add(f);
                    //Debug.Log(f);

                }
            }
            else
            {
                if (first)
                {


                    try
                    {

                        foreach (string f in Directory.GetFiles(sDir))
                        {
                            string pref = "";
                            try { pref = f.Substring(f.Length - 4); }
                            catch { }
                            if (pref.IndexOf(".mat") >= 0)
                            {
                                MatFiles.Add(f);
                                //Debug.Log(f);

                            }
                        }
                    }
                    catch { }

                }

                try
                {
                    foreach (string d in Directory.GetDirectories(sDir))
                    {
                        foreach (string f in Directory.GetFiles(d))
                        {
                            string pref = "";
                            try { pref = f.Substring(f.Length - 4); }
                            catch { }
                            if (pref.IndexOf(".mat") >= 0)
                            {
                                MatFiles.Add(f);
                                //Debug.Log(f);

                            }
                        }
                        DirSearch(d, false);
                    }
                }



                catch (System.Exception excpt)
                {
                    Debug.Log(excpt.Message);
                }
            }
        }


    }

public static class DialogText
{
    public static readonly string title = "Material Upgrader";
    public static readonly string proceed = "Proceed";
    public static readonly string ok = "Ok";
    public static readonly string cancel = "Cancel";
    public static readonly string noSelectionMessage = "You must select at least one material.";
    public static readonly string projectBackMessage = "Make sure to have a project backup before proceeding.";
}

public class MaterialUpgrader
{
    public delegate void MaterialFinalizer(Material mat);

    string m_OldShader;
    string m_NewShader;

    MaterialFinalizer m_Finalizer;

    Dictionary<string, string> m_TextureRename = new Dictionary<string, string>();
    Dictionary<string, string> m_FloatRename = new Dictionary<string, string>();
    Dictionary<string, string> m_ColorRename = new Dictionary<string, string>();

    Dictionary<string, float> m_FloatPropertiesToSet = new Dictionary<string, float>();
    Dictionary<string, Color> m_ColorPropertiesToSet = new Dictionary<string, Color>();
    List<string> m_TexturesToRemove = new List<string>();
    Dictionary<string, Texture> m_TexturesToSet = new Dictionary<string, Texture>();


    class KeywordFloatRename
    {
        public string keyword;
        public string property;
        public float setVal, unsetVal;
    }
    List<KeywordFloatRename> m_KeywordFloatRename = new List<KeywordFloatRename>();

    [Flags]
    public enum UpgradeFlags
    {
        None = 0,
        LogErrorOnNonExistingProperty = 1,
        CleanupNonUpgradedProperties = 2,
        LogMessageWhenNoUpgraderFound = 4
    }

    public void Upgrade(Material material, UpgradeFlags flags)
    {
        Material newMaterial;
        if ((flags & UpgradeFlags.CleanupNonUpgradedProperties) != 0)
        {
            newMaterial = new Material(Shader.Find(m_NewShader));
        }
        else
        {
            newMaterial = UnityEngine.Object.Instantiate(material) as Material;
            newMaterial.shader = Shader.Find(m_NewShader);
        }

        Convert(material, newMaterial);

        material.shader = Shader.Find(m_NewShader);
        material.CopyPropertiesFromMaterial(newMaterial);
        UnityEngine.Object.DestroyImmediate(newMaterial);

        if (m_Finalizer != null)
            m_Finalizer(material);
    }

    // Overridable function to implement custom material upgrading functionality
    public virtual void Convert(Material srcMaterial, Material dstMaterial)
    {
        foreach (var t in m_TextureRename)
        {
            dstMaterial.SetTextureScale(t.Value, srcMaterial.GetTextureScale(t.Key));
            dstMaterial.SetTextureOffset(t.Value, srcMaterial.GetTextureOffset(t.Key));
            dstMaterial.SetTexture(t.Value, srcMaterial.GetTexture(t.Key));
        }

        foreach (var t in m_FloatRename)
            dstMaterial.SetFloat(t.Value, srcMaterial.GetFloat(t.Key));

        foreach (var t in m_ColorRename)
            dstMaterial.SetColor(t.Value, srcMaterial.GetColor(t.Key));

        foreach (var prop in m_TexturesToRemove)
            dstMaterial.SetTexture(prop, null);

        foreach (var prop in m_TexturesToSet)
            dstMaterial.SetTexture(prop.Key, prop.Value);

        foreach (var prop in m_FloatPropertiesToSet)
            dstMaterial.SetFloat(prop.Key, prop.Value);

        foreach (var prop in m_ColorPropertiesToSet)
            dstMaterial.SetColor(prop.Key, prop.Value);
        foreach (var t in m_KeywordFloatRename)
            dstMaterial.SetFloat(t.property, srcMaterial.IsKeywordEnabled(t.keyword) ? t.setVal : t.unsetVal);
    }

    public void RenameShader(string oldName, string newName, MaterialFinalizer finalizer = null)
    {
        m_OldShader = oldName;
        m_NewShader = newName;
        m_Finalizer = finalizer;
    }

    public void RenameTexture(string oldName, string newName)
    {
        m_TextureRename[oldName] = newName;
    }

    public void RenameFloat(string oldName, string newName)
    {
        m_FloatRename[oldName] = newName;
    }

    public void RenameColor(string oldName, string newName)
    {
        m_ColorRename[oldName] = newName;
    }

    public void RemoveTexture(string name)
    {
        m_TexturesToRemove.Add(name);
    }

    public void SetFloat(string propertyName, float value)
    {
        m_FloatPropertiesToSet[propertyName] = value;
    }

    public void SetColor(string propertyName, Color value)
    {
        m_ColorPropertiesToSet[propertyName] = value;
    }

    public void SetTexture(string propertyName, Texture value)
    {
        m_TexturesToSet[propertyName] = value;
    }

    public void RenameKeywordToFloat(string oldName, string newName, float setVal, float unsetVal)
    {
        m_KeywordFloatRename.Add(new KeywordFloatRename { keyword = oldName, property = newName, setVal = setVal, unsetVal = unsetVal });
    }

    static bool IsMaterialPath(string path)
    {
        return path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase);
    }

    static MaterialUpgrader GetUpgrader(List<MaterialUpgrader> upgraders, Material material)
    {
        if (material == null || material.shader == null)
            return null;

        string shaderName = material.shader.name;
        for (int i = 0; i != upgraders.Count; i++)
        {
            if (upgraders[i].m_OldShader == shaderName)
                return upgraders[i];
        }

        return null;
    }

    //@TODO: Only do this when it exceeds memory consumption...
    static void SaveAssetsAndFreeMemory()
    {
        AssetDatabase.SaveAssets();
        GC.Collect();
        EditorUtility.UnloadUnusedAssetsImmediate();
        AssetDatabase.Refresh();
    }

    public static void UpgradeProjectFolder(List<MaterialUpgrader> upgraders, string progressBarName, UpgradeFlags flags = UpgradeFlags.None)
    {
        if (!EditorUtility.DisplayDialog(DialogText.title, "The upgrade will overwrite materials in your project. " + DialogText.projectBackMessage, DialogText.proceed, DialogText.cancel))
            return;

        int totalMaterialCount = 0;
        foreach (string s in UnityEditor.AssetDatabase.GetAllAssetPaths())
        {
            if (IsMaterialPath(s))
                totalMaterialCount++;
        }

        int materialIndex = 0;
        foreach (string path in UnityEditor.AssetDatabase.GetAllAssetPaths())
        {
            if (IsMaterialPath(path))
            {
                materialIndex++;
                if (UnityEditor.EditorUtility.DisplayCancelableProgressBar(progressBarName, string.Format("({0} of {1}) {2}", materialIndex, totalMaterialCount, path), (float)materialIndex / (float)totalMaterialCount))
                    break;

                Material m = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path) as Material;
                Upgrade(m, upgraders, flags);

                //SaveAssetsAndFreeMemory();
            }
        }

        UnityEditor.EditorUtility.ClearProgressBar();
    }

    public static void Upgrade(Material material, MaterialUpgrader upgrader, UpgradeFlags flags)
    {
        var upgraders = new List<MaterialUpgrader>();
        upgraders.Add(upgrader);
        Upgrade(material, upgraders, flags);
    }

    public static void Upgrade(Material material, List<MaterialUpgrader> upgraders, UpgradeFlags flags)
    {
        if (material == null)
            return;

        var upgrader = GetUpgrader(upgraders, material);

        if (upgrader != null)
            upgrader.Upgrade(material, flags);
        else if ((flags & UpgradeFlags.LogMessageWhenNoUpgraderFound) == UpgradeFlags.LogMessageWhenNoUpgraderFound)
            Debug.Log(string.Format("{0} material was not upgraded. There's no upgrader to convert {1} shader to selected pipeline", material.name, material.shader.name));
    }

    public static void UpgradeSelection(List<MaterialUpgrader> upgraders, string progressBarName, UpgradeFlags flags = UpgradeFlags.None)
    {
        var selection = Selection.objects;

        if (selection == null)
        {
            EditorUtility.DisplayDialog(DialogText.title, DialogText.noSelectionMessage, DialogText.ok);
            return;
        }

        List<Material> selectedMaterials = new List<Material>(selection.Length);
        for (int i = 0; i < selection.Length; ++i)
        {
            Material mat = selection[i] as Material;
            if (mat != null)
                selectedMaterials.Add(mat);
        }

        int selectedMaterialsCount = selectedMaterials.Count;
        if (selectedMaterialsCount == 0)
        {
            EditorUtility.DisplayDialog(DialogText.title, DialogText.noSelectionMessage, DialogText.ok);
            return;
        }

        if (!EditorUtility.DisplayDialog(DialogText.title, string.Format("The upgrade will overwrite {0} selected material{1}. ", selectedMaterialsCount, selectedMaterialsCount > 1 ? "s" : "") +
                DialogText.projectBackMessage, DialogText.proceed, DialogText.cancel))
            return;

        string lastMaterialName = "";
        for (int i = 0; i < selectedMaterialsCount; i++)
        {
            if (UnityEditor.EditorUtility.DisplayCancelableProgressBar(progressBarName, string.Format("({0} of {1}) {2}", i, selectedMaterialsCount, lastMaterialName), (float)i / (float)selectedMaterialsCount))
                break;

            var material = selectedMaterials[i];
            Upgrade(material, upgraders, flags);
            if (material != null)
                lastMaterialName = material.name;
        }

        UnityEditor.EditorUtility.ClearProgressBar();
    }
}

public class StandardsToHDLitMaterialUpgrader : MaterialUpgrader
{
    static readonly string Standard = "Standard";
    static readonly string Standard_Spec = "Standard (Specular setup)";
    static readonly string Standard_Rough = "Standard (Roughness setup)";

    public StandardsToHDLitMaterialUpgrader(string sourceShaderName, string destShaderName, MaterialFinalizer finalizer = null)
    {
        RenameShader(sourceShaderName, destShaderName, finalizer);

        RenameTexture("_MainTex", "_BaseColorMap");
        RenameColor("_Color", "_BaseColor");
        RenameFloat("_Glossiness", "_Smoothness");
        RenameTexture("_BumpMap", "_NormalMap");
        RenameFloat("_BumpScale", "_NormalScale");
        RenameTexture("_ParallaxMap", "_HeightMap");
        RenameTexture("_EmissionMap", "_EmissiveColorMap");
        RenameTexture("_DetailAlbedoMap", "_DetailMap");
        RenameFloat("_UVSec", "_UVDetail");
        SetFloat("_LinkDetailsWithBase", 0);
        RenameFloat("_DetailNormalMapScale", "_DetailNormalScale");
        RenameFloat("_Cutoff", "_AlphaCutoff");
        RenameKeywordToFloat("_ALPHATEST_ON", "_AlphaCutoffEnable", 1f, 0f);


        if (sourceShaderName == Standard)
        {
            SetFloat("_MaterialID", 1f);
        }

        if (sourceShaderName == Standard_Spec)
        {
            SetFloat("_MaterialID", 4f);

            RenameColor("_SpecColor", "_SpecularColor");
            RenameTexture("_SpecGlossMap", "_SpecularColorMap");
        }
    }
}
