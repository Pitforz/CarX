// Trees, bushes and grass tufts swaying in the wind, lit like real leaves.
// Wind: gusts are shared by everything (a front that crosses the scene), the swaying itself has its own rhythm per tree.
// Below "Calm below Y" (inside the channel) things barely move.
//   Mode 0 (trees, bushes): the whole tree leans a little (Ветер → Качание стволов); branches bob and leaves flutter on their own
//                           (Tree Creator data in the mesh: vertex colour x = branch phase, y = leaf edge flutter, uv2.y = branch bend).
//   Mode 1 (grass tufts):   bend grows with uv.y (0 at the root, 1 at the tip of a tuft).
// Light: normal map, gloss and translucency from the tree pack (normal_specular: normal in GA, highlight in R;
// translucency_gloss: translucency in B, gloss in A): leaves glint, and glow when the sun shines through them.
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
        [Header(Pack maps)]
        [Toggle] _UsePackMaps ("Карты из пака деревьев", Float) = 0
        _BumpSpecMap ("Нормали (GA) и блик (R): normal_specular", 2D) = "bump" {}
        _TranslucencyMap ("Просвет (B) и глянец (A): translucency_gloss", 2D) = "white" {}
        _NormalStrength ("Сила рельефа", Range(0, 2)) = 1
        _Translucency ("Просвечивание на солнце", Range(0, 3)) = 0
        _Occlusion ("Тень в глубине кроны", Range(0, 1)) = 0.5
        [Header(Wind)]
        [Enum(Tree,0,Grass,1)] _WindMode ("Режим", Float) = 0
        _TreeHeight ("Высота дерева, м (режим Tree)", Float) = 10
        _Strength ("Сила качания, м на макушке", Float) = 0.3
        _Flutter ("Дрожание листвы", Range(0, 1)) = 0
        [Toggle] _TreeCreatorData ("Ветки и листья по данным Tree Creator", Float) = 0
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
            half _UsePackMaps, _NormalStrength, _Translucency, _Occlusion;
            float _WindMode, _TreeHeight, _Strength, _Flutter, _TreeCreatorData, _GustSpeed, _CalmBelowY, _CalmFactor;
            float4 _WindDir;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpSpecMap); SAMPLER(sampler_BumpSpecMap);
        TEXTURE2D(_TranslucencyMap); SAMPLER(sampler_TranslucencyMap);
        // globals from the WindControl component (Ветер)
        float _CarXWindStrength, _CarXWindSpeed, _CarXTrunkSway, _CarXLeafFlutter;

        float WHash(float2 p) { p = frac(p * float2(0.1031, 0.1030)); p += dot(p, p.yx + 33.33); return frac((p.x + p.y) * p.x); }

        // shared gust strength 0..1 at a place: the same slow pulse for everybody, arriving a little later downwind
        float Gust(float t, float2 xz, float2 dir)
        {
            float T = t - dot(xz, dir) / max(_GustSpeed, 0.5);
            float s = sin(T * 0.63) + 0.6 * sin(T * 1.37 + 1.7) + 0.3 * sin(T * 2.71 + 0.4);
            float g = saturate(0.5 + s * 0.38);
            return g * g;
        }

        float4 SmoothTriangleWave(float4 x)
        {
            x = abs(frac(x + 0.5) * 2.0 - 1.0);
            return x * x * (3.0 - 2.0 * x);
        }

        float3 WindOffset(float3 positionOS, float3 normalOS, float2 uv, float4 color, float2 uv2, float3 positionWS)
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
            float calmK = calm ? _CalmFactor : 1.0;

            float gust = calm ? 0.45 : Gust(t, originWS.xz + (grass ? positionWS.xz - originWS.xz : 0), dir);
            // the whole tree only leans a little: trunks are stiff, the leaves do most of the moving
            float amp = _Strength * _CarXWindStrength * calmK * (grass ? 1.0 : _CarXTrunkSway);
            float freq = lerp(0.9, 1.6, r) * (grass ? 2.2 : 1.0);
            float sway = sin(t * freq + r2 * 6.2832);
            float lean = gust * 0.7 + (0.25 + 0.6 * gust) * sway;          // leans downwind with the gust, swings around that
            float bend = amp * lean * h * h;
            float2 side = float2(-dir.y, dir.x) * (amp * 0.25 * h * h * sin(t * freq * 0.73 + r * 6.2832) * (0.3 + gust));
            float3 off = float3(dir * bend + side, 0);
            off.y -= dot(off.xz, off.xz) / max(2.0 * (grass ? 0.5 : _TreeHeight), 0.1);   // keep the length, the tip dips a little

            float leaf = _CarXLeafFlutter * _CarXWindStrength * calmK * (0.35 + gust);
            if (!grass && _TreeCreatorData > 0.5)
            {
                // Tree Creator style: branches bob, leaf edges flutter along their normal, each with its own phase
                float branchPhase = dot(originWS, 1.0) + color.x;
                float vtxPhase = dot(positionOS, color.y + branchPhase);
                float4 waves = SmoothTriangleWave(frac((t + float2(vtxPhase, branchPhase)).xxyy * float4(1.975, 0.793, 0.375, 0.193)) * 2.0 - 1.0);
                float2 sum = waves.xz + waves.yw;
                float3 n = TransformObjectToWorldDir(normalOS);
                off += n * (sum.x * color.y * 0.12 * leaf);
                off += float3(dir.x * 0.5, 1.0, dir.y * 0.5) * (sum.y * uv2.y * 0.3 * leaf);
            }
            else if (_Flutter > 0)
            {
                float f = sin(t * 7.0 + dot(positionWS, float3(2.1, 1.7, 2.3))) * 0.5 + sin(t * 11.3 + dot(positionWS, float3(-1.3, 2.9, 0.7))) * 0.3;
                off += float3(dir.x, 0.6, dir.y) * (f * 0.06 * _Flutter * h * leaf);
            }
            return off;
        }

        float3 WindPositionWS(float3 positionOS, float3 normalOS, float2 uv, float4 color, float2 uv2)
        {
            float3 p = TransformObjectToWorld(positionOS);
            return p + WindOffset(positionOS, normalOS, uv, color, uv2, p);
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
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half fog : TEXCOORD4;
                half occlusion : TEXCOORD5;
                half3 tint : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 p = WindPositionWS(v.positionOS.xyz, v.normalOS, v.uv, v.color, v.uv2);
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.tangentWS = half4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                // Tree Creator keeps ambient occlusion in the vertex alpha (dark deep inside the crown)
                o.occlusion = _UsePackMaps > 0.5 ? lerp(1.0, saturate(v.color.a), _Occlusion) : 1.0;
                // grass: each tuft carries its own shade of dry grass in the vertex colour
                o.tint = _WindMode > 0.5 ? v.color.rgb : half3(1, 1, 1);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                if (_AlphaClip > 0.5) clip(tex.a - _Cutoff);
                bool grass = _WindMode > 0.5;
                tex.rgb *= i.tint;
                half side = front ? 1.0 : -1.0;
                half3 geo = normalize(i.normalWS);
                half3 n = geo;
                half gloss = 1.0, highlight = 1.0, through = 1.0;
                if (_UsePackMaps > 0.5)
                {
                    half4 bs = SAMPLE_TEXTURE2D(_BumpSpecMap, sampler_BumpSpecMap, i.uv);
                    half3 nTS;
                    nTS.xy = (bs.ag * 2.0 - 1.0) * _NormalStrength;
                    nTS.z = sqrt(saturate(1.0 - dot(nTS.xy, nTS.xy)));
                    half3 t = normalize(i.tangentWS.xyz);
                    half3 b = cross(geo, t) * i.tangentWS.w;
                    n = normalize(TransformTangentToWorld(nTS, half3x3(t, b, geo)));
                    highlight = bs.r;
                    half4 tg = SAMPLE_TEXTURE2D(_TranslucencyMap, sampler_TranslucencyMap, i.uv);
                    through = tg.b;
                    gloss = tg.a;
                }
                if (grass)
                {
                    // a tuft is lit like the ground it grows from, from whatever side you see a blade: crossed cards lit as
                    // walls made one blade straw-yellow and the next one brown
                    n = half3(0, 1, 0);
                    geo = n;
                }
                else
                {
                    n *= side;
                    geo *= side;
                }

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
                s.smoothness = _Smoothness * lerp(1.0, gloss, _UsePackMaps) * lerp(1.0, 0.5 + highlight, _UsePackMaps);
                s.normalTS = half3(0, 0, 1);
                s.occlusion = i.occlusion;
                s.alpha = 1;

                half4 col = UniversalFragmentPBR(input, s);

                // sunlight shining through the leaf: from behind it, and most of all when you look towards the sun through it
                if (_Translucency > 0)
                {
                    Light sun = GetMainLight(input.shadowCoord, input.positionWS, input.shadowMask);
                    half on = 1.0;
                #ifdef _LIGHT_LAYERS
                    on = IsMatchingLightLayer(sun.layerMask, GetMeshRenderingLayer()) ? 1.0 : 0.0;
                #endif
                    half behind = saturate(dot(-geo, sun.direction));
                    half towards = pow(saturate(dot(input.viewDirectionWS, -sun.direction)), 4.0);
                    col.rgb += tex.rgb * sun.color * (sun.shadowAttenuation * sun.distanceAttenuation * on)
                             * through * _Translucency * (behind * 0.5 + towards) * i.occlusion;
                }
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

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V vertShadow(A v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                V o;
                float3 positionWS = WindPositionWS(v.positionOS.xyz, v.normalOS, v.uv, v.color, v.uv2);
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

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V vertDepth(A v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                V o;
                o.positionCS = TransformWorldToHClip(WindPositionWS(v.positionOS.xyz, v.normalOS, v.uv, v.color, v.uv2));
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

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; };

            V vertDN(A v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                V o;
                o.positionCS = TransformWorldToHClip(WindPositionWS(v.positionOS.xyz, v.normalOS, v.uv, v.color, v.uv2));
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 fragDN(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                ClipAlpha(i.uv);
                if (_WindMode > 0.5) return half4(0, 1, 0, 0);
                return half4(NormalizeNormalPerPixel(i.normalWS * (front ? 1.0 : -1.0)), 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
