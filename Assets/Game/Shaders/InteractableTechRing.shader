// Cybernetic HUD marker for interactables: procedural orange tech circle on a quad.
// Outer dashed ring spins one way, inner tick ring the other, soft pulse. No texture needed.
Shader "JunkLite/Interactable Tech Ring"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.45, 0.1, 1)
        _Intensity ("Intensity", Range(0, 8)) = 2.5
        _Speed ("Spin Speed", Float) = 0.6
        _Dashes ("Outer Dashes", Float) = 12
        _Ticks ("Inner Ticks", Float) = 36
        _Pulse ("Pulse Amount", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One   // additive glow
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity, _Speed, _Dashes, _Ticks, _Pulse;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv * 2.0 - 1.0;
                return o;
            }

            // Soft band between r0 and r1.
            half Band(half r, half r0, half r1)
            {
                half w = fwidth(r) * 1.5;
                return smoothstep(r0 - w, r0 + w, r) * (1 - smoothstep(r1 - w, r1 + w, r));
            }

            half4 frag (Varyings i) : SV_Target
            {
                half r = length(i.uv);
                half a = atan2(i.uv.y, i.uv.x) / TWO_PI + 0.5;   // 0..1 around
                half t = _Time.y * _Speed;

                // Outer dashed ring, spins forward.
                half dash = step(0.45, frac((a + t * 0.25) * _Dashes));
                half outer = Band(r, 0.86, 0.95) * dash;
                // Thin solid ring.
                half rim = Band(r, 0.78, 0.8);
                // Inner tick ring, spins back.
                half tick = step(0.8, frac((a - t * 0.4) * _Ticks));
                half inner = Band(r, 0.6, 0.7) * tick;
                // Two bright arcs chasing around.
                half arc = Band(r, 0.72, 0.76) * step(frac(a + t * 0.6), 0.18) +
                           Band(r, 0.72, 0.76) * step(frac(a + t * 0.6 + 0.5), 0.18);

                half pulse = 1 - _Pulse + _Pulse * (0.5 + 0.5 * sin(_Time.y * 3.0));
                half alpha = saturate(outer + rim * 0.6 + inner * 0.8 + arc) * pulse;
                return half4(_Color.rgb * _Intensity, alpha * _Color.a);
            }
            ENDHLSL
        }
    }
}
