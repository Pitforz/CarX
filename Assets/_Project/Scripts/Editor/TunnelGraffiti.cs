using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>The "Граффити" tool's generator. The graph stores a count and a seed per line / room / chamber.
    /// Decals under the network's "Граффити" child are rebuilt from that, on the left and right walls (and, rarely,
    /// the flat ceiling), from the pictures in Assets/_Project/Graffiti. On an octagon the picture stays on the
    /// vertical face: it may cross a chamfer by a few centimetres, not ride up the slope. The same picture is
    /// unlikely again nearby. Tilt is a narrow gaussian (about 10° is typical, 45° is rare, 90° does not happen).</summary>
    [InitializeOnLoad]
    public static class TunnelGraffiti
    {
        public const string RootName = "Граффити";
        const string PerClickKey = "CarX.GraffitiGraph.perClick";
        const float RollSigma = 12.5f;   // degrees. Mean |tilt| ≈ 10°. 45° is ~3.6σ, 90° is ~7σ.
        const float RepeatNear = 16f;    // metres. A repeat this close is heavily down-weighted.
        const float ChamferSlack = 0.06f; // a decal may lick a chamfer by this much and no more
        const float CeilingChance = 0.03f;
        const int Version = 2;

        static TunnelGraffiti()
        {
            var prev = TunnelNetwork.BuiltHook;
            TunnelNetwork.BuiltHook = net => { if (prev != null) prev(net); Sync(net); };
        }

        public static int PerClick
        {
            get { return Mathf.Clamp(EditorPrefs.GetInt(PerClickKey, 2), 1, 8); }
            set { EditorPrefs.SetInt(PerClickKey, Mathf.Clamp(value, 1, 8)); }
        }

        public static bool Ready { get { return GraffitiLibrary.Images().Count > 0; } }

        public static int NewSeed() { return Random.Range(1, 99999); }

        public static bool CanHold(TunnelNetwork net, GraphNode n) { return TunnelTrash.CanHold(net, n); }

        /// <summary>"+ везде" adds this many pieces to a line: one per 20 m, at least one, times the click step.</summary>
        public static int EverywhereStep(TunnelNetwork net, GraphEdge e, int perClick)
        {
            float len;
            if (!net.EdgeSpan(e, out len)) return 0;
            return perClick * Mathf.Max(1, Mathf.RoundToInt(len / 20f));
        }

        class Spot
        {
            public string owner;
            public int index;
            public int seed;
            public Vector3 pos;
            public Quaternion rot;
            public float size;
            public float roll;
            public bool flip;
            public int image;
            public bool ceiling;
            public float bandA, bandB;     // wall: vertical face y0,y1. ceiling: flat span x0,x1
            public float slackA, slackB;
            public float coord;            // wall: height. tunnel ceiling: lateral x
            public float clearance;        // room/chamber ceiling: distance from the point to the nearest wall
            public Vector3 origin;         // wall: point on the face at height 0. tunnel ceiling: point at x = 0
            public Vector3 slide;          // wall: up. tunnel ceiling: right
        }

        /// <summary>Rebuilds decals that no longer match the graph. With an empty graffiti folder the counts are kept and nothing is deleted.</summary>
        public static void Sync(TunnelNetwork net)
        {
            if (net == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool any = false;
            foreach (var e in net.edges) if (e.graffiti > 0) { any = true; break; }
            if (!any) foreach (var n in net.nodes) if (n.graffiti > 0) { any = true; break; }
            var root = net.transform.Find(RootName);
            if (!any && root == null) return;
            var pics = Pictures();
            if (pics.Count == 0) return;
            net.EnsureLayout();
            if (root == null)
            {
                var go = new GameObject(RootName);
                go.transform.SetParent(net.transform, false);
                root = go.transform;
                if (TunnelNetworkEditor.Schematic) SceneVisibilityManager.instance.Hide(go, true);
            }

            var spots = new List<Spot>();
            foreach (var e in net.edges)
            {
                if (e.graffiti <= 0) continue;
                float len;
                if (!net.EdgeSpan(e, out len)) continue;
                FillEdge(net, e, len, spots);
            }
            foreach (var n in net.nodes)
            {
                if (n.graffiti <= 0 || !CanHold(net, n)) continue;
                FillNode(net, n, spots);
            }
            AssignImages(spots, pics);
            foreach (var s in spots) FitSpot(s, s.image >= 0 && s.image < pics.Count ? pics[s.image] : null);

            string libKey = LibKey(pics);
            var keep = new HashSet<Transform>();
            var byOwner = new Dictionary<string, List<Spot>>();
            foreach (var s in spots)
            {
                List<Spot> list;
                if (!byOwner.TryGetValue(s.owner, out list)) { list = new List<Spot>(); byOwner[s.owner] = list; }
                list.Add(s);
            }
            int rebuilt = 0;
            foreach (var kv in byOwner)
            {
                var g = Group(root, "Graff_" + kv.Key, keep);
                int sig = Hash(Version + "|" + kv.Key + "|" + libKey + "|" + SpotKey(kv.Value));
                if (g.signature == sig && g.transform.childCount > 0) continue;
                Clear(g);
                g.owner = kv.Key;
                g.signature = sig;
                foreach (var s in kv.Value) Spawn(g.transform, s, pics);
                g.items = kv.Value.Count;
                rebuilt++;
            }
            var dead = new List<GameObject>();
            foreach (Transform ch in root) if (!keep.Contains(ch)) dead.Add(ch.gameObject);
            foreach (var d in dead) Object.DestroyImmediate(d);
            if (root.childCount == 0 && !any) { Object.DestroyImmediate(root.gameObject); root = null; }
            if ((rebuilt > 0 || dead.Count > 0) && net.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(net.gameObject.scene);
        }

        public static void Stats(TunnelNetwork net, out int spots, out int places, out int objects)
        {
            spots = places = objects = 0;
            foreach (var e in net.edges) if (e.graffiti > 0) { spots += e.graffiti; places++; }
            foreach (var n in net.nodes) if (n.graffiti > 0) { spots += n.graffiti; places++; }
            var root = net.transform.Find(RootName);
            if (root != null) foreach (var g in root.GetComponentsInChildren<TunnelGraffitiGroup>(true)) objects += g.items;
        }

        // ------------------------------------------------------------------ pictures and weights

        static List<Texture2D> Pictures()
        {
            var list = GraffitiLibrary.Images();
            list.Sort((a, b) => string.Compare(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b), System.StringComparison.Ordinal));
            return list;
        }

        static string LibKey(List<Texture2D> pics)
        {
            var sb = new StringBuilder();
            foreach (var p in pics) sb.Append(AssetDatabase.GetAssetPath(p)).Append(';');
            return sb.ToString();
        }

        /// <summary>Each spot draws a picture with weight (d / (d + Near))², d = distance to the nearest copy already chosen.
        /// A picture that has not been used yet counts as far, so it wins over a repeat on the next wall.</summary>
        static void AssignImages(List<Spot> spots, List<Texture2D> pics)
        {
            var order = new List<int>();
            for (int i = 0; i < spots.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(spots[a].owner, spots[b].owner);
                return c != 0 ? c : spots[a].index.CompareTo(spots[b].index);
            });
            var chosen = new List<int>();
            var placed = new List<Vector3>();
            var w = new float[pics.Count];
            foreach (int si in order)
            {
                var spot = spots[si];
                var rng = new System.Random(Hash(spot.owner + "#img#" + spot.seed + "#" + spot.index));
                float total = 0f;
                for (int i = 0; i < pics.Count; i++)
                {
                    float d = 90f;
                    for (int k = 0; k < chosen.Count; k++)
                        if (chosen[k] == i) d = Mathf.Min(d, Vector3.Distance(placed[k], spot.pos));
                    float t = d / (d + RepeatNear);
                    w[i] = Mathf.Max(0.02f, t * t);
                    total += w[i];
                }
                double pick = rng.NextDouble() * total;
                int img = pics.Count - 1;
                for (int i = 0; i < pics.Count; i++)
                {
                    pick -= w[i];
                    if (pick <= 0.0) { img = i; break; }
                }
                spot.image = img;
                chosen.Add(img);
                placed.Add(spot.pos);
            }
        }

        // ------------------------------------------------------------------ where they go

        static void FillEdge(TunnelNetwork net, GraphEdge e, float len, List<Spot> spots)
        {
            float margin = Mathf.Min(1.6f, len * 0.18f);
            for (int i = 0; i < e.graffiti; i++)
            {
                var r = Rng(e.id, e.graffitiSeed, i);
                bool placed = false;
                bool wantCeil = r.NextDouble() < CeilingChance;
                for (int attempt = 0; attempt < 8 && !placed; attempt++)
                {
                    float u = (float)r.NextDouble();
                    float span = Mathf.Max(0.2f, len - 2f * margin);
                    float d = margin + u * span;
                    Vector3 axis, dir; TunnelSection sec;
                    if (!net.EdgeFrame(e, Mathf.Clamp(d, 0f, len), out axis, out dir, out sec)) break;
                    Vector3 right, up;
                    Frame(dir, out right, out up);
                    bool ceiling = wantCeil && attempt == 0;
                    if (ceiling)
                    {
                        float x0, x1, cy;
                        if (!FlatCeiling(sec, out x0, out x1, out cy) || x1 - x0 < 0.8f) ceiling = false;
                        else
                        {
                            float x = Mathf.Lerp(x0, x1, (float)r.NextDouble());
                            Vector3 pos = axis + right * x + up * cy;
                            if (net.RoomAt(pos, 0.35f) != null || net.ChamberAt(pos, 0.35f) != null) continue;
                            float size = SizeOf(r);
                            if (Crowded(spots, pos, size)) continue;
                            var spot = Make(e.id, i, e.graffitiSeed, pos, -up, dir, size, r);
                            spot.ceiling = true;
                            spot.bandA = x0; spot.bandB = x1;
                            spot.slackA = spot.slackB = ChamferSlack;
                            spot.coord = x;
                            spot.origin = axis + up * cy;
                            spot.slide = right;
                            spots.Add(spot);
                            placed = true;
                            continue;
                        }
                    }
                    float wy0, wy1;
                    if (!VerticalFace(sec, out wy0, out wy1)) continue;
                    int side = r.NextDouble() < 0.5 ? -1 : 1;
                    float y = Mathf.Lerp(wy0, wy1, (float)r.NextDouble());
                    float wall = sec.WallXAt((wy0 + wy1) * 0.5f);
                    if (wall < 0.15f) continue;
                    Vector3 posW = axis + right * (side * wall) + up * y;
                    if (attempt < 7 && (net.RoomAt(posW, 0.35f) != null || net.ChamberAt(posW, 0.35f) != null)) continue;
                    float sizeW = SizeOf(r);
                    if (attempt < 7 && Crowded(spots, posW, sizeW)) continue;
                    var wallSpot = Make(e.id, i, e.graffitiSeed, posW, -side * right, up, sizeW, r);
                    wallSpot.bandA = wy0; wallSpot.bandB = wy1;
                    wallSpot.slackA = wy0 <= 0.08f ? 0.02f : ChamferSlack;
                    wallSpot.slackB = ChamferSlack;
                    wallSpot.coord = y;
                    wallSpot.origin = axis + right * (side * wall);
                    wallSpot.slide = up;
                    spots.Add(wallSpot);
                    placed = true;
                }
            }
        }

        /// <summary>The upright side face: octagon walls between the lower bevel and the upper chamfer, a box's full side, or the steep band of a pipe.</summary>
        static bool VerticalFace(TunnelSection sec, out float y0, out float y1)
        {
            List<int> kind; int ceilEdge;
            var p = sec.BaseOutline(out kind, out ceilEdge);
            y0 = y1 = 0f;
            float bestX = 0f;
            bool found = false;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Count];
                float dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
                if (dy < 0.2f || dx > 0.04f) continue;
                float x = Mathf.Max(Mathf.Abs(a.x), Mathf.Abs(b.x));
                float lo = Mathf.Min(a.y, b.y), hi = Mathf.Max(a.y, b.y);
                if (!found || x > bestX + 0.01f)
                {
                    found = true; bestX = x; y0 = lo; y1 = hi;
                }
            }
            if (found && y1 - y0 > 0.3f) return true;
            float H = sec.Height;
            bool any = false;
            for (int i = 0; i <= 40; i++)
            {
                float y = H * i / 40f;
                float slope = Mathf.Abs(sec.WallXAt(Mathf.Clamp(y + 0.03f, 0f, H)) - sec.WallXAt(Mathf.Clamp(y - 0.03f, 0f, H))) / 0.06f;
                if (slope > 0.28f || y < 0.2f || y > H - 0.2f) continue;
                if (!any) { y0 = y1 = y; any = true; }
                else { y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
            }
            return any && y1 - y0 > 0.25f;
        }

        static bool FlatCeiling(TunnelSection sec, out float x0, out float x1, out float y)
        {
            return sec.FlatCeiling(out x0, out x1, out y) && x1 - x0 > 0.5f;
        }

        struct Wall { public Vector3 a, b, inward; public float length; public int index; }

        static void FillNode(TunnelNetwork net, GraphNode n, List<Spot> spots)
        {
            var walls = new List<Wall>();
            var blocked = new List<Vector2>();   // wall index, along, half — packed as (index, along) plus parallel halves
            var halves = new List<float>();
            float height = 3f;
            TunnelNetwork.RoomInfo room;
            if (net.rooms.TryGetValue(n.id, out room) && room.corners != null && room.corners.Count >= 3)
            {
                height = Mathf.Max(2.2f, room.height);
                var poly = room.corners;
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                    var d = (b - a);
                    float L = d.magnitude;
                    if (L < 0.4f) continue;
                    d /= L;
                    float area = 0f;
                for (int k = 0; k < poly.Count; k++)
                {
                    var u = poly[k]; var v = poly[(k + 1) % poly.Count];
                    area += u.x * v.y - v.x * u.y;
                }
                float side = area < 0f ? -1f : 1f;   // clockwise outline: inside is to the right
                walls.Add(new Wall
                    {
                        a = n.position + new Vector3(a.x, 0f, a.y),
                        b = n.position + new Vector3(b.x, 0f, b.y),
                        inward = side * new Vector3(-d.y, 0f, d.x),
                        length = L,
                        index = i
                    });
                }
                foreach (var door in room.doors)
                {
                    float half = 1.6f;
                    if (door.chain != null && door.chain.type != null)
                        half = door.chain.type.section.HalfSpan * Mathf.Max(0.5f, door.stretch) + 0.45f;
                    blocked.Add(new Vector2(door.wall, door.along));
                    halves.Add(half);
                }
            }
            else
            {
                TunnelNetwork.ChamberInfo ch;
                if (!net.chambers.TryGetValue(n.id, out ch)) return;
                height = Mathf.Max(2.2f, n.chamberHeight);
                var q = Quaternion.Euler(0f, ch.yaw, 0f);
                float hx = n.chamberSize.x * 0.5f, hz = n.chamberSize.y * 0.5f;
                var corners = new[]
                {
                    q * new Vector3(-hx, 0f, -hz), q * new Vector3(hx, 0f, -hz),
                    q * new Vector3(hx, 0f, hz), q * new Vector3(-hx, 0f, hz)
                };
                for (int i = 0; i < 4; i++)
                {
                    Vector3 a = n.position + corners[i], b = n.position + corners[(i + 1) % 4];
                    Vector3 d = b - a;
                    float L = d.magnitude;
                    if (L < 0.4f) continue;
                    d /= L;
                    walls.Add(new Wall { a = a, b = b, inward = new Vector3(-d.z, 0f, d.x), length = L, index = i });
                    blocked.Add(new Vector2(i, L * 0.5f));
                    halves.Add(1.9f);
                }
            }
            if (walls.Count == 0) return;
            List<Vector2> ceilPoly = null;
            if (net.rooms.TryGetValue(n.id, out room) && room.corners != null) ceilPoly = room.corners;
            for (int i = 0; i < n.graffiti; i++)
            {
                var r = Rng(n.id, n.graffitiSeed, i);
                bool placed = false;
                if (r.NextDouble() < CeilingChance)
                    placed = RoomCeiling(net, n, r, height, ceilPoly, spots, i);
                for (int attempt = 0; attempt < 10 && !placed; attempt++)
                {
                    var w = walls[r.Next(walls.Count)];
                    float along = Mathf.Lerp(0.45f, Mathf.Max(0.5f, w.length - 0.45f), (float)r.NextDouble());
                    bool door = false;
                    for (int k = 0; k < blocked.Count; k++)
                        if ((int)blocked[k].x == w.index && Mathf.Abs(along - blocked[k].y) < halves[k] + 0.35f) door = true;
                    if (door && attempt < 9) continue;
                    float yLo = 0.15f, yHi = Mathf.Max(yLo + 0.4f, height - 0.2f);
                    float y = Mathf.Lerp(yLo, yHi, (float)r.NextDouble());
                    Vector3 dir = (w.b - w.a).normalized;
                    Vector3 pos = w.a + dir * along + Vector3.up * y;
                    float size = SizeOf(r);
                    if (attempt < 9 && Crowded(spots, pos, size)) continue;
                    var spot = Make(n.id, i, n.graffitiSeed, pos, w.inward.normalized, Vector3.up, size, r);
                    spot.bandA = 0f; spot.bandB = height;
                    spot.slackA = 0.02f; spot.slackB = ChamferSlack;
                    spot.coord = y;
                    spot.origin = pos - Vector3.up * y;
                    spot.slide = Vector3.up;
                    spots.Add(spot);
                    placed = true;
                }
            }
        }

        static bool RoomCeiling(TunnelNetwork net, GraphNode n, System.Random r, float height, List<Vector2> poly, List<Spot> spots, int index)
        {
            if (poly == null)
            {
                TunnelNetwork.ChamberInfo ch;
                if (!net.chambers.TryGetValue(n.id, out ch)) return false;
                var q = Quaternion.Euler(0f, ch.yaw, 0f);
                float hx = n.chamberSize.x * 0.5f, hz = n.chamberSize.y * 0.5f;
                poly = new List<Vector2>();
                foreach (var c in new[] { new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz) })
                {
                    var w = q * c;
                    poly.Add(new Vector2(w.x, w.z));
                }
            }
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var v in poly) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
            for (int k = 0; k < 8; k++)
            {
                var v = new Vector2(Mathf.Lerp(min.x, max.x, (float)r.NextDouble()), Mathf.Lerp(min.y, max.y, (float)r.NextDouble()));
                if (!InsidePoly(poly, v)) continue;
                float clear = EdgeDist(poly, v);
                if (clear < 0.45f) continue;
                Vector3 pos = n.position + new Vector3(v.x, height, v.y);
                float size = SizeOf(r);
                if (Crowded(spots, pos, size)) continue;
                var spot = Make(n.id, index, n.graffitiSeed, pos, Vector3.down, Vector3.forward, size, r);
                spot.ceiling = true;
                spot.clearance = clear;
                spots.Add(spot);
                return true;
            }
            return false;
        }

        static bool InsidePoly(List<Vector2> p, Vector2 v)
        {
            bool inside = false;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            {
                if ((p[i].y > v.y) == (p[j].y > v.y)) continue;
                float x = (p[j].x - p[i].x) * (v.y - p[i].y) / (p[j].y - p[i].y) + p[i].x;
                if (v.x < x) inside = !inside;
            }
            return inside;
        }

        static float EdgeDist(List<Vector2> p, Vector2 v)
        {
            float d = float.MaxValue;
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Count], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(v - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                d = Mathf.Min(d, Vector2.Distance(v, a + ab * t));
            }
            return d;
        }

        static Spot Make(string owner, int index, int seed, Vector3 pos, Vector3 normal, Vector3 pictureUp, float size, System.Random r)
        {
            float roll = Gauss(r) * RollSigma;
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            normal.Normalize();
            Vector3 up = Vector3.ProjectOnPlane(pictureUp, normal);
            if (up.sqrMagnitude < 0.02f) up = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            return new Spot
            {
                owner = owner,
                index = index,
                seed = seed,
                pos = pos,
                roll = roll,
                rot = Quaternion.LookRotation(-normal, up.normalized) * Quaternion.Euler(0f, 0f, roll),
                size = size,
                flip = r.NextDouble() < 0.5,
                image = -1
            };
        }

        /// <summary>Shrinks and shifts a decal so it stays on the flat face. A few centimetres may still cross onto a chamfer.</summary>
        static void FitSpot(Spot s, Texture2D img)
        {
            float aspect = img != null && img.width > 0 ? (float)img.height / img.width : 0.55f;
            float sr = Mathf.Abs(Mathf.Sin(s.roll * Mathf.Deg2Rad));
            float cr = Mathf.Abs(Mathf.Cos(s.roll * Mathf.Deg2Rad));
            float Extent(float size)
            {
                float halfW = size * 0.5f, halfH = halfW * aspect;
                return s.ceiling ? halfW * cr + halfH * sr : halfW * sr + halfH * cr;
            }
            if (s.ceiling && s.clearance > 0f)
            {
                float allow = s.clearance + ChamferSlack;
                float ext = Extent(s.size);
                if (ext > allow && ext > 0.01f) s.size *= allow / ext;
                return;
            }
            if (s.slide.sqrMagnitude < 1e-6f) return;
            float lo = s.bandA - s.slackA, hi = s.bandB + s.slackB;
            if (hi - lo < 0.2f) return;
            float extNow = Extent(s.size);
            if (extNow * 2f > hi - lo && extNow > 0.01f)
            {
                s.size *= (hi - lo) / (extNow * 2f);
                extNow = Extent(s.size);
            }
            float c = Mathf.Clamp(s.coord, lo + extNow, hi - extNow);
            s.coord = c;
            s.pos = s.origin + s.slide * c;
        }

        static float SizeOf(System.Random r)
        {
            return Mathf.Lerp(0.7f, 2.2f, Mathf.Pow((float)r.NextDouble(), 1.35f));
        }

        static bool Crowded(List<Spot> spots, Vector3 pos, float size)
        {
            foreach (var s in spots)
            {
                float need = (s.size + size) * 0.42f;
                if ((s.pos - pos).sqrMagnitude < need * need) return true;
            }
            return false;
        }

        static void Spawn(Transform parent, Spot s, List<Texture2D> pics)
        {
            if (s.image < 0 || s.image >= pics.Count) return;
            var img = pics[s.image];
            float size = s.size;
            if (img != null && img.width > 0)
            {
                float h = size * img.height / img.width;
                if (h > 2.4f) size *= 2.4f / h;
            }
            var go = new GameObject(img != null ? "Граффити " + img.name : "Граффити");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = s.pos;
            go.transform.localRotation = s.rot;
            go.transform.localScale = Vector3.one;
            go.AddComponent<UnityEngine.Rendering.Universal.DecalProjector>();
            var g = go.AddComponent<GraffitiDecal>();
            g.image = img;
            g.size = size;
            g.opacity = 1f;
            g.wear = 0f;
            g.flip = s.flip;
            g.depth = Mathf.Clamp(size * 0.25f, 0.3f, 1f);
            g.Refresh();
            // paint layer only as thick as the wall under it needs: no stripes on the faces of a side branch or a corner
            float fit;
            if (GraffitiWindow.FitDepth(g, out fit)) { g.depth = fit; g.Refresh(); }
        }

        static void Frame(Vector3 dir, out Vector3 right, out Vector3 up)
        {
            right = Vector3.Cross(Vector3.up, dir);
            right = right.sqrMagnitude < 1e-6f ? Vector3.right : right.normalized;
            up = Vector3.Cross(dir, right).normalized;
        }

        static float Gauss(System.Random r)
        {
            double u1 = 1.0 - r.NextDouble();
            double u2 = 1.0 - r.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Sin(2.0 * System.Math.PI * u2));
        }

        static System.Random Rng(string owner, int seed, int spot) { return new System.Random(Hash(owner + "#" + seed + "#" + spot)); }

        static string F(float v) { return Mathf.RoundToInt(v * 1000f).ToString(CultureInfo.InvariantCulture); }

        static string SpotKey(List<Spot> list)
        {
            var sb = new StringBuilder();
            foreach (var s in list)
                sb.Append(s.index).Append(':').Append(F(s.pos.x)).Append(',').Append(F(s.pos.y)).Append(',').Append(F(s.pos.z))
                  .Append(':').Append(F(s.size)).Append(':').Append(s.image).Append(':').Append(s.flip ? 'f' : 'n')
                  .Append(':').Append(F(s.rot.x)).Append(',').Append(F(s.rot.y)).Append(',').Append(F(s.rot.z)).Append(',').Append(F(s.rot.w)).Append('|');
            return sb.ToString();
        }

        static int Hash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) { h ^= c; h *= 16777619; }
                return (int)h;
            }
        }

        static TunnelGraffitiGroup Group(Transform root, string name, HashSet<Transform> keep)
        {
            var t = root.Find(name);
            if (t == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                t = go.transform;
            }
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            keep.Add(t);
            var g = t.GetComponent<TunnelGraffitiGroup>();
            if (g == null) g = t.gameObject.AddComponent<TunnelGraffitiGroup>();
            return g;
        }

        static void Clear(TunnelGraffitiGroup g)
        {
            for (int i = g.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(g.transform.GetChild(i).gameObject);
            g.items = 0;
        }
    }
}
