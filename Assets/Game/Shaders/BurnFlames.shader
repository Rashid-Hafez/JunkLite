Shader "JunkLite/VFX/Burn Flames"
{
    Properties
    {
        _NoiseTex("Tiling Noise", 2D) = "gray" {}
        _Opacity("Opacity", Range(0,1)) = 0.85
        _BurnSeed("Phase", Float) = 0
        [HDR] _EmberColor("Ember Color", Color) = (3, 0.45, 0.015, 1)
        _EmberOpacity("Ember Opacity", Range(0,1)) = 1
        _EmberSize("Ember Size", Range(0.25,2)) = 1.6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Offset -1, -1
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float _Opacity, _BurnSeed;
                float4 _EmberColor;
                float _EmberOpacity, _EmberSize;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float4 data : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 data : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float phase = v.data.x + _BurnSeed;
                float variation = frac(phase * 7.13);
                float life = frac(_Time.y * lerp(0.65, 1.15, variation) + phase);
                // Keep flame motion; each ember has its own origin, lifetime and outward/upward drift.
                float outward = (step(0, v.positionOS.x) * 2 - 1) * lerp(0.12, 0.34, variation);
                float2 travel = float2(outward * life + sin(life * 8 + phase * 17) * life * 0.035,
                    life * lerp(0.35, 0.65, frac(phase * 3.71)));
                float shrink = lerp(1, 0.35, life * life);
                v.positionOS.xy += v.data.y * (travel + (v.uv - 0.5) * v.data.zw * (clamp(_EmberSize, 0.25, 2) * shrink - 1));
                v.positionOS.x += (1 - v.data.y) * sin(_Time.y * 3 + phase * 17) * 0.025 * v.uv.y;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                o.data = float3(phase, v.data.y, life);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                    float2(uv.x * 0.8 + i.data.x, uv.y * 0.7 - _Time.y * 0.8)).r;
                float center = 0.5 + (noise - 0.5) * uv.y * 0.65;
                float width = (1 - uv.y) * (0.37 + noise * 0.18);
                float edge = abs(uv.x - center);
                float flame = 1 - smoothstep(width - 0.065, width, edge);
                flame *= smoothstep(0, 0.14, uv.y) * (1 - smoothstep(0.78, 1, uv.y));
                float core = saturate(1 - edge / max(0.01, width)) * (1 - uv.y);
                float3 color = lerp(float3(2, 0.55, 0.015), float3(2.7, 1.6, 0.22), core);
                float ember = (1 - smoothstep(0.23, 0.5, length(uv - 0.5)));
                ember *= smoothstep(0, 0.08, i.data.z) * (1 - smoothstep(0.55, 1, i.data.z));
                return half4(lerp(color, _EmberColor.rgb, i.data.y), lerp(flame * _Opacity, ember * _EmberOpacity, i.data.y));
            }
            ENDHLSL
        }
    }
}
