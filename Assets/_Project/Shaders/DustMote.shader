// Dust particles that only sparkle where daylight actually reaches them.
Shader "CarX/DustMote"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (2, 1.9, 1.7, 1)
        _ShaftLight ("Light Position", Vector) = (0, 13, 0, 0)
        _ShaftOpening ("Opening xMin zMin xMax zMax", Vector) = (-1, -0.3, 1, 0.3)
        _ShaftParams ("Opening Y, Grate Y, Radius, Softness", Vector) = (2.4, 4.6, 0, 0.03)
        _ShaftBars ("Bars pitchX, width, pitchZ, originX", Vector) = (0.1, 0.028, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+30" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "Dust"
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
                float4 _ShaftLight;
                float4 _ShaftOpening;
                float4 _ShaftParams;
                float4 _ShaftBars;
                float4x4 _WorldToShaft;
            CBUFFER_END

            #include "Assets/_Project/Shaders/CarXShaft.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float light : TEXCOORD1;
                float eye : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.color = v.color;
                o.uv = v.uv;
                float3 sp = mul(_WorldToShaft, float4(ws, 1.0)).xyz;
                o.light = CarX_ShaftLight(sp);
                o.eye = -TransformWorldToView(ws).z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float d = length(i.uv * 2.0 - 1.0);
                float a = saturate(1.0 - d);
                a *= a;
                float vis = CarX_FogVisibility(i.eye);
                return half4(_Color.rgb * i.color.rgb * (i.color.a * a * i.light * vis), 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
