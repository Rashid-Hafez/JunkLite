using UnityEngine;

namespace junklite
{
    /// <summary>Read-only objective config. Per-run completion lives on <see cref="ObjectiveTracker"/>.</summary>
    [CreateAssetMenu(menuName = "JunkLite/Objectives/Objective Definition", fileName = "Objective_")]
    public class ObjectiveDefinition : ScriptableObject
    {
        [Tooltip("Stable key used for tracking. Leave empty to use the asset name.")]
        [SerializeField] private string id;
        [SerializeField] private string title = "Objective";
        [TextArea(1, 3)]
        [SerializeField] private string body;
        [Tooltip("Lower values are listed first in the Missions tab.")]
        [SerializeField] private int sortOrder;

        public string Id => string.IsNullOrEmpty(id) ? name : id;
        public string Title => title;
        public string Body => body;
        public int SortOrder => sortOrder;
    }
}
