// Upgrade NOTE: upgraded instancing buffer 'HologramTextUI' to new syntax.

// Made with Amplify Shader Editor v1.9.8.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "HologramTextUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        [Header(Color)]_ColorMultiplier("ColorMultiplier", Float) = 1
        [Header(Pattern)]_PatternSize("PatternSize", Float) = 30
        [KeywordEnum(Dots,Hex,Pixels)] _Pattern("Pattern", Float) = 0
        [Header(Glitch)]_GlitchAmount("GlitchAmount", Range( 0 , 1)) = 0
        _GlitchMinOpacity("GlitchMinOpacity", Range( 0 , 1)) = 0
        _GlitchTiling("GlitchTiling", Float) = 1
        _DistortionAmount("DistortionAmount", Float) = 0
        _TextDilate("TextDilate", Range( 0 , 1)) = 0.5
        _TextDilateExtension("TextDilateExtension", Range( 0 , 1)) = 0.2
        [KeywordEnum(Screen,UV)] _PatternCoords("PatternCoords", Float) = 0

    }

    SubShader
    {
		LOD 0

        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

        Stencil
        {
        	Ref [_Stencil]
        	ReadMask [_StencilReadMask]
        	WriteMask [_StencilWriteMask]
        	Comp [_StencilComp]
        	Pass [_StencilOp]
        }


        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        
        Pass
        {
            Name "Default"
        CGPROGRAM
            #define ASE_VERSION 19801

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityShaderVariables.cginc"
            #define ASE_NEEDS_FRAG_COLOR
            #pragma multi_compile_instancing
            #pragma shader_feature_local _PATTERN_DOTS _PATTERN_HEX _PATTERN_PIXELS
            #pragma shader_feature_local _PATTERNCOORDS_SCREEN _PATTERNCOORDS_UV


            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord  : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4  mask : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
                float4 ase_texcoord3 : TEXCOORD3;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;

            uniform float _PatternSize;
            uniform float _TextDilate;
            uniform float _TextDilateExtension;
            uniform float _DistortionAmount;
            UNITY_INSTANCING_BUFFER_START(HologramTextUI)
            	UNITY_DEFINE_INSTANCED_PROP(float, _ColorMultiplier)
#define _ColorMultiplier_arr HologramTextUI
            	UNITY_DEFINE_INSTANCED_PROP(float, _GlitchTiling)
#define _GlitchTiling_arr HologramTextUI
            	UNITY_DEFINE_INSTANCED_PROP(float, _GlitchAmount)
#define _GlitchAmount_arr HologramTextUI
            	UNITY_DEFINE_INSTANCED_PROP(float, _GlitchMinOpacity)
#define _GlitchMinOpacity_arr HologramTextUI
            UNITY_INSTANCING_BUFFER_END(HologramTextUI)
            inline float4 ASE_ComputeGrabScreenPos( float4 pos )
            {
            	#if UNITY_UV_STARTS_AT_TOP
            	float scale = -1.0;
            	#else
            	float scale = 1.0;
            	#endif
            	float4 o = pos;
            	o.y = pos.w * 0.5f;
            	o.y = ( pos.y - o.y ) * _ProjectionParams.x * scale + o.y;
            	return o;
            }
            
            float3 mod2D289( float3 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
            float2 mod2D289( float2 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
            float3 permute( float3 x ) { return mod2D289( ( ( x * 34.0 ) + 1.0 ) * x ); }
            float snoise( float2 v )
            {
            	const float4 C = float4( 0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439 );
            	float2 i = floor( v + dot( v, C.yy ) );
            	float2 x0 = v - i + dot( i, C.xx );
            	float2 i1;
            	i1 = ( x0.x > x0.y ) ? float2( 1.0, 0.0 ) : float2( 0.0, 1.0 );
            	float4 x12 = x0.xyxy + C.xxzz;
            	x12.xy -= i1;
            	i = mod2D289( i );
            	float3 p = permute( permute( i.y + float3( 0.0, i1.y, 1.0 ) ) + i.x + float3( 0.0, i1.x, 1.0 ) );
            	float3 m = max( 0.5 - float3( dot( x0, x0 ), dot( x12.xy, x12.xy ), dot( x12.zw, x12.zw ) ), 0.0 );
            	m = m * m;
            	m = m * m;
            	float3 x = 2.0 * frac( p * C.www ) - 1.0;
            	float3 h = abs( x ) - 0.5;
            	float3 ox = floor( x + 0.5 );
            	float3 a0 = x - ox;
            	m *= 1.79284291400159 - 0.85373472095314 * ( a0 * a0 + h * h );
            	float3 g;
            	g.x = a0.x * x0.x + h.x * x0.y;
            	g.yz = a0.yz * x12.xz + h.yz * x12.yw;
            	return 130.0 * dot( m, g );
            }
            


            v2f vert(appdata_t v )
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float4 ase_positionCS = UnityObjectToClipPos( v.vertex );
                float4 screenPos = ComputeScreenPos( ase_positionCS );
                OUT.ase_texcoord3 = screenPos;
                

                v.vertex.xyz +=  float3( 0, 0, 0 ) ;

                float4 vPosition = UnityObjectToClipPos(v.vertex);
                OUT.worldPosition = v.vertex;
                OUT.vertex = vPosition;

                float2 pixelSize = vPosition.w;
                pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                float2 maskUV = (v.vertex.xy - clampedRect.xy) / (clampedRect.zw - clampedRect.xy);
                OUT.texcoord = v.texcoord;
                OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN ) : SV_Target
            {
                //Round up the alpha color coming from the interpolator (to 1.0/256.0 steps)
                //The incoming alpha could have numerical instability, which makes it very sensible to
                //HDR color transparency blend, when it blends with the world's texture.
                const half alphaPrecision = half(0xff);
                const half invAlphaPrecision = half(1.0/alphaPrecision);
                IN.color.a = round(IN.color.a * alphaPrecision)*invAlphaPrecision;

                float _ColorMultiplier_Instance = UNITY_ACCESS_INSTANCED_PROP(_ColorMultiplier_arr, _ColorMultiplier);
                float4 screenPos = IN.ase_texcoord3;
                float4 ase_positionSSNorm = screenPos / screenPos.w;
                ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
                float4 ase_positionSS_Center = float4( ase_positionSSNorm.xy * 2 - 1, 0, 0 );
                float2 appendResult52 = (float2(( ase_positionSS_Center.x * ( _ScreenParams.x / _ScreenParams.y ) ) , ase_positionSS_Center.y));
                float2 texCoord468 = IN.texcoord.xy * float2( 1,1 ) + float2( 0,0 );
                #if defined( _PATTERNCOORDS_SCREEN )
                float2 staticSwitch469 = appendResult52;
                #elif defined( _PATTERNCOORDS_UV )
                float2 staticSwitch469 = texCoord468;
                #else
                float2 staticSwitch469 = appendResult52;
                #endif
                float2 PatternUV470 = staticSwitch469;
                float2 temp_output_419_0 = ( PatternUV470 + float2( 10,1 ) );
                float SizeLV81 = _PatternSize;
                float2 temp_output_416_0 = ( temp_output_419_0 * SizeLV81 );
                float2 break16_g60 = temp_output_416_0;
                float2 appendResult7_g60 = (float2(( break16_g60.x + ( 0.5 * step( 1.0 , ( break16_g60.y % 2.0 ) ) ) ) , ( break16_g60.y + ( 1.0 * step( 1.0 , ( break16_g60.x % 2.0 ) ) ) )));
                float temp_output_466_0 = ( 1.0 - _TextDilate );
                float4 ase_grabScreenPos = ASE_ComputeGrabScreenPos( screenPos );
                float4 ase_grabScreenPosNorm = ase_grabScreenPos / ase_grabScreenPos.w;
                float _GlitchTiling_Instance = UNITY_ACCESS_INSTANCED_PROP(_GlitchTiling_arr, _GlitchTiling);
                float2 appendResult301 = (float2(( 0.0 * ase_grabScreenPosNorm.r ) , ( ase_grabScreenPosNorm.g * _GlitchTiling_Instance )));
                float temp_output_2_0_g39 = 50.0;
                float mulTime306 = _Time.y * 3.0;
                float temp_output_2_0_g40 = 5.0;
                float simplePerlin2D304 = snoise( ( ( round( ( appendResult301 * temp_output_2_0_g39 ) ) / temp_output_2_0_g39 ) + ( round( ( mulTime306 * temp_output_2_0_g40 ) ) / temp_output_2_0_g40 ) )*100.0 );
                simplePerlin2D304 = simplePerlin2D304*0.5 + 0.5;
                float temp_output_309_0 = pow( simplePerlin2D304 , 3.0 );
                float GlitchPattern467 = temp_output_309_0;
                float _GlitchAmount_Instance = UNITY_ACCESS_INSTANCED_PROP(_GlitchAmount_arr, _GlitchAmount);
                float GlitchAmount444 = _GlitchAmount_Instance;
                float2 texCoord434 = IN.texcoord.xy * float2( 1,1 ) + float2( 0,0 );
                float2 appendResult437 = (float2(( ( (-0.5 + (GlitchPattern467 - 1.0) * (0.5 - -0.5) / (0.0 - 1.0)) * _DistortionAmount * GlitchAmount444 ) + texCoord434.x ) , texCoord434.y));
                float4 tex2DNode404 = tex2D( _MainTex, appendResult437 );
                float smoothstepResult460 = smoothstep( temp_output_466_0 , ( temp_output_466_0 + _TextDilateExtension ) , tex2DNode404.a);
                float lerpResult320 = lerp( 1.0 , temp_output_309_0 , _GlitchAmount_Instance);
                float Glitch324 = lerpResult320;
                float _GlitchMinOpacity_Instance = UNITY_ACCESS_INSTANCED_PROP(_GlitchMinOpacity_arr, _GlitchMinOpacity);
                float Alpha421 = ( IN.color.a * smoothstepResult460 * max( Glitch324 , _GlitchMinOpacity_Instance ) );
                float temp_output_2_0_g60 = Alpha421;
                float2 appendResult11_g61 = (float2(temp_output_2_0_g60 , temp_output_2_0_g60));
                float temp_output_17_0_g61 = length( ( (frac( appendResult7_g60 )*2.0 + -1.0) / appendResult11_g61 ) );
                float2 break19_g59 = ( ( temp_output_419_0 * SizeLV81 ) * float2( 1,1 ) );
                float temp_output_20_0_g59 = ( break19_g59.x * 1.5 );
                float2 appendResult14_g59 = (float2(temp_output_20_0_g59 , ( break19_g59.y + ( ( floor( temp_output_20_0_g59 ) % 2.0 ) * 0.5 ) )));
                float2 break12_g59 = abs( ( ( appendResult14_g59 % float2( 1,1 ) ) - float2( 0.5,0.5 ) ) );
                float smoothstepResult1_g59 = smoothstep( 0.0 , 1.0 , ( abs( ( max( ( ( break12_g59.x * 1.5 ) + break12_g59.y ) , ( break12_g59.y * 2.0 ) ) - 1.0 ) ) * 2.0 ));
                float lerpResult459 = lerp( 0.2 , 0.5 , Alpha421);
                float temp_output_2_0_g57 = lerpResult459;
                float2 appendResult10_g58 = (float2(temp_output_2_0_g57 , temp_output_2_0_g57));
                float2 temp_output_11_0_g58 = ( abs( (frac( (temp_output_416_0*float2( 8,8 ) + float2( 0,0 )) )*2.0 + -1.0) ) - appendResult10_g58 );
                float2 break16_g58 = ( 1.0 - ( temp_output_11_0_g58 / max( fwidth( temp_output_11_0_g58 ) , float2( 1E-05,1E-05 ) ) ) );
                #if defined( _PATTERN_DOTS )
                float staticSwitch449 = saturate( ( ( 1.0 - temp_output_17_0_g61 ) / fwidth( temp_output_17_0_g61 ) ) );
                #elif defined( _PATTERN_HEX )
                float staticSwitch449 = ( Alpha421 * smoothstepResult1_g59 );
                #elif defined( _PATTERN_PIXELS )
                float staticSwitch449 = ( saturate( min( break16_g58.x , break16_g58.y ) ) * Alpha421 );
                #else
                float staticSwitch449 = saturate( ( ( 1.0 - temp_output_17_0_g61 ) / fwidth( temp_output_17_0_g61 ) ) );
                #endif
                float4 appendResult401 = (float4(( IN.color * _ColorMultiplier_Instance ).rgb , staticSwitch449));
                

                half4 color = appendResult401;

                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                color.a *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (color.a - 0.001);
                #endif

                color.rgb *= color.a;

                return color;
            }
        ENDCG
        }
    }
    CustomEditor "AmplifyShaderEditor.MaterialInspector"
	
	Fallback Off
}
/*ASEBEGIN
Version=19801
Node;AmplifyShaderEditor.CommentaryNode;323;-2176,992;Inherit;False;2804;898.95;;18;295;310;301;303;306;308;302;307;305;304;309;321;320;326;327;324;444;467;Glitch;1,1,1,1;0;0
Node;AmplifyShaderEditor.GrabScreenPosition;295;-1440,1472;Inherit;False;0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;327;-1376,1664;Inherit;False;InstancedProperty;_GlitchTiling;GlitchTiling;7;0;Create;True;0;0;0;False;0;False;1;1.96;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;310;-1104,1472;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;326;-1088,1584;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;301;-848,1520;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;303;-848,1616;Inherit;False;Constant;_Float3;Float 3;15;0;Create;True;0;0;0;False;0;False;50;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;306;-880,1696;Inherit;False;1;0;FLOAT;3;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;308;-848,1776;Inherit;False;Constant;_Float4;Float 3;15;0;Create;True;0;0;0;False;0;False;5;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;302;-688,1536;Inherit;False;SectionsRemap;-1;;39;89ceb6885fe152447bec7ac9a7ea62de;0;2;1;FLOAT2;0,0;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.FunctionNode;307;-672,1696;Inherit;False;SectionsRemap;-1;;40;89ceb6885fe152447bec7ac9a7ea62de;0;2;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;305;-384,1616;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.NoiseGeneratorNode;304;-256,1616;Inherit;False;Simplex2D;True;False;2;0;FLOAT2;0,0;False;1;FLOAT;100;False;1;FLOAT;0
Node;AmplifyShaderEditor.PowerNode;309;-48,1616;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;3;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;321;-176,1744;Inherit;False;InstancedProperty;_GlitchAmount;GlitchAmount;5;1;[Header];Create;True;1;Glitch;0;0;False;0;False;0;0.515;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;467;160,1360;Inherit;False;GlitchPattern;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;444;183.8279,1794.5;Inherit;False;GlitchAmount;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;436;-1792,-320;Inherit;False;467;GlitchPattern;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;445;-1600,16;Inherit;False;444;GlitchAmount;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;442;-1648,-80;Inherit;False;Property;_DistortionAmount;DistortionAmount;8;0;Create;True;0;0;0;False;0;False;0;0.01;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TFHCRemapNode;443;-1568,-272;Inherit;False;5;0;FLOAT;0;False;1;FLOAT;1;False;2;FLOAT;0;False;3;FLOAT;-0.5;False;4;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;150;-3744,-528;Inherit;False;1460;466.95;;6;24;15;25;51;52;333;SCREEN SPACE PROJECTION;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;439;-1280,-240;Inherit;False;3;3;0;FLOAT;0;False;1;FLOAT;0.01;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;434;-1312,128;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.ScreenParams;24;-3696,-304;Inherit;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.LerpOp;320;160,1520;Inherit;False;3;0;FLOAT;1;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;435;-880,-16;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;461;-384,64;Inherit;False;Property;_TextDilate;TextDilate;9;0;Create;True;0;0;0;False;0;False;0.5;0.583;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.ScreenPosInputsNode;15;-3696,-480;Float;False;2;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleDivideOpNode;25;-3344,-224;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;324;384,1520;Inherit;False;Glitch;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TemplateShaderPropertyNode;403;-864,-192;Inherit;False;0;0;_MainTex;Shader;False;0;5;SAMPLER2D;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.DynamicAppendNode;437;-720,32;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;465;-416,304;Inherit;False;Property;_TextDilateExtension;TextDilateExtension;10;0;Create;True;0;0;0;False;0;False;0.2;0.308;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;466;-112,64;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;51;-3264,-464;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;441;-112,288;Inherit;False;InstancedProperty;_GlitchMinOpacity;GlitchMinOpacity;6;0;Create;True;1;Glitch;0;0;False;0;False;0;1;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;420;-64,208;Inherit;False;324;Glitch;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;464;16,112;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.1;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;52;-2992,-384;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;468;-2989.083,176.2498;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;404;-480,-176;Inherit;True;Property;_TextureSample0;Texture Sample 0;5;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SmoothstepOpNode;460;96,16;Inherit;False;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMaxOpNode;440;160,208;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;469;-2637.083,0.2498016;Inherit;False;Property;_PatternCoords;PatternCoords;11;0;Create;True;0;0;0;False;0;False;0;0;0;True;;KeywordEnum;2;Screen;UV;Create;True;True;All;9;1;FLOAT2;0,0;False;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT2;0,0;False;6;FLOAT2;0,0;False;7;FLOAT2;0,0;False;8;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.VertexColorNode;411;-432,-672;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;16;-4480,128;Inherit;False;Property;_PatternSize;PatternSize;1;1;[Header];Create;True;1;Pattern;0;0;False;0;False;30;60;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;405;288,0;Inherit;False;3;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;470;-2237.083,48.2498;Inherit;False;PatternUV;-1;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;81;-4224,128;Inherit;False;SizeLV;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;418;-112,464;Inherit;False;470;PatternUV;1;0;OBJECT;;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;421;496,48;Inherit;False;Alpha;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;419;160,432;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;10,1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode;417;144,672;Inherit;False;81;SizeLV;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;448;400,304;Inherit;False;421;Alpha;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;429;464.3066,618.1255;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;416;432,432;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.LerpOp;459;544,480;Inherit;False;3;0;FLOAT;0.2;False;1;FLOAT;0.5;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;423;720,416;Inherit;False;GridCustomUV;-1;;57;5a18cb14e13ca964f892f4b471661941;0;4;11;FLOAT2;0,0;False;5;FLOAT2;8,8;False;6;FLOAT2;0,0;False;2;FLOAT;0.35;False;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;424;720,640;Inherit;False;Hex Lattice Custom;-1;;59;185ff78529fda534280abd9013dd1fbc;0;4;25;FLOAT2;0,0;False;3;FLOAT2;1,1;False;2;FLOAT;1;False;4;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;412;800,208;Inherit;False;Dots Pattern;-1;;60;7d8d5e315fd9002418fb41741d3a59cb;1,22,1;5;21;FLOAT2;0,0;False;3;FLOAT2;8,8;False;2;FLOAT;0.9;False;4;FLOAT;0.5;False;5;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;432;1008,512;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;433;944,336;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;409;736,-96;Inherit;False;InstancedProperty;_ColorMultiplier;ColorMultiplier;0;1;[Header];Create;True;1;Color;0;0;False;0;False;1;10;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.StaticSwitch;449;1104,224;Inherit;False;Property;_Pattern;Pattern;2;0;Create;True;0;0;0;False;0;False;0;0;0;True;;KeywordEnum;3;Dots;Hex;Pixels;Create;True;True;All;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;408;928,-144;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;471;128,-256;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;333;-2704,-384;Inherit;False;ScreenPos;-1;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;458;-752,240;Inherit;False;InstancedProperty;_MipLevel;MipLevel;4;2;[Header];[IntRange];Create;True;1;Mip level aura (requires mip level enabled on texture );0;0;False;0;False;1;1;0;10;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;139;-4640,304;Inherit;False;Property;_PatternWidth;PatternWidth;3;0;Create;True;0;0;0;False;0;False;0.1;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;140;-4352,304;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;138;-4192,304;Inherit;False;LineWidth;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;401;1360,32;Inherit;False;FLOAT4;4;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;1;False;1;FLOAT4;0
Node;AmplifyShaderEditor.SamplerNode;450;-580,80;Inherit;True;Property;_TextureSample1;Texture Sample 0;5;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;MipLevel;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;2;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;397;1616,112;Float;False;True;-1;3;AmplifyShaderEditor.MaterialInspector;0;3;HologramTextUI;5056123faa0c79b47ab6ad7e8bf059a4;True;Default;0;0;Default;2;False;True;3;1;False;;10;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;True;True;True;True;True;0;True;_ColorMask;False;False;False;False;False;False;False;True;True;0;True;_Stencil;255;True;_StencilReadMask;255;True;_StencilWriteMask;0;True;_StencilComp;0;True;_StencilOp;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;2;False;;True;0;True;unity_GUIZTestMode;False;True;5;Queue=Transparent=Queue=0;IgnoreProjector=True;RenderType=Transparent=RenderType;PreviewType=Plane;CanUseSpriteAtlas=True;False;False;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;3;False;0;;0;0;Standard;0;0;1;True;False;;False;0
WireConnection;310;1;295;1
WireConnection;326;0;295;2
WireConnection;326;1;327;0
WireConnection;301;0;310;0
WireConnection;301;1;326;0
WireConnection;302;1;301;0
WireConnection;302;2;303;0
WireConnection;307;1;306;0
WireConnection;307;2;308;0
WireConnection;305;0;302;0
WireConnection;305;1;307;0
WireConnection;304;0;305;0
WireConnection;309;0;304;0
WireConnection;467;0;309;0
WireConnection;444;0;321;0
WireConnection;443;0;436;0
WireConnection;439;0;443;0
WireConnection;439;1;442;0
WireConnection;439;2;445;0
WireConnection;320;1;309;0
WireConnection;320;2;321;0
WireConnection;435;0;439;0
WireConnection;435;1;434;1
WireConnection;25;0;24;1
WireConnection;25;1;24;2
WireConnection;324;0;320;0
WireConnection;437;0;435;0
WireConnection;437;1;434;2
WireConnection;466;0;461;0
WireConnection;51;0;15;1
WireConnection;51;1;25;0
WireConnection;464;0;466;0
WireConnection;464;1;465;0
WireConnection;52;0;51;0
WireConnection;52;1;15;2
WireConnection;404;0;403;0
WireConnection;404;1;437;0
WireConnection;460;0;404;4
WireConnection;460;1;466;0
WireConnection;460;2;464;0
WireConnection;440;0;420;0
WireConnection;440;1;441;0
WireConnection;469;1;52;0
WireConnection;469;0;468;0
WireConnection;405;0;411;4
WireConnection;405;1;460;0
WireConnection;405;2;440;0
WireConnection;470;0;469;0
WireConnection;81;0;16;0
WireConnection;421;0;405;0
WireConnection;419;0;418;0
WireConnection;429;0;419;0
WireConnection;429;1;417;0
WireConnection;416;0;419;0
WireConnection;416;1;417;0
WireConnection;459;2;448;0
WireConnection;423;11;416;0
WireConnection;423;2;459;0
WireConnection;424;25;429;0
WireConnection;412;21;416;0
WireConnection;412;2;448;0
WireConnection;432;0;448;0
WireConnection;432;1;424;0
WireConnection;433;0;423;0
WireConnection;433;1;448;0
WireConnection;449;1;412;0
WireConnection;449;0;432;0
WireConnection;449;2;433;0
WireConnection;408;0;411;0
WireConnection;408;1;409;0
WireConnection;471;0;411;0
WireConnection;471;1;404;0
WireConnection;333;0;52;0
WireConnection;140;0;139;0
WireConnection;138;0;140;0
WireConnection;401;0;408;0
WireConnection;401;3;449;0
WireConnection;450;0;403;0
WireConnection;450;1;437;0
WireConnection;450;2;458;0
WireConnection;397;0;401;0
ASEEND*/
//CHKSM=8ACEF72D2F7E8FD50F9A7E9485709B28B6936812