using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Keys the player carries this run, keyed by the pickup's Unity tag (e.g. "Z Key").
    /// Keys are kept across respawns within the run.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerKeyring : MonoBehaviour
    {
        /// <summary>Item key for the grapple hook (granted by the Brute, opens the grapple box).</summary>
        public const string GrappleHook = "Grapple Hook";

        private readonly Dictionary<string, int> keys = new();

        /// <summary>Raised with the key tag whenever a key is added.</summary>
        public event Action<string> Changed;

        public void Add(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            keys.TryGetValue(tag, out int count);
            keys[tag] = count + 1;
            Changed?.Invoke(tag);
        }

        public bool Has(string tag) => Count(tag) > 0;

        public int Count(string tag) => !string.IsNullOrEmpty(tag) && keys.TryGetValue(tag, out int count) ? count : 0;
    }
}
