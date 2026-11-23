using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>Cuts a sheet with several graffiti (what image generators return) into separate transparent PNGs.
    /// The background is removed first (transparent, white or any other flat colour taken from the sheet border, including pink).
    /// Pieces are groups of painted pixels. Parts of one graffiti that share a colour and sit closer than the gap setting
    /// are glued (separate letters, a drip, a small crown). Neighbouring drawings stay apart even on a crowded sheet,
    /// so they are not merged into one decal and small pieces are not thrown away. A drawing fully inside another is kept with it.</summary>
    public static class GraffitiSheet
    {
        public const string SheetsDir = GraffitiLibrary.Root + "/_Sheets";
        const string GapPref = "CarX.Graffiti.gap";

        public class Piece { public int x, y, w, h, painted; public Color32[] px; }

        public class Result
        {
            public int width, height;
            public bool transparentBg;
            public float borderSolid;      // share of border pixels that match the background colour
            public Color32 bg;
            public List<Piece> pieces = new List<Piece>();
        }

        /// <summary>Largest gap (percent of the longer side of the sheet) that still glues parts of one graffiti together.</summary>
        public static float GapPercent
        {
            get { return EditorPrefs.GetFloat(GapPref, 2f); }
            set { EditorPrefs.SetFloat(GapPref, value); }
        }

        // ------------------------------------------------------------------ analysis (no assets touched)

        public static Result Analyze(byte[] data, float gapPercent)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(data)) { Object.DestroyImmediate(tex); return null; }
            int w = tex.width, h = tex.height;
            var src = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return Analyze(src, w, h, gapPercent);
        }

        public static Result Analyze(Color32[] src, int w, int h, float gapPercent)
        {
            var res = new Result { width = w, height = h };

            // border pixels decide what the background is
            var border = new List<Color32>();
            for (int x = 0; x < w; x++) { border.Add(src[x]); border.Add(src[(h - 1) * w + x]); }
            for (int y = 1; y < h - 1; y++) { border.Add(src[y * w]); border.Add(src[y * w + w - 1]); }
            int clear = 0;
            foreach (var c in border) if (c.a < 16) clear++;
            res.transparentBg = clear >= border.Count * 0.7f;

            var key = new Color32[src.Length];
            if (res.transparentBg)
            {
                System.Array.Copy(src, key, src.Length);
                res.borderSolid = clear / (float)border.Count;
            }
            else
            {
                var rs = new List<int>(); var gs = new List<int>(); var bs = new List<int>();
                foreach (var c in border) { rs.Add(c.r); gs.Add(c.g); bs.Add(c.b); }
                rs.Sort(); gs.Sort(); bs.Sort();
                int m = border.Count / 2;
                res.bg = new Color32((byte)rs[m], (byte)gs[m], (byte)bs[m], 255);
                int near = 0;
                foreach (var c in border) if (Dist(c, res.bg) < 0.1f) near++;
                res.borderSolid = near / (float)border.Count;
                KeyFlat(src, key, w, h, res.bg);
            }

            // Nominal glue distance is gapPercent of the long side. The reach below is the same one the old
            // cell dilation actually used (half the gap on each side, rounded up to a cell): letters of one tag
            // still join. What no longer happens is growing EVERY painted cell by that radius, which chained a
            // whole crowded sheet into one blob, and merging any two boxes that merely overlapped.
            int longSide = Mathf.Max(w, h);
            int cell = Mathf.Max(2, longSide / 384);
            int rc = Mathf.CeilToInt(gapPercent / 100f * longSide * 0.5f / Mathf.Max(1, cell));
            int gapPx = 2 * (rc * cell + cell - 1);
            int crackPx = Mathf.Max(3, Mathf.FloorToInt(0.006f * longSide));   // a hairline crack, not a drip falling toward the next sticker
            const int colorTol = 24;
            const int dust = 48;                                                // smaller than a letter, larger than sensor noise

            int count = LabelPaint(key, w, h, out var labels);
            if (count == 0) return res;
            var area = new int[count + 1];
            var sumR = new long[count + 1]; var sumG = new long[count + 1]; var sumB = new long[count + 1];
            var minX = new int[count + 1]; var minY = new int[count + 1];
            var maxX = new int[count + 1]; var maxY = new int[count + 1];
            for (int i = 1; i <= count; i++) { minX[i] = minY[i] = int.MaxValue; maxX[i] = maxY[i] = -1; }
            for (int i = 0; i < labels.Length; i++)
            {
                int l = labels[i];
                if (l == 0) continue;
                var c = key[i];
                area[l]++;
                sumR[l] += c.r; sumG[l] += c.g; sumB[l] += c.b;
                int x = i % w, y = i / w;
                if (x < minX[l]) minX[l] = x; if (x > maxX[l]) maxX[l] = x;
                if (y < minY[l]) minY[l] = y; if (y > maxY[l]) maxY[l] = y;
            }
            var live = new List<int>();
            long sheet = (long)w * h;
            var anchor = new bool[count + 1];
            for (int i = 1; i <= count; i++)
            {
                if (area[i] < dust) continue;                 // specks are not glue and must not chain two drawings together
                live.Add(i);
                anchor[i] = area[i] >= sheet * 35 / 1000;     // a whole sticker, not a letter or a signature
            }
            for (int i = 0; i < labels.Length; i++)
                if (labels[i] != 0 && area[labels[i]] < dust) labels[i] = 0;
            if (live.Count == 0) return res;

            var gaps = ChessboardGaps(labels, w, h, gapPx);
            var parent = new int[count + 1];
            for (int i = 0; i <= count; i++) parent[i] = i;

            // fragments of the same colour within the gap are one graffiti (letters, underline, drip)
            for (int gi = 0; gi < gaps.Count; gi++)
            {
                int a = gaps[gi].a, b = gaps[gi].b, g = gaps[gi].g;
                if (anchor[a] || anchor[b] || g > gapPx || !Similar(sumR, sumG, sumB, area, a, b, colorTol)) continue;
                Union(parent, a, b);
            }
            // two whole stickers join only when the same paint is actually cracked apart, not when a drip nearly reaches the next one
            for (int gi = 0; gi < gaps.Count; gi++)
            {
                int a = gaps[gi].a, b = gaps[gi].b, g = gaps[gi].g;
                if (!anchor[a] || !anchor[b] || g > crackPx || !Similar(sumR, sumG, sumB, area, a, b, colorTol)) continue;
                Union(parent, a, b);
            }
            // a small part (crown, star, signature, loose letter) joins the nearest whole sticker and only that one,
            // so it cannot become a bridge between two neighbours
            var bestGap = new Dictionary<int, int>();
            var bestTo = new Dictionary<int, int>();
            for (int gi = 0; gi < gaps.Count; gi++)
            {
                int a = gaps[gi].a, b = gaps[gi].b, g = gaps[gi].g;
                int ra = Find(parent, a), rb = Find(parent, b);
                if (ra == rb) continue;
                bool aa = AnchorGroup(parent, anchor, live, ra), bb = AnchorGroup(parent, anchor, live, rb);
                if (aa == bb) continue;
                int fr = aa ? rb : ra, ar = aa ? ra : rb;
                int old;
                if (!bestGap.TryGetValue(fr, out old) || g < old) { bestGap[fr] = g; bestTo[fr] = ar; }
            }
            var attach = new List<int>();
            foreach (var kv in bestGap)
            {
                if (kv.Value > gapPx) continue;
                int fr = kv.Key, ar = bestTo[kv.Key];
                long fa = GroupArea(parent, area, live, fr), fb = GroupArea(parent, area, live, ar);
                bool same = SimilarGroup(parent, sumR, sumG, sumB, area, live, fr, ar, colorTol);
                if (same || fa * 10 <= fb) { attach.Add(fr); attach.Add(ar); }
            }
            for (int i = 0; i + 1 < attach.Count; i += 2) Union(parent, attach[i], attach[i + 1]);

            // a drawing that sits entirely inside another (something in the hole of a ring) belongs to it
            bool grew = true;
            while (grew)
            {
                grew = false;
                var roots = new List<int>();
                var seen = new HashSet<int>();
                for (int i = 0; i < live.Count; i++)
                {
                    int r = Find(parent, live[i]);
                    if (seen.Add(r)) roots.Add(r);
                }
                var bx0 = new int[count + 1]; var by0 = new int[count + 1];
                var bx1 = new int[count + 1]; var by1 = new int[count + 1];
                var ba = new int[count + 1];
                for (int i = 0; i < roots.Count; i++) { int r = roots[i]; bx0[r] = by0[r] = int.MaxValue; bx1[r] = by1[r] = -1; }
                for (int i = 0; i < live.Count; i++)
                {
                    int id = live[i], r = Find(parent, id);
                    if (minX[id] < bx0[r]) bx0[r] = minX[id]; if (maxX[id] > bx1[r]) bx1[r] = maxX[id];
                    if (minY[id] < by0[r]) by0[r] = minY[id]; if (maxY[id] > by1[r]) by1[r] = maxY[id];
                    ba[r] += area[id];
                }
                for (int i = 0; i < roots.Count && !grew; i++)
                    for (int j = 0; j < roots.Count && !grew; j++)
                    {
                        if (i == j) continue;
                        int a = roots[i], b = roots[j];
                        if (ba[a] <= 0 || ba[a] >= ba[b]) continue;
                        if (bx0[a] >= bx0[b] && by0[a] >= by0[b] && bx1[a] <= bx1[b] && by1[a] <= by1[b])
                        { Union(parent, a, b); grew = true; }
                    }
            }

            // one tight texture per graffiti; pixels of a neighbour that happen to fall in the rectangle stay transparent
            var emitted = new HashSet<int>();
            int speckArea = Mathf.Max(150, (int)(0.0001f * sheet));
            int speckSide = Mathf.Max(18, longSide / 64);
            for (int i = 0; i < live.Count; i++)
            {
                int root = Find(parent, live[i]);
                if (!emitted.Add(root)) continue;
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1, painted = 0;
                for (int k = 0; k < live.Count; k++)
                {
                    if (Find(parent, live[k]) != root) continue;
                    int id = live[k];
                    if (minX[id] < x0) x0 = minX[id]; if (maxX[id] > x1) x1 = maxX[id];
                    if (minY[id] < y0) y0 = minY[id]; if (maxY[id] > y1) y1 = maxY[id];
                    painted += area[id];
                }
                int side = Mathf.Max(x1 - x0, y1 - y0) + 1;
                if (painted < speckArea && side < speckSide) continue;   // dust, not a decal
                int bw = x1 - x0 + 1, bh = y1 - y0 + 1;
                int pad = Mathf.Max(3, Mathf.RoundToInt(0.01f * Mathf.Max(bw, bh)));
                int px0 = Mathf.Max(0, x0 - pad), py0 = Mathf.Max(0, y0 - pad);
                int px1 = Mathf.Min(w - 1, x1 + pad), py1 = Mathf.Min(h - 1, y1 + pad);
                var p = new Piece { x = px0, y = py0, w = px1 - px0 + 1, h = py1 - py0 + 1, painted = painted };
                p.px = new Color32[p.w * p.h];
                for (int y = 0; y < p.h; y++)
                    for (int x = 0; x < p.w; x++)
                    {
                        int s = (py0 + y) * w + (px0 + x);
                        int id = labels[s];
                        if (id != 0 && Find(parent, id) == root) p.px[y * p.w + x] = key[s];
                    }
                res.pieces.Add(p);
            }

            // reading order: rows from the top (pieces that share a band of height form a row), left to right inside a row
            res.pieces.Sort((a, b) => (b.y + b.h).CompareTo(a.y + a.h));
            var ordered = new List<Piece>();
            var left = new List<Piece>(res.pieces);
            while (left.Count > 0)
            {
                var first = left[0];
                var row = new List<Piece> { first };
                for (int j = 1; j < left.Count; j++)
                {
                    var q = left[j];
                    int overlap = Mathf.Min(first.y + first.h, q.y + q.h) - Mathf.Max(first.y, q.y);
                    if (overlap > 0.4f * Mathf.Min(first.h, q.h)) row.Add(q);
                }
                row.Sort((a, b) => a.x.CompareTo(b.x));
                ordered.AddRange(row);
                foreach (var q in row) left.Remove(q);
            }
            res.pieces = ordered;
            return res;
        }

        struct Gap { public int a, b, g; }

        /// <summary>4-connected painted pixels (alpha at least 32). Returns the number of components; labels are 1-based, 0 is empty.</summary>
        static int LabelPaint(Color32[] key, int w, int h, out int[] labels)
        {
            labels = new int[key.Length];
            var stack = new int[key.Length];
            int count = 0;
            for (int i = 0; i < key.Length; i++)
            {
                if (key[i].a < 32 || labels[i] != 0) continue;
                count++;
                int top = 0;
                labels[i] = count;
                stack[top++] = i;
                while (top > 0)
                {
                    int c = stack[--top];
                    int cx = c % w, cy = c / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int n = ny * w + nx;
                        if (key[n].a >= 32 && labels[n] == 0) { labels[n] = count; stack[top++] = n; }
                    }
                }
            }
            return count;
        }

        /// <summary>Chebyshev distance between component pixels, for every pair that comes within <paramref name="gapPx"/>.</summary>
        static List<Gap> ChessboardGaps(int[] labels, int w, int h, int gapPx)
        {
            int n = labels.Length;
            var owner = new int[n];
            var dist = new int[n];
            var queue = new int[n];
            int qt = 0;
            for (int i = 0; i < n; i++)
            {
                dist[i] = -1;
                if (labels[i] == 0) continue;
                owner[i] = labels[i];
                dist[i] = 0;
                queue[qt++] = i;
            }
            var best = new Dictionary<long, int>();
            int qh = 0;
            while (qh < qt)
            {
                int c = queue[qh++];
                if (dist[c] >= gapPx) continue;
                int cx = c % w, cy = c / w;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + (k < 4 ? (k == 0 ? 1 : k == 1 ? -1 : 0) : (k < 6 ? 1 : -1));
                    int ny = cy + (k < 4 ? (k == 2 ? 1 : k == 3 ? -1 : 0) : (k % 2 == 0 ? 1 : -1));
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    int oj = owner[j];
                    if (oj == 0)
                    {
                        owner[j] = owner[c];
                        dist[j] = dist[c] + 1;
                        queue[qt++] = j;
                    }
                    else if (oj != owner[c])
                    {
                        int g = dist[c] + dist[j] + 1;   // pixel-centre chebyshev (one step further than the empty-cell count)
                        if (g > gapPx) continue;
                        int a = owner[c], b = oj;
                        if (a > b) { int t = a; a = b; b = t; }
                        long key = ((long)a << 32) | (uint)b;
                        int old;
                        if (!best.TryGetValue(key, out old) || g < old) best[key] = g;
                    }
                }
            }
            var list = new List<Gap>(best.Count);
            foreach (var kv in best)
            {
                var gap = new Gap { a = (int)(kv.Key >> 32), b = (int)kv.Key, g = kv.Value };
                list.Add(gap);
            }
            return list;
        }

        static int Find(int[] parent, int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra != rb) parent[rb] = ra;
        }

        static bool Similar(long[] sumR, long[] sumG, long[] sumB, int[] area, int a, int b, int tol)
        {
            float dr = Mathf.Abs(sumR[a] / (float)area[a] - sumR[b] / (float)area[b]);
            float dg = Mathf.Abs(sumG[a] / (float)area[a] - sumG[b] / (float)area[b]);
            float db = Mathf.Abs(sumB[a] / (float)area[a] - sumB[b] / (float)area[b]);
            return Mathf.Max(dr, Mathf.Max(dg, db)) <= tol;
        }

        static bool AnchorGroup(int[] parent, bool[] anchor, List<int> live, int root)
        {
            for (int i = 0; i < live.Count; i++)
                if (anchor[live[i]] && Find(parent, live[i]) == root) return true;
            return false;
        }

        static long GroupArea(int[] parent, int[] area, List<int> live, int root)
        {
            long s = 0;
            for (int i = 0; i < live.Count; i++)
                if (Find(parent, live[i]) == root) s += area[live[i]];
            return s;
        }

        static bool SimilarGroup(int[] parent, long[] sumR, long[] sumG, long[] sumB, int[] area, List<int> live, int ra, int rb, int tol)
        {
            long ar = 0, ag = 0, ab = 0, aa = 0, br = 0, bg = 0, bb = 0, ba = 0;
            for (int i = 0; i < live.Count; i++)
            {
                int id = live[i], r = Find(parent, id);
                if (r == ra) { ar += sumR[id]; ag += sumG[id]; ab += sumB[id]; aa += area[id]; }
                else if (r == rb) { br += sumR[id]; bg += sumG[id]; bb += sumB[id]; ba += area[id]; }
            }
            if (aa == 0 || ba == 0) return false;
            float dr = Mathf.Abs(ar / (float)aa - br / (float)ba);
            float dg = Mathf.Abs(ag / (float)aa - bg / (float)ba);
            float db = Mathf.Abs(ab / (float)aa - bb / (float)ba);
            return Mathf.Max(dr, Mathf.Max(dg, db)) <= tol;
        }

        static float Dist(Color32 a, Color32 b)
        {
            return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b))) / 255f;
        }

        /// <summary>Removes a flat background and its colour spill. Graffiti are flat paint, so the true colour of an edge pixel is the
        /// colour of the nearest solid paint, and its opacity is how far the pixel moved from the background towards that paint
        /// (the pixel is a mix of the two). Pink halos on black, light halos on dark, and pale edges on bright paint all disappear.</summary>
        static void KeyFlat(Color32[] src, Color32[] key, int w, int h, Color32 bg)
        {
            int n = src.Length;
            var d = new float[n];
            var wide = new bool[n];     // anything that differs from the background at all (includes the halo)
            var loose = new bool[n];    // clearly paint or paint mixed with a lot of paint
            for (int i = 0; i < n; i++)
            {
                d[i] = Dist(src[i], bg);
                wide[i] = d[i] >= 0.06f;
                loose[i] = d[i] >= 0.35f;
            }
            // core = paint that is at least `r` pixels away from every edge: pure colour, no spill
            int r = Mathf.Clamp(Mathf.Max(w, h) / 400, 3, 6);
            var inv = new bool[n];
            for (int i = 0; i < n; i++) inv[i] = !loose[i];
            var grown = Dilate(inv, w, h, r);
            var near = new int[n];      // index of the nearest core pixel, -1 = none
            var queue = new int[n];
            var dist = new byte[n];
            int qh = 0, qt = 0;
            for (int i = 0; i < n; i++)
            {
                near[i] = -1;
                if (loose[i] && !grown[i]) { near[i] = i; queue[qt++] = i; }
            }
            const int reach = 30;       // how far from solid paint an edge pixel may be and still be repainted
            while (qh < qt)
            {
                int c = queue[qh++];
                if (dist[c] >= reach) continue;
                int cx = c % w, cy = c / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int j = ny * w + nx;
                    if (near[j] >= 0 || !wide[j]) continue;
                    near[j] = near[c];
                    dist[j] = (byte)(dist[c] + 1);
                    queue[qt++] = j;
                }
            }
            for (int i = 0; i < n; i++)
            {
                var p = src[i];
                if (!wide[i]) { key[i] = new Color32(p.r, p.g, p.b, 0); continue; }
                if (near[i] == i) { key[i] = new Color32(p.r, p.g, p.b, p.a); continue; }
                if (near[i] < 0)
                {
                    // no solid paint nearby (thin line, speck): remove the background by un-mixing
                    float a0 = Mathf.Clamp01(d[i] / 0.9f);
                    if (a0 < 0.1f) { key[i] = new Color32(p.r, p.g, p.b, 0); continue; }
                    key[i] = new Color32(Un(p.r, bg.r, a0), Un(p.g, bg.g, a0), Un(p.b, bg.b, a0), (byte)(Mathf.SmoothStep(0.2f, 0.75f, a0) * 255f));
                    continue;
                }
                var q = src[near[i]];
                float vr = q.r - bg.r, vg = q.g - bg.g, vb = q.b - bg.b;
                float len2 = vr * vr + vg * vg + vb * vb;
                float t = len2 < 1f ? 1f : ((p.r - bg.r) * vr + (p.g - bg.g) * vg + (p.b - bg.b) * vb) / len2;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.2f) / 0.55f));
                key[i] = new Color32(q.r, q.g, q.b, (byte)(a * p.a));
            }
        }

        static byte Un(int p, int bg, float a) { return (byte)Mathf.Clamp(Mathf.RoundToInt(bg + (p - bg) / a), 0, 255); }

        static bool[] Dilate(bool[] g, int gw, int gh, int r)
        {
            if (r <= 0) return (bool[])g.Clone();
            var a = new bool[g.Length];
            var sum = new int[Mathf.Max(gw, gh) + 1];
            for (int y = 0; y < gh; y++)
            {
                for (int x = 0; x < gw; x++) sum[x + 1] = sum[x] + (g[y * gw + x] ? 1 : 0);
                for (int x = 0; x < gw; x++) a[y * gw + x] = sum[Mathf.Min(gw, x + r + 1)] - sum[Mathf.Max(0, x - r)] > 0;
            }
            var b = new bool[g.Length];
            for (int x = 0; x < gw; x++)
            {
                for (int y = 0; y < gh; y++) sum[y + 1] = sum[y] + (a[y * gw + x] ? 1 : 0);
                for (int y = 0; y < gh; y++) b[y * gw + x] = sum[Mathf.Min(gh, y + r + 1)] - sum[Mathf.Max(0, y - r)] > 0;
            }
            return b;
        }

        // ------------------------------------------------------------------ files

        public static bool IsCandidate(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") return false;
            if (path.StartsWith(GraffitiLibrary.Gen + "/")) return false;
            if (path.StartsWith(SheetsDir + "/")) return true;
            return path.StartsWith(GraffitiLibrary.Root + "/") && Path.GetDirectoryName(path).Replace('\\', '/') == GraffitiLibrary.Root;
        }

        /// <summary>Slices one picture. A picture directly in the Graffiti folder is cut only when it holds several pieces or has a flat
        /// background (single transparent pictures stay as they are); a picture in _Sheets, or with force, is always cut.
        /// Pieces go to Graffiti/&lt;name&gt;/&lt;name&gt;_01.png …, the original moves to _Sheets. Returns the number of pieces written.</summary>
        public static int Process(string assetPath, bool force)
        {
            if (!File.Exists(assetPath)) return 0;
            byte[] data;
            try { data = File.ReadAllBytes(assetPath); } catch { return 0; }
            var res = Analyze(data, GapPercent);
            if (res == null) return 0;
            bool inSheets = assetPath.StartsWith(SheetsDir + "/");
            bool direct = Path.GetDirectoryName(assetPath).Replace('\\', '/') == GraffitiLibrary.Root;
            if (!(inSheets || force || res.pieces.Count >= 2 || !res.transparentBg)) return 0;
            if (res.pieces.Count == 0)
            {
                Debug.LogWarning("CarX Graffiti: на листе «" + assetPath + "» ничего не найдено (фон не однотонный или лист пустой).");
                return 0;
            }
            if (!res.transparentBg && res.borderSolid < 0.7f)
                Debug.LogWarning("CarX Graffiti: фон листа «" + assetPath + "» неоднородный (" + Mathf.RoundToInt(res.borderSolid * 100f) + "% края совпадает с фоном), вырезка может быть грязной. Лучше однотонный фон.");

            GraffitiLibrary.EnsureFolders();
            string name = Path.GetFileNameWithoutExtension(assetPath);
            string dir = GraffitiLibrary.Root + "/" + name;
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(GraffitiLibrary.Root, name);
            int n = 0;
            foreach (var p in res.pieces)
            {
                n++;
                var t = new Texture2D(p.w, p.h, TextureFormat.RGBA32, false);
                t.SetPixels32(p.px);
                string path = dir + "/" + name + "_" + n.ToString("00") + ".png";
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            for (int k = n + 1; k < 500; k++)   // the sheet now has fewer pieces than before
            {
                string old = dir + "/" + name + "_" + k.ToString("00") + ".png";
                if (!File.Exists(old)) break;
                AssetDatabase.DeleteAsset(old);
            }
            if (direct)
            {
                if (!AssetDatabase.IsValidFolder(SheetsDir)) AssetDatabase.CreateFolder(GraffitiLibrary.Root, "_Sheets");
                string dest = AssetDatabase.GenerateUniqueAssetPath(SheetsDir + "/" + Path.GetFileName(assetPath));
                AssetDatabase.MoveAsset(assetPath, dest);
            }
            Debug.Log("CarX Graffiti: лист «" + name + "» нарезан на " + n + " граффити → " + dir);
            return n;
        }

        /// <summary>Re-cuts every sheet in _Sheets (after changing the gap setting, for example).</summary>
        public static int ProcessAll()
        {
            if (!AssetDatabase.IsValidFolder(SheetsDir)) return 0;
            int total = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SheetsDir }))
                total += Process(AssetDatabase.GUIDToAssetPath(guid), true);
            return total;
        }

        [MenuItem("Assets/CarX/Разрезать лист на граффити")]
        static void CutSelected()
        {
            int total = 0;
            foreach (var o in Selection.objects)
            {
                string p = AssetDatabase.GetAssetPath(o);
                if (IsCandidate(p) || p.StartsWith(GraffitiLibrary.Root + "/")) total += Process(p, true);
            }
            if (total == 0) Debug.Log("CarX Graffiti: нечего резать (выделите картинку PNG/JPG внутри папки Graffiti).");
            else GraffitiLibrary.Sync();
        }
    }
}
