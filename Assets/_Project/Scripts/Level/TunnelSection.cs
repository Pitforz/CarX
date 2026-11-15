using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarX.Level
{
    public enum TunnelProfile
    {
        [InspectorName("Восьмиугольник (короб с фасками)")] Octagon,
        [InspectorName("Короб с бордюрами и мокрым каналом")] BoxWithCurbs,
        [InspectorName("Прямоугольный / квадратный")] Box,
        [InspectorName("Круглая труба")] Round
    }

    /// <summary>Cross-section of a tunnel. Profile space: x = right, y = up, floor centre at (0, 0).</summary>
    [Serializable]
    public class TunnelSection
    {
        [Tooltip("Форма сечения")]
        public TunnelProfile profile = TunnelProfile.Octagon;
        [Tooltip("Ширина проёма внутри, м")]
        [Min(0.8f)] public float width = 3f;
        [Tooltip("Высота проёма внутри, м")]
        [Min(0.8f)] public float height = 2.4f;
        [Tooltip("Фаска в углах восьмиугольника, м (во всех углах, если три поля ниже равны 0)")]
        [Min(0.02f)] public float chamfer = 0.3f;
        [Tooltip("Верхние фаски (у потолка), м. 0 = как «Chamfer»")]
        [Min(0f)] public float chamferTop;
        [Tooltip("Нижние скосы вдоль стен (как низкая скамья): ширина по полу, м. 0 = как «Chamfer»")]
        [Min(0f)] public float bevelWidth;
        [Tooltip("Нижние скосы вдоль стен: высота по стене, м. 0 = как «Chamfer»")]
        [Min(0f)] public float bevelHeight;
        [Tooltip("Ширина бордюра-дорожки с каждой стороны канала, м")]
        [Min(0.1f)] public float curbWidth = 0.45f;
        [Tooltip("Высота бордюра над дном канала, м")]
        [Min(0.03f)] public float curbHeight = 0.15f;
        [Tooltip("Диаметр круглой трубы, м")]
        [Min(0.6f)] public float diameter = 2.2f;
        [Tooltip("Слой ила на дне круглой трубы, м (делает дно ровным)")]
        [Min(0.03f)] public float silt = 0.2f;
        [Tooltip("Количество граней круглой трубы")]
        [Range(8, 48)] public int roundSides = 28;

        public TunnelSection Clone() { return (TunnelSection)MemberwiseClone(); }

        float W { get { return Mathf.Max(0.4f, width * 0.5f); } }
        float R { get { return Mathf.Max(0.3f, diameter * 0.5f); } }
        float SiltDepth { get { return Mathf.Clamp(silt, 0.03f, R * 0.8f); } }

        public float Width { get { return profile == TunnelProfile.Round ? 2f * SiltHalfChord : width; } }

        public float Height
        {
            get { return profile == TunnelProfile.Round ? 2f * R - SiltDepth : Mathf.Max(0.8f, height); }
        }

        float SiltHalfChord
        {
            get { float r = R, d = r - SiltDepth; return Mathf.Sqrt(Mathf.Max(0.01f, r * r - d * d)); }
        }

        /// <summary>Half width of the walkable floor (for dressing).</summary>
        public float FloorHalfWidth
        {
            get
            {
                switch (profile)
                {
                    case TunnelProfile.Octagon: return Mathf.Max(0.2f, W - BevelW);
                    case TunnelProfile.BoxWithCurbs: return Mathf.Max(0.2f, W - CurbW);
                    case TunnelProfile.Round: return SiltHalfChord;
                    default: return W;
                }
            }
        }

        /// <summary>Half of the full opening width (a pipe is widest at mid height, not at the floor).</summary>
        public float HalfSpan { get { return profile == TunnelProfile.Round ? R : W; } }

        /// <summary>Height above this tunnel's floor where a smaller tunnel joining its side wall puts its floor (the curb top in a box with curbs).</summary>
        public float JoinFloor { get { return profile == TunnelProfile.BoxWithCurbs ? CurbH : 0f; } }

        float Chamfer { get { return Mathf.Clamp(chamfer, 0.02f, Mathf.Min(W, Height * 0.5f) * 0.9f); } }
        // separate top chamfers and bottom bevels; 0 = the common chamfer (files saved before these fields existed)
        float TopC { get { return chamferTop > 0.001f ? Mathf.Clamp(chamferTop, 0.02f, Mathf.Min(W, Height * 0.5f) * 0.9f) : Chamfer; } }
        float BevelW { get { return bevelWidth > 0.001f ? Mathf.Clamp(bevelWidth, 0.02f, W * 0.9f) : Chamfer; } }
        float BevelH { get { return bevelHeight > 0.001f ? Mathf.Clamp(bevelHeight, 0.02f, Mathf.Max(0.02f, Height * 0.9f - TopC)) : Chamfer; } }
        float CurbW { get { return Mathf.Clamp(curbWidth, 0.1f, W * 0.8f); } }
        float CurbH { get { return Mathf.Clamp(curbHeight, 0.03f, Height * 0.4f); } }

        public bool IsSmooth { get { return profile == TunnelProfile.Round; } }

        /// <summary>Basic profile, counter-clockwise. kind: 1 = floor edge, 0 = wall/ceiling. ceilingEdge = flat ceiling edge or -1.</summary>
        public List<Vector2> BaseOutline(out List<int> kind, out int ceilingEdge)
        {
            var p = new List<Vector2>();
            kind = new List<int>();
            float w = W, h = Height;
            switch (profile)
            {
                case TunnelProfile.Octagon:
                {
                    float c = TopC, bw = BevelW, bh = BevelH;
                    p.Add(new Vector2(-w + bw, 0)); p.Add(new Vector2(w - bw, 0)); p.Add(new Vector2(w, bh)); p.Add(new Vector2(w, h - c));
                    p.Add(new Vector2(w - c, h)); p.Add(new Vector2(-w + c, h)); p.Add(new Vector2(-w, h - c)); p.Add(new Vector2(-w, bh));
                    kind.AddRange(new[] { 1, 0, 0, 0, 0, 0, 0, 0 });
                    ceilingEdge = 4;
                    break;
                }
                case TunnelProfile.BoxWithCurbs:
                {
                    float cw = CurbW, ch = CurbH;
                    p.Add(new Vector2(-w + cw, 0)); p.Add(new Vector2(w - cw, 0)); p.Add(new Vector2(w - cw, ch)); p.Add(new Vector2(w, ch));
                    p.Add(new Vector2(w, h)); p.Add(new Vector2(-w, h)); p.Add(new Vector2(-w, ch)); p.Add(new Vector2(-w + cw, ch));
                    kind.AddRange(new[] { 1, 0, 1, 0, 0, 0, 1, 0 });
                    ceilingEdge = 4;
                    break;
                }
                case TunnelProfile.Round:
                {
                    float r = R, d = r - SiltDepth, a = SiltHalfChord;
                    var centre = new Vector2(0, d);
                    p.Add(new Vector2(-a, 0)); p.Add(new Vector2(a, 0));
                    kind.Add(1);
                    float aStart = Mathf.Atan2(-d, a);
                    float sweep = 2f * Mathf.PI - 2f * Mathf.Acos(Mathf.Clamp(d / r, -1f, 1f));
                    int n = Mathf.Max(6, Mathf.RoundToInt(roundSides * sweep / (2f * Mathf.PI)));
                    for (int i = 1; i < n; i++)
                    {
                        float ang = aStart + sweep * i / n;
                        p.Add(centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r);
                    }
                    for (int i = 1; i < p.Count; i++) kind.Add(0);
                    ceilingEdge = -1;
                    break;
                }
                default:
                {
                    p.Add(new Vector2(-w, 0)); p.Add(new Vector2(w, 0)); p.Add(new Vector2(w, h)); p.Add(new Vector2(-w, h));
                    kind.AddRange(new[] { 1, 0, 0, 0 });
                    ceilingEdge = 2;
                    break;
                }
            }
            return p;
        }

        /// <summary>Full profile for meshing. Walls are split at dirtLine. edgeSub per edge: 0 concrete, 1 floor, 4 dirty concrete band.</summary>
        public Profile Build(float dirtLine)
        {
            List<int> kind;
            int ceil;
            var basePts = BaseOutline(out kind, out ceil);
            var pts = new List<Vector2>();
            var sub = new List<int>();
            var isCeil = new List<bool>();
            int n = basePts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = basePts[i], b = basePts[(i + 1) % n];
                pts.Add(a);
                if (kind[i] == 1)
                {
                    sub.Add(1); isCeil.Add(false);
                    continue;
                }
                bool crosses = dirtLine > 0.01f && ((a.y < dirtLine - 1e-3f && b.y > dirtLine + 1e-3f) || (b.y < dirtLine - 1e-3f && a.y > dirtLine + 1e-3f));
                if (crosses)
                {
                    float t = (dirtLine - a.y) / (b.y - a.y);
                    var m = Vector2.Lerp(a, b, t);
                    sub.Add(a.y < b.y ? 4 : 0); isCeil.Add(false);
                    pts.Add(m);
                    sub.Add(a.y < b.y ? 0 : 4); isCeil.Add(false);
                }
                else
                {
                    bool low = Mathf.Max(a.y, b.y) <= dirtLine + 1e-3f && dirtLine > 0.01f;
                    sub.Add(low ? 4 : 0);
                    isCeil.Add(i == ceil);
                }
            }
            var prof = new Profile();
            prof.points = pts.ToArray();
            prof.edgeSub = sub.ToArray();
            prof.ceilingEdge = isCeil.IndexOf(true);
            prof.smooth = IsSmooth;
            prof.height = Height;
            prof.Prepare(IsSmooth ? new Vector2(0, R - SiltDepth) : new Vector2(0, Height * 0.5f));
            return prof;
        }

        public bool FlatCeiling(out float xMin, out float xMax, out float y)
        {
            xMin = xMax = y = 0;
            List<int> kind; int ceil;
            var p = BaseOutline(out kind, out ceil);
            if (ceil < 0) return false;
            Vector2 a = p[ceil], b = p[(ceil + 1) % p.Count];
            xMin = Mathf.Min(a.x, b.x); xMax = Mathf.Max(a.x, b.x); y = a.y;
            return true;
        }

        /// <summary>Lowest walkable surface at lateral position x.</summary>
        public float FloorHeightAt(float x)
        {
            List<int> kind; int ceil;
            var p = BaseOutline(out kind, out ceil);
            float best = float.MaxValue;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Count];
                if (Mathf.Abs(a.x - b.x) < 1e-4f) continue;
                if (x < Mathf.Min(a.x, b.x) - 1e-4f || x > Mathf.Max(a.x, b.x) + 1e-4f) continue;
                float y = Mathf.Lerp(a.y, b.y, (x - a.x) / (b.x - a.x));
                if (y < best) best = y;
            }
            return best == float.MaxValue ? 0f : best;
        }

        /// <summary>Ceiling (highest inner surface) at lateral position x.</summary>
        public float CeilingHeightAt(float x)
        {
            List<int> kind; int ceil;
            var p = BaseOutline(out kind, out ceil);
            float best = float.MinValue;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Count];
                if (Mathf.Abs(a.x - b.x) < 1e-4f) continue;
                if (x < Mathf.Min(a.x, b.x) - 1e-4f || x > Mathf.Max(a.x, b.x) + 1e-4f) continue;
                float y = Mathf.Lerp(a.y, b.y, (x - a.x) / (b.x - a.x));
                if (y > best) best = y;
            }
            return best == float.MinValue ? Height : best;
        }

        /// <summary>Corner heights of the profile, bottom to top (where WallXAt changes slope).</summary>
        public List<float> CornerHeights()
        {
            List<int> kind; int ceil;
            var ys = new List<float>();
            foreach (var v in BaseOutline(out kind, out ceil)) ys.Add(v.y);
            ys.Sort();
            return ys;
        }

        /// <summary>Right wall position (max x of the profile) at height y.</summary>
        public float WallXAt(float y)
        {
            List<int> kind; int ceil;
            var p = BaseOutline(out kind, out ceil);
            float best = 0;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Count];
                if (Mathf.Abs(a.y - b.y) < 1e-4f) continue;
                if (y < Mathf.Min(a.y, b.y) - 1e-4f || y > Mathf.Max(a.y, b.y) + 1e-4f) continue;
                float x = Mathf.Lerp(a.x, b.x, (y - a.y) / (b.y - a.y));
                if (x > best) best = x;
            }
            return best;
        }

        public int Hash()
        {
            unchecked
            {
                int h = (int)profile * 7919;
                h = h * 31 + width.GetHashCode();
                h = h * 31 + height.GetHashCode();
                h = h * 31 + chamfer.GetHashCode();
                h = h * 31 + chamferTop.GetHashCode();
                h = h * 31 + bevelWidth.GetHashCode();
                h = h * 31 + bevelHeight.GetHashCode();
                h = h * 31 + curbWidth.GetHashCode();
                h = h * 31 + curbHeight.GetHashCode();
                h = h * 31 + diameter.GetHashCode();
                h = h * 31 + silt.GetHashCode();
                h = h * 31 + roundSides;
                return h;
            }
        }
    }

    /// <summary>Prepared profile polygon with per-vertex helpers.</summary>
    public class Profile
    {
        public Vector2[] points;      // CCW
        public int[] edgeSub;         // per edge i (points[i] -> points[i+1])
        public int ceilingEdge = -1;
        public bool smooth;
        public float height;
        public Vector2 centre;
        public Vector2[] miter;       // outward offset per vertex for unit thickness
        public Vector2[] edgeOut;     // outward normal per edge
        public float[] perim;         // perimeter distance at vertex i (perim[n] = total)
        public Vector2[] smoothNormal;// inward normal per vertex for smooth profiles

        public int Count { get { return points.Length; } }

        public void Prepare(Vector2 c)
        {
            centre = c;
            int n = points.Length;
            edgeOut = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 d = points[(i + 1) % n] - points[i];
                edgeOut[i] = new Vector2(d.y, -d.x).normalized;
            }
            miter = new Vector2[n];
            smoothNormal = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 a = edgeOut[(i + n - 1) % n], b = edgeOut[i];
                float k = 1f + Vector2.Dot(a, b);
                miter[i] = k > 0.05f ? (a + b) / k : (a + b).normalized;
                smoothNormal[i] = -(points[i] - c).normalized;
            }
            perim = new float[n + 1];
            for (int i = 0; i < n; i++) perim[i + 1] = perim[i] + Vector2.Distance(points[i], points[(i + 1) % n]);
        }

        public Vector2 Offset(int i, float amount) { return points[i] + miter[i] * amount; }
        public Vector2 EdgeInward(int e) { return -edgeOut[e]; }
    }
}
