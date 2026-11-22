using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace Junklite.TitleScreen
{
    /// <summary>
    /// Cas's ASCII portrait in the middle of the title (the team artist's drawing, turned into characters by a script),
    /// drawn as live text so it can glitch as in Title screen/junklite-title:
    ///   - the mouse over her tears her rows sideways, turns some orange or blue and scrambles others into noise, hard
    ///     for the first few frames, then softer; two frames in she switches pose (angry / profile)
    ///   - with no mouse (a controller), a short burst plays now and then, so the effect still shows
    /// The poses are cas-angry.json and cas-profile.json, baked by unity-port/tools/bake-title.mjs: one rich-text line
    /// per row (each run of characters in its colour) and the same row as plain text for the torn frames.
    /// Purely cosmetic: disable it to keep the first pose still.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TitleCasArt : MonoBehaviour
    {
        const float TickSeconds = 0.07f;
        const int HardFrames = 6;
        const int SwapFrame = 2;
        const string Noise = "#%&@$=+*/\\|<>~:;01";
        const string HotClass = "cas__row--hot";
        const string ColdClass = "cas__row--cold";

        [Tooltip("The poses, in the order they swap: cas-angry.json, then cas-profile.json.")]
        [SerializeField] TextAsset[] poses = new TextAsset[0];

        [Tooltip("Seconds between the bursts that play by themselves (min, max). 0 turns them off.")]
        [SerializeField] Vector2 idleBurstSeconds = new Vector2(9f, 15f);

        [Tooltip("How long a burst that plays by itself lasts.")]
        [SerializeField] float idleBurstLength = 0.6f;

        [Serializable]
        class Pose
        {
            public int cols;
            public int rows;
            public float fontSize;
            public float padLeft;
            public string[] lines;
            public string[] plain;
        }

        readonly List<Pose> _poses = new List<Pose>();
        readonly List<Label> _rows = new List<Label>();
        readonly List<int> _dirty = new List<int>();
        readonly StringBuilder _noise = new StringBuilder(200);
        VisualElement _cas;
        int _pose;
        bool _on;
        bool _swapped;
        int _frame;
        Coroutine _ticking;
        Coroutine _idle;

        void OnDisable()
        {
            StopAllCoroutines();
            _ticking = _idle = null;
            _on = false;
            if (_cas != null) Restore();
        }

        /// <summary>Called by TitleScreenEffects each time the screen starts: the first pose, still.</summary>
        public void Restart()
        {
            StopAllCoroutines();
            _ticking = _idle = null;
            _on = false;
            if (!Bind()) return;
            _pose = 0;
            Show(_poses[0]);
            if (idleBurstSeconds.y > 0f) _idle = StartCoroutine(IdleBursts());
        }

        bool Bind()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            var cas = root?.Q("cas");
            if (cas == null) return false;
            if (_poses.Count == 0)
            {
                foreach (var asset in poses)
                    if (asset != null) _poses.Add(JsonUtility.FromJson<Pose>(asset.text));
                if (_poses.Count == 0)
                {
                    Debug.LogWarning("Cas's portrait has no poses. Add cas-angry.json and cas-profile.json to Title Cas Art.", this);
                    return false;
                }
            }
            if (cas != _cas)
            {
                _cas = cas;
                _rows.Clear();
                _cas.RegisterCallback<PointerEnterEvent>(evt => { if (evt.pointerType == UnityEngine.UIElements.PointerType.mouse) Begin(); });
                _cas.RegisterCallback<PointerLeaveEvent>(_ => End());
            }
            return true;
        }

        // A pose's rows, sized so it fits the 845 x 865 box, as in the prototype.
        void Show(Pose pose)
        {
            _dirty.Clear();
            _cas.style.paddingLeft = pose.padLeft;
            _cas.style.translate = new Translate(0, 0);
            while (_rows.Count < pose.lines.Length)
            {
                var row = new Label { pickingMode = PickingMode.Ignore, enableRichText = true };
                row.AddToClassList("cas__row");
                _cas.Add(row);
                _rows.Add(row);
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                bool used = i < pose.lines.Length;
                row.style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used) continue;
                row.text = pose.lines[i];
                row.style.fontSize = pose.fontSize;
                row.style.height = pose.fontSize * 1.31f;
                row.style.translate = new Translate(0, 0);
                row.RemoveFromClassList(HotClass);
                row.RemoveFromClassList(ColdClass);
            }
        }

        void Begin()
        {
            if (_on || _cas == null) return;
            _on = true;
            _frame = 0;
            _swapped = false;
            Tick();
            _ticking = StartCoroutine(Ticking());
        }

        void End()
        {
            if (!_on) return;
            _on = false;
            if (_ticking != null) StopCoroutine(_ticking);
            _ticking = null;
            Restore();
            Swap();
        }

        IEnumerator Ticking()
        {
            var wait = new WaitForSecondsRealtime(TickSeconds);
            while (_on)
            {
                yield return wait;
                Tick();
            }
        }

        // A controller has no hover, so a short burst plays now and then (not while the mouse is over her).
        IEnumerator IdleBursts()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(Random.Range(idleBurstSeconds.x, Mathf.Max(idleBurstSeconds.x, idleBurstSeconds.y)));
                if (_on) continue;
                Begin();
                yield return new WaitForSecondsRealtime(idleBurstLength);
                End();
            }
        }

        // One frame of the glitch: the last frame's torn rows go back, then a few new bands of rows tear.
        void Tick()
        {
            Restore();
            if (!_on) return;
            if (_frame == SwapFrame) Swap();
            bool hard = _frame++ < HardFrames;
            var pose = _poses[_pose];
            int count = pose.lines.Length;

            int bands = hard ? 4 + Random.Range(0, 4) : 1 + Random.Range(0, 3);
            for (int b = 0; b < bands; b++)
            {
                int start = Random.Range(0, count);
                int length = 1 + Random.Range(0, hard ? 7 : 3);
                float dx = (Random.value < 0.5f ? -1f : 1f) * (6f + Random.value * (hard ? 70f : 26f));
                float mode = Random.value;
                for (int r = start; r < Mathf.Min(count, start + length); r++)
                {
                    if (_dirty.Contains(r)) continue;
                    var row = _rows[r];
                    row.style.translate = new Translate(dx, 0);
                    bool recolour = mode < 0.55f;
                    if (mode < 0.38f) row.AddToClassList(HotClass);
                    else if (recolour) row.AddToClassList(ColdClass);
                    // a torn row is one colour: plain text in the row's colour, sometimes scrambled into noise
                    if (Random.value < (hard ? 0.3f : 0.12f)) row.text = NoiseOf(pose.plain[r]);
                    else if (recolour) row.text = pose.plain[r];
                    _dirty.Add(r);
                }
            }
            if (hard && Random.value < 0.5f) _cas.style.translate = new Translate((Random.value - 0.5f) * 10f, 0);
        }

        string NoiseOf(string line)
        {
            _noise.Clear();
            foreach (char c in line)
                _noise.Append(c == ' ' ? ' ' : Random.value < 0.7f ? Noise[Random.Range(0, Noise.Length)] : ' ');
            return _noise.ToString();
        }

        void Restore()
        {
            var pose = _poses.Count > 0 ? _poses[_pose] : null;
            foreach (int r in _dirty)
            {
                var row = _rows[r];
                row.style.translate = new Translate(0, 0);
                row.RemoveFromClassList(HotClass);
                row.RemoveFromClassList(ColdClass);
                if (pose != null && r < pose.lines.Length) row.text = pose.lines[r];
            }
            _dirty.Clear();
            _cas.style.translate = new Translate(0, 0);
        }

        // The other pose, once per burst.
        void Swap()
        {
            if (_swapped || _poses.Count < 2) return;
            _swapped = true;
            _pose = (_pose + 1) % _poses.Count;
            Show(_poses[_pose]);
        }
    }
}
