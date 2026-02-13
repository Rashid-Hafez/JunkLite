// Upgrade NOTE: upgraded instancing buffer 'HologramUI' to new syntax.

// Made with Amplify Shader Editor v1.9.8.1
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "HologramUI"
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
        [Header(Mip level aura (requires mip level enabled on texture ))][IntRange]_MipLevel("MipLevel", Range( 0 , 10)) = 1
        _MipLevelIntensity("MipLevelIntensity", Float) = 1
        [Header(Glitch)]_GlitchAmount("GlitchAmount", Range( 0 , 1)) = 0
        _GlitchMinOpacity("GlitchMinOpacity", Range( 0 , 1)) = 0
        _GlitchTiling("GlitchTiling", Float) = 1
        _DistortionAmount("DistortionAmount", Float) = 0
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

            uniform float _DistortionAmount;
            uniform float _PatternSize;
            UNITY_INSTANCING_BUFFER_START(HologramUI)
            	UNITY_DEFINE_INSTANCED_PROP(float, _ColorMultiplier)
#define _ColorMultiplier_arr HologramUI
            	UNITY_DEFINE_INSTANCED_PROP(float, _GlitchTiling)
#define _GlitchTiling_arr HologramUI
            	UNITY_DEFINE_INSTANCED_PROP(float, _GlitchAmount)
#define _GlitchAmount_arr HologramUI
            	UNITY_DEFINE_INSTANCED_PROP(float, _MipLevel)
#define _MipLevel_arr HologramUI
            	UNITY_DEFINE_INSTANCED_PROP(float, _MipLevelIntensity)
#define _MipLevelIntensity_arr HologramUI
            	UNITY_DEFINE_INSTANCED_PROP(float, _GlitchMinOpacity)
