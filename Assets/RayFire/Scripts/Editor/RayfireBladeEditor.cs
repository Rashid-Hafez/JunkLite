using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEditorInternal;
using RayFire;

namespace RayFireEditor
{
    [CanEditMultipleObjects]
    [CustomEditor (typeof(RayfireBlade))]
    public class RayfireBladeEditor : Editor
    {
        RayfireBlade    blade;
        ReorderableList rl_targets_list; 
        
        // Minimum & Maximum ranges
        const float damage_min   = 0f;
        const float damage_max   = 50f;
        const float force_min    = 0;
        const float force_max    = 10f;
        const float cooldown_min = 0;
        const float cooldown_max = 10f;
        
        // Serialized properties
        SerializedProperty sp_actionType;
        SerializedProperty sp_onTrigger;
        SerializedProperty sp_sliceType;
        SerializedProperty sp_gizmo;
        SerializedProperty sp_damage;
        SerializedProperty sp_skin;
        SerializedProperty sp_force;
        SerializedProperty sp_affectInactive;
        SerializedProperty sp_cooldown;
        SerializedProperty sp_targets;
        SerializedProperty sp_tag;
        SerializedProperty sp_mask;
        

        private void OnEnable()
        {
            // Get component
            blade = (RayfireBlade)target;
            
            // Set tag list
            RFUI.SetTags();
            
            // Collect layers
            RFUI.SetLayers();
            
            // Find properties
            sp_actionType     = serializedObject.FindProperty(nameof(blade.actionType));
            sp_onTrigger      = serializedObject.FindProperty(nameof(blade.onTrigger));
            sp_sliceType      = serializedObject.FindProperty(nameof(blade.sliceType));
            sp_gizmo          = serializedObject.FindProperty(nameof(blade.showGizmo));
            sp_damage         = serializedObject.FindProperty(nameof(blade.damage));
            sp_skin           = serializedObject.FindProperty(nameof(blade.skin));
            sp_force          = serializedObject.FindProperty(nameof(blade.force));
            sp_affectInactive = serializedObject.FindProperty(nameof(blade.affectInactive));
            sp_cooldown       = serializedObject.FindProperty(nameof(blade.cooldown));
            sp_targets        = serializedObject.FindProperty(nameof(blade.targets));
            sp_tag            = serializedObject.FindProperty(nameof(blade.tagFilter));
            sp_mask           = serializedObject.FindProperty(nameof(blade.mask));
            
            // Reorderable list
            rl_targets_list = new ReorderableList (serializedObject, sp_targets, true, true, true, true)
            {
                drawElementCallback = DrawInitListItems,
                drawHeaderCallback = DrawInitHeader,
                onAddCallback = AddInit,
                onRemoveCallback = RemoveInit
            };
        }
        
        /// /////////////////////////////////////////////////////////
        /// Inspector
        /// /////////////////////////////////////////////////////////

        public override void OnInspectorGUI()
        {
            // Update changed properties
            serializedObject.Update();
            
            // Cooldown
            if (Application.isPlaying == true && blade.coolDownState == true)
                GUILayout.Label (TextBld.gui_cooldown.text);
            
            // GUI
            GUI_Properties();
            GUI_Force();
            GUI_Filters();
            GUI_Targets();

            // Apply changes
            serializedObject.ApplyModifiedProperties();
        }
        
        /// /////////////////////////////////////////////////////////
        /// Properties
        /// /////////////////////////////////////////////////////////

        void GUI_Properties()
        {
            RFUI.CaptionBox (TextBld.gui_cap_prop);
            RFUI.PropertyField (sp_actionType, TextBld.gui_actionType);
            RFUI.PropertyField (sp_onTrigger,  TextBld.gui_onTrigger);
            RFUI.PropertyField (sp_sliceType,  TextBld.gui_sliceType);
            RFUI.PropertyField (sp_gizmo,  TextBld.gui_gizmo);
            RFUI.Slider (sp_damage, damage_min, damage_max, TextBld.gui_damage);
            RFUI.PropertyField (sp_skin, TextBld.gui_skin);
        }

        void GUI_Force()
        {
            if (blade.actionType == RayfireBlade.ActionType.Demolish)
                return;
            
            RFUI.CaptionBox (TextBld.gui_cap_force);
            RFUI.Slider (sp_force, force_min, force_max, TextBld.gui_force);
            //RFUI.PropertyField (sp_affectInactive, TextBld.gui_affectInactive);
        }
        
