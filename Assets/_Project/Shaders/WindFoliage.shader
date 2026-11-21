// Trees, bushes and grass tufts swaying in the wind. Gusts are shared by everything (a front that crosses the scene),
// the swaying itself has its own rhythm per tree. Below "Calm below Y" (inside the channel) things barely move.
//   Mode 0 (trees, bushes): bend grows with the object-space height (pivot at the foot), the whole object bends.
//   Mode 1 (grass tufts):   bend grows with uv.y (0 at the root, 1 at the tip of a tuft).
Shader "CarX/WindFoliage"
{
    Properties
    {
        _BaseMap ("Текстура", 2D) = "white" {}
        _BaseColor ("Цвет", Color) = (1, 1, 1, 1)
        _Cutoff ("Порог прозрачности", Range(0, 1)) = 0.45
        _Smoothness ("Гладкость", Range(0, 1)) = 0.2
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Отсечение сторон", Float) = 0
        [Toggle] _AlphaClip ("Вырезать по прозрачности", Float) = 1
        [Header(Wind)]
        [Enum(Tree,0,Grass,1)] _WindMode ("Режим", Float) = 0
        _TreeHeight ("Высота дерева, м (режим Tree)", Float) = 10
        _Strength ("Сила качания, м на макушке", Float) = 0.3
        _Flutter ("Дрожание листвы", Range(0, 1)) = 0
        _WindDir ("Направление ветра (X, Z)", Vector) = (1, 0, 0.35, 0)
        _GustSpeed ("Скорость фронта порыва, м/с", Float) = 9
        _CalmBelowY ("Ниже этой высоты почти штиль (дно канала)", Float) = -1000
        _CalmFactor ("Сила в канале", Range(0, 1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" "UniversalMaterialType" = "Lit" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff, _Smoothness, _Cull, _AlphaClip;
            float _WindMode, _TreeHeight, _Strength, _Flutter, _GustSpeed, _CalmBelowY, _CalmFactor;
            float4 _WindDir;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        float _CarXWindStrength, _CarXWindSpeed;   // globals from the WindControl component (Ветер)

        float WHash(float2 p) { p = frac(p * float2(0.1031, 0.1030)); p += dot(p, p.yx + 33.33); return frac((p.x + p.y) * p.x); }

        // shared gust strength 0..1 at a place: the same slow pulse for everybody, arriving a little later downwind
        float Gust(float t, float2 xz, float2 dir)
        {
            float T = t - dot(xz, dir) / max(_GustSpeed, 0.5);
            float s = sin(T * 0.63) + 0.6 * sin(T * 1.37 + 1.7) + 0.3 * sin(T * 2.71 + 0.4);
            float g = saturate(0.5 + s * 0.38);
            return g * g;
        }

        float3 WindOffset(float3 positionOS, float2 uv, float3 positionWS)
        {
            float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
            bool grass = _WindMode > 0.5;
            float h = grass ? saturate(uv.y) : saturate(positionOS.y / max(_TreeHeight, 0.5));
            // own rhythm per tree; for grass a smooth field over the ground, so one tuft never tears apart
            float r = grass ? 0.5 + 0.5 * sin(positionWS.x * 0.9 + positionWS.z * 1.3) : WHash(originWS.xz + 17.0);
            float r2 = grass ? 0.5 + 0.5 * sin(positionWS.x * -1.1 + positionWS.z * 0.7 + 2.0) : WHash(originWS.xz * 1.7 + 3.0);
            float baseY = grass ? positionWS.y : originWS.y;
            float2 dir = normalize(_WindDir.xz + 1e-4);
            float t = _Time.y * max(_CarXWindSpeed, 0.05);
            bool calm = baseY < _CalmBelowY;

            float gust = calm ? 0.45 : Gust(t, originWS.xz + (grass ? positionWS.xz - originWS.xz : 0), dir);
            float amp = _Strength * _CarXWindStrength * (calm ? _CalmFactor : 1.0);
            float freq = lerp(0.9, 1.6, r) * (grass ? 2.2 : 1.0);
            float sway = sin(t * freq + r2 * 6.2832);
            float lean = gust * 0.7 + (0.25 + 0.6 * gust) * sway;          // leans downwind with the gust, swings around that
            float bend = amp * lean * h * h;
            float2 side = float2(-dir.y, dir.x) * (amp * 0.25 * h * h * sin(t * freq * 0.73 + r * 6.2832) * (0.3 + gust));
            float3 off = float3(dir * bend + side, 0);
            if (_Flutter > 0)
            {
                float f = sin(t * 7.0 + dot(positionWS, float3(2.1, 1.7, 2.3))) * 0.5 + sin(t * 11.3 + dot(positionWS, float3(-1.3, 2.9, 0.7))) * 0.3;
                off += float3(dir.x, 0.6, dir.y) * (f * 0.04 * _Flutter * h * (0.3 + gust) * (calm ? _CalmFactor : 1.0) * _CarXWindStrength);
            }
            off.y -= dot(off.xz, off.xz) / max(2.0 * (grass ? 0.5 : _TreeHeight), 0.1);   // keep the length, the tip dips a little
            return off;
        }

        float3 WindPositionWS(float3 positionOS, float2 uv)
        {
            float3 p = TransformObjectToWorld(positionOS);
            return p + WindOffset(positionOS, uv, p);
        }

        void ClipAlpha(float2 uv)
        {
            if (_AlphaClip > 0.5) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a - _Cutoff);
        }
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
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 p = WindPositionWS(v.positionOS.xyz, v.uv);
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                if (_AlphaClip > 0.5) clip(tex.a - _Cutoff);
                half3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = n;
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord = i.fog;
                input.bakedGI = SampleSH(n);
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = tex.rgb;
                s.smoothness = _Smoothness;
                s.normalTS = half3(0, 0, 1);
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

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V vertShadow(A v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                V o;
                float3 positionWS = WindPositionWS(v.positionOS.xyz, v.uv);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir)));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 fragShadow(V i) : SV_Target { ClipAlpha(i.uv); return 0; }
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

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V vertDepth(A v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                V o;
                o.positionCS = TransformWorldToHClip(WindPositionWS(v.positionOS.xyz, v.uv));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half fragDepth(V i) : SV_Target { ClipAlpha(i.uv); return i.positionCS.z; }
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

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; };

            V vertDN(A v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                V o;
                o.positionCS = TransformWorldToHClip(WindPositionWS(v.positionOS.xyz, v.uv));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 fragDN(V i) : SV_Target { ClipAlpha(i.uv); return half4(NormalizeNormalPerPixel(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
    FallBack Off
}
