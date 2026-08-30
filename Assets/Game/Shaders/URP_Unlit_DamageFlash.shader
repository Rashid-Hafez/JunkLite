Shader "Game/URP_Unlit_DamageFlash"
{
    Properties
    {
        [MainTexture] _MainTex ("MainTex", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _TintColor ("Tint Color", Color) = (1,1,1,1)
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _GlowColor ("Glow Color", Color) = (1,1,1,1)
        _AmountToFlash ("Amount To Flash", Range(0,1)) = 1
        _FlashStrength ("Flash Strength", Range(0,5)) = 1
        _FlashPower ("Flash Power", Range(0.1,8)) = 2
        _BaseAlpha ("Base Alpha", Range(0,1)) = 0.8
        _MinAlpha ("Min Alpha", Range(0,1)) = 0
        _MaxAlpha ("Max Alpha", Range(0,1)) = 1
        _GlowIntensity ("Glow Intensity", Range(0,10)) = 2
        _PulseAmount ("Glow Pulse Amount", Range(0,1)) = 0
        _PulseSpeed ("Glow Pulse Speed", Range(0,20)) = 6
        _Saturation ("Saturation", Range(0,1)) = 1
        _AlphaClipThreshold ("Threshold", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        ZTest LEqual
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TintColor;
                float4 _FlashColor;
                float4 _GlowColor;
                float _AmountToFlash;
                float _FlashStrength;
                float _FlashPower;
                float _BaseAlpha;
                float _MinAlpha;
                float _MaxAlpha;
                float _GlowIntensity;
                float _PulseAmount;
                float _PulseSpeed;
                float _Saturation;
                float _AlphaClipThreshold;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float4 frag (Varyings i) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(tex.a - _AlphaClipThreshold);
                float4 baseCol = tex * _BaseColor * _TintColor * i.color;
                float alphaRange = lerp(_MinAlpha, _MaxAlpha, _BaseAlpha);
                float alpha = tex.a * alphaRange * _BaseColor.a * _TintColor.a * i.color.a;

                float flashFactor = pow(1.0 - saturate(_AmountToFlash), _FlashPower) * _FlashStrength;
                float pulse = 1.0 + (sin(_Time.y * _PulseSpeed) * _PulseAmount);
                float3 glow = (_FlashColor.rgb * _GlowColor.rgb) * flashFactor * _GlowIntensity * pulse;

                float3 color = baseCol.rgb + glow;
                float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
                color = lerp(float3(luminance, luminance, luminance), color, _Saturation);
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
