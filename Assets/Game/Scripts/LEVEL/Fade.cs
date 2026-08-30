using System.Collections;
using UnityEngine;

public class Fade : MonoBehaviour
{
    // URP Lit uses _BaseColor as the visible color. material.color aliases
    // _Color on legacy shaders and is unreliable on URP, so we drive _BaseColor
    // explicitly via a cached property ID.
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

    // Above this alpha the wall behaves as opaque (writes depth, contributes
    // to depth/normals prepass, casts shadows, sits in the AlphaTest queue).
    // Below it, we flip back to a true transparent so the room behind shows
    // through correctly. The threshold is high enough that the visual change
    // is invisible during a fade.
    private const float OpaqueAlphaThreshold = 0.99f;

    private Renderer rend;
    private Material material;
    private Coroutine fadeRoutine;

    private bool? lastDepthState;

    private void Awake()
    {
        rend = GetComponent<MeshRenderer>();

        // Use a per-renderer material instance so fades affect ONLY this
        // wall, not every other object that shares SafeRoomFadeMat. Unity
        // creates the instance on first access to .material; we cache it
        // immediately so we don't accidentally create more than one.
        material = rend.material;

        // Always start fully opaque. The instance is fresh per play session
        // so this isn't strictly required in builds, but it keeps editor
        // play-mode behaviour consistent if anything ever pre-tweaks alpha.
        ApplyAlpha(1f);
    }

    private void OnDestroy()
    {
        // Clean up the instanced material we created in Awake to avoid a
        // small per-renderer leak when the wall is destroyed.
        if (material != null)
            Destroy(material);
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
        // Guard against zero/negative durations so we don't divide by zero
        // and snap immediately.
        if (duration <= 0f)
        {
            ApplyAlpha(targetAlpha);
            fadeRoutine = null;
            yield break;
        }

        Color startColor = material.GetColor(BaseColorId);
        float startAlpha = startColor.a;

        float timer = 0f;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);

            float alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            ApplyAlpha(alpha);

            yield return null;
        }

        ApplyAlpha(targetAlpha);
        fadeRoutine = null;
    }

    private void ApplyAlpha(float alpha)
    {
        Color c = material.GetColor(BaseColorId);
        c.a = alpha;
        material.SetColor(BaseColorId, c);

        SetDepthState(alpha >= OpaqueAlphaThreshold);
    }

    /// <summary>
    /// Switches the material between "opaque-style" (writes depth, in
    /// AlphaTest queue, depth/normals/shadow passes enabled) and "transparent"
    /// (no depth write, in Transparent queue). This avoids the classic
    /// transparent-with-ZWrite bug where a faded surface still occludes
    /// everything behind it and ends up rendering as the clear color.
    /// </summary>
    private void SetDepthState(bool opaqueStyle)
    {
        if (lastDepthState == opaqueStyle) return;
        lastDepthState = opaqueStyle;

        material.SetInt(ZWriteId, opaqueStyle ? 1 : 0);
        material.SetShaderPassEnabled("DepthOnly", opaqueStyle);
        material.SetShaderPassEnabled("DepthNormals", opaqueStyle);
        material.SetShaderPassEnabled("ShadowCaster", opaqueStyle);
        material.renderQueue = opaqueStyle ? 2450 : 3000;
    }
}
