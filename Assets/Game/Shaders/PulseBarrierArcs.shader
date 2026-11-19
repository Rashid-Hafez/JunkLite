Shader "JunkLite/VFX/Pulse Barrier Arcs"
{
    Properties
    {
        [HDR] _ArcColor("Arc Color", Color) = (0.05, 1.8, 2.7, 1)
        _ArcShape("Radius X/Y, Width, Speed", Vector) = (1.05, 1.3, 0.18, 1)
        _ArcState("Health, Exit, Shattered, Opacity", Vector) = (1, 0, 0, 1)
        _ArcTiming("Age, Hit Age, Activation Duration, Exit Age", Vector) = (1, 1000, 0.25, -1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _ArcColor, _ArcShape, _ArcState, _ArcTiming;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float4 data : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 data : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float age = _ArcTiming.x;
                float activation = saturate(age / _ArcTiming.z);
                float scale = lerp(0.65, 1, 1 - pow(1 - activation, 3));
                float exit = _ArcState.y;
                scale *= lerp(1 - exit * 0.55, 1 + exit * 0.35, _ArcState.z);
                float hit = exp(-max(0, _ArcTiming.y) * 5);
                float3 position;
                float alpha = 1;
                if (v.data.z > 0.5)
                {
                    float seed = v.data.w;
                    float life = min(age, _ArcTiming.y);
                    float angle = seed * TWO_PI;
                    float2 direction = float2(cos(angle), sin(angle));
                    float2 normal = normalize(direction * _ArcShape.yx);
                    // Start at the expanded outer ribbon, then travel away from its circumference.
                    float radius = scale * (1 + hit * 0.075) + exit * _ArcState.z * 0.25;
                    radius += saturate(life) * (0.45 + seed * 0.4);
                    position = float3(direction * _ArcShape.xy * radius, -0.08);
                    position.xy += normal * _ArcShape.z;
                    position.xy += v.positionOS.xy * float2(0.026, 0.055) * (1 + seed) * (1 - saturate(life));
                    alpha = (1 - smoothstep(0.08, 0.7, life)) * saturate(life * 35);
                }
                else
                {
                    float ring = v.data.x;
                    float speed = ring < 0.5 ? 0.23 : (ring < 1.5 ? 0.7 : -0.55);
                    float angle = v.positionOS.x + age * speed * _ArcShape.w;
                    float2 direction = float2(cos(angle), sin(angle));
                    float2 normal;
                    float ribbonWidth = _ArcShape.z;
                    if (ring < 0.5)
                    {
                        position = float3(direction * _ArcShape.xy, 0);
                        normal = normalize(direction * _ArcShape.yx);
                    }
                    else
                    {
                        float tilt = ring < 1.5 ? 0.44 : -0.44;
                        float2 orbitRadii = float2(_ArcShape.x * 1.1, _ArcShape.y * 0.25);
                        float2 orbit = direction * orbitRadii;
                        position = float3(orbit.x * cos(tilt) - orbit.y * sin(tilt),
                            orbit.x * sin(tilt) + orbit.y * cos(tilt), sin(angle) * _ArcShape.x * 0.55);
                        float2 orbitNormal = normalize(direction * float2(_ArcShape.y * 0.25, _ArcShape.x * 1.1));
                        normal = float2(orbitNormal.x * cos(tilt) - orbitNormal.y * sin(tilt),
                            orbitNormal.x * sin(tilt) + orbitNormal.y * cos(tilt));
                        // Keep broad ribbons from folding into spikes at the tight ends of an orbit.
                        float tangentLength = length(direction.yx * orbitRadii);
                        float curvatureRadius = pow(tangentLength, 3) / (orbitRadii.x * orbitRadii.y);
                        ribbonWidth = min(ribbonWidth, curvatureRadius * scale * 0.8);
                    }
                    position *= scale * (1 + hit * 0.075);
                    float middle = v.data.y + age * speed * _ArcShape.w;
                    position.xy += float2(cos(middle), sin(middle)) * exit * _ArcState.z * _ArcShape.xy * 0.25;
                    // Maintain a broad, consistent ribbon width around each projected ellipse.
                    position.xy += normal * v.positionOS.y * ribbonWidth;
                }
                o.positionCS = TransformObjectToHClip(position);
                o.uv = v.uv;
                o.data = float3(v.data.z, v.data.w + v.data.x * 0.31, alpha * activation * (1 - exit));
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float hit = exp(-max(0, _ArcTiming.y) * 5);
                float edge = saturate(1 - abs(i.uv.y));
                float core = 1 - smoothstep(0.22, 0.62, abs(i.uv.y));
                float ribbon = pow(edge, 1.3) * 0.3 + core * 0.7;
                float ends = smoothstep(0, 0.06, i.uv.x) * (1 - smoothstep(0.94, 1, i.uv.x));
                float pulse = pow(saturate(0.5 + 0.5 * cos((i.uv.x * 0.7 + i.data.y - _ArcTiming.x * 0.8) * TWO_PI)), 8);
                float weak = 1 - smoothstep(0, 0.3, _ArcState.x);
                float flicker = 1 - weak * (0.22 + 0.18 * sin(_ArcTiming.x * 27 + i.data.y * 41));
                float spark = pow(saturate(1 - length(i.uv)), 1.4);
                float alpha = lerp(ribbon * ends * flicker, spark, i.data.x) * i.data.z * _ArcState.w;
                float brightness = 1 + pulse + hit * 1.6;
                return half4(_ArcColor.rgb * brightness, alpha);
            }
            ENDHLSL
        }
    }
}
