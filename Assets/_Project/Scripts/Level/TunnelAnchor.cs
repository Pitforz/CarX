using UnityEngine;

namespace CarX.Level
{
    public enum AnchorSurface
    {
        [InspectorName("Сам (по положению)")] Auto,
        [InspectorName("Пол")] Floor,
        [InspectorName("Стена")] Wall,
        [InspectorName("Потолок")] Ceiling,
        [InspectorName("Свободно (в воздухе)")] Free,
    }

    public enum AnchorMode { None, Tunnel, Chamber, Room, RoomWall }

    /// <summary>Keeps a prop, NPC spawn or decal in place on the tunnel graph: when nodes move or the tunnel type changes,
    /// the object stays at the same distance from the nearest node that did not move, on the same surface (floor, wall,
    /// ceiling), and keeps its rotation relative to that surface. Moving the object by hand simply re-binds it.
    /// Works in the editor only; in play mode the object stays where the editor left it.</summary>
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("CarX/Tunnel Anchor")]
    public class TunnelAnchor : MonoBehaviour
    {
        public TunnelNetwork network;
        [Tooltip("К какой поверхности прижат объект. «Сам» выбирает по положению: пол, стена, потолок или в воздухе.")]
        public AnchorSurface surface = AnchorSurface.Auto;

        // binding (written by Bind/Apply)
        [SerializeField, HideInInspector] AnchorMode mode;
        [SerializeField, HideInInspector] AnchorSurface bound;
        [SerializeField, HideInInspector] string edgeId, nodeId, aId, bId;
        [SerializeField, HideInInspector] Vector3 aPos, bPos;      // node (or room corner) positions at the last apply
        [SerializeField, HideInInspector] float span, distance;    // line length and the distance from node A along it
        [SerializeField, HideInInspector] int wall;
        [SerializeField, HideInInspector] float lead, across, height, offset;
        [SerializeField, HideInInspector] Vector3 local;
        [SerializeField, HideInInspector] Quaternion rotation = Quaternion.identity;   // object rotation in the surface frame
        [SerializeField, HideInInspector] Vector3 appliedPos;
        [SerializeField, HideInInspector] Quaternion appliedRot = Quaternion.identity;
        [SerializeField, HideInInspector] bool lost;
        [SerializeField, HideInInspector] AnchorSurface lastSurface;

        bool busy;

        public bool Lost { get { return lost; } }
        public AnchorMode Mode { get { return mode; } }
        public AnchorSurface BoundSurface { get { return bound; } }
        public string EdgeId { get { return edgeId; } }
        public string NodeId { get { return nodeId; } }
        public string NodeA { get { return aId; } }
        public float Distance { get { return distance; } }

        void OnEnable()
        {
            if (Application.isPlaying) return;
            TunnelNetwork.LaidOut -= OnLaidOut;
            TunnelNetwork.LaidOut += OnLaidOut;
            if (mode == AnchorMode.None && !lost) Bind();
        }

        void OnDisable() { TunnelNetwork.LaidOut -= OnLaidOut; }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();   // Update re-binds when the surface changed
        }
