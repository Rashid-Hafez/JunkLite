using UnityEditor;
using UnityEngine;

namespace junklite.Editor
{
    [CustomEditor(typeof(PlayerProgressionConfig))]
    public sealed class PlayerProgressionConfigEditor : UnityEditor.Editor
    {
        private int maximumLevel = 20;
        private int initialCost = 100;
        private int costGrowth = 50;
        private float healthGrowth = 0f;
        private float damageGrowth = 0f;
        private int previewLevel = 1;
        private float baseHealth = 100f;
        private float baseDamage = 20f;
        private float baseArmor;
        private bool generatorExpanded;

        public override void OnInspectorGUI()
        {
            var profile = (PlayerProgressionConfig)target;
            EditorGUILayout.HelpBox("Each table entry awards the next level and one upgrade point (see Resources/UpgradeTreeConfig). " +
                "Optional per-level bonuses accumulate; percentages are additive. " +
                "Increment Revision after rebalancing a published profile. Preview does not alter the character save.", MessageType.Info);
            DrawDefaultInspector();
            if (!profile.Validate(out string error)) EditorGUILayout.HelpBox(error, MessageType.Error);

            EditorGUILayout.Space();
            generatorExpanded = EditorGUILayout.Foldout(generatorExpanded, "Generate Level Table", true);
            if (generatorExpanded)
            {
                maximumLevel = EditorGUILayout.IntSlider("Maximum Level", maximumLevel, 2, 100);
                initialCost = Mathf.Max(1, EditorGUILayout.IntField("First XP Cost", initialCost));
                costGrowth = Mathf.Max(0, EditorGUILayout.IntField("XP Cost Increase", costGrowth));
                healthGrowth = Mathf.Max(0, EditorGUILayout.FloatField("Health Per Level", healthGrowth));
                damageGrowth = Mathf.Max(0, EditorGUILayout.FloatField("Damage % Per Level", damageGrowth));
                if (GUILayout.Button("Replace Table (Undo Supported)"))
                {
                    Undo.RecordObject(profile, "Generate Progression Levels");
                    profile.Generate(maximumLevel, initialCost, costGrowth, healthGrowth, damageGrowth);
                    EditorUtility.SetDirty(profile);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Cumulative Preview", EditorStyles.boldLabel);
            previewLevel = EditorGUILayout.IntSlider("Level", previewLevel, 1, profile.MaxLevel);
            baseHealth = EditorGUILayout.FloatField("Base Health", baseHealth);
            baseDamage = EditorGUILayout.FloatField("Base Attack Damage", baseDamage);
            baseArmor = EditorGUILayout.FloatField("Base Armor", baseArmor);
            EditorGUILayout.LabelField("Total XP", profile.ExperienceToReach(previewLevel).ToString("N0"));
            EditorGUILayout.LabelField("Upgrade Points From Levels", (previewLevel - 1).ToString());
            EditorGUILayout.LabelField("Maximum Health", profile.Evaluate(PlayerStat.MaxHealth, baseHealth, previewLevel).ToString("0.##"));
            EditorGUILayout.LabelField("Attack Damage (before defense)", profile.Evaluate(PlayerStat.OutgoingDamage, baseDamage, previewLevel).ToString("0.##"));
            EditorGUILayout.LabelField("Armor", profile.Evaluate(PlayerStat.Armor, baseArmor, previewLevel).ToString("0.##"));

            if (Application.isPlaying && PlayerProgression.Instance != null)
            {
                var progression = PlayerProgression.Instance;
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("Temporary preview: combat XP and preview rewards are not saved. Exit preview to restore the saved XP.", MessageType.Info);
                if (GUILayout.Button("Preview This Level In Play Mode")) progression.PreviewLevel(previewLevel);
                if (GUILayout.Button("Grant 100 Preview XP"))
                {
                    if (!progression.IsPreviewing) progression.PreviewLevel(progression.Level);
                    progression.AddExperience(100);
                }
                if (GUILayout.Button("End Preview")) progression.EndPreview();
            }
        }
    }
}
