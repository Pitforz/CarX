using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>Cuts a sheet with several graffiti (what image generators return) into separate transparent PNGs.
    /// The background is removed first (transparent, white or any other flat colour taken from the sheet border),
    /// then pieces are found as groups of painted pixels separated by empty gaps and each is trimmed and saved.</summary>
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

            // painted pixels on a coarse grid, grown by half the gap: touching cells = one graffiti
            int cell = Mathf.Max(2, Mathf.Max(w, h) / 384);
            int gw = (w + cell - 1) / cell, gh = (h + cell - 1) / cell;
            var grid = new bool[gw * gh];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (key[y * w + x].a >= 32) grid[(y / cell) * gw + x / cell] = true;
            int rc = Mathf.CeilToInt(gapPercent / 100f * Mathf.Max(w, h) * 0.5f / cell);
            var dil = Dilate(grid, gw, gh, rc);

            var labels = new int[gw * gh];
            int count = 0;
            var stack = new Stack<int>();
            for (int i = 0; i < dil.Length; i++)
            {
                if (!dil[i] || labels[i] != 0) continue;
                count++;
                labels[i] = count;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    int cx = c % gw, cy = c / gw;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = cy + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= gw || ny >= gh) continue;
                        int n = ny * gw + nx;
                        if (dil[n] && labels[n] == 0) { labels[n] = count; stack.Push(n); }
                    }
                }
            }
            if (count == 0) return res;

            var minX = new int[count + 1]; var minY = new int[count + 1];
            var maxX = new int[count + 1]; var maxY = new int[count + 1];
            var painted = new int[count + 1];
            for (int i = 1; i <= count; i++) { minX[i] = minY[i] = int.MaxValue; maxX[i] = maxY[i] = -1; }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (key[y * w + x].a < 32) continue;
                    int l = labels[(y / cell) * gw + x / cell];
                    if (l == 0) continue;
                    if (x < minX[l]) minX[l] = x; if (x > maxX[l]) maxX[l] = x;
                    if (y < minY[l]) minY[l] = y; if (y > maxY[l]) maxY[l] = y;
                    painted[l]++;
                }

            // boxes that overlap belong together (a ring around a smaller drawing)
            var boxes = new List<int[]>();   // minX, minY, maxX, maxY, painted
            for (int i = 1; i <= count; i++) if (painted[i] > 0) boxes.Add(new[] { minX[i], minY[i], maxX[i], maxY[i], painted[i] });
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int a = 0; a < boxes.Count && !merged; a++)
                    for (int b = a + 1; b < boxes.Count && !merged; b++)
                    {
                        var A = boxes[a]; var B = boxes[b];
                        if (A[0] > B[2] || B[0] > A[2] || A[1] > B[3] || B[1] > A[3]) continue;
                        A[0] = Mathf.Min(A[0], B[0]); A[1] = Mathf.Min(A[1], B[1]);
                        A[2] = Mathf.Max(A[2], B[2]); A[3] = Mathf.Max(A[3], B[3]);
                        A[4] += B[4];
                        boxes.RemoveAt(b);
                        merged = true;
                    }
            }

            int minPainted = Mathf.Max(200, (int)(0.0005f * w * h));   // specks and dust are not graffiti
            foreach (var bx in boxes)
            {
                if (bx[4] < minPainted) continue;
                int bw = bx[2] - bx[0] + 1, bh = bx[3] - bx[1] + 1;
                int pad = Mathf.Max(3, Mathf.RoundToInt(0.01f * Mathf.Max(bw, bh)));
                int x0 = Mathf.Max(0, bx[0] - pad), y0 = Mathf.Max(0, bx[1] - pad);
                int x1 = Mathf.Min(w - 1, bx[2] + pad), y1 = Mathf.Min(h - 1, bx[3] + pad);
                var p = new Piece { x = x0, y = y0, w = x1 - x0 + 1, h = y1 - y0 + 1, painted = bx[4] };
                p.px = new Color32[p.w * p.h];
                for (int y = 0; y < p.h; y++)
                    System.Array.Copy(key, (y0 + y) * w + x0, p.px, y * p.w, p.w);
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
            for (int k = n + 1; k < 100; k++)   // the sheet now has fewer pieces than before
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
