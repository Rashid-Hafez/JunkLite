Shader "JunkLite/GodRayCone"
{
    Properties
    {
        [HDR] _Color ("Light Color", Color) = (1,1,1,1)
        _MainTex ("Ray Texture (Alpha)", 2D) = "white" {}
        _DepthFade ("Soft Depth Fade", Range(0, 10)) = 1.0
        _CameraFade ("Camera Distance Fade", Range(0, 5)) = 1.0
        _EdgeFade ("Edge Smoothing (Fresnel)", Range(0, 5)) = 2.0
        _SideFade ("Side Dispersal", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            Cull Off
            Blend One One // Additive

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float3 normalWS : TEXCOORD3;
                float3 viewDirWS : TEXCOORD4;
            };

            sampler2D _MainTex;
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _DepthFade;
                float _CameraFade;
                float _EdgeFade;
                float _SideFade;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.screenPos = ComputeScreenPos(output.positionCS);
                
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.viewDirWS = GetWorldSpaceViewDir(positionWS);
                
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                // Texture sampling
                float4 tex = tex2D(_MainTex, input.uv);
                
                // Screen depth calculation for soft intersection
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfaceDepth = input.screenPos.w;
                
                // Soft Depth Fade (hides harsh intersections with walls)
                float depthFade = saturate((sceneDepth - surfaceDepth) * _DepthFade);
                
                // Camera Distance Fade (hides mesh when clipping camera)
                float cameraFade = saturate((surfaceDepth - _ProjectionParams.y) * _CameraFade);
                
                // Edge Smoothing (Fresnel-like effect to soften cone boundaries)
                float3 normal = normalize(input.normalWS);
                float3 viewDir = normalize(input.viewDirWS);
                float fresnel = pow(1.0 - saturate(dot(normal, viewDir)), _EdgeFade);
                
                // Side Dispersal (fades out at the UV edges of the cone)
                float uvEdgeFade = saturate(input.uv.x / _SideFade) * saturate((1.0 - input.uv.x) / _SideFade);
                
                // Final Alpha/Intensity
                float finalAlpha = tex.a * depthFade * cameraFade * fresnel * uvEdgeFade;
                
                return _Color * finalAlpha;
            }
            ENDHLSL
        }
    }
}
