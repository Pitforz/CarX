using System.Collections.Generic;
using CarX.Level;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    public static partial class CanalTextures
    {
        /// <summary>
        /// Four kinds of dry desert grass side by side, 256 x 256 each, alpha = stems (Canal_GrassAtlas.png): 0 straw clump,
        /// 1 foxtail with fuzzy seed heads, 2 arching brome (cheatgrass) with hanging spikelets, 3 dry branching weed with dead leaves.
        /// CanalEntrance.GrowGrass gives each tuft one kind when the grass texture is four times as wide as high. Generated when
        /// missing (or always with force); a picture of your own can take its place if it keeps the four cells.
        /// </summary>
        public static Texture2D GrassAtlas(bool force)
        {
            string path = TexDir + "/Canal_GrassAtlas.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null && !force) return t;
            var p = new GrassPaint(256, 4, 2311);
            p.PaintStraw(0);
            p.PaintFoxtail(1);
            p.PaintBrome(2);
            p.PaintWeed(3);
            return SaveRGBA(path, p.W, p.H, p.px, false, true);
        }

        /// <summary>Paints stems, blades, seed heads and leaves into the cells of the grass atlas (y = 0 is the ground).</summary>
        sealed class GrassPaint
        {
            public readonly int S, W, H;
            public readonly Color[] px;
            readonly System.Random rnd;
            const float Step = 0.35f;

            // sRGB colours of dry grass in the Mojave in late summer
            static readonly Color[] StrawTones = {
                new Color(0.86f, 0.78f, 0.56f), new Color(0.80f, 0.70f, 0.46f), new Color(0.74f, 0.64f, 0.42f),
                new Color(0.90f, 0.84f, 0.66f), new Color(0.70f, 0.66f, 0.56f), new Color(0.78f, 0.62f, 0.40f) };
            static readonly Color[] ReddishTones = { new Color(0.72f, 0.54f, 0.46f), new Color(0.68f, 0.50f, 0.44f), new Color(0.78f, 0.62f, 0.50f) };
            static readonly Color[] WeedTones = { new Color(0.60f, 0.52f, 0.40f), new Color(0.55f, 0.47f, 0.36f), new Color(0.66f, 0.58f, 0.45f) };
            static readonly Color[] LeafTones = { new Color(0.58f, 0.52f, 0.34f), new Color(0.53f, 0.51f, 0.35f), new Color(0.66f, 0.56f, 0.38f) };
            static readonly Color Head = new Color(0.93f, 0.87f, 0.67f), Bristle = new Color(0.96f, 0.91f, 0.76f), Seed = new Color(0.45f, 0.36f, 0.26f);

            public GrassPaint(int cell, int cells, int seed)
            {
                S = cell; W = cell * cells; H = cell;
                px = new Color[W * H];
                // see-through pixels carry a dry-grass colour, so the mip maps do not darken the edges of the stems
                for (int i = 0; i < px.Length; i++) px[i] = new Color(0.68f, 0.60f, 0.42f, 0f);
                rnd = new System.Random(seed);
            }

            float R() { return (float)rnd.NextDouble(); }
            /// <summary>-1..1, mostly near 0.</summary>
            float Bell() { return (R() + R() + R() - 1.5f) / 1.5f; }
            Color Pick(Color[] set) { var c = set[rnd.Next(set.Length)] * (0.94f + 0.1f * R()); c.a = 1f; return c; }

            /// <summary>Straw clump: a fan of thin blades, some arching over, some broken.</summary>
            public void PaintStraw(int k)
            {
                for (int b = 0; b < 90; b++)
                {
                    float x0 = S * 0.5f + Bell() * S * 0.12f;
                    // blades growing further out lean out more
                    float a = (x0 - S * 0.5f) / (S * 0.12f) * 0.35f + (R() - 0.5f) * 0.9f;
                    float len = S * (0.38f + 0.55f * R());
                    if (R() < 0.12f) len *= 0.5f;
                    float droop = R() < 0.35f ? 0.5f + R() * 1.1f : R() * 0.35f;
                    Stem(k, x0, 0f, len, a, (R() - 0.5f) * 0.4f, droop, 1.1f + R() * 0.7f, 0.55f, Pick(StrawTones), 0.55f);
                }
            }

            /// <summary>Foxtail: low leaves, then upright stems topped with a fuzzy spindle-shaped seed head.</summary>
            public void PaintFoxtail(int k)
            {
                for (int b = 0; b < 20; b++)
                    Stem(k, S * 0.5f + Bell() * S * 0.1f, 0f, S * (0.16f + 0.24f * R()), (R() - 0.5f) * 2f, (R() - 0.5f) * 0.6f,
                         0.5f + R() * 1.2f, 1.7f + R() * 0.6f, 0.6f, Pick(StrawTones), 0.5f);
                int stems = 9 + rnd.Next(5);
                for (int b = 0; b < stems; b++)
                {
                    var c = Pick(StrawTones);
                    var pts = Stem(k, S * 0.5f + Bell() * S * 0.08f, 0f, S * (0.55f + 0.33f * R()), (R() - 0.5f) * 0.7f, (R() - 0.5f) * 0.3f,
                                   0.1f + R() * 0.35f, 1f, 0.8f, c, 0.6f);
                    var hc = Color.Lerp(c, Head, 0.65f);
                    int from = Mathf.FloorToInt(pts.Count * (0.78f - 0.07f * R()));
                    for (int i = from; i < pts.Count; i++)
                    {
                        float t = (float)(i - from) / Mathf.Max(1, pts.Count - 1 - from);
                        var p = pts[i];
                        float lit = 0.85f + 0.2f * t;
                        Dab(k, p.x, p.y, 0.6f + 2.4f * Mathf.Sin(Mathf.PI * (0.12f + 0.8f * t)), new Color(hc.r * lit, hc.g * lit, hc.b * lit, 1f));
                        if (R() < 0.45f)
                        {
                            float s = R() < 0.5f ? -1f : 1f;
                            Line(k, p.x, p.y, p.z + s * (0.3f + R() * 0.6f), 4f + R() * 6f, 0.5f, Bristle);
                        }
                    }
                }
            }

            /// <summary>Brome (cheatgrass): arching stems, the upper part a nodding panicle of hanging spikelets with long awns; some reddish.</summary>
            public void PaintBrome(int k)
            {
                for (int b = 0; b < 14; b++)
                    Stem(k, S * 0.5f + Bell() * S * 0.1f, 0f, S * (0.12f + 0.2f * R()), (R() - 0.5f) * 2.2f, (R() - 0.5f) * 0.8f,
                         0.6f + R() * 0.8f, 1.6f, 0.6f, Pick(StrawTones), 0.5f);
                int stems = 8 + rnd.Next(5);
                for (int b = 0; b < stems; b++)
                {
                    var c = R() < 0.4f ? Pick(ReddishTones) : Pick(StrawTones);
                    var pts = Stem(k, S * 0.5f + Bell() * S * 0.08f, 0f, S * (0.5f + 0.35f * R()), (R() - 0.5f) * 1.1f, 0f,
                                   0.6f + R() * 0.9f, 0.95f, 0.6f, c, 0.6f);
                    var lc = Color.Lerp(c, Head, 0.3f);
                    int i = Mathf.FloorToInt(pts.Count * 0.55f);
                    while (i < pts.Count)
                    {
                        var p = pts[i];
                        float s = R() < 0.5f ? -1f : 1f;
                        var stalk = Stem(k, p.x, p.y, 5f + R() * 10f, p.z + s * (0.7f + R() * 0.6f), 0f, 0.8f + R() * 0.8f, 0.5f, 0.45f, c, 1f);
                        var e = stalk[stalk.Count - 1];
                        var sp = Stem(k, e.x, e.y, 8f + R() * 6f, e.z + Mathf.Sign(e.z) * 0.3f, 0f, 0f, 1.5f, 0.7f, lc, 0.95f);
                        var tip = sp[sp.Count - 1];
                        Line(k, tip.x, tip.y, tip.z, 6f + R() * 7f, 0.5f, lc);
                        i += Mathf.FloorToInt((5f + R() * 9f) / Step);
                    }
                }
            }

            /// <summary>Dry weed: a few branching grey-brown stems with curled dead leaves and seed clusters at the tips.</summary>
            public void PaintWeed(int k)
            {
                int stems = 4 + rnd.Next(3);
                for (int b = 0; b < stems; b++)
                {
                    var c = Pick(WeedTones);
                    var pts = Stem(k, S * 0.5f + Bell() * S * 0.08f, 0f, S * (0.55f + 0.35f * R()), (R() - 0.5f) * 1f, (R() - 0.5f) * 0.4f,
                                   0.15f, 1.7f, 0.8f, c, 0.55f);
                    Branches(k, pts, c, 2);
                    var top = pts[pts.Count - 1];
                    for (int q = 0; q < 5; q++) Dab(k, top.x + (R() - 0.5f) * 4f, top.y + (R() - 0.5f) * 4f, 1f + R() * 0.7f, Seed);
                    for (int j = 8; j < pts.Count / 4; j += 25 + rnd.Next(30)) Leaf(k, pts[j], R() < 0.5f ? -1f : 1f, 1.3f);
                }
            }

            void Branches(int k, List<Vector3> pts, Color c, int depth)
            {
                float s = R() < 0.5f ? -1f : 1f;
                int i = Mathf.FloorToInt(pts.Count * (0.2f + 0.1f * R()));
                while (i < pts.Count - 8)
                {
                    var p = pts[i];
                    float t = (float)i / pts.Count;
                    float len = (1.05f - t) * (45f + 55f * R()) * (depth >= 2 ? 1f : 0.55f);
                    var br = Stem(k, p.x, p.y, len, p.z + s * (0.6f + R() * 0.6f), -s * 0.35f, 0f, depth >= 2 ? 1f : 0.75f, 0.55f, c, 0.9f);
                    for (int j = 6; j < br.Count; j += 9 + rnd.Next(12)) Leaf(k, br[j], R() < 0.5f ? -1f : 1f, 1.2f);
                    if (depth > 1 && br.Count > 40) Branches(k, br, c, depth - 1);
                    var e = br[br.Count - 1];
                    for (int q = 0; q < 4; q++) Dab(k, e.x + (R() - 0.5f) * 3f, e.y + (R() - 0.5f) * 3f, 0.9f + R() * 0.6f, Seed);
                    s = -s;
                    i += Mathf.FloorToInt((14f + 18f * R()) / Step);
                }
            }

            void Leaf(int k, Vector3 p, float s, float size)
            {
                Stem(k, p.x, p.y, (3.5f + R() * 3.5f) * size, p.z + s * (0.8f + R() * 0.6f), s * (R() - 0.3f) * 1.5f, 0f, 1.5f * size, 0.6f,
                     Pick(LeafTones), 0.9f);
            }

            /// <summary>
            /// A stem or blade from (x, y): starts at angle a from the vertical (radians, + leans right), bends by curve radians over its
            /// length and droops by up to droop radians more towards the side it leans, more and more towards the tip. Radius r0 at the base,
            /// r1 at the tip; the base is in the shade of the tuft. Returns the points (x, y, angle) for seed heads and branches.
            /// </summary>
            List<Vector3> Stem(int k, float x, float y, float len, float a, float curve, float droop, float r0, float r1, Color c, float shade)
            {
                var pts = new List<Vector3>();
                len = Mathf.Max(1f, len);
                int n = Mathf.Max(2, Mathf.CeilToInt(len / Step));
                float side = a >= 0f ? 1f : -1f;
                for (int i = 0; i <= n; i++)
                {
                    float t = (float)i / n;
                    pts.Add(new Vector3(x, y, a));
                    float lit = Mathf.Lerp(shade, 1.05f, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t * 1.8f)));
                    Dab(k, x, y, Mathf.Lerp(r0, r1, t), new Color(c.r * lit, c.g * lit, c.b * lit, 1f));
                    x += Mathf.Sin(a) * Step;
                    y += Mathf.Cos(a) * Step;
                    a += (curve + 2f * droop * t * side) * Step / len;
                }
                return pts;
            }

            /// <summary>A thin straight line (bristle, awn).</summary>
            void Line(int k, float x, float y, float a, float len, float r, Color c)
            {
                float sx = Mathf.Sin(a) * Step, sy = Mathf.Cos(a) * Step;
                int n = Mathf.CeilToInt(len / Step);
                for (int i = 0; i <= n; i++) Dab(k, x + sx * i, y + sy * i, r, c);
            }

            /// <summary>A round dab of radius r pixels in cell k with anti-aliased alpha (stems stay whole in the mip maps); 2 px margin to the cell edges.</summary>
            void Dab(int k, float x, float y, float r, Color c)
            {
                const int m = 2;
                if (x < m || x > S - m || y < 0f || y > H - m) return;
                int ox = k * S;
                int x0 = Mathf.Max(m, Mathf.FloorToInt(x - r - 1f)), x1 = Mathf.Min(S - m - 1, Mathf.CeilToInt(x + r + 1f));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(y - r - 1f)), y1 = Mathf.Min(H - m - 1, Mathf.CeilToInt(y + r + 1f));
                for (int iy = y0; iy <= y1; iy++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        float dx = ix + 0.5f - x, dy = iy + 0.5f - y;
                        float al = Mathf.Clamp01(r + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                        if (al <= 0f) continue;
                        int id = iy * W + ox + ix;
                        var o = px[id];
                        px[id] = new Color(Mathf.Lerp(o.r, c.r, al), Mathf.Lerp(o.g, c.g, al), Mathf.Lerp(o.b, c.b, al), Mathf.Max(o.a, al));
                    }
            }
        }
    }

    public static partial class CanalEntranceBuilder
    {
        [MenuItem("CarX/Canal/Трава: четыре вида сухой травы")]
        static void GrassVarietyMenu() { Debug.Log("CarX трава: " + GrassVariety()); }

        /// <summary>Bridge: the four-kind dry grass picture on the grass material, and the tufts grown again (each one kind).</summary>
        public static string GrassVariety() { return GrassVarietyWith(false); }

        /// <summary>Bridge: the same, painting the picture again first (after changing the painter).</summary>
        public static string GrassVarietyRepaint() { return GrassVarietyWith(true); }

        static string GrassVarietyWith(bool repaint)
        {
            var c = Object.FindAnyObjectByType<CanalEntrance>();
            if (c == null) return "no canal";
            if (c.grassTuft == null) return "no grass material";
            var tex = CanalTextures.GrassAtlas(repaint);
            if (tex == null) return "no grass picture";
            Undo.RecordObject(c.grassTuft, "Dry grass kinds");
            c.grassTuft.SetTexture("_BaseMap", tex);
            c.grassTuft.SetTextureScale("_BaseMap", Vector2.one);
            c.grassTuft.SetTextureOffset("_BaseMap", Vector2.zero);
            EditorUtility.SetDirty(c.grassTuft);
            AssetDatabase.SaveAssetIfDirty(c.grassTuft);
            RegrowGrass(c);
            int verts = 0;
            var root = c.transform.Find(CanalEntrance.ChannelRootName + "/Трава");
            if (root != null)
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) verts += mf.sharedMesh.vertexCount;
            return "picture " + tex.width + "x" + tex.height + ", tufts " + verts / 12;
        }
    }
}
