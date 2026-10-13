using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Missions tab page. Lists the scene's ObjectiveTracker objectives by copying an authored,
    /// inactive row template. Rebinds whenever the page is shown, so it follows scene changes.
    /// </summary>
    public class MissionsPageView : MonoBehaviour
    {
        [Tooltip("Inactive authored row; copies are made from it, it is never shown itself.")]
        [SerializeField] private MissionRowView rowTemplate;
        [Tooltip("Where row copies go. Defaults to the template's parent.")]
        [SerializeField] private Transform rowParent;
        [Tooltip("Optional. Shown when the level has no objectives.")]
        [SerializeField] private TMP_Text emptyLabel;

        private readonly List<MissionRowView> rows = new();
        private readonly List<ObjectiveDefinition> sorted = new();
        private ObjectiveTracker subscribedTracker;

        private void OnEnable()
        {
            Rebind();
            Refresh();
        }

        private void OnDisable() => Unbind();

        public void Refresh()
        {
            sorted.Clear();
            ObjectiveTracker tracker = subscribedTracker;
            if (tracker != null)
            {
                foreach (ObjectiveDefinition objective in tracker.Objectives)
                    if (objective != null && !sorted.Contains(objective)) sorted.Add(objective);
                if (tracker.Primary != null && !sorted.Contains(tracker.Primary)) sorted.Add(tracker.Primary);
                sorted.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            }

            for (int i = 0; i < sorted.Count; i++)
            {
                MissionRowView row = GetRow(i);
                if (row == null) break;
                row.gameObject.SetActive(true);
                row.Bind(sorted[i], tracker.IsComplete(sorted[i]), sorted[i] == tracker.Primary);
            }
            for (int i = sorted.Count; i < rows.Count; i++)
                rows[i].gameObject.SetActive(false);

            if (emptyLabel != null) emptyLabel.gameObject.SetActive(sorted.Count == 0);
        }

        private MissionRowView GetRow(int index)
        {
            if (index < rows.Count) return rows[index];
            if (rowTemplate == null) return null;

            if (rowTemplate.gameObject.activeSelf) rowTemplate.gameObject.SetActive(false);
            Transform parent = rowParent != null ? rowParent : rowTemplate.transform.parent;
            MissionRowView row = Instantiate(rowTemplate, parent, false);
            row.name = $"{rowTemplate.name} {index}";
            rows.Add(row);
            return row;
        }

        private void Rebind()
        {
            ObjectiveTracker tracker = ObjectiveTracker.Instance;
            if (tracker == subscribedTracker) return;
            Unbind();
            subscribedTracker = tracker;
            if (subscribedTracker != null) subscribedTracker.Changed += Refresh;
        }

        private void Unbind()
        {
            if (subscribedTracker == null) return;
            subscribedTracker.Changed -= Refresh;
            subscribedTracker = null;
        }
    }
}