        void GUI_Filters()
        {
            RFUI.CaptionBox (TextBld.gui_cap_filter);
            RFUI.Slider (sp_cooldown, cooldown_min, cooldown_max, TextBld.gui_cooldown);
            RFUI.TagField (sp_tag, TextBld.gui_tagFilter);
            RFUI.MaskField (sp_mask, TextBld.gui_mask);
        }
        
        void GUI_Targets()
        {
            RFUI.CaptionBox (TextBld.gui_cap_targets);
            rl_targets_list.DoLayoutList();
            if (Application.isPlaying == true && blade.HasTargets == true)
                if (GUILayout.Button (TextBld.gui_btn_slice, GUILayout.Height (25)))
                    foreach (var bl in targets)
                        if (bl as RayfireBlade != null)
                            (bl as RayfireBlade).SliceTarget();
        }
        
        /// /////////////////////////////////////////////////////////
        /// ReorderableList draw
        /// /////////////////////////////////////////////////////////
        
        void DrawInitListItems(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = rl_targets_list.serializedProperty.GetArrayElementAtIndex(index);
            EditorGUI.PropertyField(new Rect(rect.x, rect.y+2, EditorGUIUtility.currentViewWidth - 80f, EditorGUIUtility.singleLineHeight), element, GUIContent.none);
        }
        
        void DrawInitHeader(Rect rect)
        {
            rect.x += 10;
            EditorGUI.LabelField(rect, TextBld.gui_targ);
        }

        void AddInit(ReorderableList list)
        {
            if (blade.targets == null)
                blade.targets = new List<GameObject>();
            blade.targets.Add (null);
            list.index = list.count;
        }
        
        void RemoveInit(ReorderableList list)
        {
            if (blade.HasTargets == true)
            {
                blade.targets.RemoveAt (list.index);
                list.index = list.index - 1;
            }
        }
        
        /// /////////////////////////////////////////////////////////
        /// Methods
        /// /////////////////////////////////////////////////////////
        
        [DrawGizmo (GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        static void DrawGizmosSelected (RayfireBlade targ, GizmoType gizmoType)
        {
            // Draw bounding gizmo
            GizmoDraw (targ);
        }
        
        static void GizmoDraw (RayfireBlade targ)
        {
            if (targ.showGizmo == true)
            {
                // Gizmo properties
                Gizmos.color = RFUI.color_blue;
           
                Vector3 p1 = new Vector3 (1f,     1f,     0f);
                Vector3 p2 = new Vector3 (1f,     -1f,    0f);
                Vector3 p3 = new Vector3 (-1f,    -1f,    0f);
                Vector3 p4 = new Vector3 (-1f,    1f,     0f);
                Vector3 p5 = new Vector3 (0.85f,  0.85f,  0f);
                Vector3 p6 = new Vector3 (-0.85f, -0.85f, 0f);
                if (targ.sliceType == PlaneType.YZ)
                {
                    p1.x = 0;
                    p2.x = 0;
                    p3.x = 0;
                    p4.x = 0;
                    p1.z = 1f;
                    p2.z = 1f;
                    p3.z = -1f;
                    p4.z = -1f;
                    p5.x = 0;
                    p5.z = -0.85f;
                    p6.x = 0;
                    p6.z = 0.85f;
                }
                else if (targ.sliceType == PlaneType.XZ)
                {
                    p1.y = 0;
                    p2.y = 0;
                    p3.y = 0;
                    p4.y = 0;
                    p1.z = -1f;
                    p2.z = 1f;
                    p3.z = 1f;
                    p4.z = -1f;
                    p5.y = 0;
                    p5.z = -0.85f;
                    p6.y = 0;
                    p6.z = 0.85f;
                }
                
                Gizmos.DrawLine (targ.transform.TransformPoint (p5), targ.transform.TransformPoint (p6));
                Gizmos.DrawLine (targ.transform.TransformPoint (p1), targ.transform.TransformPoint (p2));
                Gizmos.DrawLine (targ.transform.TransformPoint (p3), targ.transform.TransformPoint (p2));
                Gizmos.DrawLine (targ.transform.TransformPoint (p1), targ.transform.TransformPoint (p4));
                Gizmos.DrawLine (targ.transform.TransformPoint (p3), targ.transform.TransformPoint (p4));
            }
        }
    }
}