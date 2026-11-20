// Ground of the canal (floor gravel, soil on the outer bank, strips at the top): four layers blended in ONE pass
// by the mesh vertex colours, so it has no layer limit trouble and no second pass (unlike the Unity Terrain shader).
//   layer 0 (base)   : fine gravel      - where the vertex colour is black
//   layer 1 (red)    : coarse gravel
//   layer 2 (green)  : silt / soil
//   layer 3 (blue)   : dry grass and litter
// Paint the colours with Polybrush (Paint Vertex Colors). Albedo alpha = height (stones poke through sand at the edges).
// Cost: 8 texture reads per pixel (albedo + normal of 4 layers), about the same as URP Lit with a detail map.
Shader "CarX/CanalGround"
{
    Properties
    {
        _L0 ("0 Мелкий гравий (A = высота)", 2D) = "gray" {}
        [Normal] _N0 ("0 Нормали", 2D) = "bump" {}
        _T0 ("0 Размер плитки, м", Float) = 2.5
        _L1 ("1 Крупный гравий, красный", 2D) = "gray" {}
        [Normal] _N1 ("1 Нормали", 2D) = "bump" {}
        _T1 ("1 Размер плитки, м", Float) = 3
        _L2 ("2 Ил и земля, зелёный", 2D) = "gray" {}
        [Normal] _N2 ("2 Нормали", 2D) = "bump" {}
        _T2 ("2 Размер плитки, м", Float) = 4
        _L3 ("3 Сухая трава и мусор, синий", 2D) = "gray" {}
        [Normal] _N3 ("3 Нормали", 2D) = "bump" {}
        _T3 ("3 Размер плитки, м", Float) = 2
        _Tint ("Общий оттенок", Color) = (1, 1, 1, 1)
        _NormalStrength ("Сила нормалей", Range(0, 2)) = 1
        _Smoothness ("Гладкость", Range(0, 1)) = 0.08
        _HeightBlend ("Стык по высоте камней", Range(0, 1)) = 0.5
        _BlendSharp ("Резкость стыка", Range(0.02, 1)) = 0.2
        _Macro ("Крупные пятна яркости", Range(0, 0.5)) = 0.12
        _MacroSize ("Размер пятен, м", Float) = 14
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "UniversalMaterialType" = "Lit" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _L0_ST, _L1_ST, _L2_ST, _L3_ST;
            float _T0, _T1, _T2, _T3;
            half4 _Tint;
            half _NormalStrength, _Smoothness, _HeightBlend, _BlendSharp, _Macro;
            float _MacroSize;
        CBUFFER_END

        TEXTURE2D(_L0); SAMPLER(sampler_L0);
        TEXTURE2D(_L1);
        TEXTURE2D(_L2);
        TEXTURE2D(_L3);
        TEXTURE2D(_N0); SAMPLER(sampler_N0);
        TEXTURE2D(_N1);
        TEXTURE2D(_N2);
        TEXTURE2D(_N3);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half4 color : TEXCOORD4;
                half fog : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = v.uv;
                o.color = v.color;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float Hash2(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash2(i), Hash2(i + float2(1, 0)), f.x), lerp(Hash2(i + float2(0, 1)), Hash2(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                // layer weights from the vertex colour: black = base gravel
                half3 c = saturate(i.color.rgb);
                half4 w = half4(saturate(1.0 - (c.r + c.g + c.b)), c.r, c.g, c.b);

                float2 uv0 = i.uv / max(_T0, 0.01), uv1 = i.uv / max(_T1, 0.01), uv2 = i.uv / max(_T2, 0.01), uv3 = i.uv / max(_T3, 0.01);
                half4 a0 = SAMPLE_TEXTURE2D(_L0, sampler_L0, uv0);
                half4 a1 = SAMPLE_TEXTURE2D(_L1, sampler_L0, uv1);
                half4 a2 = SAMPLE_TEXTURE2D(_L2, sampler_L0, uv2);
                half4 a3 = SAMPLE_TEXTURE2D(_L3, sampler_L0, uv3);

                // height blend: the highest stones of a layer win near the border, one layer never shows where its weight is 0
                half4 h = half4(a0.a, a1.a, a2.a, a3.a);
                half4 v4 = w + h * _HeightBlend;
                half m = max(max(v4.x, v4.y), max(v4.z, v4.w)) - _BlendSharp;
                half4 b = max(v4 - m, 0.0) * saturate(w * 40.0);
                b /= max(b.x + b.y + b.z + b.w, 1e-4);

                half3 albedo = a0.rgb * b.x + a1.rgb * b.y + a2.rgb * b.z + a3.rgb * b.w;
                half3 tn = UnpackNormal(SAMPLE_TEXTURE2D(_N0, sampler_N0, uv0)) * b.x
                         + UnpackNormal(SAMPLE_TEXTURE2D(_N1, sampler_N0, uv1)) * b.y
                         + UnpackNormal(SAMPLE_TEXTURE2D(_N2, sampler_N0, uv2)) * b.z
                         + UnpackNormal(SAMPLE_TEXTURE2D(_N3, sampler_N0, uv3)) * b.w;
                tn.xy *= _NormalStrength;
                tn = normalize(tn);

                // large soft brightness patches against visible tiling (no texture read)
                float2 mp = i.positionWS.xz / max(_MacroSize, 0.5);
                half macro = (half)(VNoise(mp) * 0.65 + VNoise(mp * 2.7 + 13.1) * 0.35);
                albedo *= _Tint.rgb * (1.0 + (macro - 0.5) * 2.0 * _Macro);

                half3 nWS = normalize(i.normalWS);
                half3 tWS = normalize(i.tangentWS.xyz);
                half3 bWS = cross(nWS, tWS) * i.tangentWS.w;
                half3 normalWS = normalize(TransformTangentToWorld(tn, half3x3(tWS, bWS, nWS)));

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = normalWS;
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord = i.fog;
                input.bakedGI = SampleSH(normalWS);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = 0;
                s.specular = 0;
                s.smoothness = _Smoothness;
                s.normalTS = tn;
                s.occlusion = 1;
                s.alpha = 1;

                half4 col = UniversalFragmentPBR(input, s);
                col.rgb = MixFog(col.rgb, input.fogCoord);
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };

            V vertShadow(A v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                V o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir)));
                return o;
            }

            half4 fragShadow(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };

            V vertDepth(A v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half fragDepth(V i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertDN
            #pragma fragment fragDN
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            V vertDN(A v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                V o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 fragDN(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
    FallBack Off
}
