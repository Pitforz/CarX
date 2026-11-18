// Dual Kawase blur for the dark-orientation mode. Pass 0 = downsample, pass 1 = upsample.
Shader "Hidden/CarX/DarkOrientation/Blur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        half3 Tap(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
        }
        ENDHLSL

        Pass
        {
            Name "Down"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDown

            half4 FragDown(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 o = _BlitTexture_TexelSize.xy;
                half3 s = Tap(uv) * 4.0;
                s += Tap(uv - o);
                s += Tap(uv + o);
                s += Tap(uv + float2(o.x, -o.y));
                s += Tap(uv - float2(o.x, -o.y));
                return half4(s * 0.125, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Up"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUp

            half4 FragUp(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 o = _BlitTexture_TexelSize.xy;
                half3 s = Tap(uv + float2(-o.x * 2.0, 0.0));
                s += Tap(uv + float2(-o.x, o.y)) * 2.0;
                s += Tap(uv + float2(0.0, o.y * 2.0));
                s += Tap(uv + float2(o.x, o.y)) * 2.0;
                s += Tap(uv + float2(o.x * 2.0, 0.0));
                s += Tap(uv + float2(o.x, -o.y)) * 2.0;
                s += Tap(uv + float2(0.0, -o.y * 2.0));
                s += Tap(uv + float2(-o.x, -o.y)) * 2.0;
                return half4(s / 12.0, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
