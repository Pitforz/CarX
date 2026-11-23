#ifndef CARX_SHAFT_INCLUDED
#define CARX_SHAFT_INCLUDED

// Shared helpers for daylight falling through a ceiling opening.
// Expects these uniforms to be declared by the including shader (inside UnityPerMaterial):
//   float4 _ShaftLight;    // xyz: light position in shaft space
//   float4 _ShaftOpening;  // rect opening: xMin, zMin, xMax, zMax (shaft space)
//   float4 _ShaftParams;   // x: opening plane y, y: grate plane y, z: round radius (0 = rect), w: edge softness
//   float4 _ShaftBars;     // x: bar pitch along x, y: bar width, z: pitch along z, w: bar origin x
// Optional (define CARX_SHAFT_SHARP before the include and declare it):
//   float4 _ShaftSharp;    // x: bar shadow blur at the grate (m), y: its growth per metre below the grate,
//                          // z: opening edge blur (m), w: its growth per metre below the opening

#ifdef CARX_SHAFT_SHARP
    #define CARX_SHARP _ShaftSharp
#else
    #define CARX_SHARP float4(0.002, 0.004, _ShaftParams.w, 0.012)
#endif

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

// Inside the opening outline at plane point g (rect or round), blurred by soft (m).
float CarX_Aperture(float2 g, float soft)
{
    soft = max(soft, 1e-4);
    if (_ShaftParams.z > 0.0)
        return saturate((_ShaftParams.z - length(g)) / (2.5 * soft) + 0.5);
    float dx = min(g.x - _ShaftOpening.x, _ShaftOpening.z - g.x);
    float dz = min(g.y - _ShaftOpening.y, _ShaftOpening.w - g.y);
    return saturate(dx / (2.5 * soft) + 0.5) * saturate(dz / (2.5 * soft) + 0.5);
}

// How much direct daylight reaches point p (shaft space): through the top of the shaft (grate bars) and through the
// opening in the tunnel ceiling, seen from the light point; includes spreading.
float CarX_ShaftLight(float3 p)
{
    float drop = _ShaftLight.y - p.y;
    if (drop <= 0.01) return 0.0;
    float4 sharp = CARX_SHARP;
    // the opening in the ceiling
    float kh = (_ShaftLight.y - _ShaftParams.x) / drop;
    float2 g = _ShaftLight.xz + (p.xz - _ShaftLight.xz) * kh;
    float m = CarX_Aperture(g, sharp.z + max(0.0, _ShaftParams.x - p.y) * sharp.w);
    if (m <= 0.0) return 0.0;
    // the top of the shaft with the bars (same outline)
    float kg = (_ShaftLight.y - _ShaftParams.y) / drop;
    float2 b = _ShaftLight.xz + (p.xz - _ShaftLight.xz) * kg;
    float below = max(0.0, _ShaftParams.y - p.y);
    m *= CarX_Aperture(b, sharp.z + below * sharp.w);
    m *= CarX_Bars(b, 1.5 * (sharp.x + below * sharp.y));
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
