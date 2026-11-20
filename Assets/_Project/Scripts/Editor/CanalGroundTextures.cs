using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>
    /// Placeholder textures of the slab channel: four ground layers for the CarX/CanalGround shader (albedo with height in alpha
    /// + normal map), clean slab concrete, chain-link fence and a dry grass tuft (both with alpha). Replace any file freely.
    /// </summary>
    public static partial class CanalTextures
    {
        public enum GroundKind { FineGravel, CoarseGravel, Silt, DryGrass }

        /// <summary>Albedo (A = height) and normal map of one ground layer; generated when missing (or always with force).</summary>
        public static void GetGround(GroundKind kind, bool force, out Texture2D albedo, out Texture2D normal)
        {
            string a = TexDir + "/Canal_G_" + kind + ".png", n = TexDir + "/Canal_G_" + kind + "_N.png";
            albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(a);
            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(n);
            if (!force && albedo != null && normal != null) return;
            var col = new Color[N * N];
            var h = new float[N * N];
            float strength;
            switch (kind)
            {
                case GroundKind.FineGravel: strength = WashGravel(col, h, false); break;
                case GroundKind.CoarseGravel: strength = WashGravel(col, h, true); break;
                case GroundKind.Silt: strength = Silt(col, h); break;
                default: strength = DryGrass(col, h); break;
            }
            // height into alpha, normalised to 0..1
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in h) { lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
            for (int i = 0; i < col.Length; i++) col[i].a = (h[i] - lo) / Mathf.Max(1e-4f, hi - lo);
            albedo = SaveRGBA(a, N, N, col, false);
            normal = Save(n, N, N, NormalFromHeight(h, strength), true);
        }

        /// <summary>Light grey desert wash gravel (fine) or bigger, darker cobbles (coarse) on sandy grit.</summary>
        static float WashGravel(Color[] col, float[] h, bool coarse)
        {
            var layers = coarse ? new[] { new Cells(16, 711), new Cells(30, 712), new Cells(58, 713) }
                                : new[] { new Cells(48, 721), new Cells(92, 722), new Cells(170, 723) };
            float[] presence = coarse ? new[] { 0.6f, 0.85f, 0.9f } : new[] { 0.55f, 0.85f, 0.95f };
            float[] lift = { 1f, 0.72f, 0.48f };
            Color p0 = coarse ? new Color(0.50f, 0.49f, 0.47f) : new Color(0.64f, 0.63f, 0.60f);
            Color p1 = coarse ? new Color(0.58f, 0.53f, 0.46f) : new Color(0.72f, 0.69f, 0.63f);
            Color p2 = coarse ? new Color(0.44f, 0.38f, 0.33f) : new Color(0.55f, 0.53f, 0.50f);
            Color p3 = coarse ? new Color(0.36f, 0.36f, 0.36f) : new Color(0.68f, 0.62f, 0.55f);
            Color sandC = coarse ? new Color(0.58f, 0.54f, 0.47f) : new Color(0.68f, 0.64f, 0.57f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (float)x / N, v = (float)y / N;
                    float wu = u + (Fbm(u, v, 16, 2, 74) - 0.5f) * 0.012f, wv = v + (Fbm(u, v, 16, 2, 75) - 0.5f) * 0.012f;
                    float density = 0.8f + 0.4f * Fbm(u, v, 3, 3, 78);
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
                    float grit = Fbm(u, v, 128, 2, 76), low = Fbm(u, v, 3, 4, 77);
                    Color sand = sandC * (0.86f + 0.24f * grit);
                    if (Hash01(x, y, 79) > 0.9f) sand *= 0.8f;
                    sand *= 1f - 0.3f * shadow;
                    Color stone = Palette(tone, p0, p1, p2, p3) * (0.82f + 0.32f * tone2) * (0.84f + 0.2f * Mathf.Sqrt(Mathf.Max(0f, 1f - edge * edge)));
                    float stoneMask = Smooth(0f, 0.1f, top);
                    Color o = Color.Lerp(sand, stone, stoneMask);
                    o = Color.Lerp(o, sandC * 1.05f, Smooth(0.5f, 0.85f, Fbm(u, v, 6, 3, 80)) * 0.3f * stoneMask);   // dust on the stones
                    o *= 0.92f + 0.16f * low;
                    col[y * N + x] = o;
                    h[y * N + x] = top + 0.1f * grit;
                }
            return coarse ? 5.5f : 4.5f;
        }

        /// <summary>Dry silt / soil: tan-brown with darker clods and a few pebbles, no cracks.</summary>
        static float Silt(Color[] col, float[] h)
        {
            var clods = new Cells(40, 731);
            var peb = new Cells(70, 732);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (float)x / N, v = (float)y / N;
                    float low = Fbm(u, v, 4, 4, 733), fine = Fbm(u, v, 96, 3, 734), grain = Hash01(x, y, 735);
                    var cl = clods.At(u, v);
                    float clod = Smooth(0.42f, 0.2f, cl.f1) * Smooth(0.45f, 0.75f, cl.r2);
                    Color c = Color.Lerp(new Color(0.60f, 0.52f, 0.42f), new Color(0.50f, 0.42f, 0.34f), Smooth(0.4f, 0.7f, low));
                    c *= (0.93f + 0.12f * fine) * (0.96f + 0.08f * grain);
                    c = Color.Lerp(c, c * 0.82f, clod);
                    float height = 0.35f * clod + 0.25f * fine + 0.05f * grain;
                    var q = peb.At(u, v);
                    if (q.r1 > 0.88f)
                    {
                        float rad = 0.2f + 0.15f * Frac(q.r1 * 9.1f);
                        float pm = Smooth(rad + 0.08f, rad, q.f1);
                        c = Color.Lerp(c, Palette(q.r2, new Color(0.55f, 0.53f, 0.50f), new Color(0.62f, 0.56f, 0.47f), new Color(0.45f, 0.40f, 0.35f), new Color(0.66f, 0.63f, 0.58f)), pm);
                        height += pm * 0.5f;
                    }
                    col[y * N + x] = c;
                    h[y * N + x] = height;
                }
            return 3f;
        }

        /// <summary>Litter of dry yellowed grass blades, twigs and dead leaves over silt (blends with the gravel colour).</summary>
        static float DryGrass(Color[] col, float[] h)
        {
            // the silt underneath
            Silt(col, h);
            for (int i = 0; i < col.Length; i++) { col[i] *= 0.85f; h[i] *= 0.3f; }
            var rnd = new System.Random(741);
            System.Func<float> R = () => (float)rnd.NextDouble();
            // blades and twigs as short strokes, the ones drawn later lie on top
            int strokes = 26000;
            for (int k = 0; k < strokes; k++)
            {
                bool twig = R() < 0.06f;
                float x0 = R() * N, y0 = R() * N, ang = R() * Mathf.PI * 2f;
                float len = twig ? 30f + R() * 70f : 12f + R() * 34f, wid = twig ? 2.2f + R() * 1.5f : 1.2f + R() * 0.9f;
                float bend = (R() - 0.5f) * (twig ? 0.2f : 0.8f);
                float tone = R();
                Color c = twig ? Color.Lerp(new Color(0.36f, 0.29f, 0.22f), new Color(0.50f, 0.43f, 0.34f), tone)
                               : Color.Lerp(new Color(0.62f, 0.55f, 0.36f), new Color(0.80f, 0.72f, 0.50f), tone);
                if (!twig && R() < 0.25f) c = Color.Lerp(c, new Color(0.56f, 0.55f, 0.50f), 0.6f);   // bleached grey blades
                float layer = 0.4f + 0.6f * (float)k / strokes;
                int steps = Mathf.CeilToInt(len);
                for (int s = 0; s < steps; s++)
                {
                    float t = (float)s / steps, a = ang + bend * t;
                    float px = x0 + Mathf.Cos(a) * s, py = y0 + Mathf.Sin(a) * s;
                    float w = wid * (1f - 0.6f * t);
                    int r = Mathf.CeilToInt(w);
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            if (d > w) continue;
                            int ix = ((Mathf.FloorToInt(px) + dx) % N + N) % N, iy = ((Mathf.FloorToInt(py) + dy) % N + N) % N;
                            int idx = iy * N + ix;
                            float hh = layer * (twig ? 1.2f : 1f) * (1f - 0.5f * d / Mathf.Max(0.5f, w));
                            if (hh < h[idx]) continue;
                            h[idx] = hh;
                            col[idx] = c * (0.85f + 0.3f * (1f - d / Mathf.Max(0.5f, w)));
                        }
                }
            }
            return 4f;
        }

        /// <summary>Clean cast concrete for the slabs: soft blotches, fine grain, rare pores. No joints, no cracks (the joints are geometry).</summary>
        public static void GetSlab(bool force, out Texture2D albedo, out Texture2D normal)
        {
            string a = TexDir + "/Canal_Slab.png", n = TexDir + "/Canal_Slab_N.png";
            albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(a);
            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(n);
            if (!force && albedo != null && normal != null) return;
            var col = new Color[N * N];
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (float)x / N, v = (float)y / N;
                    float blot = Fbm(u, v, 3, 4, 751), fine = Fbm(u, v, 160, 2, 752), mid = Fbm(u, v, 24, 3, 753);
                    Color c = new Color(0.70f, 0.69f, 0.66f) * (0.93f + 0.12f * blot) * (0.97f + 0.06f * fine) * (0.98f + 0.04f * mid);
                    float height = 0.1f * mid + 0.15f * fine;
                    if (Hash01(x, y, 754) > 0.9992f) { c *= 0.7f; height -= 0.5f; }
                    col[y * N + x] = c;
                    h[y * N + x] = height;
                }
            albedo = Save(a, N, N, col, false);
            normal = Save(n, N, N, NormalFromHeight(h, 1.5f), true);
        }

        /// <summary>Galvanised chain-link mesh, 5 cm diamonds; the texture covers 0.4 × 0.4 m. Alpha = wire.</summary>
        public static Texture2D ChainLink(bool force)
        {
            string path = TexDir + "/Canal_ChainLink.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null && !force) return t;
            const int S = 512;
            const float cells = 8f;
            var col = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (float)x / S * cells, v = (float)y / S * cells;
                    // two families of diagonal wires, each with a little zigzag where they hook into each other
                    float d1 = Mathf.Abs(Frac(u + v + 0.06f * Mathf.Sin(v * Mathf.PI * 2f)) - 0.5f);
                    float d2 = Mathf.Abs(Frac(u - v + 0.06f * Mathf.Sin(v * Mathf.PI * 2f + 1.3f)) - 0.5f);
                    float wire = Mathf.Max(Smooth(0.43f, 0.47f, d1), Smooth(0.43f, 0.47f, d2));
                    float shade = 0.75f + 0.25f * Mathf.Max(Smooth(0.47f, 0.5f, d1), Smooth(0.47f, 0.5f, d2));
                    float rust = Smooth(0.6f, 0.85f, Fbm((float)x / S, (float)y / S, 4, 3, 761));
                    Color c = Color.Lerp(new Color(0.62f, 0.64f, 0.65f), new Color(0.42f, 0.30f, 0.22f), rust * 0.6f) * shade;
                    c.a = wire;
                    col[y * S + x] = c;
                }
            return SaveRGBA(path, S, S, col, false, true);
        }

        /// <summary>A tuft of dry yellowed grass (blades rise from the bottom centre). Alpha = blades.</summary>
        public static Texture2D GrassTuft(bool force)
        {
            string path = TexDir + "/Canal_GrassTuft.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null && !force) return t;
            const int S = 256;
            var col = new Color[S * S];
            for (int i = 0; i < col.Length; i++) col[i] = new Color(0.55f, 0.48f, 0.32f, 0f);
            var rnd = new System.Random(771);
            System.Func<float> R = () => (float)rnd.NextDouble();
            for (int b = 0; b < 70; b++)
            {
                float x0 = S * (0.5f + (R() - 0.5f) * 0.35f), lean = (R() - 0.5f) * 1.6f, hgt = S * (0.45f + R() * 0.5f), droop = R() * 0.6f;
                Color c = Color.Lerp(new Color(0.58f, 0.50f, 0.30f), new Color(0.82f, 0.74f, 0.50f), R());
                if (R() < 0.3f) c = Color.Lerp(c, new Color(0.55f, 0.54f, 0.48f), 0.6f);
                int steps = Mathf.CeilToInt(hgt);
                for (int s = 0; s < steps; s++)
                {
                    float tt = (float)s / steps;
                    float px = x0 + lean * s * 0.5f + droop * tt * tt * S * 0.25f * Mathf.Sign(lean), py = s * (1f - droop * tt * 0.4f);
                    float w = 2.4f * (1f - tt) + 0.6f;
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        if (Mathf.Abs(dx) > w) continue;
                        int ix = Mathf.FloorToInt(px) + dx, iy = Mathf.FloorToInt(py);
                        if (ix < 0 || ix >= S || iy < 0 || iy >= S) continue;
                        col[iy * S + ix] = new Color(c.r * (0.75f + 0.35f * tt), c.g * (0.75f + 0.35f * tt), c.b * (0.75f + 0.35f * tt), 1f);
                    }
                }
            }
            return SaveRGBA(path, S, S, col, false, true);
        }

        /// <summary>Like Save, but keeps the alpha channel. cutout = alpha-tested (mips keep the coverage, so a fence does not vanish far away).</summary>
        static Texture2D SaveRGBA(string path, int w, int h, Color[] px, bool normalMap, bool cutout = false)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            var c32 = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                c32[i] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f + 0.5f), (byte)(Mathf.Clamp01(c.g) * 255f + 0.5f), (byte)(Mathf.Clamp01(c.b) * 255f + 0.5f), (byte)(Mathf.Clamp01(c.a) * 255f + 0.5f));
            }
            t.SetPixels32(c32);
            Directory.CreateDirectory(TexDir);
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = cutout ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            if (path.EndsWith("ChainLink.png")) imp.wrapMode = TextureWrapMode.Repeat;
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = cutout;
            imp.mipmapEnabled = true;
            imp.mipMapsPreserveCoverage = cutout;
            imp.alphaTestReferenceValue = 0.5f;
            imp.anisoLevel = 4;
            imp.maxTextureSize = 2048;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
