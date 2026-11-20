using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>
    /// Procedural placeholder textures for the canal (tileable albedo + normal map). Swap them for real ones at any time:
    /// the materials only reference the files.
    /// </summary>
    public static partial class CanalTextures
    {
        public const string TexDir = "Assets/_Project/Textures/Canal";
        const int N = 1024;

        public enum Kind { Gravel, Concrete, StonesConcrete, Dirt }

        /// <summary>Albedo and normal map of one ground kind; generated when missing (or always with force).</summary>
        public static void Get(Kind kind, bool force, out Texture2D albedo, out Texture2D normal)
        {
            string a = TexDir + "/Canal_" + kind + ".png", n = TexDir + "/Canal_" + kind + "_N.png";
            albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(a);
            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(n);
            if (!force && albedo != null && normal != null) return;
            var col = new Color[N * N];
            var h = new float[N * N];
            float strength;
            switch (kind)
            {
                case Kind.Gravel: strength = Gravel(col, h); break;
                case Kind.Concrete: strength = Concrete(col, h); break;
                case Kind.StonesConcrete: strength = Stones(col, h); break;
                default: strength = Dirt(col, h); break;
            }
            albedo = Save(a, N, N, col, false);
            normal = Save(n, N, N, NormalFromHeight(h, strength), true);
        }

        // ------------------------------------------------------------------ ground kinds (u, v in 0..1, tileable)

        /// <summary>Loose wash gravel: rounded 1-9 cm stones of mixed colour lying on sandy grit (not a paving).</summary>
        static float Gravel(Color[] col, float[] h)
        {
            var layers = new[] { new Cells(30, 101), new Cells(56, 202), new Cells(104, 203) };
            float[] presence = { 0.5f, 0.8f, 0.92f }, lift = { 1f, 0.72f, 0.48f };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (float)x / N, v = (float)y / N;
                    // warped lookup so the stones are not round discs; denser and sandier areas
                    float wu = u + (Fbm(u, v, 16, 2, 14) - 0.5f) * 0.014f, wv = v + (Fbm(u, v, 16, 2, 15) - 0.5f) * 0.014f;
                    float density = 0.75f + 0.5f * Fbm(u, v, 3, 3, 18);
                    float top = 0f, tone = 0f, tone2 = 0f, edge = 1f, shadow = 0f;
                    for (int l = 0; l < 3; l++)
                    {
                        var c = layers[l].At(wu, wv);
                        if (c.r2 > presence[l] * density) continue;
                        float t = c.f1 / (0.30f + 0.22f * Frac(c.r1 * 7.31f));
                        shadow = Mathf.Max(shadow, Smooth(1.45f, 1f, t) * lift[l]);
                        if (t >= 1f) continue;
                        float dome = Mathf.Sqrt(1f - t * t) * lift[l] * (0.8f + 0.4f * Frac(c.r2 * 5.3f));
                        if (dome > top) { top = dome; tone = c.r1; tone2 = Frac(c.r2 * 13.7f); edge = t; }
                    }
                    float grit = Fbm(u, v, 128, 2, 16), low = Fbm(u, v, 3, 4, 7);
                    Color sand = new Color(0.56f, 0.50f, 0.42f) * (0.82f + 0.3f * grit);
                    if (Hash01(x, y, 17) > 0.9f) sand *= 0.72f;
                    sand *= 1f - 0.4f * shadow;                                        // grit darker next to the stones
                    Color stone = Palette(tone, new Color(0.56f, 0.55f, 0.53f), new Color(0.68f, 0.62f, 0.52f), new Color(0.60f, 0.47f, 0.38f), new Color(0.40f, 0.39f, 0.38f));
                    stone *= (0.78f + 0.4f * tone2) * (0.8f + 0.25f * Mathf.Sqrt(Mathf.Max(0f, 1f - edge * edge)));
                    float stoneMask = Smooth(0f, 0.1f, top);
                    Color o = Color.Lerp(sand, stone, stoneMask);
                    // dust settles on the stones in patches
                    o = Color.Lerp(o, new Color(0.70f, 0.63f, 0.52f), Smooth(0.45f, 0.8f, Fbm(u, v, 6, 3, 11)) * 0.35f * stoneMask);
                    o *= 0.88f + 0.24f * low;
                    col[y * N + x] = o;
                    h[y * N + x] = top + 0.1f * grit;
                }
            return 5f;
        }

        /// <summary>Cast concrete slope: blotches, pores, an expansion joint every tile, a few hairline cracks.</summary>
        static float Concrete(Color[] col, float[] h)
        {
            var cracks = new Cells(5, 303);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (float)x / N, v = (float)y / N;
                    float blot = Fbm(u, v, 4, 4, 21), fine = Fbm(u, v, 128, 2, 22), stain = Fbm(u, v, 2, 3, 23);
                    Color c = new Color(0.62f, 0.61f, 0.58f) * (0.9f + 0.2f * blot) * (0.96f + 0.08f * fine);
                    c = Color.Lerp(c, new Color(0.50f, 0.46f, 0.40f), Smooth(0.55f, 0.85f, stain) * 0.35f);
                    float height = 0.08f * blot + 0.12f * fine;
                    // pores
                    float pore = Hash01(x, y, 24);
                    if (pore > 0.9965f) { c *= 0.55f; height -= 0.6f; }
                    // expansion joints along the tile edges
                    float ju = Mathf.Min(u, 1f - u) * N, jv = Mathf.Min(v, 1f - v) * N;
                    float joint = 1f - Smooth(1.5f, 4f, Mathf.Min(ju, jv));
                    c = Color.Lerp(c, new Color(0.22f, 0.21f, 0.2f), joint);
                    height -= joint * 1.2f;
                    // hairline cracks in places
                    var cc = cracks.At(u, v);
                    float crack = (1f - Smooth(0.004f, 0.012f, cc.f2 - cc.f1)) * Smooth(0.6f, 0.75f, Fbm(u, v, 3, 2, 25));
                    c = Color.Lerp(c, new Color(0.3f, 0.29f, 0.27f), crack * 0.8f);
                    height -= crack * 0.5f;
                    col[y * N + x] = c;
                    h[y * N + x] = height;
                }
            return 2.5f;
        }

        /// <summary>Grouted riprap: irregular rocks of two sizes set in concrete, dirt in the grout and on the rocks.</summary>
        static float Stones(Color[] col, float[] h)
        {
            var big = new Cells(8, 404);
            var small = new Cells(15, 405);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (float)x / N, v = (float)y / N;
                    float wu = u + (Fbm(u, v, 6, 3, 30) - 0.5f) * 0.05f, wv = v + (Fbm(u, v, 6, 3, 37) - 0.5f) * 0.05f;
                    float rough = Fbm(u, v, 32, 3, 31), facet = Fbm(u, v, 12, 2, 38);
                    float gap = 0.07f + 0.08f * Fbm(u, v, 8, 2, 32);                    // grout joint width varies
                    float top = 0f, tone = 0f, tone2 = 0f;
                    for (int l = 0; l < 2; l++)
                    {
                        var c = (l == 0 ? big : small).At(wu, wv);
                        if (l == 1 && c.r2 > 0.75f) continue;
                        float e = c.f2 - c.f1, reach = (l == 0 ? 0.5f : 0.46f) + 0.25f * Frac(c.r1 * 3.1f);
                        float inside = Smooth(gap, gap + 0.09f, e) * Smooth(reach + 0.1f, reach, c.f1);
                        float d = inside * (l == 0 ? 1f : 0.8f) * (0.06f + 0.94f * Mathf.Sqrt(Mathf.Clamp01((e - gap) * 2.2f)));
                        if (d > top) { top = d; tone = c.r1; tone2 = c.r2; }
                    }
                    float stoneMask = Smooth(0.02f, 0.12f, top);
                    Color stone = Palette(tone, new Color(0.50f, 0.49f, 0.47f), new Color(0.56f, 0.51f, 0.45f), new Color(0.42f, 0.40f, 0.38f), new Color(0.60f, 0.58f, 0.55f));
                    stone *= 0.75f + 0.3f * rough + 0.18f * tone2 + 0.1f * (facet - 0.5f);
                    float dirtAmt = Mathf.Clamp01(Smooth(0.35f, 0.75f, Fbm(u, v, 3, 3, 33)) + (1f - stoneMask) * 0.3f);
                    Color grout = Color.Lerp(new Color(0.60f, 0.59f, 0.56f) * (0.9f + 0.15f * Fbm(u, v, 48, 2, 34)), new Color(0.36f, 0.30f, 0.24f), dirtAmt * 0.75f);
                    Color o = Color.Lerp(grout, stone, stoneMask);
                    o = Color.Lerp(o, new Color(0.45f, 0.38f, 0.30f), Smooth(0.55f, 0.85f, Fbm(u, v, 4, 3, 35)) * 0.25f);
                    col[y * N + x] = o;
                    h[y * N + x] = top + (0.22f * (rough - 0.5f) + 0.12f * (facet - 0.5f)) * stoneMask + 0.04f * Fbm(u, v, 128, 2, 36);
                }
            return 4.5f;
        }

        /// <summary>Desert sand (grain, wind ripples, pebbles) with patches of dried, partly cracked mud.</summary>
        static float Dirt(Color[] col, float[] h)
        {
            var plates = new Cells(26, 606);
            var peb = new Cells(60, 505);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (float)x / N, v = (float)y / N;
                    float low = Fbm(u, v, 4, 4, 41), grain = Hash01(x, y, 42), warp = Fbm(u, v, 4, 2, 43);
                    float ripple = 0.5f + 0.5f * Mathf.Sin((v * 22f + u * 4f + warp * 3f) * Mathf.PI * 2f);
                    Color sand = new Color(0.74f, 0.65f, 0.52f) * (0.9f + 0.18f * low) * (0.95f + 0.1f * grain) * (0.97f + 0.06f * ripple);
                    float hs = 0.25f * ripple * Smooth(0.3f, 0.7f, Fbm(u, v, 2, 2, 44)) + 0.1f * grain;

                    var p = plates.At(u, v);
                    float e = p.f2 - p.f1;
                    float crack = 1f - Smooth(0.015f, 0.05f, e + (Fbm(u, v, 32, 2, 51) - 0.5f) * 0.03f);
                    float fine = Fbm(u, v, 128, 2, 53), patch = Fbm(u, v, 3, 4, 55);
                    float m = Smooth(0.5f, 0.7f, patch) * 0.6f, cracked = Smooth(0.68f, 0.8f, patch);
                    Color mud = new Color(0.56f, 0.47f, 0.37f) * (0.85f + 0.3f * low) * (0.94f + 0.12f * fine) * (0.9f + 0.2f * p.r1);
                    mud = Color.Lerp(mud, new Color(0.30f, 0.25f, 0.2f), Smooth(0.62f, 0.82f, Fbm(u, v, 8, 3, 54)) * 0.5f);
                    mud = Color.Lerp(mud, new Color(0.24f, 0.2f, 0.16f), crack * cracked * 0.75f);
                    float hm = (1f - crack * cracked) * (0.6f + 0.25f * (1f - Smooth(0.05f, 0.2f, e)) * cracked) + 0.15f * fine;

                    Color o = Color.Lerp(sand, mud, m);
                    float height = Mathf.Lerp(hs, hm, m);
                    var q = peb.At(u, v);
                    if (q.r1 > 0.94f)
                    {
                        float rad = 0.22f + 0.16f * Frac(q.r1 * 11.3f);
                        float pm = Smooth(rad + 0.1f, rad, q.f1) * (1f - m * 0.7f);
                        o = Color.Lerp(o, Palette(q.r2, new Color(0.5f, 0.48f, 0.45f), new Color(0.6f, 0.52f, 0.42f), new Color(0.42f, 0.36f, 0.3f), new Color(0.66f, 0.62f, 0.56f)), pm);
                        height += pm * 0.35f;
                    }
                    col[y * N + x] = o;
                    h[y * N + x] = height;
                }
            return 3.5f;
        }

        // ------------------------------------------------------------------ billboard and sign faces

        /// <summary>Placeholder casino-style ad for the roadside billboard (replace with your own picture).</summary>
        public static Texture2D BillboardAd(bool force)
        {
            string path = TexDir + "/Canal_BillboardAd.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null && !force) return t;
            const int W = 1536, H = 452;
            var col = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / W, v = (float)y / H;
                    Color c = Color.Lerp(new Color(0.22f, 0.05f, 0.30f), new Color(0.85f, 0.15f, 0.45f), u * 0.8f + v * 0.3f);
                    // gold chip on the left
                    Vector2 d = new Vector2((u - 0.16f) * W / H, v - 0.5f);
                    float r = d.magnitude;
                    if (r < 0.38f)
                    {
                        float ang = Mathf.Atan2(d.y, d.x);
                        bool stripe = Mathf.Repeat(ang / (Mathf.PI * 2f) * 8f, 1f) < 0.5f && r > 0.28f;
                        c = stripe ? new Color(0.95f, 0.92f, 0.85f) : new Color(0.95f, 0.70f, 0.15f);
                        if (r < 0.2f) c = new Color(0.75f, 0.1f, 0.12f);
                        if (Mathf.Abs(r - 0.28f) < 0.012f || Mathf.Abs(r - 0.2f) < 0.01f) c = new Color(0.98f, 0.88f, 0.5f);
                    }
                    // "headline" and "small print" bars on the right
                    if (u > 0.36f && u < 0.94f && v > 0.58f && v < 0.82f && Mathf.Repeat(u * 14f, 1f) < 0.86f) c = new Color(1f, 0.93f, 0.55f);
                    if (u > 0.36f && u < 0.80f && v > 0.36f && v < 0.48f && Mathf.Repeat(u * 26f, 1f) < 0.8f) c = Color.white;
                    if (v < 0.16f) c = Mathf.Repeat(u * 40f, 1f) < 0.15f && v > 0.06f && v < 0.1f ? Color.white : new Color(0.08f, 0.02f, 0.1f);
                    col[y * W + x] = c;
                }
            return Save(path, W, H, col, false);
        }

        /// <summary>Yellow diamond warning sign with an exclamation mark (UV square = sign square).</summary>
        public static Texture2D WarningSign(bool force)
        {
            string path = TexDir + "/Canal_WarningSign.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null && !force) return t;
            const int S = 256;
            var col = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                    float dd = Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f);
                    Color c = new Color(0.93f, 0.72f, 0.08f);
                    if (dd > 0.43f) c = new Color(0.06f, 0.06f, 0.06f);
                    if (dd > 0.47f) c = new Color(0.93f, 0.72f, 0.08f);
                    bool bar = Mathf.Abs(u - 0.5f) < 0.045f * (0.6f + v) && v > 0.36f && v < 0.74f;
                    bool dot = new Vector2(u - 0.5f, v - 0.28f).magnitude < 0.05f;
                    if (bar || dot) c = new Color(0.06f, 0.06f, 0.06f);
                    float grime = Fbm(u, v, 4, 3, 61);
                    col[y * S + x] = c * (0.85f + 0.2f * grime);
                }
            return Save(path, S, S, col, false);
        }

        /// <summary>White plate with a red band and black "text" lines.</summary>
        public static Texture2D Plate(bool force)
        {
            string path = TexDir + "/Canal_SignPlate.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null && !force) return t;
            const int W = 256, H = 172;
            var col = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                    Color c = new Color(0.9f, 0.89f, 0.85f);
                    if (v > 0.72f) c = new Color(0.72f, 0.1f, 0.08f);
                    bool line = (v > 0.48f && v < 0.6f && u > 0.12f && u < 0.88f) || (v > 0.24f && v < 0.33f && u > 0.2f && u < 0.8f) || (v > 0.08f && v < 0.15f && u > 0.25f && u < 0.75f);
                    if (line && Mathf.Repeat(u * 18f, 1f) < 0.75f) c = new Color(0.08f, 0.08f, 0.08f);
                    if (u < 0.03f || u > 0.97f || v < 0.04f || v > 0.96f) c = new Color(0.5f, 0.5f, 0.5f);
                    col[y * W + x] = c * (0.85f + 0.2f * Fbm(u, v, 4, 3, 71));
                }
            return Save(path, W, H, col, false);
        }

        // ------------------------------------------------------------------ helpers

        static Texture2D Save(string path, int w, int h, Color[] px, bool normalMap)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, !normalMap ? false : true);
            var c32 = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                c32[i] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f + 0.5f), (byte)(Mathf.Clamp01(c.g) * 255f + 0.5f), (byte)(Mathf.Clamp01(c.b) * 255f + 0.5f), 255);
            }
            t.SetPixels32(c32);
            Directory.CreateDirectory(TexDir);
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !normalMap;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.maxTextureSize = 2048;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Tangent-space normal map (+Y up) from a tileable height field.</summary>
        static Color[] NormalFromHeight(float[] h, float strength)
        {
            var o = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float hx = h[y * N + (x + 1) % N] - h[y * N + (x + N - 1) % N];
                    float hy = h[((y + 1) % N) * N + x] - h[((y + N - 1) % N) * N + x];
                    var n = new Vector3(-hx * strength, -hy * strength, 1f).normalized;
                    o[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            return o;
        }

        static Color Palette(float t, Color a, Color b, Color c, Color d)
        {
            if (t < 0.4f) return Color.Lerp(a, b, t / 0.4f * 0.5f);
            if (t < 0.65f) return Color.Lerp(b, c, (t - 0.4f) / 0.25f * 0.6f);
            if (t < 0.85f) return Color.Lerp(a, d, (t - 0.65f) / 0.2f);
            return Color.Lerp(c, b, (t - 0.85f) / 0.15f);
        }

        static float Frac(float x) { return x - Mathf.Floor(x); }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        struct Cell { public float f1, f2, r1, r2; }

        /// <summary>Tileable Voronoi on an n×n grid of jittered points.</summary>
        class Cells
        {
            readonly int n;
            readonly Vector2[] jit;
            readonly float[] r1, r2;

            public Cells(int n, int salt)
            {
                this.n = n;
                jit = new Vector2[n * n]; r1 = new float[n * n]; r2 = new float[n * n];
                var rnd = new System.Random(salt);
                for (int i = 0; i < n * n; i++)
                {
                    jit[i] = new Vector2(0.15f + 0.7f * (float)rnd.NextDouble(), 0.15f + 0.7f * (float)rnd.NextDouble());
                    r1[i] = (float)rnd.NextDouble(); r2[i] = (float)rnd.NextDouble();
                }
            }

            public Cell At(float u, float v)
            {
                float x = u * n, y = v * n;
                int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
                var c = new Cell { f1 = 9f, f2 = 9f };
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int gx = cx + dx, gy = cy + dy;
                        int k = (((gy % n) + n) % n) * n + (((gx % n) + n) % n);
                        float px = gx + jit[k].x - x, py = gy + jit[k].y - y;
                        float d = Mathf.Sqrt(px * px + py * py);
                        if (d < c.f1) { c.f2 = c.f1; c.f1 = d; c.r1 = r1[k]; c.r2 = r2[k]; }
                        else if (d < c.f2) c.f2 = d;
                    }
                return c;
            }
        }

        /// <summary>Tileable fractal value noise, 0..1; period = cells of the first octave per tile.</summary>
        static float Fbm(float u, float v, int period, int octaves, int salt)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * VNoise(u * period, v * period, period, salt + o * 17);
                norm += amp; amp *= 0.5f; period *= 2;
            }
            return sum / norm;
        }

        static float VNoise(float x, float y, int period, int salt)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(x0, y0, period, salt), b = Hash(x0 + 1, y0, period, salt), c = Hash(x0, y0 + 1, period, salt), d = Hash(x0 + 1, y0 + 1, period, salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Hash(int x, int y, int period, int salt)
        {
            x = ((x % period) + period) % period; y = ((y % period) + period) % period;
            return Hash01(x, y, salt);
        }

        static float Hash01(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + salt * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
