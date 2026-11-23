// Volumetric daylight column under a storm grate: raymarched box, clipped by scene depth,
// bars of the grate projected from the light give the god-ray stripes.
Shader "CarX/LightShaft"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.93, 0.8, 1)
        _Density ("Density", Range(0, 2)) = 0.2
        _BoxMin ("Box Min (shaft space)", Vector) = (-1, 0, -1, 0)
        _BoxMax ("Box Max (shaft space)", Vector) = (1, 5, 1, 0)
        _ShaftLight ("Light Position", Vector) = (0, 13, 0, 0)
        _ShaftOpening ("Opening xMin zMin xMax zMax", Vector) = (-1, -0.3, 1, 0.3)
        _ShaftParams ("Opening Y, Grate Y, Radius, Softness", Vector) = (2.4, 4.6, 0, 0.03)
        _ShaftBars ("Bars pitchX, width, pitchZ, originX", Vector) = (0.1, 0.028, 0, 0)
        _ShaftSharp ("Blur: bars, bars/m, edge, edge/m", Vector) = (0.004, 0.006, 0.02, 0.01)
        _NoiseScale ("Dust Noise Scale", Float) = 2.3
        _NoiseSpeed ("Dust Noise Speed", Float) = 0.12
        _NoiseStrength ("Dust Noise Strength", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Volume"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Density;
                float4 _BoxMin;
                float4 _BoxMax;
                float4 _ShaftLight;
                float4 _ShaftOpening;
                float4 _ShaftParams;
                float4 _ShaftBars;
                float4 _ShaftSharp;
                float _NoiseScale;
                float _NoiseSpeed;
                float _NoiseStrength;
            CBUFFER_END

            #define CARX_SHAFT_SHARP
            #include "Assets/_Project/Shaders/CarXShaft.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionOS = v.positionOS.xyz;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 camOS = TransformWorldToObject(GetCameraPositionWS());
                float3 rayOS = i.positionOS - camOS;
                float3 safeRay = rayOS + (abs(rayOS) < 1e-6) * 1e-6;
                float3 inv = 1.0 / safeRay;
                float3 t0 = (_BoxMin.xyz - camOS) * inv;
                float3 t1 = (_BoxMax.xyz - camOS) * inv;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                float tNear = max(max(tmin.x, tmin.y), max(tmin.z, 0.0));
                float tFar = min(min(tmax.x, tmax.y), tmax.z);

                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float3 rayWS = i.positionWS - GetCameraPositionWS();
                float eyePerT = max(dot(rayWS, -UNITY_MATRIX_V[2].xyz), 1e-4);
                tFar = min(tFar, sceneEye / eyePerT);
                if (tFar <= tNear) return 0;

                const int STEPS = 32;
                float dt = (tFar - tNear) / STEPS;
                float jitter = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(0.06711056, 0.00583715))));
                float time = _Time.y * _NoiseSpeed;
                float accum = 0.0;
                [loop]
                for (int s = 0; s < STEPS; s++)
                {
                    float t = tNear + (s + jitter) * dt;
                    float3 p = camOS + rayOS * t;
                    float a = CarX_ShaftLight(p);
                    if (a > 0.001)
                    {
                        float3 q = p * _NoiseScale + float3(time * 0.3, time, time * 0.21);
                        float n = CarX_Noise3(q) * 0.65 + CarX_Noise3(q * 2.9 + 17.3) * 0.35;
                        float dens = a * lerp(1.0, n * 1.9, _NoiseStrength);
                        accum += dens * CarX_FogVisibility(t * eyePerT);
                    }
                }
                accum *= dt * length(rayWS) * _Density;
                return half4(_Color.rgb * accum, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
