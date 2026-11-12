// Unlit glowing surface (bright sky seen through grates and manhole holes).
Shader "CarX/Emissive"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (4, 4.3, 4.8, 1)
        _Horizon ("Edge Darkening", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Emissive"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero
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
                float _Horizon;
                float4 _ShaftLight;
                float4 _ShaftOpening;
                float4 _ShaftParams;
                float4 _ShaftBars;
            CBUFFER_END

            #include "Assets/_Project/Shaders/CarXShaft.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float eye : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = v.uv;
                o.eye = -TransformWorldToView(ws).z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 c = i.uv * 2.0 - 1.0;
                float edge = 1.0 - _Horizon * saturate(dot(c, c));
                return half4(_Color.rgb * edge * CarX_FogVisibility(i.eye), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
