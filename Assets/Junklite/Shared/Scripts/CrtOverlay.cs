using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace Junklite.Shared
{
    /// <summary>
    /// Animates the CRT layer from Crt.uxml: moving grain, the rolling band, and a strip of the screen
    /// that tears every few seconds. Disable it to keep the overlay but stop everything moving.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class CrtOverlay : MonoBehaviour
    {
        [Tooltip("Part of the 1920x1080 screen that tears, in reference pixels. Leave the size at zero for no tearing.")]
        [SerializeField] RectInt glitchArea;

        [Tooltip("The screen's background image. A tear shows a shifted strip of it.")]
        [SerializeField] Texture2D glitchSource;

        [Tooltip("Optional brighter art for the second strip, lined up with the glitch area. Empty uses a tinted copy of the background.")]
        [SerializeField] Texture2D ghostSource;

        [SerializeField] Color ghostTint = new Color(0.36f, 0.94f, 0.89f, 1f);
        [SerializeField] float firstGlitchDelay = 2.6f;

        VisualElement _grain;
        VisualElement _roll;
        VisualElement _tear;
        VisualElement _ghost;

        public RectInt GlitchArea => glitchArea;

        void OnEnable() => Restart();

        void OnDisable()
        {
            StopAllCoroutines();
            HideTear();
        }

        public void Restart()
        {
            StopAllCoroutines();
            if (!Bind()) return;

            HideTear();
            StartCoroutine(MoveGrain());
            StartCoroutine(RollBand());
            if (_tear != null) StartCoroutine(Glitch());
        }

        // The UI Document rebuilds its tree when the UXML is reimported, so check the elements are still the live ones.
        bool Bind()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) return false;
            if (_grain != null && root.Contains(_grain)) return true;

            _grain = root.Q("fx-grain");
            _roll = root.Q("fx-roll");
            _tear = null;
            _ghost = null;
            if (_grain == null || _roll == null) return false;

            // The template instance would otherwise catch clicks meant for the screen under it.
            root.Query(className: "crt").ForEach(e => e.pickingMode = PickingMode.Ignore);

            var glitch = root.Q("glitch");
            if (glitch != null && glitchSource != null && glitchArea.width > 0 && glitchArea.height > 0)
            {
                glitch.pickingMode = PickingMode.Ignore;
                glitch.style.left = glitchArea.x;
                glitch.style.top = glitchArea.y;
                glitch.style.width = glitchArea.width;
                glitch.style.height = glitchArea.height;
                glitch.Clear();

                _tear = AddSlice(glitch, glitchSource, Color.white, new Vector2(1920, 1080));
                _ghost = ghostSource != null
                    ? AddSlice(glitch, ghostSource, Color.white, new Vector2(ghostSource.width, ghostSource.height))
                    : AddSlice(glitch, glitchSource, ghostTint, new Vector2(1920, 1080));
            }
            return true;
        }

        IEnumerator MoveGrain()
        {
            Vector2[] offsets =
            {
                new Vector2(0, 0), new Vector2(-4, 3), new Vector2(3, -5),
                new Vector2(-6, -2), new Vector2(5, 4),
            };

            var wait = new WaitForSecondsRealtime(0.15f);
            for (int i = 0; ; i = (i + 1) % offsets.Length)
            {
                _grain.style.translate = new Translate(Length.Percent(offsets[i].x), Length.Percent(offsets[i].y));
                yield return wait;
            }
        }

        IEnumerator RollBand()
        {
            var wait = new WaitForSecondsRealtime(9f);
            while (true)
            {
                _roll.RemoveFromClassList("fx--roll-moving");
                yield return null;
                yield return null;
                _roll.AddToClassList("fx--roll-moving");
                yield return wait;
            }
        }

        IEnumerator Glitch()
        {
            yield return new WaitForSecondsRealtime(firstGlitchDelay);

            int frames = 3;
            while (true)
            {
                for (int i = 0; i < frames; i++)
                {
                    PlaceSlice(_tear, Random.Range(0f, 85f), Random.Range(2f, 11f), Random.Range(-13f, 13f), 1f);

                    float offset = Random.Range(6f, 16f) * (Random.value > 0.5f ? 1f : -1f);
                    float opacity = Random.value > 0.35f ? 0.9f : 0f;
                    PlaceSlice(_ghost, Random.Range(0f, 85f), Random.Range(1f, 6f), offset, opacity);

                    yield return new WaitForSecondsRealtime(Random.Range(0.038f, 0.084f));
                }

                HideTear();
                yield return new WaitForSecondsRealtime(Random.Range(4.2f, 9f));
                frames = Random.Range(2, 5);
            }
        }

        void HideTear()
        {
            if (_tear == null) return;
            _tear.style.opacity = 0;
            _ghost.style.opacity = 0;
        }

        static VisualElement AddSlice(VisualElement parent, Texture2D art, Color tint, Vector2 size)
        {
            // Full-screen art has to be shifted to the strip it covers; the userData flag records which kind this is.
            var slice = new VisualElement { pickingMode = PickingMode.Ignore, userData = size.x >= 1920f };
            slice.AddToClassList("glitch__slice");

            var image = new VisualElement { pickingMode = PickingMode.Ignore };
            image.AddToClassList("glitch__art");
            image.style.width = size.x;
            image.style.height = size.y;
            image.style.backgroundImage = art;
            image.style.unityBackgroundImageTintColor = tint;

            slice.Add(image);
            parent.Add(slice);
            return slice;
        }

        void PlaceSlice(VisualElement slice, float topPercent, float heightPercent, float offsetX, float opacity)
        {
            float top = glitchArea.height * topPercent / 100f;
            slice.style.top = top;
            slice.style.height = glitchArea.height * heightPercent / 100f;
            slice.style.translate = new Translate(offsetX, 0);
            slice.style.opacity = opacity;

            var image = slice[0];
            bool fullScreen = (bool)slice.userData;
            image.style.left = fullScreen ? -glitchArea.x : 0;
            image.style.top = fullScreen ? -(glitchArea.y + top) : -top;
        }
    }
}
