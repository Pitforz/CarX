// Thin beams of daylight falling through the holes of a manhole cover. uv.y: 0 at the floor, 1 at the hole.
Shader "CarX/LightRay"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 0.93, 0.8, 1)
        _Intensity ("Intensity", Float) = 1.2
        _EdgePower ("Edge Power", Float) = 2.5
        _NoiseScale ("Dust Noise Scale", Float) = 6
        _NoiseSpeed ("Dust Noise Speed", Float) = 0.25
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Rays"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _EdgePower;
                float _NoiseScale;
                float _NoiseSpeed;
                float4 _ShaftLight;
                float4 _ShaftOpening;
                float4 _ShaftParams;
                float4 _ShaftBars;
            CBUFFER_END

            #include "Assets/_Project/Shaders/CarXShaft.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                float ndv = abs(dot(normalize(i.normalWS), V));
                float core = pow(ndv, _EdgePower);
                float h = saturate(i.uv.y);
                float fade = smoothstep(0.0, 0.12, h) * (0.35 + 0.65 * h);
                float3 q = i.positionWS * _NoiseScale + float3(0, _Time.y * _NoiseSpeed, 0);
                float n = CarX_Noise3(q) * 0.7 + 0.3;
                float eye = -TransformWorldToView(i.positionWS).z;
                float near = saturate((eye - 0.15) * 2.0);
                float vis = CarX_FogVisibility(eye);
                return half4(_Color.rgb * (_Intensity * core * fade * n * near * vis), 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
