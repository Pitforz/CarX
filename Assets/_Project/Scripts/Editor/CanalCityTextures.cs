using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>Textures for the city layer: casino pylon faces, a lawyer billboard and tower facades. Names and numbers are fictional.</summary>
    public static partial class CanalTextures
    {
        static readonly Color Haze = new Color(0.80f, 0.85f, 0.92f);

        public static Texture2D CasinoSign(bool force)
        {
            string path = TexDir + "/City_CasinoSign.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null && !force) return t;
            const int W = 1024, H = 640;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = (float)y / H;
                    Color c = Color.Lerp(new Color(0.95f, 0.42f, 0.08f), new Color(0.55f, 0.03f, 0.20f), v);
                    int bx = Mathf.Min(x, W - 1 - x), by = Mathf.Min(y, H - 1 - y), b = Mathf.Min(bx, by);
                    if (b < 26) c = new Color(0.95f, 0.74f, 0.22f);
                    if (b >= 34 && b < 40) c = new Color(0.25f, 0.85f, 0.95f);
                    // bulbs along the gold frame
                    float along = bx < by ? y : x;
                    if (b < 26 && Mathf.Abs(b - 13) < 8 && Mathf.Abs(Mathf.Repeat(along, 34f) - 17f) < 8f) c = new Color(1f, 0.97f, 0.86f);
                    px[y * W + x] = c;
                }
            Text(px, W, H, "CARIOCA", W / 2 + 7, 400 - 7, 21, new Color(0.25f, 0.02f, 0.08f));
            Text(px, W, H, "CARIOCA", W / 2, 400, 21, new Color(1f, 0.96f, 0.72f));
            Text(px, W, H, "ALL SUITE HOTEL", W / 2, 222, 8, Color.white);
            Text(px, W, H, "CASINO", W / 2, 118, 11, new Color(0.30f, 0.92f, 1f));
            return Save(path, W, H, px, false);
        }

        public static Texture2D PaybackSign(bool force)
        {
            string path = TexDir + "/City_PaybackSign.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null && !force) return t;
            const int W = 1024, H = 576;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    px[y * W + x] = (x % 6 < 4 && y % 6 < 4) ? new Color(0.06f, 0.06f, 0.07f) : new Color(0.02f, 0.02f, 0.025f);
            Text(px, W, H, "99%", W / 2, 370, 30, new Color(1f, 0.84f, 0.10f));
            Text(px, W, H, "PAYBACK", W / 2, 165, 14, new Color(1f, 0.22f, 0.15f));
            Text(px, W, H, "SLOTS", W / 2, 58, 9, Color.white);
            return Save(path, W, H, px, false);
        }

        public static Texture2D LawyerAd(bool force)
        {
            string path = TexDir + "/City_LawyerAd.png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null && !force) return t;
            const int W = 1536, H = 452, split = 470;
            var px = new Color[W * H];
            var navy = new Color(0.07f, 0.12f, 0.30f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Color c = x < split ? navy : Color.Lerp(new Color(1f, 0.80f, 0.10f), new Color(1f, 0.92f, 0.35f), (float)y / H);
                    if (x >= split && x < split + 10) c = Color.white;
                    // the lawyer: suit, shirt, tie, head
                    float dx = x - 235f;
                    float shoulderW = Mathf.Lerp(190f, 120f, Mathf.Clamp01((y - 40f) / 190f));
                    if (y < 230 && Mathf.Abs(dx) < shoulderW) c = new Color(0.14f, 0.14f, 0.17f);
                    if (y < 230 && y > 60 && Mathf.Abs(dx) < (y - 60f) * 0.32f) c = Color.white;
                    if (y < 215 && y > 70 && Mathf.Abs(dx) < 12f + (215f - y) * 0.04f) c = new Color(0.75f, 0.08f, 0.10f);
                    float hy = y - 315f;
                    if (dx * dx / (66f * 66f) + hy * hy / (82f * 82f) < 1f) c = hy > 38f ? new Color(0.22f, 0.15f, 0.10f) : new Color(0.86f, 0.66f, 0.52f);
                    if (y > 228 && y < 250 && Mathf.Abs(dx) < 34f) c = new Color(0.86f, 0.66f, 0.52f);
                    px[y * W + x] = c;
                }
            int mid = (split + W) / 2;
            Text(px, W, H, "HURT IN A CRASH?", mid, 372, 9, navy);
            Text(px, W, H, "CALL SAM", mid + 6, 205 - 6, 20, navy);
            Text(px, W, H, "CALL SAM", mid, 205, 20, new Color(0.80f, 0.05f, 0.05f));
            Text(px, W, H, "702-555-0147", mid, 52, 9, navy);
            Text(px, W, H, "MORDECAI INJURY LAW", split / 2, 24, 4, Color.white);
            return Save(path, W, H, px, false);
        }

        /// <summary>Facade tile = 6.4 m wide x 12.8 m tall (4 floors), already blended with distance haze.</summary>
        public static Texture2D Facade(string style, bool force)
        {
            string path = TexDir + "/City_Facade_" + style + ".png";
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null && !force) return t;
            const int W = 128, H = 256;
            Color g0, g1, span, mull, band0 = Color.clear, band1 = Color.clear;
            bool punched = false;
            switch (style)
            {
                case "Carnival": g0 = new Color(0.14f, 0.28f, 0.58f); g1 = new Color(0.32f, 0.52f, 0.82f); span = new Color(0.10f, 0.18f, 0.40f); mull = new Color(0.75f, 0.78f, 0.82f);
                    band0 = new Color(0.62f, 0.08f, 0.12f); band1 = new Color(0.86f, 0.24f, 0.24f); break;
                case "Gold": g0 = new Color(0.50f, 0.36f, 0.12f); g1 = new Color(0.88f, 0.70f, 0.36f); span = new Color(0.46f, 0.35f, 0.16f); mull = new Color(0.90f, 0.80f, 0.55f); break;
                case "White": g0 = new Color(0.18f, 0.24f, 0.32f); g1 = new Color(0.34f, 0.42f, 0.52f); span = new Color(0.88f, 0.87f, 0.84f); mull = span; punched = true; break;
                default: g0 = new Color(0.09f, 0.12f, 0.16f); g1 = new Color(0.26f, 0.31f, 0.37f); span = new Color(0.08f, 0.10f, 0.12f); mull = new Color(0.36f, 0.38f, 0.41f); break;
            }
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int floor = y / 64, fy = y % 64, pane = x / 32, mx = x % 32;
                    float h = Hash(pane * 7 + floor * 13 + style.Length);
                    float k = fy / 64f;
                    Color glass = floor == 3 && band0.a > 0f ? Color.Lerp(band0, band1, k) : Color.Lerp(g0, g1, k);
                    glass *= 0.9f + h * 0.2f;
                    if (h > 0.82f) glass = Color.Lerp(glass, new Color(0.85f, 0.82f, 0.75f), 0.45f); // drawn curtains
                    Color c;
                    if (punched) c = (mx >= 5 && mx < 28 && fy >= 16 && fy < 56) ? glass : span;
                    else c = fy < 11 ? span : (mx < 2 ? mull : glass);
                    px[y * W + x] = Color.Lerp(c, Haze, 0.32f);
                }
            return Save(path, W, H, px, false);
        }

        static float Hash(int n)
        {
            unchecked { n = (n << 13) ^ n; return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f; }
        }

        // ------------------------------------------------------------------ 5x7 block font

        static Dictionary<char, string> font;

        static void Text(Color[] px, int W, int H, string s, int cx, int cy, int scale, Color col)
        {
            if (font == null) font = MakeFont();
            int width = s.Length * 6 * scale - scale, x0 = cx - width / 2, y0 = cy - 7 * scale / 2;
            for (int i = 0; i < s.Length; i++)
            {
                string g;
                if (!font.TryGetValue(char.ToUpperInvariant(s[i]), out g)) continue;
                for (int r = 0; r < 7; r++)
                    for (int col5 = 0; col5 < 5; col5++)
                    {
                        if (g[r * 5 + col5] != '1') continue;
                        int gx = x0 + (i * 6 + col5) * scale, gy = y0 + (6 - r) * scale;
                        for (int yy = 0; yy < scale; yy++)
                            for (int xx = 0; xx < scale; xx++)
                            {
                                int X = gx + xx, Y = gy + yy;
                                if (X >= 0 && X < W && Y >= 0 && Y < H) px[Y * W + X] = col;
                            }
                    }
            }
        }

        static Dictionary<char, string> MakeFont()
        {
            var f = new Dictionary<char, string>();
            System.Action<char, string> A = (ch, rows) => f[ch] = rows.Replace(" ", "");
            A('A', "01110 10001 10001 11111 10001 10001 10001"); A('B', "11110 10001 10001 11110 10001 10001 11110");
            A('C', "01110 10001 10000 10000 10000 10001 01110"); A('D', "11110 10001 10001 10001 10001 10001 11110");
            A('E', "11111 10000 10000 11110 10000 10000 11111"); A('F', "11111 10000 10000 11110 10000 10000 10000");
            A('G', "01110 10001 10000 10111 10001 10001 01111"); A('H', "10001 10001 10001 11111 10001 10001 10001");
            A('I', "01110 00100 00100 00100 00100 00100 01110"); A('J', "00111 00010 00010 00010 00010 10010 01100");
            A('K', "10001 10010 10100 11000 10100 10010 10001"); A('L', "10000 10000 10000 10000 10000 10000 11111");
            A('M', "10001 11011 10101 10101 10001 10001 10001"); A('N', "10001 10001 11001 10101 10011 10001 10001");
            A('O', "01110 10001 10001 10001 10001 10001 01110"); A('P', "11110 10001 10001 11110 10000 10000 10000");
            A('Q', "01110 10001 10001 10001 10101 10010 01101"); A('R', "11110 10001 10001 11110 10100 10010 10001");
            A('S', "01111 10000 10000 01110 00001 00001 11110"); A('T', "11111 00100 00100 00100 00100 00100 00100");
            A('U', "10001 10001 10001 10001 10001 10001 01110"); A('V', "10001 10001 10001 10001 10001 01010 00100");
            A('W', "10001 10001 10001 10101 10101 10101 01010"); A('X', "10001 10001 01010 00100 01010 10001 10001");
            A('Y', "10001 10001 01010 00100 00100 00100 00100"); A('Z', "11111 00001 00010 00100 01000 10000 11111");
            A('0', "01110 10001 10011 10101 11001 10001 01110"); A('1', "00100 01100 00100 00100 00100 00100 01110");
            A('2', "01110 10001 00001 00010 00100 01000 11111"); A('3', "11111 00010 00100 00010 00001 10001 01110");
            A('4', "00010 00110 01010 10010 11111 00010 00010"); A('5', "11111 10000 11110 00001 00001 10001 01110");
            A('6', "00110 01000 10000 11110 10001 10001 01110"); A('7', "11111 00001 00010 00100 01000 01000 01000");
            A('8', "01110 10001 10001 01110 10001 10001 01110"); A('9', "01110 10001 10001 01111 00001 00010 01100");
            A('%', "11000 11001 00010 00100 01000 10011 00011"); A('-', "00000 00000 00000 11111 00000 00000 00000");
            A('?', "01110 10001 00001 00010 00100 00000 00100"); A('!', "00100 00100 00100 00100 00100 00000 00100");
            A('.', "00000 00000 00000 00000 00000 01100 01100"); A('$', "00100 01111 10100 01110 00101 11110 00100");
            A(' ', "00000 00000 00000 00000 00000 00000 00000");
            return f;
        }
    }
}
