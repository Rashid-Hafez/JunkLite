using System;
using System.Collections.Generic;
using UnityEngine;

namespace Junklite.Inventory
{
    public enum InventoryTab
    {
        Inventory,
        Missions,
        // JunkLite: drawn by the game (UpgradesScreenView) into the "upgrades" element.
        Upgrades,
    }

    /// <summary>One card on the Missions tab.</summary>
    [Serializable]
    public class InfoEntry
    {
        [Tooltip("Small orange line above the title, e.g. MISSION 01.")]
        public string kicker;
        public string title;
        [TextArea(2, 6)] public string body;

        [Tooltip("Optional lines under the text, such as a mission's objectives.")]
        public List<string> points = new List<string>();

        public InfoEntry() { }

        public InfoEntry(string kicker, string title, string body, params string[] points)
        {
            this.kicker = kicker;
            this.title = title;
            this.body = body;
            this.points = new List<string>(points);
        }
    }
}
