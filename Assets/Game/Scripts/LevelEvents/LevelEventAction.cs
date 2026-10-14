using System;
using System.Collections;

namespace junklite
{
    /// <summary>
    /// One step of a <see cref="LevelEvent"/>. Steps run in order; a step can wait (dialogue, timeline)
    /// by yielding. Actions are plain serialized data plus behaviour; they hold no runtime state
    /// between plays, and any player lock they take goes through the event so it is always released.
    /// </summary>
    [Serializable]
    public abstract class LevelEventAction
    {
        public abstract IEnumerator Run(LevelEvent context);

        /// <summary>One-line description shown in the Inspector list.</summary>
        public virtual string Summary => GetType().Name;

        /// <summary>Returns a problem with this action's setup, or null when it is fine.</summary>
        public virtual string Validate() => null;
    }

    /// <summary>Menu path for the Inspector's "Add Action" dropdown, e.g. "Objectives/Start Objective".</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class LevelEventActionMenuAttribute : System.Attribute
    {
        public string Path { get; }
        public LevelEventActionMenuAttribute(string path) => Path = path;
    }
}
