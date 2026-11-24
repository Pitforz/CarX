using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>Automation check of the sheet slicer / background keying (bridge: call CarX.EditorTools.GraffitiSheetTest.Run or .Real).
    /// Run: synthetic sheets. Real: the sheets in Graffiti/_Sheets (writes Logs/shots/real_*.png, changes nothing in the project).</summary>
    static class GraffitiSheetTest
    {
        static int ok, bad;
        static StringBuilder log;
        static void Check(bool c, string what) { if (c) ok++; else { bad++; log.Append("\n  FAIL " + what); } }

        // disc, ring, two-part tag, triangle with a drip, bar on a size x size sheet
        static Color32[] Sheet(int size, Color32 bg, bool transparent, Color32 paint)
        {
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = transparent ? new Color32(0, 0, 0, 0) : bg;
            System.Action<int, int, System.Func<float, float, float>> draw = (cx, cy, sdf) =>
            {
                for (int y = Mathf.Max(0, cy - 420); y < Mathf.Min(size, cy + 420); y++)
                    for (int x = Mathf.Max(0, cx - 420); x < Mathf.Min(size, cx + 420); x++)
                    {
                        float a = Mathf.Clamp01(0.5f - sdf(x - cx, y - cy));
                        if (a <= 0f) continue;
                        var b = px[y * size + x];
                        Color32 under = transparent ? new Color32(paint.r, paint.g, paint.b, 0) : b;
                        px[y * size + x] = new Color32((byte)Mathf.Lerp(under.r, paint.r, a), (byte)Mathf.Lerp(under.g, paint.g, a), (byte)Mathf.Lerp(under.b, paint.b, a),
                            transparent ? (byte)(Mathf.Max(b.a / 255f, a) * 255) : (byte)255);
                    }
            };
            draw(330, 1500, (x, y) => Mathf.Sqrt(x * x + y * y) - 250f);
            draw(1000, 1500, (x, y) => Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 260f) - 40f);
            draw(1700, 1500, (x, y) => Mathf.Max(Mathf.Abs(x + 115f) - 100f, Mathf.Abs(y) - 160f));
            draw(1700, 1500, (x, y) => Mathf.Max(Mathf.Abs(x - 115f) - 100f, Mathf.Abs(y) - 160f));
            draw(500, 500, (x, y) => Mathf.Max(Mathf.Max(-y - 200f, y + x * 1.5f - 150f), -x * 1.5f + y - 150f));
            draw(500, 260, (x, y) => Mathf.Max(Mathf.Abs(x) - 6f, Mathf.Abs(y) - 60f));
            draw(1400, 600, (x, y) => Mathf.Max(Mathf.Abs(x) - 380f, Mathf.Abs(y) - 90f));
            return px;
        }

        static Color32[] Blur(Color32[] px, int size, int r)
        {
            var o = new Color32[px.Length];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int sr = 0, sg = 0, sb = 0, c = 0;
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            var q = px[Mathf.Clamp(y + dy, 0, size - 1) * size + Mathf.Clamp(x + dx, 0, size - 1)];
                            sr += q.r; sg += q.g; sb += q.b; c++;
                        }
                    o[y * size + x] = new Color32((byte)(sr / c), (byte)(sg / c), (byte)(sb / c), 255);
                }
            return o;
        }

        static void Noise(Color32[] px, int amp, int seed)
        {
            var rn = new System.Random(seed);
            for (int i = 0; i < px.Length; i++)
            {
                int nz = rn.Next(-amp, amp + 1);
                px[i] = new Color32((byte)Mathf.Clamp(px[i].r + nz, 0, 255), (byte)Mathf.Clamp(px[i].g + nz, 0, 255), (byte)Mathf.Clamp(px[i].b + nz, 0, 255), 255);
            }
        }

        /// <summary>Visible pixels that are clearly magenta-ish (red and blue both well above green).</summary>
        static int Pink(Color32[] px)
        {
            int n = 0;
            foreach (var c in px) if (c.a > 20 && c.r > c.g + 35 && c.b > c.g + 35) n++;
            return n;
        }

        /// <summary>Visible pixels whose colour is far from the paint colour (any spill or halo).</summary>
        static int Off(Color32[] px, Color32 paint, int tol)
        {
            int n = 0;
            foreach (var c in px) if (c.a > 20 && Mathf.Max(Mathf.Abs(c.r - paint.r), Mathf.Max(Mathf.Abs(c.g - paint.g), Mathf.Abs(c.b - paint.b))) > tol) n++;
            return n;
        }

        static void Magenta(string label, Color32 paint, int blur, int noise)
        {
            const int S = 2048;
            var px = Sheet(S, new Color32(250, 5, 249, 255), false, paint);
            if (blur > 0) px = Blur(px, S, blur);
            Noise(px, noise, 5);
            var r = GraffitiSheet.Analyze(px, S, S, 2f);
            Check(r.pieces.Count == 5, label + ": pieces " + r.pieces.Count + " (want 5)");
            int off = 0, vis = 0, pink = 0;
            foreach (var p in r.pieces) { off += Off(p.px, paint, 12); pink += Pink(p.px); foreach (var c in p.px) if (c.a > 20) vis++; }
            Check(vis > 100000, label + ": visible " + vis);
            Check(off <= vis * 0.006f, label + ": " + off + " visible pixels off the paint colour (of " + vis + "), pink " + pink);
        }

        public static void Run()
        {
            ok = bad = 0;
            log = new StringBuilder();
            const int S = 2048;
            // plain sheets: transparent / white / green
            foreach (var kv in new[] { "transparent", "white", "green" })
            {
                var paint = new Color32(200, 30, 35, 255);
                Color32[] px = kv == "transparent" ? Sheet(S, default(Color32), true, paint)
                    : kv == "white" ? Sheet(S, new Color32(255, 255, 255, 255), false, paint)
                    : Sheet(S, new Color32(0, 255, 0, 255), false, paint);
                var r = GraffitiSheet.Analyze(px, S, S, 2f);
                Check(r.pieces.Count == 5, kv + ": pieces " + r.pieces.Count);
                if (r.pieces.Count == 5)
                {
                    Check(r.pieces[0].w > 480 && r.pieces[0].w < 560, kv + ": disc first, w " + r.pieces[0].w);
                    Check(r.pieces[2].w > 380 && r.pieces[2].w < 460, kv + ": two letters one piece");
                    Check(r.pieces[3].h > 300 && r.pieces[4].w > 740, kv + ": bottom row order");
                    var ring = r.pieces[1];
                    Check(ring.px[(ring.h / 2) * ring.w + ring.w / 2].a == 0, kv + ": ring hole transparent");
                }
            }
            // the user's case: flat colour paint on pink, soft + noisy edges
            Magenta("black on magenta", new Color32(0, 0, 0, 255), 2, 4);
            Magenta("black on magenta, strong blur", new Color32(10, 10, 10, 255), 4, 6);
            Magenta("white on magenta", new Color32(255, 255, 255, 255), 2, 4);
            Magenta("blue on magenta", new Color32(20, 40, 200, 255), 2, 4);
            Magenta("yellow on magenta", new Color32(255, 230, 0, 255), 2, 4);
            Magenta("red on magenta", new Color32(190, 10, 20, 255), 2, 4);
            Debug.Log("CarX sheet test: ok " + ok + ", bad " + bad + log);
        }

        public static void Real()
        {
            ok = bad = 0;
            log = new StringBuilder();
            foreach (var f in Directory.GetFiles(GraffitiSheet.SheetsDir, "*.png"))
            {
                var res = GraffitiSheet.Analyze(File.ReadAllBytes(f), GraffitiSheet.GapPercent);
                if (res == null) continue;
                int pink = 0, vis = 0;
                foreach (var pc in res.pieces) { pink += Pink(pc.px); foreach (var c in pc.px) if (c.a > 20) vis++; }
                log.Append("\n  " + Path.GetFileName(f) + ": " + res.width + "x" + res.height + ", bg " + (res.transparentBg ? "transparent" : res.bg.r + "," + res.bg.g + "," + res.bg.b) +
                           ", pieces " + res.pieces.Count + ", visible " + vis + ", pink " + pink);
                Strip(res, "Logs/shots/real_" + Path.GetFileNameWithoutExtension(f) + ".png");
            }
            Debug.Log("CarX sheet real:" + log);
        }

        // pieces over white (top) and over dark concrete (bottom)
        static void Strip(GraffitiSheet.Result res, string file)
        {
            int W = 1800, rowH = 300;
            var m = new Texture2D(W, rowH * 2, TextureFormat.RGB24, false);
            var px = new Color32[W * rowH * 2];
            for (int i = 0; i < px.Length; i++) px[i] = i / (W * rowH) == 0 ? new Color32(70, 68, 64, 255) : new Color32(255, 255, 255, 255);
            m.SetPixels32(px);
            int x = 10;
            foreach (var p in res.pieces)
            {
                float s = Mathf.Min((rowH - 20f) / p.h, (W / 3f) / p.w);
                int tw = (int)(p.w * s), th = (int)(p.h * s);
                if (x + tw > W) break;
                for (int yy = 0; yy < th; yy++)
                    for (int xx = 0; xx < tw; xx++)
                    {
                        var c = p.px[Mathf.Min(p.h - 1, (int)(yy / s)) * p.w + Mathf.Min(p.w - 1, (int)(xx / s))];
                        float a = c.a / 255f;
                        for (int row = 0; row < 2; row++)
                        {
                            var b = m.GetPixel(x + xx, row * rowH + 10 + yy);
                            m.SetPixel(x + xx, row * rowH + 10 + yy, Color.Lerp(b, new Color(c.r / 255f, c.g / 255f, c.b / 255f), a));
                        }
                    }
                x += tw + 10;
            }
            Directory.CreateDirectory("Logs/shots");
            File.WriteAllBytes(file, m.EncodeToPNG());
            Object.DestroyImmediate(m);
        }
    }
}
