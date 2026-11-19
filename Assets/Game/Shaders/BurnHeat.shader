Shader "JunkLite/VFX/Burn Heat"
{
    Properties
    {
        [PerRendererData] _MainTex("Body Alpha", 2D) = "white" {}
        _NoiseTex("Tiling Noise", 2D) = "gray" {}
        [HDR] _BurnColor("Heat Color", Color) = (2.5, 0.38, 0.01, 1)
        _BurnStrength("Strength", Float) = 0.7
        _BurnAlpha("Body Opacity", Float) = 1
        _BurnSeed("Phase", Float) = 0
        _BurnTickTime("Last Damage Tick", Float) = -10000
        _BurnPulse("Pulse Strength / Duration", Vector) = (0.5, 0.18, 0, 0)
        _BurnBounds("Local Min / Inverse Size", Vector) = (0, 0, 1, 1)
        _BurnFlip("Sprite Flip", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Blend SrcAlpha One
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _BurnColor, _BurnBounds, _BurnPulse, _BurnFlip;
                float _BurnStrength, _BurnAlpha, _BurnSeed, _BurnTickTime;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 body : TEXCOORD1; half alpha : TEXCOORD2; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                v.positionOS.xy *= _BurnFlip.xy;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                // Position, not atlas UV: a Spine arm and torso share the same heat field.
                o.body = (v.positionOS.xy - _BurnBounds.xy) * _BurnBounds.zw;
                o.alpha = v.color.a * _BurnAlpha;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.alpha;
                float2 flow = i.body * float2(2.1, 1.5) + float2(_BurnSeed, -_Time.y * 0.65);
                half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, flow).r;
                half patches = smoothstep(0.32, 0.75, noise);
                half pulse = saturate(1 - (_Time.y - _BurnTickTime) / max(0.02, _BurnPulse.y)) * _BurnPulse.x;
                half strength = _BurnStrength * (0.12 + patches * 0.65) + pulse * 0.45;
                return half4(_BurnColor.rgb, alpha * strength);
            }
            ENDHLSL
        }
    }
}
