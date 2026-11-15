// Hand mask for the dark-orientation mode: R = coverage, G = 0.1 / eye depth (nearest wins via BlendOp Max).
// The only pass uses a custom LightMode, so URP never draws the hands itself (no depth, shadows or SSAO);
// DarkOrientationRenderPass draws them explicitly into the mask.
Shader "Hidden/CarX/DarkOrientation/HandMask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "HandMask"
            Tags { "LightMode" = "DarkOrientationHand" }
            Cull Off
            ZWrite Off
            ZTest Always
            BlendOp Max
            Blend One One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_X_FLOAT(_DO_SceneDepth);
            float4x4 _DO_ViewProj;
            float4 _DO_CamPos;
            float4 _DO_CamFwd;
            float4 _DO_ZParams;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float eye : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = mul(_DO_ViewProj, float4(ws, 1.0));
                o.eye = dot(ws - _DO_CamPos.xyz, _DO_CamFwd.xyz);
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float raw = LOAD_TEXTURE2D_X(_DO_SceneDepth, uint2(i.positionCS.xy)).r;
                float sceneEye = 1.0 / (_DO_ZParams.z * raw + _DO_ZParams.w);
                // Fingers resting on a surface may dip a few millimetres into it.
                if (sceneEye < i.eye - 0.01) discard;
                return float4(1.0, 0.1 / max(i.eye, 0.01), 0.0, 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