#define _GlitchMinOpacity_arr HologramUI
            UNITY_INSTANCING_BUFFER_END(HologramUI)
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
                float4 temp_output_408_0 = ( IN.color * _ColorMultiplier_Instance );
                float4 screenPos = IN.ase_texcoord3;
                float4 ase_grabScreenPos = ASE_ComputeGrabScreenPos( screenPos );
                float4 ase_grabScreenPosNorm = ase_grabScreenPos / ase_grabScreenPos.w;
                float _GlitchTiling_Instance = UNITY_ACCESS_INSTANCED_PROP(_GlitchTiling_arr, _GlitchTiling);
                float2 appendResult301 = (float2(( 0.0 * ase_grabScreenPosNorm.r ) , ( ase_grabScreenPosNorm.g * _GlitchTiling_Instance )));
                float temp_output_2_0_g39 = 50.0;
                float mulTime306 = _Time.y * 3.0;
                float temp_output_2_0_g40 = 5.0;
                float simplePerlin2D304 = snoise( ( ( round( ( appendResult301 * temp_output_2_0_g39 ) ) / temp_output_2_0_g39 ) + ( round( ( mulTime306 * temp_output_2_0_g40 ) ) / temp_output_2_0_g40 ) )*100.0 );
                simplePerlin2D304 = simplePerlin2D304*0.5 + 0.5;
                float GlitchPattern460 = simplePerlin2D304;
                float _GlitchAmount_Instance = UNITY_ACCESS_INSTANCED_PROP(_GlitchAmount_arr, _GlitchAmount);
                float GlitchAmount444 = _GlitchAmount_Instance;
                float2 texCoord434 = IN.texcoord.xy * float2( 1,1 ) + float2( 0,0 );
                float2 appendResult437 = (float2(( ( (-1.0 + (GlitchPattern460 - 1.5) * (1.0 - -1.0) / (0.0 - 1.5)) * _DistortionAmount * GlitchAmount444 ) + texCoord434.x ) , texCoord434.y));
                float _MipLevel_Instance = UNITY_ACCESS_INSTANCED_PROP(_MipLevel_arr, _MipLevel);
                float _MipLevelIntensity_Instance = UNITY_ACCESS_INSTANCED_PROP(_MipLevelIntensity_arr, _MipLevelIntensity);
                float4 ase_positionSSNorm = screenPos / screenPos.w;
                ase_positionSSNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_positionSSNorm.z : ase_positionSSNorm.z * 0.5 + 0.5;
                float4 ase_positionSS_Center = float4( ase_positionSSNorm.xy * 2 - 1, 0, 0 );
                float2 appendResult52 = (float2(( ase_positionSS_Center.x * ( _ScreenParams.x / _ScreenParams.y ) ) , ase_positionSS_Center.y));
                float2 ScreenPos333 = appendResult52;
                float2 texCoord461 = IN.texcoord.xy * float2( 1,1 ) + float2( 0,0 );
                #if defined( _PATTERNCOORDS_SCREEN )
                float2 staticSwitch462 = ScreenPos333;
                #elif defined( _PATTERNCOORDS_UV )
                float2 staticSwitch462 = texCoord461;
                #else
                float2 staticSwitch462 = ScreenPos333;
                #endif
                float2 PatternUV463 = staticSwitch462;
                float2 temp_output_419_0 = ( PatternUV463 + float2( 10,1 ) );
                float SizeLV81 = _PatternSize;
                float2 temp_output_416_0 = ( temp_output_419_0 * SizeLV81 );
                float2 break16_g60 = temp_output_416_0;
                float2 appendResult7_g60 = (float2(( break16_g60.x + ( 0.5 * step( 1.0 , ( break16_g60.y % 2.0 ) ) ) ) , ( break16_g60.y + ( 1.0 * step( 1.0 , ( break16_g60.x % 2.0 ) ) ) )));
                float lerpResult320 = lerp( 1.0 , pow( simplePerlin2D304 , 3.0 ) , _GlitchAmount_Instance);
                float Glitch324 = lerpResult320;
                float _GlitchMinOpacity_Instance = UNITY_ACCESS_INSTANCED_PROP(_GlitchMinOpacity_arr, _GlitchMinOpacity);
                float Alpha421 = ( IN.color.a * tex2D( _MainTex, appendResult437 ).a * max( Glitch324 , _GlitchMinOpacity_Instance ) );
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
                float4 appendResult401 = (float4(( temp_output_408_0 + ( temp_output_408_0 * ( ( 1.0 - tex2Dlod( _MainTex, float4( appendResult437, 0, _MipLevel_Instance) ).a ) * _MipLevelIntensity_Instance ) ) ).rgb , staticSwitch449));
                

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
Node;AmplifyShaderEditor.CommentaryNode;323;-2176,992;Inherit;False;2804;898.95;;18;295;310;301;303;306;308;302;307;305;304;309;321;320;326;327;324;444;460;Glitch;1,1,1,1;0;0
Node;AmplifyShaderEditor.GrabScreenPosition;295;-1440,1472;Inherit;False;0;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;327;-1376,1664;Inherit;False;InstancedProperty;_GlitchTiling;GlitchTiling;8;0;Create;True;0;0;0;False;0;False;1;2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;310;-1104,1472;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0.5;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;326;-1088,1584;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;301;-848,1520;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;303;-848,1616;Inherit;False;Constant;_Float3;Float 3;15;0;Create;True;0;0;0;False;0;False;50;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleTimeNode;306;-880,1696;Inherit;False;1;0;FLOAT;3;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;308;-848,1776;Inherit;False;Constant;_Float4;Float 3;15;0;Create;True;0;0;0;False;0;False;5;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;302;-688,1536;Inherit;False;SectionsRemap;-1;;39;e07d5d5a126b6f243ac17cc867746f65;0;2;1;FLOAT2;0,0;False;2;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.FunctionNode;307;-672,1696;Inherit;False;SectionsRemap;-1;;40;e07d5d5a126b6f243ac17cc867746f65;0;2;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;305;-384,1616;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.NoiseGeneratorNode;304;-256,1616;Inherit;False;Simplex2D;True;False;2;0;FLOAT2;0,0;False;1;FLOAT;100;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;321;-176,1744;Inherit;False;InstancedProperty;_GlitchAmount;GlitchAmount;6;1;[Header];Create;True;1;Glitch;0;0;False;0;False;0;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;460;169.8065,1364.979;Inherit;False;GlitchPattern;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;150;-3744,-528;Inherit;False;1460;466.95;;6;24;15;25;51;52;333;SCREEN SPACE PROJECTION;1,1,1,1;0;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;444;183.8279,1794.5;Inherit;False;GlitchAmount;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;436;-1792,-320;Inherit;False;460;GlitchPattern;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.ScreenParams;24;-3696,-304;Inherit;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.PowerNode;309;-48,1616;Inherit;False;False;2;0;FLOAT;0;False;1;FLOAT;3;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;445;-1600,16;Inherit;False;444;GlitchAmount;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;442;-1648,-80;Inherit;False;Property;_DistortionAmount;DistortionAmount;9;0;Create;True;0;0;0;False;0;False;0;0.02;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.ScreenPosInputsNode;15;-3696,-480;Float;False;2;False;0;5;FLOAT4;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleDivideOpNode;25;-3344,-224;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TFHCRemapNode;443;-1568,-272;Inherit;False;5;0;FLOAT;0;False;1;FLOAT;1.5;False;2;FLOAT;0;False;3;FLOAT;-1;False;4;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.LerpOp;320;160,1520;Inherit;False;3;0;FLOAT;1;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;439;-1280,-240;Inherit;False;3;3;0;FLOAT;0;False;1;FLOAT;0.01;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;434;-1312,128;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;51;-3264,-464;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;435;-880,-16;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;324;384,1520;Inherit;False;Glitch;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;52;-2992,-384;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TemplateShaderPropertyNode;403;-864,-192;Inherit;False;0;0;_MainTex;Shader;False;0;5;SAMPLER2D;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.GetLocalVarNode;420;-384,288;Inherit;False;324;Glitch;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;441;-480,368;Inherit;False;InstancedProperty;_GlitchMinOpacity;GlitchMinOpacity;7;0;Create;True;1;Glitch;0;0;False;0;False;0;1;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;437;-720,32;Inherit;False;FLOAT2;4;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;333;-2704,-384;Inherit;False;ScreenPos;-1;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;461;-2704,160;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SamplerNode;404;-432,-112;Inherit;True;Property;_TextureSample0;Texture Sample 0;5;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;Auto;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.SimpleMaxOpNode;440;-144,320;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.VertexColorNode;411;-336,-304;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.StaticSwitch;462;-2352,-16;Inherit;False;Property;_PatternCoords;PatternCoords;10;0;Create;True;0;0;0;False;0;False;0;0;0;True;;KeywordEnum;2;Screen;UV;Create;True;True;All;9;1;FLOAT2;0,0;False;0;FLOAT2;0,0;False;2;FLOAT2;0,0;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT2;0,0;False;6;FLOAT2;0,0;False;7;FLOAT2;0,0;False;8;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;405;64,48;Inherit;False;3;3;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;16;-4480,128;Inherit;False;Property;_PatternSize;PatternSize;1;1;[Header];Create;True;1;Pattern;0;0;False;0;False;30;60;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;463;-1984,0;Inherit;False;PatternUV;-1;True;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;81;-4224,128;Inherit;False;SizeLV;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;458;-752,240;Inherit;False;InstancedProperty;_MipLevel;MipLevel;4;2;[Header];[IntRange];Create;True;1;Mip level aura (requires mip level enabled on texture );0;0;False;0;False;1;2;0;10;0;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;421;384,-64;Inherit;False;Alpha;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.GetLocalVarNode;418;-112,464;Inherit;False;463;PatternUV;1;0;OBJECT;;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;419;160,432;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;10,1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.GetLocalVarNode;448;400,304;Inherit;False;421;Alpha;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SamplerNode;450;-464,80;Inherit;True;Property;_TextureSample1;Texture Sample 0;5;0;Create;True;0;0;0;False;0;False;-1;None;None;True;0;False;white;Auto;False;Object;-1;MipLevel;Texture2D;8;0;SAMPLER2D;;False;1;FLOAT2;0,0;False;2;FLOAT;2;False;3;FLOAT2;0,0;False;4;FLOAT2;0,0;False;5;FLOAT;1;False;6;FLOAT;0;False;7;SAMPLERSTATE;;False;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.GetLocalVarNode;417;144,672;Inherit;False;81;SizeLV;1;0;OBJECT;;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;429;464.3066,618.1255;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.OneMinusNode;452;7.32605,302.2412;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;455;16,368;Inherit;False;InstancedProperty;_MipLevelIntensity;MipLevelIntensity;5;0;Create;True;0;0;0;False;0;False;1;41.38;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;416;432,432;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT;0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.LerpOp;459;544,480;Inherit;False;3;0;FLOAT;0.2;False;1;FLOAT;0.5;False;2;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;409;736,-96;Inherit;False;InstancedProperty;_ColorMultiplier;ColorMultiplier;0;1;[Header];Create;True;1;Color;0;0;False;0;False;1;10;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;408;928,-144;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;454;416,208;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;423;720,416;Inherit;False;GridCustomUV;-1;;57;f91aea11d974db3439518532de40981b;0;4;11;FLOAT2;0,0;False;5;FLOAT2;8,8;False;6;FLOAT2;0,0;False;2;FLOAT;0.35;False;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;424;720,640;Inherit;False;Hex Lattice Custom;-1;;59;7789ca3715bcafc41a0760e358709579;0;4;25;FLOAT2;0,0;False;3;FLOAT2;1,1;False;2;FLOAT;1;False;4;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.FunctionNode;412;800,208;Inherit;False;Dots Pattern;-1;;60;7d8d5e315fd9002418fb41741d3a59cb;1,22,1;5;21;FLOAT2;0,0;False;3;FLOAT2;8,8;False;2;FLOAT;0.9;False;4;FLOAT;0.5;False;5;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;432;1008,512;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;456;1040,16;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;433;944,336;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;457;1149.206,-109.9184;Inherit;False;2;2;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.StaticSwitch;449;1104,224;Inherit;False;Property;_Pattern;Pattern;2;0;Create;True;0;0;0;False;0;False;0;0;0;True;;KeywordEnum;3;Dots;Hex;Pixels;Create;True;True;All;9;1;FLOAT;0;False;0;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;5;FLOAT;0;False;6;FLOAT;0;False;7;FLOAT;0;False;8;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;139;-4640,304;Inherit;False;Property;_PatternWidth;PatternWidth;3;0;Create;True;0;0;0;False;0;False;0.1;0;0;1;0;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;140;-4352,304;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RegisterLocalVarNode;138;-4192,304;Inherit;False;LineWidth;-1;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DynamicAppendNode;401;1360,32;Inherit;False;FLOAT4;4;0;FLOAT3;0,0,0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;1;False;1;FLOAT4;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;397;1616,112;Float;False;True;-1;3;AmplifyShaderEditor.MaterialInspector;0;3;HologramUI;5056123faa0c79b47ab6ad7e8bf059a4;True;Default;0;0;Default;2;False;True;3;1;False;;10;False;;0;1;False;;0;False;;False;False;False;False;False;False;False;False;False;False;False;False;True;2;False;;False;True;True;True;True;True;0;True;_ColorMask;False;False;False;False;False;False;False;True;True;0;True;_Stencil;255;True;_StencilReadMask;255;True;_StencilWriteMask;0;True;_StencilComp;0;True;_StencilOp;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;2;False;;True;0;True;unity_GUIZTestMode;False;True;5;Queue=Transparent=Queue=0;IgnoreProjector=True;RenderType=Transparent=RenderType;PreviewType=Plane;CanUseSpriteAtlas=True;False;False;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;3;False;0;;0;0;Standard;0;0;1;True;False;;False;0
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
WireConnection;460;0;304;0
WireConnection;444;0;321;0
WireConnection;309;0;304;0
WireConnection;25;0;24;1
WireConnection;25;1;24;2
WireConnection;443;0;436;0
WireConnection;320;1;309;0
WireConnection;320;2;321;0
WireConnection;439;0;443;0
WireConnection;439;1;442;0
WireConnection;439;2;445;0
WireConnection;51;0;15;1
WireConnection;51;1;25;0
WireConnection;435;0;439;0
WireConnection;435;1;434;1
WireConnection;324;0;320;0
WireConnection;52;0;51;0
WireConnection;52;1;15;2
WireConnection;437;0;435;0
WireConnection;437;1;434;2
WireConnection;333;0;52;0
WireConnection;404;0;403;0
WireConnection;404;1;437;0
WireConnection;440;0;420;0
WireConnection;440;1;441;0
WireConnection;462;1;333;0
WireConnection;462;0;461;0
WireConnection;405;0;411;4
WireConnection;405;1;404;4
WireConnection;405;2;440;0
WireConnection;463;0;462;0
WireConnection;81;0;16;0
WireConnection;421;0;405;0
WireConnection;419;0;418;0
WireConnection;450;0;403;0
WireConnection;450;1;437;0
WireConnection;450;2;458;0
WireConnection;429;0;419;0
WireConnection;429;1;417;0
WireConnection;452;0;450;4
WireConnection;416;0;419;0
WireConnection;416;1;417;0
WireConnection;459;2;448;0
WireConnection;408;0;411;0
WireConnection;408;1;409;0
WireConnection;454;0;452;0
WireConnection;454;1;455;0
WireConnection;423;11;416;0
WireConnection;423;2;459;0
WireConnection;424;25;429;0
WireConnection;412;21;416;0
WireConnection;412;2;448;0
WireConnection;432;0;448;0
WireConnection;432;1;424;0
WireConnection;456;0;408;0
WireConnection;456;1;454;0
WireConnection;433;0;423;0
WireConnection;433;1;448;0
WireConnection;457;0;408;0
WireConnection;457;1;456;0
WireConnection;449;1;412;0
WireConnection;449;0;432;0
WireConnection;449;2;433;0
WireConnection;140;0;139;0
WireConnection;138;0;140;0
WireConnection;401;0;457;0
WireConnection;401;3;449;0
WireConnection;397;0;401;0
ASEEND*/
//CHKSM=75C7923D7664D5275B010CC9354EE218D89A5967