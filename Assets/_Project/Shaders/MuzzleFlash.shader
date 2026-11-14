Shader "CarX/Muzzle Flash"
{
    Properties
    {
        [HDR] _Color ("Glow Color", Color) = (1, 0.7, 0.35, 1)
        _Phase ("Flash Phase", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "MuzzleGlow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Phase;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float radiusSquared = dot(p, p);
                // A soft, slightly uneven core and a faint halo; no hard sphere silhouette or texture edge.
                float shape = 1.0 + 0.12 * sin(p.x * 13.0 + _Phase * 4.0)
                    * sin(p.y * 11.0 - _Phase * 3.0);
                float edge = 1.0 - smoothstep(0.35, 1.0, radiusSquared);
                float core = exp2(-radiusSquared * 18.0) * 2.4;
                float halo = exp2(-radiusSquared * 5.0) * 0.12;
                half3 color = _Color.rgb * ((core + halo) * shape * edge);
                color = MixFogColor(color, half3(0, 0, 0), input.fogFactor);
                return half4(color, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
