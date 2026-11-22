using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarX.Level
{
    public enum GraphNodeKind
    {
        [InspectorName("Авто")] Auto,
        [InspectorName("Камера")] Chamber,
        [InspectorName("Комната")] Room,
        [InspectorName("Выход наружу")] Exit       // new kinds go to the end: Unity stores this as a number
    }

    [Serializable]
    public class GraphNode
    {
        public string id;
        [Tooltip("Положение узла (пол тоннеля), локально относительно графа")]
        public Vector3 position;
        [Tooltip("Авто: тоннели врезаются друг в друга напрямую (поменьше в стену главного). Камера: в узле бетонная камера-развязка")]
        public GraphNodeKind kind = GraphNodeKind.Auto;
        [Tooltip("Радиус скругления этого угла, м. 0 = минимально допустимый для типа")]
        [Min(0f)] public float cornerRadius;
        [Tooltip("Врезка или переход в этом узле: подъём меньшего тоннеля над полом большего, м")]
        [Min(0f)] public float joinLift;

        [Header("Камера (если в узле камера)")]
        public Vector2 chamberSize = new Vector2(4.8f, 4.8f);
        [Min(2f)] public float chamberHeight = 3.3f;
        [Tooltip("Повернуть камеру по первой линии автоматически")]
        public bool autoYaw = true;
        public float yaw;
        public bool chamberShaft;
        public ShaftSpec shaft = ShaftSpec.Preset(ShaftType.GrateSquare, 0f);

        [Header("Комната (если узел - многоугольная комната)")]
        [Tooltip("Углы комнаты относительно узла (X, Z)")]
        public List<Vector2> roomCorners = new List<Vector2>();
        [Tooltip("Высота потолка, м. 0 = по самому высокому из подходящих тоннелей")]
        [Min(0f)] public float roomHeight;
        [Tooltip("Бетонные колонны в комнате (позиция относительно узла)")]
        public List<ColumnSpec> columns = new List<ColumnSpec>();
        public int seed = 1;

        [Header("Выход наружу (если узел - выход)")]
        [Tooltip("Яркость дневного света в этом выходе (1 = как в настройках библиотеки типов)")]
        [Range(0f, 4f)] public float exitBrightness = 1f;
        [Tooltip("Снаружи настоящая сцена (например канал CanalEntrance): не рисовать за проёмом светящуюся коробку «неба»")]
        public bool exitOpenAir;

        [Header("Мусор (инструмент «Мусор»; для комнат и камер)")]
        [Tooltip("Сколько точек с мусором в этой комнате или камере")]
        [Min(0)] public int trash;
        [Tooltip("Сид мусора: другой сид - мусор лежит по-другому")]
        public int trashSeed;

        /// <summary>Default plan of a new room: an octagon about 10 m across.</summary>
        public static List<Vector2> DefaultRoom()
        {
            var l = new List<Vector2>();
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) * Mathf.PI / 4f;
                l.Add(new Vector2(Mathf.Round(Mathf.Cos(a) * 10f) * 0.5f, Mathf.Round(Mathf.Sin(a) * 10f) * 0.5f));
            }
            return l;
        }
    }

    [Serializable]
    public class GraphEdge
    {
        public string id;
        public string a, b;
        public TunnelType type;
        public int seed = 1;
        [Tooltip("Подъёмы наверх на этом отрезке. Distance = метры от узла A")]
        public List<ShaftSpec> shafts = new List<ShaftSpec>();
        [Tooltip("Сколько точек с мусором на этом отрезке (инструмент «Мусор»)")]
        [Min(0)] public int trash;
        [Tooltip("Сид мусора: другой сид - мусор лежит по-другому")]
        public int trashSeed;
    }

    public struct GraphIssue
    {
        public string nodeId, edgeId;
        public Vector3 position;   // local
        public string message;
    }

    /// <summary>
    /// Tunnel graph: nodes + edges are the source of truth, tunnels and chambers under "_Generated" are built from them.
    /// Corners are rounded with the smallest radius the tunnel type allows, so no joint between slabs bends
    /// more than its maxJointAngle. Corners that do not fit become chambers.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("CarX/Tunnel Network")]
    public class TunnelNetwork : MonoBehaviour
    {
        public TunnelTypeLibrary library;
        [Tooltip("Раскидывать лужи по новым тоннелям")]
        public bool puddles = true;
        public List<GraphNode> nodes = new List<GraphNode>();
        public List<GraphEdge> edges = new List<GraphEdge>();

        public const string GeneratedRoot = "_Generated";
        /// <summary>Extra rendering layer on all tunnel geometry (Default stays on): lets an open-air exit light take shadows
        /// from the tunnel walls only, so people standing in the mouth do not throw long shadows into the tunnel.</summary>
        public const uint TunnelRenderingLayer = 1u << 4;
        public const float MaxTunnelLength = 3000f;
        /// <summary>Editor hook: scatter puddles on a rebuilt tunnel.</summary>
        public static Action<TunnelPath, int> DressingHook;
        /// <summary>Editor: runs after every build (the trash tool regenerates what changed).</summary>
        public static Action<TunnelNetwork> BuiltHook;
        /// <summary>Editor sets this in schematic mode: lay out only, build meshes later.</summary>
        public static bool deferBuild;

        public class Chain
        {
            public readonly List<string> nodes = new List<string>();
            public readonly List<GraphEdge> edges = new List<GraphEdge>();
            public TunnelType type;
            public List<Vector3> samples = new List<Vector3>();
            public float[] nodeDist;
            public float total;
            public int startSide = -1, endSide = -1;   // chamber side at each end, -1 = no chamber
            public string startJoin, endJoin;          // this end enters the side wall of a main tunnel at that node
            public List<Vector2> holeStart, holeEnd;   // end wall with an opening (type change without a chamber)
            public readonly List<Vector3> elbows = new List<Vector3>();   // sharp corners built as mitred elbows
            public RoomDoorInfo startDoor, endDoor;    // this end opens into a polygonal room
            /// <summary>This end enters the side wall of a bigger tunnel: the opening (own profile coordinates, cut at the bigger tunnel's ceiling)
            /// and how far the walls go on past the end at each height to meet the bigger tunnel's wall (x = height, y = depth; empty = flush).</summary>
            public List<Vector2> openStart, openEnd, revealStart, revealEnd;
            public readonly List<SideOpening> openings = new List<SideOpening>();
            public string key;
        }

        /// <summary>Node where tunnels meet without a chamber: the main tunnel passes through, the rest enter its side walls.</summary>
        public class JoinInfo
        {
            public GraphNode node;
            public GraphEdge e1, e2;
            public Vector3 tangent;
            public TunnelType mainType;
            /// <summary>Type change end to end: e1 (bigger type) ends with a wall, the other tunnel goes on through an opening in it.</summary>
            public bool reducer, fits;
            public float y0;                 // floor of the other tunnel above the main floor
            public List<Vector2> hole;       // opening in the main section's coordinates
            /// <summary>Sharp turn to another type: the other tunnel enters the side wall this far back from the main tunnel's end.</summary>
            public float back;
        }

        public class RoomDoorInfo
        {
            public Chain chain;
            public int end, wall;
            public float along, stretch = 1f;     // stretch > 1: the tunnel meets the wall at an angle
            public Vector3 point, outward, wallNormal;
        }

        public class RoomInfo
        {
            public GraphNode node;
            public List<Vector2> corners;
            public float height;
            public readonly List<RoomDoorInfo> doors = new List<RoomDoorInfo>();
        }

        public class ChamberInfo
        {
            public GraphNode node;
            public float yaw;
            public readonly Chain[] sideChains = new Chain[4];
            public readonly GraphEdge[] sideEdges = new GraphEdge[4];
        }

        [NonSerialized] public readonly List<Chain> chains = new List<Chain>();
        [NonSerialized] public readonly Dictionary<string, ChamberInfo> chambers = new Dictionary<string, ChamberInfo>();
        [NonSerialized] public readonly List<GraphIssue> issues = new List<GraphIssue>();
        [NonSerialized] public readonly Dictionary<string, JoinInfo> joins = new Dictionary<string, JoinInfo>();
        [NonSerialized] bool layoutDirty = true, buildDirty = true;
        [NonSerialized] readonly Dictionary<string, GraphNode> nodeMap = new Dictionary<string, GraphNode>();
        [NonSerialized] readonly Dictionary<string, List<GraphEdge>> adj = new Dictionary<string, List<GraphEdge>>();
        [NonSerialized] readonly HashSet<string> forced = new HashSet<string>();
        [NonSerialized] readonly HashSet<string> chamberSet = new HashSet<string>();
        [NonSerialized] readonly HashSet<string> roomSet = new HashSet<string>();
        [NonSerialized] public readonly Dictionary<string, RoomInfo> rooms = new Dictionary<string, RoomInfo>();
        [NonSerialized] readonly Dictionary<GraphEdge, Chain> edgeChain = new Dictionary<GraphEdge, Chain>();
        [NonSerialized] readonly HashSet<Chain> laid = new HashSet<Chain>();

        // ------------------------------------------------------------------ lifecycle

        void OnEnable()
        {
            MarkDirty();
            if (library != null) { TunnelTypeLibrary.Active = library; library.ApplyAtmosphere(); }
        }

        void OnValidate()
        {
            MarkDirty();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void Update()
        {
            if (Application.isPlaying) return;   // the level is already built; play mode never rebuilds it
            try
            {
                if (layoutDirty) Layout();
                if (buildDirty && !deferBuild) Build();
            }
            catch (Exception e)
            {
                layoutDirty = buildDirty = false;   // do not retry every frame
                Debug.LogException(e, this);
            }
        }

        public static void MarkAllDirty()
        {
            foreach (var n in FindObjectsByType<TunnelNetwork>(FindObjectsInactive.Exclude)) n.MarkDirty();
        }

        public void MarkDirty()
        {
            layoutDirty = true;
            buildDirty = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        public bool BuildPending { get { return buildDirty; } }
        public void EnsureLayout() { if (layoutDirty) Layout(); }

        // ------------------------------------------------------------------ graph helpers

        public static string NewId(string prefix) { return prefix + Guid.NewGuid().ToString("N").Substring(0, 8); }

        public GraphNode Node(string id)
        {
            if (id == null) return null;
            foreach (var n in nodes) if (n.id == id) return n;
            return null;
        }

        public GraphEdge Edge(string id)
        {
            if (id == null) return null;
            foreach (var e in edges) if (e.id == id) return e;
            return null;
        }

        public List<GraphEdge> EdgesOf(string nodeId)
        {
            var list = new List<GraphEdge>();
            foreach (var e in edges) if (e.a == nodeId || e.b == nodeId) list.Add(e);
            return list;
        }

        public static string Other(GraphEdge e, string id) { return e.a == id ? e.b : e.a; }

        public GraphNode AddNode(Vector3 localPos)
        {
            var n = new GraphNode { id = NewId("n"), position = localPos, seed = UnityEngine.Random.Range(1, 99999) };
            nodes.Add(n);
            MarkDirty();
            return n;
        }

        public GraphEdge AddEdge(string a, string b, TunnelType type)
        {
            if (a == b) return null;
            foreach (var e in edges) if ((e.a == a && e.b == b) || (e.a == b && e.b == a)) return e;
            var ne = new GraphEdge { id = NewId("e"), a = a, b = b, type = type, seed = UnityEngine.Random.Range(1, 99999) };
            edges.Add(ne);
            MarkDirty();
            return ne;
        }

        /// <summary>Inserts a node into an edge; shafts stay where they were.</summary>
        public GraphNode SplitEdge(GraphEdge e, Vector3 localPos)
        {
            var A = Node(e.a); var B = Node(e.b);
            if (A == null || B == null) return null;
            float len = Vector3.Distance(A.position, B.position);
            float t = len > 1e-4f ? Mathf.Clamp01(Vector3.Dot(localPos - A.position, B.position - A.position) / (len * len)) : 0.5f;
            var p = Vector3.Lerp(A.position, B.position, t);
            var n = new GraphNode { id = NewId("n"), position = p, seed = UnityEngine.Random.Range(1, 99999) };
            nodes.Add(n);
            var e2 = new GraphEdge { id = e.id + "_" + NewId("").Substring(0, 4), a = n.id, b = e.b, type = e.type, seed = e.seed + 1 };
            float cut = len * t;
            for (int i = e.shafts.Count - 1; i >= 0; i--)
            {
                if (e.shafts[i].distance > cut)
                {
                    var s = e.shafts[i];
                    s.distance -= cut;
                    e2.shafts.Add(s);
                    e.shafts.RemoveAt(i);
                }
            }
            // trash spots are shared by length, each half keeps its own seed
            int moved = Mathf.RoundToInt(e.trash * (1f - t));
            e2.trash = moved;
            e2.trashSeed = e.trashSeed + 1;
            e.trash -= moved;
            e.b = n.id;
            edges.Add(e2);
            MarkDirty();
            return n;
        }

        public void RemoveNode(string id)
        {
            edges.RemoveAll(e => e.a == id || e.b == id);
            nodes.RemoveAll(n => n.id == id);
            MarkDirty();
        }

        public void RemoveEdge(string id)
        {
            edges.RemoveAll(e => e.id == id);
            MarkDirty();
        }

        public Vector3 World(Vector3 local) { return transform.TransformPoint(local); }
        public Vector3 Local(Vector3 world) { return transform.InverseTransformPoint(world); }

        public TunnelType TypeOf(GraphEdge e)
        {
            if (e.type != null) return e.type;
            return library != null ? library.Default : null;
        }

        public bool IsChamber(string nodeId) { EnsureLayout(); return chamberSet.Contains(nodeId); }
        public bool IsRoom(string nodeId) { EnsureLayout(); return roomSet.Contains(nodeId); }

        public Chain ChainOf(GraphEdge e)
        {
            EnsureLayout();
            Chain c;
            return e != null && edgeChain.TryGetValue(e, out c) ? c : null;
        }

        // ------------------------------------------------------------------ anchors (props, NPCs, decals)

        /// <summary>Raised after every layout: anchored objects follow the tunnels.</summary>
        public static event Action<TunnelNetwork> LaidOut;

        Chain EdgeInChain(GraphEdge e, out int j, out bool forward)
        {
            j = -1;
            forward = true;
            var c = ChainOf(e);
            if (c == null || c.type == null || c.nodeDist == null || c.samples.Count < 2) return null;
            j = c.edges.IndexOf(e);
            if (j < 0 || j + 1 >= c.nodeDist.Length) return null;
            forward = e.a == c.nodes[j];
            return c;
        }

        /// <summary>Length of a line measured along its tunnel, node to node.</summary>
        public bool EdgeSpan(GraphEdge e, out float length)
        {
            length = 0f;
            int j; bool fwd;
            var c = EdgeInChain(e, out j, out fwd);
            if (c == null) return false;
            length = Mathf.Abs(c.nodeDist[j + 1] - c.nodeDist[j]);
            return true;
        }

        /// <summary>Tunnel axis on the floor and the direction from node A to node B, at a distance from A along line e.</summary>
        public bool EdgeFrame(GraphEdge e, float distFromA, out Vector3 axis, out Vector3 dirAB, out TunnelSection section)
        {
            axis = dirAB = Vector3.zero;
            section = null;
            int j; bool fwd;
            var c = EdgeInChain(e, out j, out fwd);
            if (c == null) return false;
            float dA = fwd ? c.nodeDist[j] : c.nodeDist[j + 1];
            float d = Mathf.Clamp(fwd ? dA + distFromA : dA - distFromA, 0f, c.total);
            axis = PointAt(c, d);
            dirAB = TangentAt(c, d) * (fwd ? 1f : -1f);
            section = c.type.section;
            return true;
        }

        /// <summary>The tunnel a point (network space) is in or next to: its line and the distance from that line's node A.
        /// False when every tunnel is further than maxOutside from its walls.</summary>
        public bool ClosestOnTunnels(Vector3 p, float maxOutside, out GraphEdge edge, out float distFromA)
        {
            EnsureLayout();
            edge = null;
            distFromA = 0f;
            float best = float.MaxValue, bestD = 0f;
            Chain bc = null;
            foreach (var c in chains)
            {
                if (c.type == null || c.nodeDist == null || c.samples.Count < 2) continue;
                var sec = c.type.section;
                float H = sec.Height, half = sec.HalfSpan, acc = 0f;
                var S = c.samples;
                for (int i = 0; i + 1 < S.Count; i++)
                {
                    Vector3 a = S[i], d = S[i + 1] - a;
                    float l = d.magnitude;
                    if (l < 1e-5f) continue;
                    var f = d / l;
                    var r = Vector3.Cross(Vector3.up, f);
                    r = r.sqrMagnitude < 1e-6f ? Vector3.right : r.normalized;
                    var u = Vector3.Cross(f, r);
                    var q = p - a;
                    float s = Vector3.Dot(q, f), x = Vector3.Dot(q, r), y = Vector3.Dot(q, u);
                    float ds = s < 0f ? -s : (s > l ? s - l : 0f);
                    float dy = y < 0f ? -y : (y > H ? y - H : 0f);
                    float rough = Mathf.Max(ds, dy, Mathf.Abs(x) - half);
                    if (rough > maxOutside) { acc += l; continue; }
                    float dx = Mathf.Max(0f, Mathf.Abs(x) - sec.WallXAt(Mathf.Clamp(y, 0f, H)));
                    float cost = ds * ds + dy * dy + dx * dx + 1e-3f * x * x / (half * half);   // inside two tunnels: the one it is more central in
                    if (cost < best) { best = cost; bc = c; bestD = acc + Mathf.Clamp(s, 0f, l); }
                    acc += l;
                }
            }
            if (bc == null || best > maxOutside * maxOutside + 1e-3f) return false;
            for (int j = 0; j < bc.edges.Count; j++)
            {
                if (bestD > bc.nodeDist[j + 1] && j + 1 < bc.edges.Count) continue;
                edge = bc.edges[j];
                distFromA = edge.a == bc.nodes[j] ? bestD - bc.nodeDist[j] : bc.nodeDist[j + 1] - bestD;
                return true;
            }
            return false;
        }

        /// <summary>The junction chamber containing a point (network space), walls widened by margin.</summary>
        public ChamberInfo ChamberAt(Vector3 p, float margin)
        {
            EnsureLayout();
            foreach (var kv in chambers)
            {
                var n = kv.Value.node;
                var q = Quaternion.Inverse(Quaternion.Euler(0f, kv.Value.yaw, 0f)) * (p - n.position);
                if (Mathf.Abs(q.x) <= n.chamberSize.x * 0.5f + margin && Mathf.Abs(q.z) <= n.chamberSize.y * 0.5f + margin &&
                    q.y >= -margin && q.y <= n.chamberHeight + margin) return kv.Value;
            }
            return null;
        }

        /// <summary>The polygonal room containing a point (network space), walls widened by margin.</summary>
        public RoomInfo RoomAt(Vector3 p, float margin)
        {
            EnsureLayout();
            foreach (var kv in rooms)
            {
                var info = kv.Value;
                var q = p - info.node.position;
                if (q.y < -margin || q.y > info.height + margin) continue;
                var v = new Vector2(q.x, q.z);
                if (InPolygon(info.corners, v) || PolyDistance(info.corners, v) <= margin) return info;
            }
            return null;
        }

        public static bool InPolygon(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }

        /// <summary>Distance to the nearest wall of a polygon; the wall index and the distance along it.</summary>
        public static float PolyDistance(List<Vector2> poly, Vector2 p, out int wall, out float along)
        {
            float best = float.MaxValue;
            wall = -1;
            along = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Count], d = b - a;
                float l = d.magnitude;
                if (l < 1e-5f) continue;
                float t = Mathf.Clamp(Vector2.Dot(p - a, d / l), 0f, l);
                float dist = Vector2.Distance(p, a + d / l * t);
                if (dist < best) { best = dist; wall = i; along = t; }
            }
            return best;
        }

        public static float PolyDistance(List<Vector2> poly, Vector2 p) { int w; float t; return PolyDistance(poly, p, out w, out t); }

        // ------------------------------------------------------------------ layout

        public void Layout()
        {
            layoutDirty = false;
            nodeMap.Clear();
            adj.Clear();
            foreach (var n in nodes)
            {
                if (string.IsNullOrEmpty(n.id)) n.id = NewId("n");
                nodeMap[n.id] = n;
                adj[n.id] = new List<GraphEdge>();
            }
            foreach (var e in edges)
            {
                if (string.IsNullOrEmpty(e.id)) e.id = NewId("e");
                if (e.a == e.b || !nodeMap.ContainsKey(e.a) || !nodeMap.ContainsKey(e.b)) continue;
                adj[e.a].Add(e);
                adj[e.b].Add(e);
            }
            forced.Clear();
            for (int pass = 0; pass < 12; pass++)
            {
                issues.Clear();
                chains.Clear();
                chambers.Clear();
                var promote = new List<string>();
                DetermineChambers();
                if (!BuildChains(promote)) { forced.UnionWith(promote); continue; }
                AssignSides();
                AssignRooms();
                LayAll(promote);
                if (promote.Count == 0) break;
                forced.UnionWith(promote);
            }
            CollectOpenings();
            edgeChain.Clear();
            foreach (var c in chains) foreach (var e in c.edges) edgeChain[e] = c;
            if (LaidOut != null) LaidOut(this);
        }

        int Degree(string id) { List<GraphEdge> l; return adj.TryGetValue(id, out l) ? l.Count : 0; }

        void DetermineChambers()
        {
            chamberSet.Clear();
            roomSet.Clear();
            joins.Clear();
            foreach (var n in nodes)
            {
                if (n.kind == GraphNodeKind.Room) { roomSet.Add(n.id); continue; }
                var l = adj[n.id];
                bool ch = n.kind == GraphNodeKind.Chamber || forced.Contains(n.id);
                if (!ch && l.Count >= 3)
                {
                    var J = PickMain(n, l);
                    if (J != null) joins[n.id] = J; else ch = true;
                }
                if (!ch && l.Count == 2 && TypeOf(l[0]) != TypeOf(l[1]))
                {
                    var J = Reducer(n, l);
                    if (J != null) joins[n.id] = J; else ch = true;
                }
                if (ch && (l.Count > 0 || n.kind == GraphNodeKind.Chamber)) chamberSet.Add(n.id);
                if (n.kind == GraphNodeKind.Exit && (l.Count != 1 || ch))
                    issues.Add(new GraphIssue { nodeId = n.id, position = n.position, message = "«Выход наружу» работает только на свободном конце тоннеля (узел с одной линией)" });
            }
        }

        /// <summary>Dead end that opens to the outside (no end wall, daylight and a light shining in).</summary>
        bool IsExit(string id)
        {
            GraphNode n;
            return nodeMap.TryGetValue(id, out n) && n.kind == GraphNodeKind.Exit && Degree(id) == 1 && !chamberSet.Contains(id);
        }

        Vector3 FlatDir(GraphNode n, GraphEdge e)
        {
            var d = nodeMap[Other(e, n.id)].position - n.position;
            d.y = 0f;
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        /// <summary>Main tunnel through a 3+ node: the widest type, then the straightest pair. Null = no pair of one type.</summary>
        JoinInfo PickMain(GraphNode n, List<GraphEdge> l)
        {
            JoinInfo best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                {
                    var T = TypeOf(l[i]);
                    if (T == null || T != TypeOf(l[j])) continue;
                    Vector3 di = FlatDir(n, l[i]), dj = FlatDir(n, l[j]);
                    float score = T.section.Width * 10f - Vector3.Dot(di, dj);
                    if (score <= bestScore) continue;
                    bestScore = score;
                    var t = dj - di;
                    if (t.sqrMagnitude < 1e-6f) t = Vector3.Cross(Vector3.up, di);
                    best = new JoinInfo { node = n, e1 = l[i], e2 = l[j], tangent = t.normalized, mainType = T };
                }
            return best;
        }

        /// <summary>Does a tunnel arriving at this node along "arriving" go on (and along which line)?</summary>
        bool Continues(string id, GraphEdge arriving, out GraphEdge next)
        {
            next = null;
            if (chamberSet.Contains(id) || roomSet.Contains(id)) return false;
            JoinInfo J;
            if (joins.TryGetValue(id, out J))
            {
                if (arriving == J.e1) next = J.e2;
                else if (arriving == J.e2) next = J.e1;
                return next != null;
            }
            var l = adj[id];
            if (l.Count != 2) return false;
            next = l[0] == arriving ? l[1] : l[0];
            return true;
        }

        bool PassThrough(string id)
        {
            if (chamberSet.Contains(id) || roomSet.Contains(id)) return false;
            JoinInfo J;
            if (joins.TryGetValue(id, out J)) return !J.reducer;
            return Degree(id) == 2;
        }

        bool IsBranchEnd(string id, GraphEdge e)
        {
            JoinInfo J;
            return joins.TryGetValue(id, out J) && e != J.e1 && e != J.e2;
        }

        bool BuildChains(List<string> promote)
        {
            var used = new HashSet<GraphEdge>();
            foreach (var n in nodes)
            {
                foreach (var start in adj[n.id])
                {
                    if (used.Contains(start)) continue;
                    GraphEdge back;
                    if (Continues(n.id, start, out back)) continue;   // the tunnel does not end here
                    var c = new Chain();
                    string cur = n.id;
                    var e = start;
                    c.nodes.Add(cur);
                    while (true)
                    {
                        used.Add(e);
                        c.edges.Add(e);
                        string next = Other(e, cur);
                        c.nodes.Add(next);
                        GraphEdge nextEdge;
                        if (next == c.nodes[0] || !Continues(next, e, out nextEdge) || used.Contains(nextEdge)) break;
                        cur = next;
                        e = nextEdge;
                    }
                    c.type = TypeOf(c.edges[0]);
                    string minId = c.edges[0].id;
                    foreach (var x in c.edges) if (string.CompareOrdinal(x.id, minId) < 0) minId = x.id;
                    c.key = "Tunnel_" + minId;
                    string first = c.nodes[0], last = c.nodes[c.nodes.Count - 1];
                    c.startJoin = IsBranchEnd(first, c.edges[0]) ? first : null;
                    c.endJoin = IsBranchEnd(last, c.edges[c.edges.Count - 1]) && (last != first || c.nodes.Count > 2) ? last : null;
                    chains.Add(c);
                }
            }
            // loops made only of pass-through nodes: put a chamber into each of them
            foreach (var e in edges)
            {
                if (used.Contains(e) || !adj.ContainsKey(e.a) || !adj.ContainsKey(e.b) || e.a == e.b) continue;
                promote.Add(e.a);
                return false;
            }
            return true;
        }

        static readonly Vector3[] SideDirs = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };

        void AssignSides()
        {
            foreach (var id in chamberSet)
            {
                var node = nodeMap[id];
                var info = new ChamberInfo { node = node };
                var l = adj[id];
                if (node.autoYaw && l.Count > 0)
                {
                    var d = nodeMap[Other(l[0], id)].position - node.position;
                    info.yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                    node.yaw = info.yaw;
                }
                else info.yaw = node.yaw;
                chambers[id] = info;
            }
            foreach (var c in chains)
            {
                c.startSide = -1;
                c.endSide = -1;
            }
            foreach (var kv in chambers)
            {
                var info = kv.Value;
                string id = kv.Key;
                var inv = Quaternion.Euler(0f, -info.yaw, 0f);
                // candidates: (chain, end, side, score)
                var cand = new List<KeyValuePair<float, int[]>>();
                for (int ci = 0; ci < chains.Count; ci++)
                {
                    var c = chains[ci];
                    for (int end = 0; end < 2; end++)
                    {
                        string nid = end == 0 ? c.nodes[0] : c.nodes[c.nodes.Count - 1];
                        if (nid != id) continue;
                        string nb = end == 0 ? c.nodes[1] : c.nodes[c.nodes.Count - 2];
                        var d = nodeMap[nb].position - info.node.position;
                        d.y = 0f;
                        if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
                        var local = inv * d.normalized;
                        for (int s = 0; s < 4; s++)
                            cand.Add(new KeyValuePair<float, int[]>(Vector3.Dot(local, SideDirs[s]), new[] { ci, end, s }));
                    }
                }
                cand.Sort((x, y) => y.Key.CompareTo(x.Key));
                var doneEnds = new HashSet<long>();
                foreach (var cnd in cand)
                {
                    int ci = cnd.Value[0], end = cnd.Value[1], s = cnd.Value[2];
                    long k = ci * 2L + end;
                    if (doneEnds.Contains(k) || info.sideChains[s] != null) continue;
                    var c = chains[ci];
                    info.sideChains[s] = c;
                    info.sideEdges[s] = end == 0 ? c.edges[0] : c.edges[c.edges.Count - 1];
                    if (end == 0) c.startSide = s; else c.endSide = s;
                    doneEnds.Add(k);
                    if (cnd.Key < 0.7f)
                        issues.Add(new GraphIssue { nodeId = id, position = info.node.position, message = "Линия входит в камеру сильно наискось (камеры пока прямоугольные): выровняйте её по стене камеры или поверните камеру" });
                }
                foreach (var cnd in cand)
                {
                    int ci = cnd.Value[0], end = cnd.Value[1];
                    if (!doneEnds.Contains(ci * 2L + end))
                    {
                        doneEnds.Add(ci * 2L + end);
                        issues.Add(new GraphIssue { nodeId = id, position = info.node.position, message = "В камере больше 4 тоннелей: лишний закрыт заглушкой" });
                    }
                }
            }
        }

        /// <summary>Each tunnel end at a room gets a doorway in the wall its line crosses (floor level). A wall too narrow for the
        /// doorway gets its corner cut off square to the tunnel, so a doorway never runs past the end of its wall.</summary>
        void AssignRooms()
        {
            rooms.Clear();
            foreach (var id in roomSet)
            {
                var node = nodeMap[id];
                var poly = node.roomCorners != null && node.roomCorners.Count >= 3 ? new List<Vector2>(node.roomCorners) : GraphNode.DefaultRoom();
                if (TunnelRoom.Area(poly) < 0f) poly.Reverse();
                var info = new RoomInfo { node = node, corners = poly };
                var ends = new List<KeyValuePair<Chain, int>>();
                foreach (var c in chains)
                    for (int end = 0; end < 2; end++)
                    {
                        string nid = end == 0 ? c.nodes[0] : c.nodes[c.nodes.Count - 1];
                        if (nid == id && c.type != null && c.nodes.Count >= 2) ends.Add(new KeyValuePair<Chain, int>(c, end));
                    }
                // pass 1: make room on the walls (cuts change the wall list, so doors are placed afterwards)
                for (int pass = 0; pass < 2; pass++)
                    foreach (var ce in ends)
                    {
                        var d = RoomDir(node, ce.Key, ce.Value);
                        int wall; float s, stretch;
                        if (!RoomWall(poly, d, out wall, out s, out stretch)) continue;
                        float half = ce.Key.type.section.HalfSpan * stretch + 0.15f;
                        float len = Vector2.Distance(poly[wall], poly[(wall + 1) % poly.Count]);
                        if (len >= 2f * half) continue;
                        var cut = CutFacet(poly, d, ce.Key.type.section.HalfSpan + 0.15f);
                        if (cut != null) poly = cut;
                    }
                info.corners = poly;
                // pass 2: doors
                float maxH = 0f;
                foreach (var ce in ends)
                {
                    var c = ce.Key;
                    int end = ce.Value;
                    var d = RoomDir(node, c, end);
                    int wall; float s, stretch;
                    if (!RoomWall(poly, d, out wall, out s, out stretch))
                    {
                        issues.Add(new GraphIssue { nodeId = id, position = node.position, message = "Узел комнаты вне её контура: поправьте углы комнаты" });
                        continue;
                    }
                    Vector2 A = poly[wall], B = poly[(wall + 1) % poly.Count];
                    float len = Vector2.Distance(A, B);
                    Vector2 u2 = (B - A) / Mathf.Max(1e-4f, len);
                    var nW = new Vector3(u2.y, 0f, -u2.x);
                    // up to 60° off the wall normal the tunnel goes straight through the wall (its end is cut along the wall)
                    bool oblique = stretch > 1.001f;
                    float half = c.type.section.HalfSpan * stretch + 0.15f;
                    if (len < 2f * half)
                        issues.Add(new GraphIssue { nodeId = id, position = node.position, message = "Стена комнаты уже тоннеля «" + c.type.Label + "»: раздвиньте её углы" });
                    float along = Mathf.Clamp(s * len, Mathf.Min(half, len * 0.5f), Mathf.Max(len - half, len * 0.5f));
                    Vector2 p = A + u2 * along;
                    var door = new RoomDoorInfo { chain = c, end = end, wall = wall, along = along, stretch = stretch, wallNormal = nW,
                                                  point = node.position + new Vector3(p.x, 0f, p.y), outward = oblique ? new Vector3(d.x, 0f, d.y) : nW };
                    info.doors.Add(door);
                    if (end == 0) c.startDoor = door; else c.endDoor = door;
                    maxH = Mathf.Max(maxH, c.type.section.Height);
                }
                for (int i = 0; i < info.doors.Count; i++)
                    for (int j = i + 1; j < info.doors.Count; j++)
                    {
                        RoomDoorInfo a = info.doors[i], b = info.doors[j];
                        if (a.wall == b.wall && Mathf.Abs(a.along - b.along) < a.chain.type.section.HalfSpan * a.stretch + b.chain.type.section.HalfSpan * b.stretch + 0.3f)
                            issues.Add(new GraphIssue { nodeId = id, position = a.point, message = "Два тоннеля входят в одну стену комнаты слишком близко: добавьте угол или разведите линии" });
                    }
                info.height = node.roomHeight > 0.01f ? node.roomHeight : Mathf.Max(2.5f, maxH);
                if (node.roomHeight > 0.01f && node.roomHeight < maxH)
                    issues.Add(new GraphIssue { nodeId = id, position = node.position, message = "Потолок комнаты ниже входящего тоннеля: его конец закрыт стенкой выше потолка" });
                // a tunnel taller than the room: its end gets a wall above the doorway
                foreach (var door in info.doors)
                {
                    var sec = door.chain.type.section;
                    if (sec.Height <= info.height + 0.005f) continue;
                    List<int> kind; int ceil;
                    var h = MeshKit.ClipOutline(sec.BaseOutline(out kind, out ceil), 0f, info.height);
                    if (h.Count < 3) continue;
                    if (door.end == 0) door.chain.holeStart = h; else door.chain.holeEnd = h;
                }
                rooms[id] = info;
            }
        }

        Vector2 RoomDir(GraphNode room, Chain c, int end)
        {
            string nb = end == 0 ? c.nodes[1] : c.nodes[c.nodes.Count - 2];
            var d3 = nodeMap[nb].position - room.position;
            var d = new Vector2(d3.x, d3.z);
            return d.sqrMagnitude < 1e-6f ? Vector2.up : d.normalized;
        }

        /// <summary>Wall crossed by the ray from the room centre along d, where (0..1 along it) and the doorway stretch (1 = square, up to 2 at 60°).</summary>
        static bool RoomWall(List<Vector2> poly, Vector2 d, out int wall, out float s, out float stretch)
        {
            wall = -1; s = 0f; stretch = 1f;
            float best = float.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i], e = poly[(i + 1) % poly.Count] - a;
                float den = MeshKit.Cross2(d, e);
                if (Mathf.Abs(den) < 1e-9f) continue;
                float t = MeshKit.Cross2(a, e) / den, u = MeshKit.Cross2(a, d) / den;
                if (t > 0f && u >= 0f && u <= 1f && t < best) { best = t; wall = i; s = u; }
            }
            if (wall < 0) return false;
            Vector2 A = poly[wall], B = poly[(wall + 1) % poly.Count];
            Vector2 u2 = (B - A).normalized;
            float cos = Vector2.Dot(d, new Vector2(u2.y, -u2.x));
            if (cos >= 0.5f) stretch = 1f / cos;
            return true;
        }

        /// <summary>Cuts the room outline square to direction d as far out as a wall of half width "half" each side of the ray still fits.
        /// Null = no such place.</summary>
        static List<Vector2> CutFacet(List<Vector2> poly, Vector2 d, float half)
        {
            Vector2 lat = new Vector2(-d.y, d.x);
            float tMax = float.MinValue;
            foreach (var v in poly) tMax = Mathf.Max(tMax, Vector2.Dot(v, d));
            for (float t = tMax - 0.05f; t > 0.5f; t -= 0.05f)
            {
                // where the line dot(p, d) = t crosses the outline, around the ray
                float lo = float.MinValue, hi = float.MaxValue;
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                    float da = Vector2.Dot(a, d) - t, db = Vector2.Dot(b, d) - t;
                    if ((da > 0f) == (db > 0f)) continue;
                    float x = Vector2.Dot(Vector2.Lerp(a, b, da / (da - db)), lat);
                    if (x <= 0f) lo = Mathf.Max(lo, x); else hi = Mathf.Min(hi, x);
                }
                if (lo == float.MinValue || hi == float.MaxValue || lo > -half || hi < half) continue;
                var res = new List<Vector2>();
                for (int i = 0; i < poly.Count; i++)
                {
                    Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
                    float da = Vector2.Dot(a, d) - t, db = Vector2.Dot(b, d) - t;
                    if (da <= 0f) res.Add(a);
                    if ((da > 0f) != (db > 0f)) res.Add(Vector2.Lerp(a, b, da / (da - db)));
                }
                return res.Count >= 3 ? res : null;
            }
            return null;
        }

        public Vector3 WallPoint(ChamberInfo info, int side, out Vector3 outward)
        {
            var rot = Quaternion.Euler(0f, info.yaw, 0f);
            outward = rot * SideDirs[side];
            float half = side % 2 == 0 ? info.node.chamberSize.y * 0.5f : info.node.chamberSize.x * 0.5f;
            return info.node.position + outward * half;
        }

        /// <summary>Lays every tunnel, a main tunnel before the tunnels that enter its side wall: its slab across each opening is
        /// made straight and level first, and the branch then starts exactly on that wall (no gap even on a slope or a bend).</summary>
        void LayAll(List<string> promote)
        {
            edgeChain.Clear();
            foreach (var c in chains) foreach (var e in c.edges) edgeChain[e] = c;
            laid.Clear();
            var pending = new List<Chain>(chains);
            while (pending.Count > 0)
            {
                int pick = 0;
                for (int i = 0; i < pending.Count; i++) if (MainsLaid(pending[i])) { pick = i; break; }
                var c = pending[pick];
                pending.RemoveAt(pick);
                LayChain(c, promote);
                WidenOpenings(c);
                laid.Add(c);
            }
        }

        bool MainsLaid(Chain c)
        {
            for (int end = 0; end < 2; end++)
            {
                string id = end == 0 ? c.startJoin : c.endJoin;
                JoinInfo J;
                Chain m;
                if (id == null || !joins.TryGetValue(id, out J) || J.reducer) continue;
                if (edgeChain.TryGetValue(J.e1, out m) && m != c && !laid.Contains(m)) return false;
            }
            return true;
        }

        /// <summary>One long, straight and level slab across each side opening of this tunnel (openings that overlap along the
        /// tunnel share one), so no slab joint, joint filling or rim runs through a hole and the branch meets a flat, plumb wall.</summary>
        void WidenOpenings(Chain main)
        {
            if (main.type == null || main.nodeDist == null || main.samples.Count < 2) return;
            var spans = new List<Vector2>();
            for (int ni = 0; ni < main.nodes.Count; ni++)
            {
                JoinInfo J;
                if (!joins.TryGetValue(main.nodes[ni], out J) || J.reducer || !main.edges.Contains(J.e1)) continue;
                float dist = main.nodeDist[ni];
                if (J.back > 0f) dist += ni == 0 ? J.back : -J.back;
                foreach (var e in adj[J.node.id])
                {
                    if (e == J.e1 || e == J.e2) continue;
                    var bt = TypeOf(e);
                    if (bt == null) continue;
                    float y0, x;
                    BranchFit(main.type.section, bt.section, out y0, out x, J.node.joinLift);
                    var P = BranchOpening(main.type.section, bt.section, y0, x, new List<Vector2>());
                    float half = 0f;
                    foreach (var v in P) half = Mathf.Max(half, Mathf.Abs(v.x));
                    if (P.Count >= 3) spans.Add(new Vector2(dist - half - 0.3f, dist + half + 0.3f));
                }
            }
            if (spans.Count == 0) return;
            spans.Sort((a, b) => a.x.CompareTo(b.x));
            var merged = new List<Vector2>();
            foreach (var sp in spans)
            {
                if (merged.Count > 0 && sp.x <= merged[merged.Count - 1].y + 0.3f)
                    merged[merged.Count - 1] = new Vector2(merged[merged.Count - 1].x, Mathf.Max(merged[merged.Count - 1].y, sp.y));
                else merged.Add(sp);
            }
            for (int i = merged.Count - 1; i >= 0; i--) WidenSlab(main, merged[i].x, merged[i].y, main.type.maxJointAngle);
        }

        void LayChain(Chain c, List<string> promote)
        {
            var T = c.type;
            if (T == null)
            {
                issues.Add(new GraphIssue { edgeId = c.edges[0].id, position = nodeMap[c.nodes[0]].position, message = "У графа нет библиотеки типов тоннелей" });
                return;
            }
            float L = Mathf.Max(0.5f, T.segmentLength);
            float theta = Mathf.Max(0.2f, T.maxJointAngle) * Mathf.Deg2Rad;
            float Rmin = T.MinRadius;
            float minLead = Mathf.Max(L, 1.5f);

            var Q = new List<Vector3>();
            var qNode = new List<string>();
            foreach (var id in c.nodes) { Q.Add(nodeMap[id].position); qNode.Add(id); }

            int leadStart = -1, leadEnd = -1;
            Vector3 outS = Vector3.zero, outE = Vector3.zero;
            float wallS = 0f, wallE = 0f;
            ChamberInfo info;
            Vector3 w;
            bool endS = false, endE = false;
            if (c.startSide >= 0 && chambers.TryGetValue(c.nodes[0], out info)) { w = WallPoint(info, c.startSide, out outS); endS = true; }
            else if (c.startDoor != null) { w = c.startDoor.point; outS = c.startDoor.outward; endS = true; }
            else if (c.startJoin != null) { BranchPoint(c, true, out w, out outS); endS = true; }
            else w = Vector3.zero;
            if (endS)
            {
                wallS = Vector3.Distance(w, Q[0]);
                Q[0] = w; qNode[0] = null;
                Q.Insert(1, w + outS * minLead); qNode.Insert(1, null);
                leadStart = 1;
            }
            if (c.endSide >= 0 && chambers.TryGetValue(c.nodes[c.nodes.Count - 1], out info)) { w = WallPoint(info, c.endSide, out outE); endE = true; }
            else if (c.endDoor != null) { w = c.endDoor.point; outE = c.endDoor.outward; endE = true; }
            else if (c.endJoin != null) { BranchPoint(c, false, out w, out outE); endE = true; }
            if (endE)
            {
                int last = Q.Count - 1;
                wallE = Vector3.Distance(w, Q[last]);
                Q[last] = w; qNode[last] = null;
                Q.Insert(last, w + outE * minLead); qNode.Insert(last, null);
                leadEnd = last;   // index of the inserted corner
            }

            // grow the lead-in straights so their bends fit
            // (never past the next point: a short line or a U-turn would otherwise send it kilometres away)
            bool shortS = false, shortE = false;
            for (int it = 0; it < 5; it++)
            {
                if (leadStart > 0 && leadStart + 1 < Q.Count)
                {
                    float phi = Angle(outS, Q[leadStart + 1] - Q[leadStart]);
                    float want = Rmin * Mathf.Tan(Mathf.Min(phi, 2.1f) * 0.5f) + 0.05f;
                    float avail = Vector3.Dot(Q[leadStart + 1] - Q[0], outS) - 0.5f;
                    shortS = want > avail;
                    Q[leadStart] = Q[0] + outS * Mathf.Max(minLead, Mathf.Min(want, avail));
                }
                if (leadEnd > 0 && leadEnd - 1 >= 0)
                {
                    int last = Q.Count - 1;
                    float phi = Angle(-outE, Q[leadEnd] - Q[leadEnd - 1]);
                    float want = Rmin * Mathf.Tan(Mathf.Min(phi, 2.1f) * 0.5f) + 0.05f;
                    float avail = Vector3.Dot(Q[leadEnd - 1] - Q[last], outE) - 0.5f;
                    shortE = want > avail;
                    Q[leadEnd] = Q[last] + outE * Mathf.Max(minLead, Mathf.Min(want, avail));
                }
            }
            // a lead-in turn too sharp for an arc becomes a mitred elbow right after the straight stub (like any sharp corner)
            float span = T.section.HalfSpan;
            bool elbS = shortS && LeadElbow(Q, leadStart, 0, outS, span, minLead);
            bool elbE = shortE && LeadElbow(Q, leadEnd, Q.Count - 1, outE, span, minLead);
            if ((shortS && !elbS) || (shortE && !elbE))
                issues.Add(new GraphIssue { edgeId = c.edges[0].id, position = shortS && !elbS ? Q[0] : Q[Q.Count - 1], message = "Линия слишком короткая или уходит назад у стены камеры/тоннеля: отодвиньте узел дальше (нужен плавный поворот)" });
            // hard limits: a broken graph must never hang the editor
            float polyLen = 0f;
            for (int i = 1; i < Q.Count; i++) polyLen += Vector3.Distance(Q[i - 1], Q[i]);
            if (polyLen > MaxTunnelLength || float.IsNaN(polyLen))
            {
                issues.Add(new GraphIssue { edgeId = c.edges[0].id, position = nodeMap[c.nodes[0]].position, message = "Тоннель длиннее " + MaxTunnelLength.ToString("0") + " м или точка улетела далеко: не строится, проверьте узлы" });
                c.samples = new List<Vector3>();
                return;
            }

            int m = Q.Count;
            var tan = new float[m];
            var rad = new float[m];
            var phis = new float[m];
            var elbow = new bool[m];
            for (int i = 1; i < m - 1; i++)
            {
                float phi = Angle(Q[i] - Q[i - 1], Q[i + 1] - Q[i]);
                phis[i] = phi;
                if (phi < 1e-3f) continue;
                float R = Rmin;
                if (qNode[i] != null) R = Mathf.Max(R, nodeMap[qNode[i]].cornerRadius);
                rad[i] = R;
                tan[i] = R * Mathf.Tan(Mathf.Min(phi, 3.0f) * 0.5f);
                if ((i == leadStart && elbS) || (i == leadEnd && elbE)) { elbow[i] = true; tan[i] = 0f; rad[i] = 0f; }
            }
            // does every bend fit between its neighbours?
            for (int j = 0; j + 1 < m; j++)
            {
                float len = Vector3.Distance(Q[j], Q[j + 1]);
                float need = tan[j] + tan[j + 1];
                if (need <= len + 1e-3f) continue;
                int pick = -1;
                bool pj = j > 0 && qNode[j] != null && PassThrough(qNode[j]);
                bool pk = j + 1 < m - 1 && qNode[j + 1] != null && PassThrough(qNode[j + 1]);
                if (pj && pk) pick = tan[j] >= tan[j + 1] ? j : j + 1;
                else if (pj) pick = j;
                else if (pk) pick = j + 1;
                if (pick >= 0)
                {
                    // a sharp corner on a plain line becomes a mitred elbow; too sharp (or a join) -> chamber
                    string pid = qNode[pick];
                    if (Degree(pid) == 2 && !joins.ContainsKey(pid) && phis[pick] <= MaxElbow * Mathf.Deg2Rad && !elbow[pick])
                    {
                        elbow[pick] = true;
                        tan[pick] = 0f;
                        rad[pick] = 0f;
                        j = Mathf.Max(-1, j - 2);
                        continue;
                    }
                    promote.Add(pid);
                    return;
                }
            }
            // remaining conflicts (lead-in corners): squeeze and report
            for (int pass = 0; pass < 3; pass++)
                for (int j = 0; j + 1 < m; j++)
                {
                    float len = Vector3.Distance(Q[j], Q[j + 1]);
                    float need = tan[j] + tan[j + 1];
                    if (need <= len + 1e-3f || need < 1e-4f) continue;
                    float f = len / need * 0.999f;
                    tan[j] *= f; tan[j + 1] *= f;
                    if (j > 0 && phis[j] > 1e-3f) rad[j] = tan[j] / Mathf.Tan(phis[j] * 0.5f);
                    if (j + 1 < m - 1 && phis[j + 1] > 1e-3f) rad[j + 1] = tan[j + 1] / Mathf.Tan(phis[j + 1] * 0.5f);
                    issues.Add(new GraphIssue { edgeId = c.edges[0].id, position = Q[j], message = "Слишком крутой изгиб у камеры для типа «" + T.Label + "» (нужен радиус " + Rmin.ToString("0") + " м): выровняйте линию по оси камеры или отодвиньте соседний узел" });
                }

            // sample: straight - arc - straight ...
            var S = new List<Vector3> { Q[0] };
            for (int i = 1; i < m - 1; i++)
            {
                Vector3 d1 = (Q[i] - Q[i - 1]).normalized, d2 = (Q[i + 1] - Q[i]).normalized;
                Vector3 A = Q[i] - d1 * tan[i];
                StraightTo(S, A, L);
                if (tan[i] > 1e-4f)
                {
                    float phi = phis[i];
                    float R = rad[i];
                    int n = Mathf.Max(1, Mathf.CeilToInt(phi / theta - 1e-3f));
                    Vector3 bis = (d2 - d1).normalized;
                    Vector3 O = Q[i] + bis * (R / Mathf.Cos(phi * 0.5f));
                    Vector3 axis = Vector3.Cross(d1, d2).normalized;
                    Vector3 v = A - O;
                    for (int k = 1; k <= n; k++) S.Add(O + Quaternion.AngleAxis(phi * Mathf.Rad2Deg * k / n, axis) * v);
                }
            }
            StraightTo(S, Q[m - 1], L);
            // elbows: the slabs next to a mitre must be longer than the mitre's inner cut-back
            c.elbows.Clear();
            float halfW = span;
            for (int i = 1; i < m - 1; i++)
            {
                if (!elbow[i]) continue;
                c.elbows.Add(Q[i]);
                int e = S.FindIndex(p => (p - Q[i]).sqrMagnitude < 1e-6f);
                if (e <= 0 || e >= S.Count - 1) continue;
                float need = halfW * Mathf.Tan(phis[i] * 0.5f) + 0.3f;
                while (e - 1 > 0 && Vector3.Distance(S[e - 1], S[e]) < need && !IsElbow(c, S[e - 1])) { S.RemoveAt(e - 1); e--; }
                while (e + 1 < S.Count - 1 && Vector3.Distance(S[e + 1], S[e]) < need && !IsElbow(c, S[e + 1])) S.RemoveAt(e + 1);
                if (Vector3.Distance(S[e - 1], S[e]) < need || Vector3.Distance(S[e + 1], S[e]) < need)
                    issues.Add(new GraphIssue { edgeId = c.edges[0].id, position = Q[i], message = "Угол слишком близко к другому углу или концу: отодвиньте узлы" });
            }
            // oblique room doorway: the first slab must be longer than the slanted cut of its end
            for (int end = 0; end < 2; end++)
            {
                var door = end == 0 ? c.startDoor : c.endDoor;
                if (door == null || door.stretch <= 1.001f) continue;
                float need = span * Mathf.Sqrt(door.stretch * door.stretch - 1f) + 0.3f;
                if (end == 0) while (S.Count > 2 && Vector3.Distance(S[0], S[1]) < need && !IsElbow(c, S[1])) S.RemoveAt(1);
                else while (S.Count > 2 && Vector3.Distance(S[S.Count - 1], S[S.Count - 2]) < need && !IsElbow(c, S[S.Count - 2])) S.RemoveAt(S.Count - 2);
            }
            c.samples = S;

            // distances of the graph nodes along the laid path (for shafts and anchors)
            var cum = new float[S.Count];
            for (int i = 1; i < S.Count; i++) cum[i] = cum[i - 1] + Vector3.Distance(S[i - 1], S[i]);
            c.total = cum[S.Count - 1];
            c.nodeDist = new float[c.nodes.Count];
            for (int j = 0; j < c.nodes.Count; j++)
            {
                if (j == 0 && endS) { c.nodeDist[j] = -wallS; continue; }
                if (j == c.nodes.Count - 1 && endE) { c.nodeDist[j] = c.total + wallE; continue; }
                c.nodeDist[j] = Project(S, cum, nodeMap[c.nodes[j]].position);
            }

            CheckJoints(c);
        }

        public const float MaxElbow = 100f;

        /// <summary>Lead-in corner Q[k] (straight stub out of the wall at Q[end]) turned into a sharp elbow: the stub gets just long enough
        /// for the mitre. False = the turn is too sharp for an elbow.</summary>
        static bool LeadElbow(List<Vector3> Q, int k, int end, Vector3 outDir, float halfSpan, float minLead)
        {
            int nb = k + (k > end ? 1 : -1);
            if (k <= 0 || k >= Q.Count - 1 || nb < 0 || nb >= Q.Count) return false;
            float lead = Vector3.Distance(Q[k], Q[end]);
            float avail = Vector3.Dot(Q[nb] - Q[end], outDir) - 0.5f;
            for (int it = 0; it < 4; it++)
            {
                float phi = Angle(outDir, Q[nb] - Q[k]);
                if (phi > MaxElbow * Mathf.Deg2Rad) return false;
                float need = halfSpan * Mathf.Tan(phi * 0.5f) + 0.6f;
                lead = Mathf.Max(minLead, Mathf.Min(need, Mathf.Max(avail, minLead)));
                Q[k] = Q[end] + outDir * lead;
            }
            return Angle(outDir, Q[nb] - Q[k]) <= MaxElbow * Mathf.Deg2Rad;
        }

        static bool IsElbow(Chain c, Vector3 p)
        {
            foreach (var e in c.elbows) if ((e - p).sqrMagnitude < 1e-4f) return true;
            return false;
        }

        void CheckJoints(Chain c)
        {
            var S = c.samples;
            for (int i = 1; i + 1 < S.Count; i++)
            {
                if (IsElbow(c, S[i]) || IsElbow(c, S[i - 1]) || IsElbow(c, S[i + 1])) continue;
                float a = Angle(S[i] - S[i - 1], S[i + 1] - S[i]) * Mathf.Rad2Deg;
                if (a > c.type.maxJointAngle + 0.05f && issues.Count < 200)
                {
                    issues.Add(new GraphIssue { edgeId = c.edges[0].id, position = S[i], message = "Стык плит " + a.ToString("0.0") + "° больше допустимых " + c.type.maxJointAngle.ToString("0.#") + "° (часто из-за врезки на изгибе: перенесите узел врезки на прямой участок)" });
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ direct joins

        /// <summary>Height of the branch floor above the main floor and how far from the main axis the branch ends.</summary>
        /// <summary>Branch floor height above the main floor (level with it, or with the curbs) and how far from the main axis the branch ends:
        /// at the outermost point of the main wall over the branch height, so no part of the branch ever sticks into the main tunnel.</summary>
        public static void BranchFit(TunnelSection main, TunnelSection branch, out float y0, out float xEntry, float lift = 0f)
        {
            float mh = main.Height, bh = branch.Height;
            y0 = Mathf.Clamp(main.JoinFloor + lift, 0f, Mathf.Max(0f, mh - 0.5f));
            float top = Mathf.Min(y0 + bh, mh);
            xEntry = Mathf.Max(main.WallXAt(y0), main.WallXAt(top));
            foreach (var y in main.CornerHeights())
                if (y > y0 && y < top) xEntry = Mathf.Max(xEntry, main.WallXAt(y));
            if (xEntry < 0.05f) xEntry = main.Width * 0.5f;
        }

        /// <summary>The hole a branch makes in the main side wall (branch profile coordinates, cut at the main ceiling) and its reveal:
        /// how far the branch walls go on past the branch end at each height (x = height above the branch floor, y = depth) to meet a
        /// main wall that leans in (chamfers, pipe). Empty reveal = the main wall is flat there.</summary>
        public static List<Vector2> BranchOpening(TunnelSection main, TunnelSection branch, float y0, float xEntry, List<Vector2> reveal)
        {
            List<int> kind; int ceil;
            var P = MeshKit.ClipOutline(branch.BaseOutline(out kind, out ceil), 0f, main.Height - y0);
            reveal.Clear();
            if (P.Count < 3) return P;
            float top = 0f;
            foreach (var v in P) top = Mathf.Max(top, v.y);
            var ys = new List<float> { 0f, top };
            foreach (var y in main.CornerHeights())
                if (y - y0 > 1e-4f && y - y0 < top - 1e-4f) ys.Add(y - y0);
            ys.Sort();
            bool any = false;
            foreach (var y in ys)
            {
                float e = Mathf.Max(0f, xEntry - main.WallXAt(y + y0));
                if (e > 1e-3f) any = true;
                reveal.Add(new Vector2(y, e));
            }
            if (!any) reveal.Clear();
            return P;
        }

        void BranchPoint(Chain c, bool atStart, out Vector3 w, out Vector3 outward)
        {
            string id = atStart ? c.nodes[0] : c.nodes[c.nodes.Count - 1];
            string nb = atStart ? c.nodes[1] : c.nodes[c.nodes.Count - 2];
            var J = joins[id];
            var node = nodeMap[id];
            if (J.reducer) { outward = J.tangent; w = node.position + Vector3.up * J.y0; return; }
            var d = nodeMap[nb].position - node.position;
            float y0, x;
            BranchFit(J.mainType.section, c.type.section, out y0, out x, node.joinLift);
            Chain m;
            if (edgeChain.TryGetValue(J.e1, out m) && m != c && laid.Contains(m) && m.nodeDist != null && m.samples.Count >= 2)
            {
                // the main tunnel is already laid: start on its real wall (its slab here is straight and level)
                int ni = m.nodes.IndexOf(id);
                if (ni >= 0)
                {
                    float dist = m.nodeDist[ni];
                    if (J.back > 0f) dist += ni == 0 ? J.back : -J.back;
                    var r = Vector3.Cross(Vector3.up, TangentAt(m, dist)).normalized;
                    outward = Vector3.Dot(d, r) >= 0f ? r : -r;
                    w = PointAt(m, dist) + outward * x + Vector3.up * y0;
                    return;
                }
            }
            var right = Vector3.Cross(Vector3.up, J.tangent).normalized;
            outward = Vector3.Dot(d, right) >= 0f ? right : -right;
            w = node.position - J.tangent * J.back + outward * x + Vector3.up * y0;
        }

        /// <summary>Two different types meeting end to end: the bigger one ends with a wall, the other goes on through an opening in it.
        /// Null = they do not overlap enough (or the line turns back): the node becomes a chamber.</summary>
        JoinInfo Reducer(GraphNode n, List<GraphEdge> l)
        {
            TunnelType A = TypeOf(l[0]), B = TypeOf(l[1]);
            if (A == null || B == null || l[0] == l[1]) return null;
            List<int> kind; int ceil;
            var oa = A.section.BaseOutline(out kind, out ceil);
            var ob = B.section.BaseOutline(out kind, out ceil);
            float aa = Mathf.Abs(Area(oa)), ab = Mathf.Abs(Area(ob));
            bool aMain = aa >= ab;
            GraphEdge me = aMain ? l[0] : l[1], se = aMain ? l[1] : l[0];
            TunnelType M = aMain ? A : B;
            var om = aMain ? oa : ob;
            var os = aMain ? ob : oa;
            float small = aMain ? ab : aa;
            var t = n.position - nodeMap[Other(me, n.id)].position;
            var tf = new Vector3(t.x, 0f, t.z);
            if (tf.sqrMagnitude < 1e-4f) return null;
            if (Vector3.Angle(tf, FlatDir(n, se)) > 25f)
            {
                // sharp turn: the bigger tunnel ends with a plain wall, the other one comes out of its side wall near the end
                var ss = TypeOf(se).section;
                return new JoinInfo { node = n, e1 = me, e2 = null, tangent = t.normalized, mainType = M, back = ss.HalfSpan + 0.5f };
            }
            // floor level with the bigger tunnel (a small step up is fine if the smaller one then fits inside whole)
            float baseY = M.section.JoinFloor + n.joinLift, y0;
            if (!FitHole(M.section, os, baseY, baseY + 0.1f, out y0)) y0 = baseY;
            var shifted = new List<Vector2>(os.Count);
            foreach (var v in os) shifted.Add(new Vector2(v.x, v.y + y0));
            var hole = Intersect(om, shifted);
            float ah = hole.Count >= 3 ? Mathf.Abs(Area(hole)) : 0f;
            if (ah < 0.5f || ah < 0.3f * small) return null;
            return new JoinInfo { node = n, e1 = me, e2 = null, tangent = t.normalized, mainType = M, reducer = true, y0 = y0, hole = hole, fits = ah >= 0.97f * small };
        }

        /// <summary>Lowest height (lift..maxLift) at which the outline fits inside the bigger section with a 5 cm margin.</summary>
        static bool FitHole(TunnelSection big, List<Vector2> o, float lift, float maxLift, out float y0)
        {
            float top = 0f;
            foreach (var v in o) top = Mathf.Max(top, v.y);
            float bh = big.Height;
            for (y0 = lift; y0 <= maxLift + 1e-4f && y0 + top <= bh - 0.05f + 1e-4f; y0 += 0.05f)
            {
                bool ok = true;
                foreach (var v in o)
                {
                    float y = v.y + y0;
                    if (y < big.FloorHeightAt(v.x) - 0.001f || Mathf.Abs(v.x) > big.WallXAt(y) - 0.05f) { ok = false; break; }
                }
                if (ok) return true;
            }
            y0 = lift;
            return false;
        }

        static float Area(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) { Vector2 u = p[i], v = p[(i + 1) % p.Count]; a += u.x * v.y - v.x * u.y; }
            return a * 0.5f;
        }

        static float Side(Vector2 p, Vector2 q, Vector2 x) { return (q.x - p.x) * (x.y - p.y) - (q.y - p.y) * (x.x - p.x); }

        static bool IsConvex(List<Vector2> p)
        {
            float sign = 0f;
            for (int i = 0; i < p.Count; i++)
            {
                float c = Side(p[i], p[(i + 1) % p.Count], p[(i + 2) % p.Count]);
                if (Mathf.Abs(c) < 1e-6f) continue;
                if (sign == 0f) sign = Mathf.Sign(c);
                else if (Mathf.Sign(c) != sign) return false;
            }
            return true;
        }

        static List<Vector2> ConvexHull(List<Vector2> pts)
        {
            var p = new List<Vector2>(pts);
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int i = 0; i < p.Count; i++)
                {
                    var q = pass == 0 ? p[i] : p[p.Count - 1 - i];
                    while (h.Count >= start + 2 && Side(h[h.Count - 2], h[h.Count - 1], q) <= 0f) h.RemoveAt(h.Count - 1);
                    h.Add(q);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        /// <summary>Intersection of two outlines (one of them must be convex, otherwise the hull of b is used). Counter-clockwise.</summary>
        static List<Vector2> Intersect(List<Vector2> a, List<Vector2> b)
        {
            List<Vector2> subj, clip;
            if (IsConvex(b)) { subj = a; clip = b; }
            else if (IsConvex(a)) { subj = b; clip = a; }
            else { subj = a; clip = ConvexHull(b); }
            clip = new List<Vector2>(clip);
            if (Area(clip) < 0f) clip.Reverse();
            var outp = new List<Vector2>(subj);
            if (Area(outp) < 0f) outp.Reverse();
            for (int i = 0; i < clip.Count && outp.Count > 0; i++)
            {
                Vector2 p = clip[i], q = clip[(i + 1) % clip.Count];
                var input = outp;
                outp = new List<Vector2>();
                for (int k = 0; k < input.Count; k++)
                {
                    Vector2 cur = input[k], prev = input[(k + input.Count - 1) % input.Count];
                    float sc = Side(p, q, cur), sp = Side(p, q, prev);
                    bool cin = sc >= -1e-6f, pin = sp >= -1e-6f;
                    if (cin != pin) outp.Add(Vector2.Lerp(prev, cur, sp / (sp - sc)));
                    if (cin) outp.Add(cur);
                }
            }
            var res = new List<Vector2>();
            foreach (var v in outp)
                if (res.Count == 0 || (v - res[res.Count - 1]).sqrMagnitude > 1e-8f) res.Add(v);
            while (res.Count > 1 && (res[0] - res[res.Count - 1]).sqrMagnitude <= 1e-8f) res.RemoveAt(res.Count - 1);
            return res;
        }

        static Vector3 TangentAt(Chain c, float dist)
        {
            var S = c.samples;
            float acc = 0f;
            for (int i = 0; i + 1 < S.Count; i++)
            {
                float l = Vector3.Distance(S[i], S[i + 1]);
                if (acc + l >= dist || i + 2 == S.Count) return (S[i + 1] - S[i]).normalized;
                acc += l;
            }
            return Vector3.forward;
        }

        void CollectOpenings()
        {
            var mainOf = new Dictionary<GraphEdge, Chain>();
            foreach (var c in chains) { c.openings.Clear(); foreach (var e in c.edges) mainOf[e] = c; }
            var halfOf = new Dictionary<SideOpening, float>();
            foreach (var c in chains)
            {
                if (c.type == null) continue;
                for (int end = 0; end < 2; end++)
                {
                    string id = end == 0 ? c.startJoin : c.endJoin;
                    if (id == null) continue;
                    var J = joins[id];
                    Chain main;
                    if (J.reducer)
                    {
                        // the bigger tunnel ends with a wall that has an opening; the other one too, unless it fits inside whole
                        if (mainOf.TryGetValue(J.e1, out main))
                        {
                            if (main.nodes[0] == id && main.edges[0] == J.e1) main.holeStart = J.hole;
                            else main.holeEnd = J.hole;
                        }
                        if (!J.fits)
                        {
                            var h = new List<Vector2>(J.hole.Count);
                            foreach (var v in J.hole) h.Add(new Vector2(v.x, v.y - J.y0));
                            if (end == 0) c.holeStart = h; else c.holeEnd = h;
                        }
                        continue;
                    }
                    if (!mainOf.TryGetValue(J.e1, out main) || main.type == null || main.nodeDist == null || main.samples.Count < 2) continue;
                    int ni = main.nodes.IndexOf(id);
                    if (ni < 0) continue;
                    float dist = main.nodeDist[ni];
                    if (J.back > 0f) dist += ni == 0 ? J.back : -J.back;
                    Vector3 w, o;
                    BranchPoint(c, end == 0, out w, out o);
                    var right = Vector3.Cross(Vector3.up, TangentAt(main, dist));
                    float y0, x;
                    BranchFit(main.type.section, c.type.section, out y0, out x, J.node.joinLift);
                    var reveal = new List<Vector2>();
                    var P = BranchOpening(main.type.section, c.type.section, y0, x, reveal);
                    if (P.Count < 3) continue;
                    var op = new SideOpening { distance = dist, side = Vector3.Dot(o, right) >= 0f ? 1 : -1 };
                    float half = 0f;
                    foreach (var v in P) { op.outline.Add(new Vector2(v.x, v.y + y0)); half = Mathf.Max(half, Mathf.Abs(v.x)); }
                    main.openings.Add(op);
                    halfOf[op] = half;
                    // the branch end has no rim of its own: its walls run on to the main wall, framed by the hole in it;
                    // a branch taller than the main tunnel gets a wall above the opening
                    bool taller = c.type.section.Height + y0 > main.type.section.Height + 0.005f;
                    if (end == 0) { c.openStart = P; c.revealStart = reveal; if (taller) c.holeStart = P; }
                    else { c.openEnd = P; c.revealEnd = reveal; if (taller) c.holeEnd = P; }
                }
            }
            foreach (var c in chains)
            {
                if (c.openings.Count == 0) continue;
                c.openings.Sort((a, b) => a.distance.CompareTo(b.distance));
                for (int i = 0; i < c.openings.Count; i++)
                    for (int j = i + 1; j < c.openings.Count; j++)
                    {
                        SideOpening a = c.openings[i], b = c.openings[j];
                        if (a.side == b.side && b.distance - a.distance < halfOf[a] + halfOf[b] + 0.3f)
                        {
                            issues.Add(new GraphIssue { edgeId = c.edges[0].id, position = PointAt(c, a.distance), message = "Две врезки с одной стороны слишком близко: разнесите узлы" });
                            break;
                        }
                    }
                // (the slabs across the openings were made straight and level in WidenOpenings)
                CheckJoints(c);
            }
        }

        static Vector3 PointAt(Chain c, float dist)
        {
            var S = c.samples;
            float acc = 0f;
            for (int i = 0; i + 1 < S.Count; i++)
            {
                float l = Vector3.Distance(S[i], S[i + 1]);
                if (acc + l >= dist) return Vector3.Lerp(S[i], S[i + 1], l > 1e-5f ? (dist - acc) / l : 0f);
                acc += l;
            }
            return S[S.Count - 1];
        }

        /// <summary>Replaces the samples between lo and hi by one straight slab. On a gentle slope the slab is also levelled
        /// (so a side tunnel's opening is cut square), when the joints next to it stay within maxAngle degrees.</summary>
        static void WidenSlab(Chain c, float lo, float hi, float maxAngle = -1f)
        {
            var S = c.samples;
            lo = Mathf.Max(lo, 0.3f);
            hi = Mathf.Min(hi, c.total - 0.3f);
            if (hi <= lo) return;
            Vector3 pLo = PointAt(c, lo), pHi = PointAt(c, hi);
            int k;
            if (maxAngle > 0f)
            {
                float y = PointAt(c, (lo + hi) * 0.5f).y;
                if (Mathf.Abs(pLo.y - y) > 0.001f || Mathf.Abs(pHi.y - y) > 0.001f)
                {
                    Vector3 a = new Vector3(pLo.x, y, pLo.z), b = new Vector3(pHi.x, y, pHi.z);
                    // the height change moves to the neighbouring slabs: keep them long, so their joints stay gentle
                    var L = ReplaceSpan(S, lo, hi, a, b, Mathf.Max(1f, c.type != null ? c.type.segmentLength * 0.75f : 1f), out k);
                    float worst = 0f;
                    if (k > 0) worst = Mathf.Max(worst, Vector3.Angle(L[k] - L[k - 1], L[k + 1] - L[k]));
                    if (k + 2 < L.Count) worst = Mathf.Max(worst, Vector3.Angle(L[k + 1] - L[k], L[k + 2] - L[k + 1]));
                    if (worst <= maxAngle * 0.95f) { c.samples = L; return; }
                }
            }
            c.samples = ReplaceSpan(S, lo, hi, pLo, pHi, 0f, out k);
        }

        /// <summary>Samples with the span (lo, hi) replaced by a and b; samples closer than clear to the span are dropped too
        /// (never the first or the last one). k = index of a in the result.</summary>
        static List<Vector3> ReplaceSpan(List<Vector3> S, float lo, float hi, Vector3 a, Vector3 b, float clear, out int k)
        {
            var outList = new List<Vector3>(S.Count + 2);
            float acc = 0f;
            bool addedLo = false, addedHi = false;
            k = -1;
            for (int i = 0; i < S.Count; i++)
            {
                if (i > 0) acc += Vector3.Distance(S[i - 1], S[i]);
                if (!addedLo && acc > lo) { k = outList.Count; outList.Add(a); addedLo = true; }
                if (!addedHi && acc > hi) { outList.Add(b); addedHi = true; }
                bool keep = i == 0 || i == S.Count - 1;
                if (!keep && acc > lo - clear && acc < hi + clear) continue;
                outList.Add(S[i]);
            }
            return outList;
        }

        static float Angle(Vector3 a, Vector3 b)
        {
            if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return 0f;
            return Vector3.Angle(a, b) * Mathf.Deg2Rad;
        }

        static void StraightTo(List<Vector3> S, Vector3 b, float L)
        {
            Vector3 a = S[S.Count - 1];
            float dist = Vector3.Distance(a, b);
            if (dist < 0.02f) { S[S.Count - 1] = b; return; }
            int count = Mathf.Clamp(Mathf.RoundToInt(dist / L), 1, 5000);
            for (int k = 1; k <= count; k++) S.Add(Vector3.Lerp(a, b, (float)k / count));
        }

        static float Project(List<Vector3> S, float[] cum, Vector3 p)
        {
            float best = float.MaxValue, at = 0f;
            for (int i = 0; i + 1 < S.Count; i++)
            {
                Vector3 a = S[i], b = S[i + 1], ab = b - a;
                float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                float d = (a + ab * t - p).sqrMagnitude;
                if (d < best) { best = d; at = cum[i] + (cum[i + 1] - cum[i]) * t; }
            }
            return at;
        }

        // ------------------------------------------------------------------ build

        public void ForceRebuild()
        {
            var root = transform.Find(GeneratedRoot);
            if (root != null)
            {
                foreach (var p in root.GetComponentsInChildren<TunnelPath>(true)) p.drivenSignature = 0;
                foreach (var j in root.GetComponentsInChildren<TunnelJunction>(true)) j.drivenSignature = 0;
            }
            MarkDirty();
        }

        public void Build()
        {
            buildDirty = false;
            EnsureLayout();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int builtPaths = 0, builtChambers = 0;
            var root = transform.Find(GeneratedRoot);
            if (root == null)
            {
                var go = new GameObject(GeneratedRoot);
                go.transform.SetParent(transform, false);
                root = go.transform;
            }
            var keep = new HashSet<Transform>();
            var pathOf = new Dictionary<Chain, TunnelPath>();
            var libMats = library != null ? library.materials : null;
            if (library != null) { TunnelTypeLibrary.Active = library; library.ApplyAtmosphere(); }

            foreach (var c in chains)
            {
                if (c.type == null || c.samples.Count < 2) continue;
                var t = Child(root, c.key);
                keep.Add(t);
                var path = t.GetComponent<TunnelPath>();
                if (path == null) path = t.gameObject.AddComponent<TunnelPath>();
                pathOf[c] = path;
                var shafts = MapShafts(c);
                // end walls: dead ends, and walls with an opening (type change, a tunnel taller than what it enters)
                bool holeS = c.holeStart != null && c.holeStart.Count >= 3, holeE = c.holeEnd != null && c.holeEnd.Count >= 3;
                bool capS = holeS || (c.startSide < 0 && c.startDoor == null && c.startJoin == null);
                bool capE = holeE || (c.endSide < 0 && c.endDoor == null && c.endJoin == null);
                // an exit to the outside: an open end with the slab's rim, no wall
                bool exitS = capS && !holeS && IsExit(c.nodes[0]);
                bool exitE = capE && !holeE && IsExit(c.nodes[c.nodes.Count - 1]);
                if (exitS) capS = false;
                if (exitE) capE = false;
                // A bridge end is an opening for this build only. The node stays a dead end in the graph YAML.
                if (c.nodes.Count > 0 && !exitS && !holeS && TunnelBridge.Opens(this, c.nodes[0])) capS = false;
                if (c.nodes.Count > 0 && !exitE && !holeE && TunnelBridge.Opens(this, c.nodes[c.nodes.Count - 1])) capE = false;
                if (exitS || exitE) BuildExits(root, keep, c, exitS, exitE, libMats);
                int sig = unchecked(Signature(c, shafts, capS, capE) * 31 + (exitS ? 13 : 0) + (exitE ? 29 : 0));
                if (path.drivenSignature == sig && path.drivenSamples.Count == c.samples.Count) continue;
                var T = c.type;
                path.section = T.section.Clone();
                path.segmentLength = T.segmentLength;
                path.slabThickness = T.slabThickness;
                path.minJointGap = T.minJointGap;
                path.jointSink = T.jointSink;
                path.edgeBevel = T.edgeBevel;
                path.dirtLine = T.dirtLine;
                path.channelWater = T.channelWater;
                path.waterDepth = T.waterDepth;
                path.materials = T.materials != null ? T.materials : libMats;
                path.seed = c.edges[0].seed;
                path.startAtWall = !exitS && (!capS || holeS);   // no rim where it meets another tunnel or room
                path.endAtWall = !exitE && (!capE || holeE);
                path.capStart = capS;
                path.capEnd = capE;
                path.straightEnds = false;
                path.shafts = shafts;
                path.openings = new List<SideOpening>(c.openings);
                path.capHoleStart = holeS ? new List<Vector2>(c.holeStart) : new List<Vector2>();
                path.capHoleEnd = holeE ? new List<Vector2>(c.holeEnd) : new List<Vector2>();
                path.startOpening = c.openStart != null ? new List<Vector2>(c.openStart) : new List<Vector2>();
                path.endOpening = c.openEnd != null ? new List<Vector2>(c.openEnd) : new List<Vector2>();
                path.startReveal = c.revealStart != null ? new List<Vector2>(c.revealStart) : new List<Vector2>();
                path.endReveal = c.revealEnd != null ? new List<Vector2>(c.revealEnd) : new List<Vector2>();
                path.drivenMiters = new List<Vector3>(c.elbows);
                path.startPlane = c.startDoor != null && c.startDoor.stretch > 1.001f ? c.startDoor.wallNormal : Vector3.zero;
                path.endPlane = c.endDoor != null && c.endDoor.stretch > 1.001f ? c.endDoor.wallNormal : Vector3.zero;
                path.seamless = T.seamless;
                path.drivenSamples = new List<Vector3>(c.samples);
                path.drivenSignature = sig;
                path.Rebuild(true);
                builtPaths++;
                if (puddles && (library == null || library.puddles) && DressingHook != null) DressingHook(path, c.edges[0].seed);
            }
            // the look says no puddles: take away the ones scattered earlier
            if (library != null && !library.puddles)
                foreach (var kv in pathOf)
                {
                    var d = kv.Value.transform.Find(TunnelPath.DressingName);
                    if (d == null) continue;
                    if (Application.isPlaying) Destroy(d.gameObject); else DestroyImmediate(d.gameObject);
                }

            foreach (var kv in chambers)
            {
                var info = kv.Value;
                var n = info.node;
                var t = Child(root, "Chamber_" + n.id);
                keep.Add(t);
                var j = t.GetComponent<TunnelJunction>();
                if (j == null) j = t.gameObject.AddComponent<TunnelJunction>();
                TunnelPath[] sides = new TunnelPath[4];
                for (int s = 0; s < 4; s++)
                {
                    TunnelPath p;
                    if (info.sideChains[s] != null && pathOf.TryGetValue(info.sideChains[s], out p)) sides[s] = p;
                }
                int sig = 17;
                unchecked
                {
                    sig = sig * 31 + n.position.GetHashCode();
                    sig = sig * 31 + info.yaw.GetHashCode();
                    sig = sig * 31 + n.chamberSize.GetHashCode();
                    sig = sig * 31 + n.chamberHeight.GetHashCode();
                    sig = sig * 31 + (n.chamberShaft ? Hash(JsonUtility.ToJson(n.shaft)) : 1);
                    sig = sig * 31 + n.seed;
                    for (int s = 0; s < 4; s++) sig = sig * 31 + (sides[s] != null ? sides[s].drivenSignature : 0);
                }
                t.localPosition = n.position;
                t.localRotation = Quaternion.Euler(0f, info.yaw, 0f);
                if (j.drivenSignature == sig) continue;
                j.sizeX = n.chamberSize.x;
                j.sizeZ = n.chamberSize.y;
                j.height = n.chamberHeight;
                j.hasShaft = n.chamberShaft;
                j.shaft = n.shaft;
                j.seed = n.seed;
                j.materials = libMats;
                for (int s = 0; s < 4; s++) j.SetSide(s, sides[s]);
                j.drivenSignature = sig;
                j.Rebuild(true);
                builtChambers++;
            }

            foreach (var kv in rooms)
            {
                var info = kv.Value;
                var n = info.node;
                var t = Child(root, "Room_" + n.id);
                keep.Add(t);
                t.localPosition = n.position;
                t.localRotation = Quaternion.identity;
                var room = t.GetComponent<TunnelRoom>();
                if (room == null) room = t.gameObject.AddComponent<TunnelRoom>();
                var doorList = new List<RoomDoor>();
                foreach (var d in info.doors)
                {
                    List<int> kind; int ceil;
                    var o = d.chain.type.section.BaseOutline(out kind, out ceil);
                    for (int i = 0; i < o.Count; i++) o[i] = new Vector2(o[i].x * d.stretch, o[i].y);
                    doorList.Add(new RoomDoor { wall = d.wall, along = d.along, outline = o });
                }
                int sig = 23;
                unchecked
                {
                    foreach (var v in info.corners) sig = sig * 31 + Mathf.RoundToInt(v.x * 1000f) * 7 + Mathf.RoundToInt(v.y * 1000f);
                    sig = sig * 31 + Mathf.RoundToInt(info.height * 1000f);
                    foreach (var d in doorList) sig = sig * 31 + Hash(JsonUtility.ToJson(d));
                    foreach (var col in n.columns) sig = sig * 31 + Hash(JsonUtility.ToJson(col));
                    sig = sig * 31 + n.seed;
                    sig = sig * 31 + (libMats != null ? Hash(libMats.name) : 0);
                    sig = sig * 31 + 2;   // build version
                }
                if (room.drivenSignature == sig && sig != 0) continue;
                room.corners = new List<Vector2>(info.corners);
                room.height = info.height;
                room.doors = doorList;
                room.columns = new List<ColumnSpec>();
                foreach (var col in n.columns) room.columns.Add(JsonUtility.FromJson<ColumnSpec>(JsonUtility.ToJson(col)));
                room.seed = n.seed;
                room.materials = libMats;
                room.drivenSignature = sig;
                room.Rebuild();
                builtChambers++;
            }

            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask |= TunnelRenderingLayer;
            var dead = new List<GameObject>();
            foreach (Transform ch in root) if (!keep.Contains(ch)) dead.Add(ch.gameObject);
            foreach (var g in dead)
            {
                if (Application.isPlaying) Destroy(g); else DestroyImmediate(g);
            }
            if (BuiltHook != null)
            {
                try { BuiltHook(this); }
                catch (Exception ex) { Debug.LogException(ex, this); }
            }
            if (builtPaths + builtChambers + dead.Count > 0)
                Debug.Log("CarX graph build: tunnels " + builtPaths + ", chambers " + builtChambers + ", removed " + dead.Count + ", " + watch.ElapsedMilliseconds + " ms");
        }

        void BuildExits(Transform root, HashSet<Transform> keep, Chain c, bool atStart, bool atEnd, TunnelMaterials mats)
        {
            int n = c.samples.Count;
            for (int end = 0; end < 2; end++)
            {
                if (end == 0 ? !atStart : !atEnd) continue;
                string id = end == 0 ? c.nodes[0] : c.nodes[c.nodes.Count - 1];
                Vector3 p = end == 0 ? c.samples[0] : c.samples[n - 1];
                Vector3 q = end == 0 ? c.samples[1] : c.samples[n - 2];
                Vector3 dir = p - q;
                if (dir.sqrMagnitude < 1e-6f) continue;
                var t = Child(root, "Exit_" + id);
                keep.Add(t);
                t.localPosition = p;
                t.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                var ex = t.GetComponent<TunnelExit>();
                if (ex == null) ex = t.gameObject.AddComponent<TunnelExit>();
                var sec = c.type.section;
                ex.width = sec.HalfSpan * 2f;
                ex.height = sec.Height;
                ex.brightness = nodeMap[id].exitBrightness;
                ex.skyMaterial = mats != null ? mats.sky : null;
                ex.fakeSky = !nodeMap[id].exitOpenAir;
                ex.tunnelShadowsOnly = nodeMap[id].exitOpenAir;
                if (library != null)
                {
                    ex.lightIntensity = library.exitLightIntensity;
                    ex.lightRange = library.exitLightRange;
                    ex.lightBack = library.exitLightBack;
                    ex.lightColor = library.daylightColor;
                    ex.skyColor = library.SkyColor;
                    ex.exposureProfile = library.exitExposure;
                    ex.exposureDepth = library.exitExposureDepth;
                    ex.shadows = library.exitShadows;
                }
                ex.Rebuild();
            }
        }

        static Transform Child(Transform root, string name)
        {
            var t = root.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            return go.transform;
        }

        List<ShaftSpec> MapShafts(Chain c)
        {
            var list = new List<ShaftSpec>();
            for (int j = 0; j < c.edges.Count; j++)
            {
                var e = c.edges[j];
                bool forward = e.a == c.nodes[j];
                float dA = forward ? c.nodeDist[j] : c.nodeDist[j + 1];
                foreach (var s in e.shafts)
                {
                    var k = s.Clone();
                    k.distance = Mathf.Clamp(forward ? dA + s.distance : dA - s.distance, 0.5f, Mathf.Max(0.5f, c.total - 0.5f));
                    list.Add(k);
                }
            }
            return list;
        }

        int Signature(Chain c, List<ShaftSpec> shafts, bool capS, bool capE)
        {
            unchecked
            {
                // only values that stay the same between editor sessions (no instance ids)
                var T = c.type;
                int h = Hash(T.name + "|" + JsonUtility.ToJson(T.section));
                h = h * 31 + Hash(T.segmentLength + "|" + T.maxJointAngle + "|" + T.slabThickness + "|" + T.minJointGap + "|" + T.jointSink + "|" +
                                  T.edgeBevel + "|" + T.dirtLine + "|" + T.seamless + "|" + T.channelWater + "|" + T.waterDepth + "|" + (T.materials != null ? T.materials.name : ""));
                foreach (var p in c.samples)
                {
                    h = h * 31 + Mathf.RoundToInt(p.x * 1000f);
                    h = h * 31 + Mathf.RoundToInt(p.y * 1000f);
                    h = h * 31 + Mathf.RoundToInt(p.z * 1000f);
                }
                foreach (var s in shafts) h = h * 31 + Hash(JsonUtility.ToJson(s));
                foreach (var o in c.openings) h = h * 31 + Hash(JsonUtility.ToJson(o));
                if (c.holeStart != null) foreach (var v in c.holeStart) h = h * 31 + Mathf.RoundToInt(v.x * 1000f) * 7 + Mathf.RoundToInt(v.y * 1000f);
                h = h * 31 + 17;
                foreach (var v in c.elbows) h = h * 31 + Mathf.RoundToInt(v.x * 100f) * 3 + Mathf.RoundToInt(v.z * 100f);
                if (c.startDoor != null) h = h * 31 + Mathf.RoundToInt(c.startDoor.stretch * 1000f) + Mathf.RoundToInt(c.startDoor.wallNormal.x * 1000f) * 5;
                if (c.endDoor != null) h = h * 31 + Mathf.RoundToInt(c.endDoor.stretch * 1000f) + Mathf.RoundToInt(c.endDoor.wallNormal.x * 1000f) * 5;
                if (c.holeEnd != null) foreach (var v in c.holeEnd) h = h * 31 + Mathf.RoundToInt(v.x * 1000f) * 7 + Mathf.RoundToInt(v.y * 1000f);
                foreach (var l in new[] { c.openStart, c.revealStart, c.openEnd, c.revealEnd })
                {
                    h = h * 31 + 19;
                    if (l != null) foreach (var v in l) h = h * 31 + Mathf.RoundToInt(v.x * 1000f) * 7 + Mathf.RoundToInt(v.y * 1000f);
                }
                h = h * 31 + 3;   // build version: bump when the mesh code changes
                h = h * 31 + c.edges[0].seed;
                h = h * 31 + (capS ? 3 : 5) + (capE ? 7 : 11);
                h = h * 31 + (library != null && library.materials != null ? Hash(library.materials.name) : 0);
                return h == 0 ? 1 : h;
            }
        }

        static int Hash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char ch in s) { h ^= ch; h *= 16777619; }
                return (int)h;
            }
        }
    }
}
