using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class Fade : MonoBehaviour
{
    private Renderer rend;
    private Material material;
    private Coroutine fadeRoutine;

    private void Awake()
    {
        rend = GetComponent<MeshRenderer>();
        material = rend.material; // creates instance (safe)
    }

    public void FadeIn(float duration)
    {
        StartFade(1f, duration);
    }

    public void FadeOut(float duration)
    {
        StartFade(0f, duration);
    }

    private void StartFade(float targetAlpha, float duration)
    {
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(FadeRoutine(targetAlpha, duration));
    }

    private IEnumerator FadeRoutine(float targetAlpha, float duration)
    {
        float timer = 0f;
        float startAlpha = material.color.a;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = timer / duration;

            float alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            Color c = material.color;
            c.a = alpha;
            material.color = c;

            yield return null;
        }

        Color final = material.color;
        final.a = targetAlpha;
        material.color = final;
    }
}
