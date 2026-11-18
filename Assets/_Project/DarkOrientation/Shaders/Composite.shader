// Final image of the dark-orientation mode: dimmed blurred scene, depth-map spheres around touch points,
// hands drawn as outlined silhouettes on top. _DO_Blend fades the whole effect in and out.
Shader "Hidden/CarX/DarkOrientation/Composite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define ZONE_COUNT 4

            TEXTURE2D_X(_DO_Blur);
            TEXTURE2D_X(_DO_Mask);
            TEXTURE2D_X_FLOAT(_DO_SceneDepth);

            float4 _DO_ScreenSize;      // xy = size, zw = 1 / size
            float4 _DO_ZParams;
            float4 _DO_CamPos;
            float4 _DO_CamFwd;
            float4 _DO_RayBL, _DO_RayBR, _DO_RayTL, _DO_RayTR;
            float4 _DO_Zones[ZONE_COUNT];     // xyz = centre, w > 0.5 means an object or a person (tight zone)
            float4 _DO_ZoneN0, _DO_ZoneN1, _DO_ZoneN2, _DO_ZoneN3;   // xyz = surface normal at the touch
            float4 _DO_ZoneWeights;
            float4 _DO_ZoneParams;      // x = wall core, y = wall end, z = object core, w = object end (m)
            float4 _DO_RingParams;      // x = ring spacing, y = ring strength, z = cos of the face cutoff angle
            float4 _DO_DepthNear;
            float4 _DO_DepthFar;
            float4 _DO_Look;            // x = dim, y = desaturate, z = edge strength, w = blend
            float4 _DO_HandFill;
            float4 _DO_HandOutline;
            float4 _DO_HandInner;
            float4 _DO_HandParams;      // x = outline width px, y = inner lines strength

            float EyeDepth(float raw)
            {
                return 1.0 / (_DO_ZParams.z * raw + _DO_ZParams.w);
            }

            float EyeAt(int2 px)
            {
                px = clamp(px, int2(0, 0), int2(_DO_ScreenSize.xy) - 1);
                return EyeDepth(LOAD_TEXTURE2D_X(_DO_SceneDepth, px).r);
            }

            float3 WorldAt(int2 px)
            {
                float2 uv = (float2(px) + 0.5) * _DO_ScreenSize.zw;
                float3 ray = lerp(lerp(_DO_RayBL.xyz, _DO_RayBR.xyz, uv.x), lerp(_DO_RayTL.xyz, _DO_RayTR.xyz, uv.x), uv.y);
                return _DO_CamPos.xyz + ray * EyeAt(px);
            }

            // Surface normal from depth. Each axis takes the neighbour on the same surface (smaller step),
            // so silhouette edges do not bend the normal. Faces the camera.
            float3 NormalAt(int2 px, float3 ws, float3 ray)
            {
                float3 r = WorldAt(px + int2(1, 0)) - ws;
                float3 l = ws - WorldAt(px - int2(1, 0));
                float3 u = WorldAt(px + int2(0, 1)) - ws;
                float3 d = ws - WorldAt(px - int2(0, 1));
                float3 dx = dot(r, r) < dot(l, l) ? r : l;
                float3 dy = dot(u, u) < dot(d, d) ? u : d;
                float3 n = normalize(cross(dy, dx));
                return dot(n, ray) > 0.0 ? -n : n;
            }

            float3 ZoneNormal(int i)
            {
                return i == 0 ? _DO_ZoneN0.xyz : i == 1 ? _DO_ZoneN1.xyz : i == 2 ? _DO_ZoneN2.xyz : _DO_ZoneN3.xyz;
            }

            float2 MaskAt(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_DO_Mask, sampler_PointClamp, uv, 0).rg;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half3 scene = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb;
                half3 blur = SAMPLE_TEXTURE2D_X_LOD(_DO_Blur, sampler_LinearClamp, uv, 0).rgb;
                half lum = dot(blur, half3(0.2126, 0.7152, 0.0722));
                half3 col = lerp(blur, lum.xxx, _DO_Look.y) * _DO_Look.x;

                // World position of the scene behind the pixel.
                int2 px = int2(uv * _DO_ScreenSize.xy);
                float eye = EyeAt(px);
                float3 ray = lerp(lerp(_DO_RayBL.xyz, _DO_RayBR.xyz, uv.x), lerp(_DO_RayTL.xyz, _DO_RayTR.xyz, uv.x), uv.y);
                float3 ws = _DO_CamPos.xyz + ray * eye;
                float3 n = NormalAt(px, ws, ray);
                float facing = abs(dot(n, normalize(ray)));

                // Wall: bright out to the wall core, gone at the wall end. Object or person: the same with the tight pair.
                // Faces turned away from the touched surface by more than the cutoff angle stay dark.
                float w = 0.0;
                float dist = 1.0;
                float zoneEnd = _DO_ZoneParams.y;
                float3 centre = ws;
                [unroll]
                for (int i = 0; i < ZONE_COUNT; i++)
                {
                    bool small = _DO_Zones[i].w > 0.5;
                    float solid = small ? _DO_ZoneParams.z : _DO_ZoneParams.x;
                    float reach = small ? _DO_ZoneParams.w : _DO_ZoneParams.y;
                    float d = distance(ws, _DO_Zones[i].xyz);
                    float3 zn = ZoneNormal(i);
                    float face = dot(zn, zn) > 0.25 ? smoothstep(_DO_RingParams.z - 0.08, _DO_RingParams.z + 0.08, dot(n, normalize(zn))) : 1.0;
                    float wi = (1.0 - smoothstep(solid, reach, d)) * face * _DO_ZoneWeights[i];
                    if (wi > w)
                    {
                        w = wi;
                        dist = d;
                        zoneEnd = reach;
                        centre = _DO_Zones[i].xyz;
                    }
                }

                if (w > 0.001)
                {
                    float centreEye = dot(centre - _DO_CamPos.xyz, _DO_CamFwd.xyz);
                    float t = saturate(0.5 + (centreEye - eye) / (2.0 * zoneEnd));
                    float lap = abs(EyeAt(px + int2(1, 0)) + EyeAt(px - int2(1, 0)) + EyeAt(px + int2(0, 1)) + EyeAt(px - int2(0, 1)) - 4.0 * eye);
                    float edge = saturate(lap / max(eye, 0.05) * 25.0) * _DO_Look.z;
                    float ring = _DO_RingParams.y * smoothstep(0.85, 1.0, frac(dist / max(_DO_RingParams.x, 0.005))) * saturate(1.0 - dist / zoneEnd);
                    half3 depthMap = lerp(_DO_DepthFar.rgb, _DO_DepthNear.rgb, t) * lerp(0.55, 1.0, facing);
                    depthMap += (edge + ring) * _DO_DepthNear.rgb;
                    col = lerp(col, depthMap, w);
                }

                // Hands: filled silhouette, outer outline by dilation, inner lines from hand depth steps.
                float2 m = MaskAt(uv);
                float cover = m.r;
                float handEye = 0.1 / max(m.g, 1e-4);
                float2 offset = _DO_ScreenSize.zw * _DO_HandParams.x;
                float dilated = 0.0;
                float depthStep = 0.0;
                [unroll]
                for (int k = 0; k < 8; k++)
                {
                    float a = k * 0.78539816;
                    float2 dir = float2(cos(a), sin(a));
                    float2 outer = MaskAt(uv + dir * offset);
                    float2 close = MaskAt(uv + dir * offset * 0.5);
                    dilated = max(dilated, max(outer.r, close.r));
                    if (cover > 0.5 && close.r > 0.5)
                        depthStep = max(depthStep, abs(handEye - 0.1 / max(close.g, 1e-4)));
                }
                float outline = saturate(dilated - cover);
                float innerLine = cover * saturate((depthStep - 0.015) / 0.01) * _DO_HandParams.y;
                col = lerp(col, _DO_HandFill.rgb, cover);
                col = lerp(col, _DO_HandInner.rgb, innerLine);
                col = lerp(col, _DO_HandOutline.rgb, outline);

                return half4(lerp(scene, col, _DO_Look.w), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
