using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace Junklite.HUD
{
    /// <summary>Settings for one <see cref="HudFx.Tear"/>. The defaults are the prototype's.</summary>
    public class TearOptions
    {
        public int frames = 6;
        public float amp = 22f;
        public float chance = 0.55f;
        public float hot = 0.32f;
        public float cold = 0.18f;

        /// <summary>A housekeeping glitch (the objective folding) that shouldn't wake a faded HUD.</summary>
        public bool quiet;
    }

    /// <summary>
    /// The HUD's glitch helpers, ported from HUD/shared/js/core.js: text scramble, number counter, torn rows,
    /// shake, chroma ghosts and timers. Everything runs on unscaled real time, so the HUD keeps animating
    /// while the game is paused. Cooldowns and other game rules run on game time in their own components.
    /// </summary>
    public static class HudFx
    {
        const string Noise = "#/\\|=+*<>_-01";

        /// <summary>Fewer frames and no sideways movement, for players who turn motion down.</summary>
        public static bool ReducedMotion { get; set; }

        /// <summary>Fires whenever an element reacts to something (every tear and shake). The idle fade listens to it.</summary>
        public static event Action Activity;

        public static void RaiseActivity() => Activity?.Invoke();

        // ---------- timers ----------

        public sealed class Timer
        {
            internal double Next;
            internal double Interval;
            internal Func<bool> Tick;
            public bool Done { get; internal set; }

            public void Cancel() => Done = true;
        }

        static readonly List<Timer> Timers = new List<Timer>();
        static readonly List<Timer> Adding = new List<Timer>();
        static Runner _runner;

        static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>Runs <paramref name="action"/> once, after <paramref name="seconds"/> of real time.</summary>
        public static Timer After(float seconds, Action action)
        {
            return Every(seconds, seconds, () =>
            {
                action();
                return false;
            });
        }

        /// <summary>Calls <paramref name="tick"/> every <paramref name="interval"/> seconds until it returns false.</summary>
        public static Timer Every(float interval, float firstDelay, Func<bool> tick)
        {
            var timer = new Timer { Next = Now + firstDelay, Interval = Mathf.Max(0.001f, interval), Tick = tick };
            if (!Application.isPlaying)
            {
                // Outside Play mode there is no frame loop; run to the end at once.
                int guard = 0;
                while (guard++ < 1000 && SafeTick(timer)) { }
                timer.Done = true;
                return timer;
            }

            EnsureRunner();
            Adding.Add(timer);
            return timer;
        }

        static void EnsureRunner()
        {
            if (_runner != null) return;
            var go = new GameObject("HudFx") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
        }

        static bool SafeTick(Timer timer)
        {
            try
            {
                return timer.Tick();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        static void Step()
        {
            if (Adding.Count > 0)
            {
                Timers.AddRange(Adding);
                Adding.Clear();
            }

            double now = Now;
            for (int i = 0; i < Timers.Count; i++)
            {
                var timer = Timers[i];
                if (timer.Done || now < timer.Next) continue;
                timer.Next = Math.Max(timer.Next + timer.Interval, now + timer.Interval * 0.5);
                if (!SafeTick(timer)) timer.Done = true;
            }
            Timers.RemoveAll(t => t.Done);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Timers.Clear();
            Adding.Clear();
            Scrambles.Clear();
            Counts.Clear();
            Owners.Clear();
            Shakes.Clear();
            Chromas.Clear();
            Activity = null;
            _runner = null;
        }

        sealed class Runner : MonoBehaviour
        {
            void Update() => Step();
        }

        // ---------- text ----------

        sealed class ScrambleState
        {
            public Timer Timer;
            public string Final;
        }

        static readonly Dictionary<Label, ScrambleState> Scrambles = new Dictionary<Label, ScrambleState>();
        static readonly Dictionary<Label, Timer> Counts = new Dictionary<Label, Timer>();

        /// <summary>A few frames of noise, then the final text, as on the title screen.</summary>
        public static void Scramble(Label label, int ms = 260, string finalText = null)
        {
            if (label == null) return;
            Scrambles.TryGetValue(label, out var state);
            string text = finalText ?? state?.Final ?? label.text;
            state?.Timer?.Cancel();
            if (ReducedMotion || !Application.isPlaying)
            {
                Scrambles.Remove(label);
                label.text = text;
                return;
            }

            state = new ScrambleState { Final = text };
            Scrambles[label] = state;
            label.text = Noisy(text);
            double end = Now + ms / 1000.0;
            state.Timer = Every(0.04f, 0.04f, () =>
            {
                if (Now >= end)
                {
                    label.text = text;
                    if (Scrambles.TryGetValue(label, out var s) && s == state) Scrambles.Remove(label);
                    return false;
                }
                label.text = Noisy(text);
                return true;
            });
        }

        /// <summary>Sets a label's text, cancelling any scramble or count still running on it.</summary>
        public static void SetText(Label label, string text)
        {
            if (label == null) return;
            CancelText(label);
            label.text = text;
        }

        /// <summary>The text a label is heading for, even mid-scramble.</summary>
        public static string FinalText(Label label)
        {
            if (label == null) return null;
            return Scrambles.TryGetValue(label, out var state) ? state.Final : label.text;
        }

        static void CancelText(Label label)
        {
            if (Scrambles.TryGetValue(label, out var state))
            {
                state.Timer?.Cancel();
                Scrambles.Remove(label);
            }
            if (Counts.TryGetValue(label, out var count))
            {
                count.Cancel();
                Counts.Remove(label);
            }
        }

        static string Noisy(string text)
        {
            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (char.IsWhiteSpace(c) || c == '/' || c == '.') continue;
                if (Random.value < 0.34f) chars[i] = Noise[Random.Range(0, Noise.Length)];
            }
            return new string(chars);
        }

        /// <summary>Counts a number from one value to another with an ease-out (health readout).</summary>
        public static void Count(Label label, float from, float to, int ms, Func<float, string> format = null)
        {
            if (label == null) return;
            format ??= v => Mathf.RoundToInt(v).ToString();
            CancelText(label);
            if (ReducedMotion || ms <= 0 || !Application.isPlaying)
            {
                label.text = format(to);
                return;
            }

            double start = Now;
            Timer timer = null;
            timer = Every(0.016f, 0f, () =>
            {
                float k = Mathf.Min(1f, (float)((Now - start) * 1000.0 / ms));
                float eased = 1f - Mathf.Pow(1f - k, 3f);
                label.text = format(from + (to - from) * eased);
                if (k < 1f) return true;
                if (Counts.TryGetValue(label, out var t) && t == timer) Counts.Remove(label);
                return false;
            });
            Counts[label] = timer;
        }

        // ---------- torn rows ----------

        static readonly Dictionary<VisualElement, object> Owners = new Dictionary<VisualElement, object>();

        /// <summary>
        /// Torn rows: for a few 70 ms frames, some rows slide sideways and some turn hot (yellow) or cyan.
        /// <paramref name="done"/> runs when the rows are back in place.
        /// </summary>
        public static Timer Tear(IEnumerable<VisualElement> rows, TearOptions options = null, Action done = null)
        {
            var o = options ?? new TearOptions();
            var list = new List<VisualElement>();
            if (rows != null)
            {
                foreach (var row in rows) if (row != null) list.Add(row);
            }

            if (list.Count == 0 || !Application.isPlaying)
            {
                done?.Invoke();
                return null;
            }

            if (!o.quiet) RaiseActivity();
            var id = new object();
            foreach (var row in list) Owners[row] = id;

            bool still = ReducedMotion;
            int frames = still ? Mathf.Min(o.frames, 3) : o.frames;
            int frame = 0;

            void Restore()
            {
                foreach (var row in list)
                {
                    if (!Owners.TryGetValue(row, out var owner) || owner != id) continue;
                    row.style.translate = StyleKeyword.Null;
                    row.RemoveFromClassList("tear--hot");
                    row.RemoveFromClassList("tear--cold");
                }
            }

            bool Tick()
            {
                Restore();
                if (frame++ >= frames)
                {
                    foreach (var row in list)
                    {
                        if (Owners.TryGetValue(row, out var owner) && owner == id) Owners.Remove(row);
                    }
                    done?.Invoke();
                    return false;
                }

                float fade = 1f - (frame - 1f) / Mathf.Max(1, o.frames);
                foreach (var row in list)
                {
                    if (!Owners.TryGetValue(row, out var owner) || owner != id || Random.value > o.chance) continue;
                    if (!still) row.style.translate = new Translate(Sign() * Random.Range(3f, o.amp * fade + 3f), 0);
                    float m = Random.value;
                    if (m < o.hot) row.AddToClassList("tear--hot");
                    else if (m < o.hot + o.cold) row.AddToClassList("tear--cold");
                }
                return true;
            }

            if (!Tick()) return null;
            return Every(0.07f, 0.07f, Tick);
        }

        // ---------- shake ----------

        static readonly Dictionary<VisualElement, Timer> Shakes = new Dictionary<VisualElement, Timer>();

        /// <summary>A short sideways rattle, for refused input.</summary>
        public static void Shake(VisualElement element, float amp = 6f)
        {
            if (element == null) return;
            RaiseActivity();
            if (ReducedMotion || !Application.isPlaying) return;

            if (Shakes.TryGetValue(element, out var old)) old.Cancel();
            float[] steps = { amp, -amp, amp * 0.6f, -amp * 0.6f, amp * 0.3f, 0f };
            int i = 0;
            Timer timer = null;
            timer = Every(0.04f, 0.04f, () =>
            {
                element.style.translate = new Translate(steps[i], 0);
                if (++i < steps.Length) return true;
                element.style.translate = StyleKeyword.Null;
                if (Shakes.TryGetValue(element, out var t) && t == timer) Shakes.Remove(element);
                return false;
            });
            Shakes[element] = timer;
        }

        // ---------- chroma ghosts ----------

        static readonly Dictionary<VisualElement, Timer> Chromas = new Dictionary<VisualElement, Timer>();

        /// <summary>Sets all three labels of a chroma heading.</summary>
        public static void SetChroma(VisualElement wrap, string text)
        {
            if (wrap == null) return;
            wrap.Query<Label>().ForEach(l => SetText(l, text));
        }

        /// <summary>The two ghost copies of a heading show and jitter for a moment.</summary>
        public static void Chroma(VisualElement wrap, int ms = 320, float amp = 5f)
        {
            if (wrap == null) return;
            var a = wrap.Q(className: "chroma__ghost--a");
            var b = wrap.Q(className: "chroma__ghost--b");
            if (a == null || b == null) return;

            if (Chromas.TryGetValue(wrap, out var old)) old.Cancel();
            wrap.AddToClassList("chroma--on");
            if (!Application.isPlaying)
            {
                wrap.RemoveFromClassList("chroma--on");
                return;
            }

            bool still = ReducedMotion;
            void Jig()
            {
                float x = still ? 2f : Random.Range(2f, amp);
                float y = still ? 0f : Random.Range(-1.5f, 1.5f);
                a.style.translate = new Translate(-x, y);
                b.style.translate = new Translate(x, -y);
            }

            Jig();
            double end = Now + ms / 1000.0;
            Timer timer = null;
            timer = Every(0.06f, 0.06f, () =>
            {
                if (Now < end)
                {
                    Jig();
                    return true;
                }
                wrap.RemoveFromClassList("chroma--on");
                a.style.translate = StyleKeyword.Null;
                b.style.translate = StyleKeyword.Null;
                if (Chromas.TryGetValue(wrap, out var t) && t == timer) Chromas.Remove(wrap);
                return false;
            });
            Chromas[wrap] = timer;
        }

        // ---------- small helpers ----------

        public static float Sign() => Random.value < 0.5f ? -1f : 1f;

        /// <summary>Adds a class for a moment, then takes it off again.</summary>
        public static Timer Pulse(VisualElement element, string className, float seconds)
        {
            if (element == null) return null;
            element.AddToClassList(className);
            return After(seconds, () => element.RemoveFromClassList(className));
        }

        /// <summary>Two-digit padding, as in 01 / 03.</summary>
        public static string Pad(int n) => n.ToString("00");

        /// <summary>A panel-time delay of a couple of frames, so a class change is styled before the next one lands.</summary>
        public static Timer NextFrames(Action action) => After(0.034f, action);
    }
}
