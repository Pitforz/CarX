#ifndef CARX_SHAFT_INCLUDED
#define CARX_SHAFT_INCLUDED

// Shared helpers for daylight falling through a ceiling opening.
// Expects these uniforms to be declared by the including shader (inside UnityPerMaterial):
//   float4 _ShaftLight;    // xyz: light position in shaft space
//   float4 _ShaftOpening;  // rect opening: xMin, zMin, xMax, zMax (shaft space)
//   float4 _ShaftParams;   // x: opening plane y, y: grate plane y, z: round radius (0 = rect), w: edge softness
//   float4 _ShaftBars;     // x: bar pitch along x, y: bar width, z: pitch along z, w: bar origin x

float CarX_FogVisibility(float eyeDepth)
{
#if defined(FOG_EXP2)
    float f = unity_FogParams.x * eyeDepth;
    return saturate(exp2(-f * f));
#elif defined(FOG_EXP)
    float f = unity_FogParams.y * eyeDepth;
    return saturate(exp2(-f));
#elif defined(FOG_LINEAR)
    return saturate(eyeDepth * unity_FogParams.z + unity_FogParams.w);
#else
    return 1.0;
#endif
}

float CarX_Bars(float2 g, float pen)
{
    float m = 1.0;
    if (_ShaftBars.x > 0.0)
    {
        float d = abs(frac((g.x - _ShaftBars.w) / _ShaftBars.x + 0.5) - 0.5) * _ShaftBars.x;
        m *= smoothstep(_ShaftBars.y * 0.5 - pen, _ShaftBars.y * 0.5 + pen, d);
    }
    if (_ShaftBars.z > 0.0)
    {
        float d = abs(frac(g.y / _ShaftBars.z + 0.5) - 0.5) * _ShaftBars.z;
        m *= smoothstep(_ShaftBars.y * 0.5 - pen, _ShaftBars.y * 0.5 + pen, d);
    }
    return m;
}

// How much direct daylight reaches point p (shaft space). Includes the opening mask, bar shadows and spreading.
float CarX_ShaftLight(float3 p)
{
    float drop = _ShaftLight.y - p.y;
    if (drop <= 0.01) return 0.0;
    float kh = (_ShaftLight.y - _ShaftParams.x) / drop;
    float2 g = _ShaftLight.xz + (p.xz - _ShaftLight.xz) * kh;
    float soft = _ShaftParams.w + max(0.0, _ShaftParams.x - p.y) * 0.012;
    float m;
    if (_ShaftParams.z > 0.0)
    {
        m = saturate((_ShaftParams.z - length(g)) / soft);
    }
    else
    {
        float dx = min(g.x - _ShaftOpening.x, _ShaftOpening.z - g.x);
        float dz = min(g.y - _ShaftOpening.y, _ShaftOpening.w - g.y);
        m = saturate(min(dx, dz) / soft);
    }
    if (m <= 0.0) return 0.0;
    float kg = (_ShaftLight.y - _ShaftParams.y) / drop;
    float2 b = _ShaftLight.xz + (p.xz - _ShaftLight.xz) * kg;
    float pen = 0.003 + max(0.0, _ShaftParams.y - p.y) * 0.006;
    m *= CarX_Bars(b, pen);
    float k = min(kh, 1.4);
    return m * k * k;
}

float CarX_Hash3(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float CarX_Noise3(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(CarX_Hash3(i + float3(0, 0, 0)), CarX_Hash3(i + float3(1, 0, 0)), f.x),
                     lerp(CarX_Hash3(i + float3(0, 1, 0)), CarX_Hash3(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(CarX_Hash3(i + float3(0, 0, 1)), CarX_Hash3(i + float3(1, 0, 1)), f.x),
                     lerp(CarX_Hash3(i + float3(0, 1, 1)), CarX_Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

#endif
