using System.Collections;
using Junklite.Shared;
using UnityEngine;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace Junklite.TitleScreen
{
    /// <summary>
    /// The title's small live touches: the header clock, the payload line's scramble every 11 to 19 seconds, and a
    /// restart for Cas's portrait (TitleCasArt) and the CRT layer (CrtOverlay). Purely cosmetic: disable this component
    /// to turn them off.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TitleScreenEffects : MonoBehaviour
    {
        const string ScrambleCharacters = "#/\\|=+*<>_-01";

        Label _payload;
        Label _clock;
        string _payloadText;

        void OnDisable()
        {
            StopAllCoroutines();
            if (!IsBound()) return;
            _payload.text = _payloadText;
        }

        /// <summary>Called by TitleScreenController each time the screen starts.</summary>
        public void Restart()
        {
            StopAllCoroutines();
            var crt = GetComponent<CrtOverlay>();
            if (crt != null && crt.enabled) crt.Restart();
            var cas = GetComponent<TitleCasArt>();
            if (cas != null && cas.enabled) cas.Restart();
            if (!Bind()) return;

            _payload.text = _payloadText;
            StartCoroutine(ScramblePayload());
            StartCoroutine(RunClock());
        }

        // The prototype's in-world clock: 2348.06.06, from 18:36:12, one second a second.
        IEnumerator RunClock()
        {
            float start = Time.unscaledTime;
            var wait = new WaitForSecondsRealtime(1f);
            while (true)
            {
                int seconds = Mathf.FloorToInt(Time.unscaledTime - start) + 12;
                int minutes = 36 + seconds / 60;
                _clock.text = $"2348.06.06  18:{minutes:00}:{seconds % 60:00}";
                yield return wait;
            }
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
            return _payload != null && root != null && root.Contains(_payload);
        }

        bool Bind()
        {
            if (IsBound()) return true;

            var root = GetComponent<UIDocument>().rootVisualElement;
            _payload = root?.Q<Label>("payload");
            _clock = root?.Q<Label>("clock");
            if (_payload == null || _clock == null)
            {
                _payload = null;
                return false;
            }

            _payloadText = _payload.text;
            return true;
        }
    }
}
