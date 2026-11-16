// Dash after-image for Spine skeletons whose materials are URP Lit / Complex Lit.
// Same look as "Spine/Special/SkeletonGhost", but samples _BaseMap: SkeletonGhost
// clones the skeleton material and swaps the shader, and URP Lit materials have
// no _MainTex in builds (it only survives as a stale editor property), which made
// the ghost draw solid quads. Premultiplied alpha, unlit, no depth.
Shader "JunkLite/SkeletonGhostURP" {
    Properties {
        [MainColor] _Color ("Main Color", Color) = (1,1,1,1)
        [MainTexture][NoScaleOffset] _BaseMap ("Base (RGB) Alpha (A)", 2D) = "white" {}
        _TextureFade ("Texture Fade Out", Range(0,1)) = 0
        [HideInInspector] _StencilRef("Stencil Reference", Float) = 1.0
        [HideInInspector][Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp("Stencil Comparison", Float) = 8
    }
    SubShader {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Stencil {
            Ref[_StencilRef]
            Comp[_StencilComp]
            Pass Keep
        }

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _BaseMap;
            fixed4 _Color;
            fixed _TextureFade;

            struct VertexInput {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct VertexOutput {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            VertexOutput vert (VertexInput v) {
                VertexOutput o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (VertexOutput i) : SV_Target {
                fixed4 tc = tex2D(_BaseMap, i.uv);
                tc.rgb = max(_TextureFade, tc.rgb);
                return tc * ((i.color * _Color) * tc.a);
            }
            ENDCG
        }
    }
}
