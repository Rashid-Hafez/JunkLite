using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using URPGlitch;

namespace junklite
{
    /// <summary>
    /// The URPGlitch passes blit the full screen every frame, even when the glitch volumes are at zero.
    /// This turns each renderer feature on only while its volume values are non-zero.
    /// Self-starting (no scene wiring). Reads last frame's volume stack, so a glitch starts one frame late.
    /// </summary>
    public static class GlitchFeatureGate
    {
        private static readonly List<ScriptableRendererFeature> analogFeatures = new();
        private static readonly List<ScriptableRendererFeature> digitalFeatures = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            analogFeatures.Clear();
            digitalFeatures.Clear();
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset urp) return;

            foreach (ScriptableRendererData data in urp.rendererDataList)
            {
                if (data == null) continue;
                foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                {
                    if (feature is AnalogGlitchRenderFeature) analogFeatures.Add(feature);
                    else if (feature is DigitalGlitchFeature) digitalFeatures.Add(feature);
                }
            }

            RenderPipelineManager.beginContextRendering -= Gate;
            RenderPipelineManager.beginContextRendering += Gate;
            Application.quitting -= Restore;
            Application.quitting += Restore;
        }

        private static void Gate(ScriptableRenderContext context, List<Camera> cameras)
        {
            VolumeStack stack = VolumeManager.instance.stack;
            if (stack == null) return;

            AnalogGlitchVolume analog = stack.GetComponent<AnalogGlitchVolume>();
            DigitalGlitchVolume digital = stack.GetComponent<DigitalGlitchVolume>();

            SetActive(analogFeatures, analog != null &&
                (analog.scanLineJitter.value > 0f || analog.verticalJump.value > 0f ||
                 analog.horizontalShake.value > 0f || analog.colorDrift.value > 0f));
            SetActive(digitalFeatures, digital != null && digital.IsActive);
        }

        // Features are assets: put them back on so the Editor never saves them disabled.
        private static void Restore()
        {
            RenderPipelineManager.beginContextRendering -= Gate;
            SetActive(analogFeatures, true);
            SetActive(digitalFeatures, true);
        }

        private static void SetActive(List<ScriptableRendererFeature> features, bool active)
        {
            for (int i = 0; i < features.Count; i++)
                if (features[i] != null && features[i].isActive != active) features[i].SetActive(active);
        }
    }
}