#endif

        void Update()
        {
            if (Application.isPlaying || busy || !gameObject.scene.IsValid()) return;
            var net = Net();
            if (net == null) return;
            if (surface != lastSurface) { Bind(); return; }
            Vector3 p = net.transform.InverseTransformPoint(transform.position);
            Quaternion r = Quaternion.Inverse(net.transform.rotation) * transform.rotation;
            if ((p - appliedPos).sqrMagnitude > 1e-8f || Quaternion.Angle(r, appliedRot) > 0.01f) Bind();   // moved by hand
        }

        void OnLaidOut(TunnelNetwork net)
        {
            if (busy || this == null || !isActiveAndEnabled || Application.isPlaying) return;
            if (net != Net()) return;
            Apply();
        }

        TunnelNetwork Net()
        {
            if (network != null) return network;
            network = GetComponentInParent<TunnelNetwork>();
            if (network != null) return network;
            float best = float.MaxValue;
            foreach (var n in FindObjectsByType<TunnelNetwork>(FindObjectsInactive.Exclude))
            {
                if (n.gameObject.scene != gameObject.scene) continue;
                foreach (var node in n.nodes)
                {
                    float d = (n.World(node.position) - transform.position).sqrMagnitude;
                    if (d < best) { best = d; network = n; }
                }
            }
            return network;
        }

        // ------------------------------------------------------------------ bind: remember where the object is

        /// <summary>Re-binds the object at its current position (what moving it by hand does).</summary>
        public void Bind()
        {
            var net = Net();
            if (net == null || busy) return;
            busy = true;
            try { BindNow(net); }
            finally { busy = false; }
        }

        void BindNow(TunnelNetwork net)
        {
            net.EnsureLayout();
            lastSurface = surface;
            Vector3 p = net.transform.InverseTransformPoint(transform.position);
            Quaternion r = Quaternion.Inverse(net.transform.rotation) * transform.rotation;
            lost = false;

            var room = net.RoomAt(p, 0.1f);
            if (room != null)
            {
                var n = room.node;
                var v = new Vector2(p.x - n.position.x, p.z - n.position.z);
                int w; float along;
                float d = TunnelNetwork.PolyDistance(room.corners, v, out w, out along);
                bool onWall = surface == AnchorSurface.Wall || (surface == AnchorSurface.Auto && d < 0.35f);
                if (onWall && w >= 0)
                {
                    mode = AnchorMode.RoomWall;
                    bound = AnchorSurface.Wall;
                    nodeId = n.id;
                    wall = w;
                    Vector2 a = room.corners[w], b = room.corners[(w + 1) % room.corners.Count];
                    aPos = new Vector3(a.x, 0f, a.y);
                    bPos = new Vector3(b.x, 0f, b.y);
                    span = Vector2.Distance(a, b);
                    distance = along;
                    Vector3 o, t, inward;
                    RoomWallFrame(n, a, b, along, out o, out t, out inward);
                    offset = Vector3.Dot(p - o, inward);
                    height = p.y - n.position.y;
                    lead = Vector3.Dot(p - o, t);
                    var fr = Quaternion.LookRotation(t, inward);
                    rotation = Quaternion.Inverse(fr) * r;
                }
                else
                {
                    mode = AnchorMode.Room;
                    bound = surface == AnchorSurface.Auto ? AnchorSurface.Free : surface;
                    nodeId = n.id;
                    local = p - n.position;
                    rotation = r;
                }
                Done(net);
                return;
            }

            var ch = net.ChamberAt(p, 0.05f);
            if (ch != null)
            {
                mode = AnchorMode.Chamber;
                bound = surface == AnchorSurface.Auto ? AnchorSurface.Free : surface;
                nodeId = ch.node.id;
                var yaw = Quaternion.Euler(0f, ch.yaw, 0f);
                local = Quaternion.Inverse(yaw) * (p - ch.node.position);
                rotation = Quaternion.Inverse(yaw) * r;
                Done(net);
                return;
            }

            GraphEdge e;
            float dist;
            if (!net.ClosestOnTunnels(p, 1.5f, out e, out dist) || !net.EdgeSpan(e, out span))
            {
                mode = AnchorMode.None;
                lost = true;
                Done(net);
                return;
            }
            mode = AnchorMode.Tunnel;
            edgeId = e.id;
            aId = e.a;
            bId = e.b;
            aPos = net.Node(e.a).position;
            bPos = net.Node(e.b).position;
            distance = dist;
            Vector3 axis, dir;
            TunnelSection sec;
            net.EdgeFrame(e, dist, out axis, out dir, out sec);
            Vector3 right, up;
            Frame(dir, out right, out up);
            var q = p - axis;
            lead = Vector3.Dot(q, dir);
            across = Vector3.Dot(q, right);
            height = Vector3.Dot(q, up);
            bound = surface != AnchorSurface.Auto ? surface : Classify(sec, across, height);
            switch (bound)
            {
                case AnchorSurface.Floor: offset = height - sec.FloorHeightAt(across); break;
                case AnchorSurface.Ceiling: offset = sec.CeilingHeightAt(across) - height; break;
                case AnchorSurface.Wall: offset = sec.WallXAt(WallY(sec, height)) - Mathf.Abs(across); break;
                default: offset = 0f; break;
            }
            Vector3 pos, n2;
            SurfacePoint(sec, axis, dir, right, up, out pos, out n2);
            rotation = Quaternion.Inverse(SurfaceFrame(dir, up, n2)) * r;
            Done(net);
        }

        static AnchorSurface Classify(TunnelSection sec, float x, float y)
        {
            float dF = y - sec.FloorHeightAt(x);
            float dC = sec.CeilingHeightAt(x) - y;
            float dW = sec.WallXAt(WallY(sec, y)) - Mathf.Abs(x);
            float m = Mathf.Min(Mathf.Abs(dF), Mathf.Abs(dC), Mathf.Abs(dW));
            if (m > 0.35f) return AnchorSurface.Free;
            if (Mathf.Abs(dF) <= m) return AnchorSurface.Floor;
            if (Mathf.Abs(dW) <= m) return AnchorSurface.Wall;
            return AnchorSurface.Ceiling;
        }

        // ------------------------------------------------------------------ apply: follow the graph

        /// <summary>Moves the object after the graph changed: same distance from the nearest node that did not move, same surface.</summary>
        public void Apply()
        {
            var net = Net();
            if (net == null || busy) return;
            busy = true;
            try
            {
                if (!ApplyNow(net)) BindNow(net);   // the line or node it was on is gone: bind where it stands
            }
            finally { busy = false; }
        }

        bool ApplyNow(TunnelNetwork net)
        {
            net.EnsureLayout();
            if (mode == AnchorMode.None) return false;
            Vector3 p;
            Quaternion r;
            if (mode == AnchorMode.Tunnel)
            {
                var e = net.Edge(edgeId);
                float len;
                if (e == null || e.a != aId || e.b != bId || !net.EdgeSpan(e, out len)) return false;
                Vector3 A = net.Node(e.a).position, B = net.Node(e.b).position;
                distance = KeepDistance(distance, span, len, (A - aPos).sqrMagnitude < 1e-8f, (B - bPos).sqrMagnitude < 1e-8f);
                aPos = A;
                bPos = B;
                span = len;
                Vector3 axis, dir;
                TunnelSection sec;
                if (!net.EdgeFrame(e, distance, out axis, out dir, out sec)) return false;
                Vector3 right, up, n;
                Frame(dir, out right, out up);
                SurfacePoint(sec, axis, dir, right, up, out p, out n);
                r = SurfaceFrame(dir, up, n) * rotation;
            }
            else
            {
                var node = net.Node(nodeId);
                if (node == null) return false;
                if (mode == AnchorMode.Chamber)
                {
                    TunnelNetwork.ChamberInfo ch;
                    if (!net.chambers.TryGetValue(nodeId, out ch)) return false;
                    var yaw = Quaternion.Euler(0f, ch.yaw, 0f);
                    p = node.position + yaw * local;
                    r = yaw * rotation;
                }
                else
                {
                    TunnelNetwork.RoomInfo room;
                    if (!net.rooms.TryGetValue(nodeId, out room)) return false;
                    if (mode == AnchorMode.Room)
                    {
                        p = node.position + local;
                        r = rotation;
                    }
                    else
                    {
                        var poly = room.corners;
                        int w = FindWall(poly, wall, aPos, bPos);
                        if (w < 0) return false;
                        Vector2 a = poly[w], b = poly[(w + 1) % poly.Count];
                        Vector3 A = new Vector3(a.x, 0f, a.y), B = new Vector3(b.x, 0f, b.y);
                        float len = Vector2.Distance(a, b);
                        distance = Mathf.Clamp(KeepDistance(distance, span, len, (A - aPos).sqrMagnitude < 1e-8f, (B - bPos).sqrMagnitude < 1e-8f), 0f, len);
                        wall = w;
                        aPos = A;
                        bPos = B;
                        span = len;
                        Vector3 o, t, inward;
                        RoomWallFrame(node, a, b, distance, out o, out t, out inward);
                        p = o + t * lead + inward * offset + Vector3.up * height;
                        r = Quaternion.LookRotation(t, inward) * rotation;
                    }
                }
            }
            Place(net, p, r);
            return true;
        }

        /// <summary>The rule: keep the distance from the nearest node that stayed put; both moved: keep the fraction.</summary>
        static float KeepDistance(float d, float oldLen, float newLen, bool aSame, bool bSame)
        {
            if (Mathf.Abs(newLen - oldLen) < 1e-5f) return d;
            if (aSame && bSame) return d <= oldLen * 0.5f ? d : newLen - (oldLen - d);
            if (aSame) return d;
            if (bSame) return newLen - (oldLen - d);
            return oldLen > 1e-4f ? d / oldLen * newLen : d;
        }

        static int FindWall(System.Collections.Generic.List<Vector2> poly, int w, Vector3 A, Vector3 B)
        {
            int n = poly.Count;
            System.Func<int, Vector3> P = i => new Vector3(poly[i % n].x, 0f, poly[i % n].y);
            if (w >= 0 && w < n && ((P(w) - A).sqrMagnitude < 1e-6f || (P(w + 1) - B).sqrMagnitude < 1e-6f)) return w;
            for (int i = 0; i < n; i++)
                if ((P(i) - A).sqrMagnitude < 1e-6f || (P(i + 1) - B).sqrMagnitude < 1e-6f) return i;
            return w >= 0 && w < n ? w : -1;
        }

        static void RoomWallFrame(GraphNode n, Vector2 a, Vector2 b, float along, out Vector3 o, out Vector3 t, out Vector3 inward)
        {
            var d = (b - a).normalized;
            t = new Vector3(d.x, 0f, d.y);
            inward = new Vector3(-d.y, 0f, d.x);   // corners are counter-clockwise: inside is on the left
            o = n.position + new Vector3(a.x, 0f, a.y) + t * along;
        }

        static void Frame(Vector3 dir, out Vector3 right, out Vector3 up)
        {
            right = Vector3.Cross(Vector3.up, dir);
            right = right.sqrMagnitude < 1e-6f ? Vector3.right : right.normalized;
            up = Vector3.Cross(dir, right).normalized;
        }

        /// <summary>The object's position for the bound surface, and that surface's normal (into the tunnel).</summary>
        void SurfacePoint(TunnelSection sec, Vector3 axis, Vector3 dir, Vector3 right, Vector3 up, out Vector3 pos, out Vector3 normal)
        {
            float x = across, y = height;
            Vector2 n2 = new Vector2(0f, 1f);
            const float h = 0.02f;
            switch (bound)
            {
                case AnchorSurface.Floor:
                    y = sec.FloorHeightAt(x) + offset;
                    n2 = new Vector2(-(sec.FloorHeightAt(x + h) - sec.FloorHeightAt(x - h)) / (2f * h), 1f);
                    break;
                case AnchorSurface.Ceiling:
                    y = sec.CeilingHeightAt(x) - offset;
                    n2 = new Vector2((sec.CeilingHeightAt(x + h) - sec.CeilingHeightAt(x - h)) / (2f * h), -1f);
                    break;
                case AnchorSurface.Wall:
                {
                    float s = x < 0f ? -1f : 1f, yc = WallY(sec, y);
                    x = s * (sec.WallXAt(yc) - offset);
                    float slope = (sec.WallXAt(yc + h) - sec.WallXAt(yc - h)) / (2f * h);
                    n2 = new Vector2(-s, slope);
                    break;
                }
            }
            n2.Normalize();
            pos = axis + dir * lead + right * x + up * y;
            normal = right * n2.x + up * n2.y;
            if (bound == AnchorSurface.Free) normal = up;
        }

        static float WallY(TunnelSection sec, float y) { return Mathf.Clamp(y, 0.02f, sec.Height - 0.02f); }

        static Quaternion SurfaceFrame(Vector3 dir, Vector3 up, Vector3 normal)
        {
            var f = Vector3.ProjectOnPlane(dir, normal);
            if (f.sqrMagnitude < 1e-6f) f = Vector3.ProjectOnPlane(up, normal);
            return Quaternion.LookRotation(f.normalized, normal);
        }

        void Place(TunnelNetwork net, Vector3 p, Quaternion r)
        {
            var wp = net.transform.TransformPoint(p);
            var wr = net.transform.rotation * r;
            bool moved = (transform.position - wp).sqrMagnitude > 1e-6f || Quaternion.Angle(transform.rotation, wr) > 0.05f;
            transform.SetPositionAndRotation(wp, wr);
            Done(net);
#if UNITY_EDITOR
            if (moved && gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

        void Done(TunnelNetwork net)
        {
            appliedPos = net.transform.InverseTransformPoint(transform.position);
            appliedRot = Quaternion.Inverse(net.transform.rotation) * transform.rotation;
        }

        // ------------------------------------------------------------------ gizmo

        void OnDrawGizmosSelected()
        {
            var net = Net();
            if (net == null) return;
            if (lost) { Gizmos.color = Color.red; Gizmos.DrawWireSphere(transform.position, 0.4f); return; }
            Gizmos.color = new Color(1f, 0.8f, 0.2f);
            if (mode == AnchorMode.Tunnel)
            {
                var e = net.Edge(edgeId);
                Vector3 axis, dir;
                TunnelSection sec;
                if (e != null && net.EdgeFrame(e, distance, out axis, out dir, out sec))
                {
                    Gizmos.DrawLine(transform.position, net.World(axis));
                    var a = net.Node(e.a);
                    if (a != null) Gizmos.DrawLine(net.World(axis), net.World(a.position));
                }
            }
            else if (!string.IsNullOrEmpty(nodeId))
            {
                var n = net.Node(nodeId);
                if (n != null) Gizmos.DrawLine(transform.position, net.World(n.position));
            }
        }
    }
}
