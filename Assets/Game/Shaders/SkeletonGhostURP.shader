// Dash after-image for Spine skeletons. Same look as "Spine/Special/SkeletonGhost".
// SkeletonGhost clones the skeleton material and swaps in this shader, so the texture
// sits in whichever slot the original shader uses: _BaseMap (URP Lit / Complex Lit) or
// _MainTex (Spine shaders, e.g. Universal Render Pipeline/Spine/Sprite). Builds strip
// the other slot (in the editor it can survive as a stale property), and sampling an
// empty slot gives Unity's tiny default texture, which drew the ghost as solid quads.
// So both are declared and the larger one (the real atlas) is used.
// Premultiplied alpha, unlit, no depth.
Shader "JunkLite/SkeletonGhostURP" {
    Properties {
        [MainColor] _Color ("Main Color", Color) = (1,1,1,1)
        [MainTexture][NoScaleOffset] _BaseMap ("Base (RGB) Alpha (A)", 2D) = "white" {}
        [NoScaleOffset] _MainTex ("Spine Texture (RGB) Alpha (A)", 2D) = "white" {}
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
            sampler2D _MainTex;
            float4 _BaseMap_TexelSize;
            float4 _MainTex_TexelSize;
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
                // Unity's default textures are a few texels wide; a character atlas is not.
                fixed4 tc = _MainTex_TexelSize.z > _BaseMap_TexelSize.z
                    ? tex2D(_MainTex, i.uv)
                    : tex2D(_BaseMap, i.uv);
                tc.rgb = max(_TextureFade, tc.rgb);
                return tc * ((i.color * _Color) * tc.a);
            }
            ENDCG
        }
    }
}
