using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace Junklite.HUD
{
    /// <summary>
    /// A portrait cut into horizontal strips (HUD.Portrait in the prototype). Each strip holds the whole image
    /// shifted up, so together they line up into one picture. A glitch slides strips sideways and flashes them
    /// hot (yellow) or cyan, and <see cref="Set"/> swaps the picture in the middle of one.
    /// </summary>
    public class SlicedPortrait
    {
        readonly List<VisualElement> _slices = new List<VisualElement>();
        readonly List<VisualElement> _images = new List<VisualElement>();
        HudFx.Timer _busy;

        public Texture2D Current { get; private set; }
        public bool Busy => _busy != null && !_busy.Done;

        public SlicedPortrait(VisualElement host, int count, Texture2D image = null)
        {
            host.Clear();
            count = Mathf.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                var slice = new VisualElement { pickingMode = PickingMode.Ignore };
                slice.AddToClassList("pt__slice");
                slice.style.top = Length.Percent(i * 100f / count);
                slice.style.height = Length.Percent(100f / count);

                var img = new VisualElement { pickingMode = PickingMode.Ignore };
                img.AddToClassList("pt__img");
                img.style.top = Length.Percent(-i * 100f);
                img.style.height = Length.Percent(count * 100f);
                slice.Add(img);

                foreach (var band in new[] { "pt__band--hot", "pt__band--cold" })
                {
                    var b = new VisualElement { pickingMode = PickingMode.Ignore };
                    b.AddToClassList("pt__band");
                    b.AddToClassList(band);
                    slice.Add(b);
                }

                host.Add(slice);
                _slices.Add(slice);
                _images.Add(img);
            }

            Paint(image);
            Current = image;
        }

        /// <summary>Swaps the picture with a glitch. With <paramref name="glitch"/> off it swaps at once.</summary>
        public void Set(Texture2D image, int frames = 7, float amp = 46f, bool force = false, bool glitch = true, Action done = null)
        {
            if (image == Current && !force)
            {
                done?.Invoke();
                return;
            }

            Current = image;
            if (!glitch || !Application.isPlaying)
            {
                Cancel();
                Paint(image);
                done?.Invoke();
                return;
            }
            Run(frames, amp, 2, true, image, 4, done);
        }

        /// <summary>A small idle flicker: one or two strips for two frames.</summary>
        public void Flicker()
        {
            if (Busy) return;
            Run(2, 14f, -1, false, null, 1, null);
        }

        public void Burst(int frames = 5, float amp = 36f) => Run(frames, amp, -1, false, null, 4, null);

        void Cancel()
        {
            _busy?.Cancel();
            _busy = null;
            Restore();
        }

        void Paint(Texture2D image)
        {
            foreach (var img in _images)
                img.style.backgroundImage = image != null ? new StyleBackground(image) : new StyleBackground(StyleKeyword.None);
        }

        void Restore()
        {
            for (int i = 0; i < _slices.Count; i++)
            {
                _images[i].style.translate = StyleKeyword.Null;
                _slices[i].RemoveFromClassList("pt__slice--hot");
                _slices[i].RemoveFromClassList("pt__slice--cold");
            }
        }

        void Run(int frames, float amp, int swapAt, bool swap, Texture2D image, int density, Action done)
        {
            _busy?.Cancel();
            bool still = HudFx.ReducedMotion;
            int n = _slices.Count;
            int f = 0;

            bool Tick()
            {
                Restore();
                if (swap && f == swapAt) Paint(image);
                if (f++ >= frames)
                {
                    if (swap) Paint(image);
                    _busy = null;
                    done?.Invoke();
                    return false;
                }

                float fade = 1f - (f - 1f) / frames;
                int bands = 1 + Random.Range(0, density);
                for (int b = 0; b < bands; b++)
                {
                    int start = Random.Range(0, n);
                    int length = 1 + Random.Range(0, 3);
                    float dx = HudFx.Sign() * Random.Range(4f, amp * fade + 4f);
                    float mode = Random.value;
                    for (int i = start; i < Mathf.Min(n, start + length); i++)
                    {
                        if (!still) _images[i].style.translate = new Translate(dx, 0);
                        if (mode < 0.4f) _slices[i].AddToClassList("pt__slice--hot");
                        else if (mode < 0.62f) _slices[i].AddToClassList("pt__slice--cold");
                    }
                }
                return true;
            }

            if (!Tick()) return;
            _busy = HudFx.Every(0.07f, 0.07f, Tick);
        }
    }
}
