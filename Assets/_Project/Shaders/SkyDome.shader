// Sky around the camera for open-air scenes (canal entrance). Unlit, no fog, drawn at the far plane
// only where nothing else was drawn, so it never covers geometry and is never clipped by the camera far plane.
Shader "CarX/SkyDome"
{
    Properties
    {
        [HDR] _Zenith ("Zenith", Color) = (7, 9, 15, 1)
        [HDR] _Horizon ("Horizon", Color) = (19, 13.5, 14.5, 1)
        [HDR] _Ground ("Below Horizon", Color) = (6, 5, 4.8, 1)
        [HDR] _SunColor ("Sun", Color) = (80, 66, 58, 1)
        _SunDir ("Direction To Sun", Vector) = (0, 0.6, 0.8, 0)
        _SunSize ("Sun Size", Range(0.00005, 0.01)) = 0.0004
        _Glow ("Glow Around Sun", Range(0, 2)) = 0.5
        [HDR] _CloudLit ("Clouds Lit", Color) = (26, 20, 20, 1)
        [HDR] _CloudShade ("Clouds Shade", Color) = (11, 10, 13, 1)
        _Clouds ("Clouds: cover, 1/size, wind, seed", Vector) = (0.45, 1, 0.004, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Skybox" }
        Pass
        {
            Name "Sky"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Zenith;
                float4 _Horizon;
                float4 _Ground;
                float4 _SunColor;
                float4 _SunDir;
                float _SunSize;
                float _Glow;
                float4 _CloudLit;
                float4 _CloudShade;
                float4 _Clouds;
            CBUFFER_END

            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i), b = Hash(i + float2(1, 0)), c = Hash(i + float2(0, 1)), d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int o = 0; o < 6; o++) { s += a * VNoise(p); p = p * 2.03 + float2(17.1, 9.7); a *= 0.5; }
                return s / 0.984;
            }

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 d = normalize(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(_WorldSpaceCameraPos + d * 100.0);
            #if UNITY_REVERSED_Z
                o.positionCS.z = o.positionCS.w * 1e-6;
            #else
                o.positionCS.z = o.positionCS.w * (1.0 - 1e-6);
            #endif
                o.dir = d;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;
                float3 c = y >= 0.0 ? lerp(_Horizon.rgb, _Zenith.rgb, pow(saturate(y), 0.45))
                                    : lerp(_Horizon.rgb, _Ground.rgb, saturate(-y * 40.0));
                float3 s = normalize(_SunDir.xyz);
                float m = saturate(dot(d, s));
                c += _SunColor.rgb * _Glow * 0.02 * pow(m, 12.0);
                float3 sunDisc = _SunColor.rgb * smoothstep(1.0 - _SunSize, 1.0 - _SunSize * 0.5, m);

                // clouds: a flat layer high above, fbm coverage, lit towards the sun, fading into the horizon haze
                float cover = 0.0;
                if (y > 0.0 && _Clouds.x > 0.001)
                {
                    float2 uv = d.xz / (y + 0.06) * 0.35 * _Clouds.y + _Clouds.w + _Time.y * _Clouds.z * float2(1.0, 0.35);
                    float n = Fbm(uv);
                    float thick = saturate((n - (1.0 - _Clouds.x)) * 3.0);
                    cover = smoothstep(0.0, 0.35, thick) * smoothstep(0.0, 0.1, y);
                    float self = saturate(Fbm(uv + s.xz * 0.15) - n + 0.5);          // brighter on the side facing the sun
                    float3 cl = lerp(_CloudShade.rgb, _CloudLit.rgb, saturate(0.35 + self * 0.9 - thick * 0.45));
                    cl += _SunColor.rgb * 0.015 * pow(m, 6.0) * (1.0 - thick);         // silver lining near the sun
                    cl = lerp(_Horizon.rgb, cl, smoothstep(0.0, 0.3, y));
                    c = lerp(c, cl, cover);
                }
                c += sunDisc * (1.0 - cover);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
