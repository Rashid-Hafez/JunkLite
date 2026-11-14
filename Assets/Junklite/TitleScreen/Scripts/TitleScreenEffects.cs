using System.Collections;
using Junklite.Shared;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace Junklite.TitleScreen
{
    /// <summary>
    /// The portrait reveal and the payload scramble. The CRT layer and the portrait glitch are on CrtOverlay.
    /// Purely cosmetic: disable this component to turn them off.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TitleScreenEffects : MonoBehaviour
    {
        const float PortraitTop = 188f;
        const float PortraitHeight = 598f;
        const string ScrambleCharacters = "#/\\|=+*<>_-01";

        VisualElement _cover;
        Label _payload;
        string _payloadText;

        void OnDisable()
        {
            StopAllCoroutines();
            if (!IsBound()) return;
            _cover.style.display = DisplayStyle.None;
            _payload.text = _payloadText;
        }

        /// <summary>Called by TitleScreenController each time the screen starts.</summary>
        public void Restart()
        {
            StopAllCoroutines();
            var crt = GetComponent<CrtOverlay>();
            if (crt != null && crt.enabled) crt.Restart();
            if (!Bind()) return;

            _payload.text = _payloadText;
            StartCoroutine(RevealPortrait());
            StartCoroutine(ScramblePayload());
        }

        IEnumerator RevealPortrait()
        {
            const int steps = 24;

            _cover.style.display = DisplayStyle.Flex;
            _cover.style.top = PortraitTop;
            _cover.style.height = PortraitHeight;
            yield return new WaitForSecondsRealtime(0.38f);

            var wait = new WaitForSecondsRealtime(0.76f / steps);
            for (int i = 1; i <= steps; i++)
            {
                float revealed = PortraitHeight * i / steps;
                _cover.style.top = PortraitTop + revealed;
                _cover.style.height = PortraitHeight - revealed;
                yield return wait;
            }
            _cover.style.display = DisplayStyle.None;
        }

        IEnumerator ScramblePayload()
        {
            var characters = new char[_payloadText.Length];
            var wait = new WaitForSecondsRealtime(0.04f);
            while (true)
            {
                yield return new WaitForSecondsRealtime(Random.Range(11f, 19f));

                float end = Time.unscaledTime + 0.26f;
                while (Time.unscaledTime < end)
                {
                    for (int i = 0; i < characters.Length; i++)
                    {
                        char c = _payloadText[i];
                        bool keep = c == ' ' || c == '/' || Random.value >= 0.32f;
                        characters[i] = keep ? c : ScrambleCharacters[Random.Range(0, ScrambleCharacters.Length)];
                    }
                    _payload.text = new string(characters);
                    yield return wait;
                }
                _payload.text = _payloadText;
            }
        }

        // The UI Document rebuilds its tree when the UXML is reimported, so check the elements are still the live ones.
        bool IsBound()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            return _cover != null && root != null && root.Contains(_cover);
        }

        bool Bind()
        {
            if (IsBound()) return true;

            var root = GetComponent<UIDocument>().rootVisualElement;
            _cover = root?.Q("portrait-cover");
            _payload = root?.Q<Label>("payload");
            if (_cover == null || _payload == null)
            {
                _cover = null;
                return false;
            }

            _payloadText = _payload.text;
            return true;
        }
    }
}
