Shader "CarX/UI/NoteBackground"
{
    Properties { [PerRendererData] _MainTex("Screen", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half3 color = 0;
                float weight = 0;
                [unroll] for (int y = -3; y <= 3; y++)
                [unroll] for (int x = -3; x <= 3; x++)
                {
                    float w = exp(-0.23 * (x*x+y*y));
                    float2 uv = saturate(i.uv + float2(x,y) * _MainTex_TexelSize.xy * 7);
                    color += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb * w;
                    weight += w;
                }
                float vignette = saturate(1 - length((i.uv-0.5)*1.3));
                return half4(color / weight * lerp(0.2,0.55,vignette),1);
            }
            ENDHLSL
        }
    }
}
