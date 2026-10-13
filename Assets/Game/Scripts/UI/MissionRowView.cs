using TMPro;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// One authored row in the Missions page. Only binds text and toggles authored marks;
    /// all look (fonts, colours, layout) lives on the prefab.
    /// </summary>
    public class MissionRowView : MonoBehaviour
    {
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text body;
        [Tooltip("Shown when the objective is complete.")]
        [SerializeField] private GameObject completeMark;
        [Tooltip("Shown on the level's primary objective.")]
        [SerializeField] private GameObject primaryMark;

        public void Bind(ObjectiveDefinition objective, bool complete, bool primary)
        {
            if (title != null) title.text = objective.Title;
            if (body != null)
            {
                body.text = objective.Body;
                body.gameObject.SetActive(!string.IsNullOrEmpty(objective.Body));
            }
            if (completeMark != null) completeMark.SetActive(complete);
            if (primaryMark != null) primaryMark.SetActive(primary);
        }
    }
}
