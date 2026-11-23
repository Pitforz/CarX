using System.Collections.Generic;
using CarX.NPC;
using UnityEngine;

namespace CarX.Level
{
    /// <summary>
    /// A bridge between a dead end of one graph and a dead end of another.
    /// The span is exactly one slab (one segment of tunnel A's type) and sits flush on tunnel A's end.
    /// Graph B is moved and turned (about the vertical) as a whole so that its dead end meets the span's far end, on the same axis.
    /// Nodes, edges and shafts of either TunnelNetwork are never written; only graph B's transform changes.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("CarX/Tunnel Bridge")]
    public class TunnelBridge : MonoBehaviour
    {
        public const int MaxPerNetwork = 2;

        [Tooltip("Первый граф. Стоит на месте")]
        public TunnelNetwork networkA;
        [Tooltip("Тупик первого графа")]
        public string nodeA;
        [Tooltip("Второй граф. Двигается и поворачивается целиком, чтобы его тупик встал встык с пролётом")]
        public TunnelNetwork networkB;
        [Tooltip("Тупик второго графа")]
        public string nodeB;
        [Tooltip("Не используется: пролёт берёт тип тоннеля графа A, чтобы сечения совпали")]
        public TunnelType type;

        public bool Built { get; private set; }
        public string Status { get; private set; }
        /// <summary>Built, but something will not fit (e.g. the two tunnels are of different types).</summary>
        public string Note { get; private set; }
        public float SpanWidth { get; private set; }
        public float SpanHeight { get; private set; }
        public IReadOnlyList<Vector3> Floors => floors;

        static int revision;
        readonly List<Vector3> floors = new List<Vector3>();
        int seen = -1;
        bool hasLast;
        Vector3 lastPA, lastDA, lastPB, lastDB;

        public static void Touch() { revision++; }

        void OnEnable()
        {
            Apply();
            seen = revision;
            Remember();
            if (Application.isPlaying) EnsureNav();
        }

        void OnValidate() { revision++; }

        void OnDisable()
        {
            revision++;
            TouchNetworks();
        }

        void Update()
        {
            Vector3 pa, da, pb, db;
            bool moved = Frames(out pa, out da, out pb, out db) &&
                         (!hasLast || pa != lastPA || da != lastDA || pb != lastPB || db != lastDB);
            if (seen == revision && !moved) return;
            Apply();
            seen = revision;
            Remember();
        }

        public void RebuildNow()
        {
            Apply();
            seen = revision;
            Remember();
        }

        void Remember()
        {
            hasLast = Frames(out lastPA, out lastDA, out lastPB, out lastDB);
        }

        bool Frames(out Vector3 pa, out Vector3 da, out Vector3 pb, out Vector3 db)
        {
            TunnelType ta, tb;
            pb = db = Vector3.zero;
            return TryEnd(networkA, nodeA, out pa, out da, out ta) && TryEnd(networkB, nodeB, out pb, out db, out tb);
        }

        /// <summary>This graph node is an end of an accepted bridge, so the build leaves the cap off.</summary>
        public static bool Opens(TunnelNetwork net, string nodeId)
        {
            if (net == null || string.IsNullOrEmpty(nodeId)) return false;
            string unused;
            var accepted = Accepted(null, out unused);
            for (int i = 0; i < accepted.Count; i++)
                if (Takes(accepted[i], net, nodeId)) return true;
            return false;
        }

        /// <summary>True when another accepted bridge already uses this node.</summary>
        public static bool NodeTaken(TunnelNetwork net, string nodeId, TunnelBridge except)
        {
            if (net == null || string.IsNullOrEmpty(nodeId)) return false;
            string unused;
            var accepted = Accepted(null, out unused);
            for (int i = 0; i < accepted.Count; i++)
            {
                var b = accepted[i];
                if (b == except) continue;
                if (Takes(b, net, nodeId)) return true;
            }
            return false;
        }

        /// <summary>Floor samples for audio. Only graph A claims the span, so two binders do not place it twice.</summary>
        public bool TryAudioSpan(TunnelNetwork asker, List<Vector3> into, out float width, out float height)
        {
            width = SpanWidth;
            height = SpanHeight;
            if (into != null) into.Clear();
            if (!Built || asker == null || asker != networkA || floors.Count < 2) return false;
            if (into != null) into.AddRange(floors);
            return true;
        }

        public bool TryWorldEnds(out Vector3 worldA, out Vector3 worldB)
        {
            worldA = worldB = Vector3.zero;
            var na = NodeOf(networkA, nodeA);
            var nb = NodeOf(networkB, nodeB);
            if (na == null || nb == null) return false;
            worldA = networkA.World(na.position);
            worldB = networkB.World(nb.position);
            return true;
        }

        /// <summary>
        /// The tunnel end at a dead-end node: the end point of the axis (world), the outward direction (world) and the tunnel type.
        /// False when no tunnel of the graph ends with a dead end at this node.
        /// </summary>
        public static bool TryEnd(TunnelNetwork net, string nodeId, out Vector3 point, out Vector3 outward, out TunnelType tunnelType)
        {
            point = outward = Vector3.zero;
            tunnelType = null;
            if (net == null || string.IsNullOrEmpty(nodeId) || net.Node(nodeId) == null) return false;
            net.EnsureLayout();
            var chains = net.chains;
            for (int i = 0; i < chains.Count; i++)
            {
                var c = chains[i];
                if (c == null || c.type == null || c.samples.Count < 2 || c.nodes.Count < 2) continue;
                int n = c.samples.Count;
                Vector3 p, q;
                if (c.nodes[0] == nodeId && c.startSide < 0 && c.startJoin == null && c.startDoor == null &&
                    (c.holeStart == null || c.holeStart.Count < 3))
                {
                    p = c.samples[0];
                    q = c.samples[1];
                }
                else if (c.nodes[c.nodes.Count - 1] == nodeId && c.endSide < 0 && c.endJoin == null && c.endDoor == null &&
                         (c.holeEnd == null || c.holeEnd.Count < 3))
                {
                    p = c.samples[n - 1];
                    q = c.samples[n - 2];
                }
                else continue;
                Vector3 d = net.transform.TransformDirection(p - q);
                if (d.sqrMagnitude < 1e-8f) continue;
                point = net.World(p);
                outward = d.normalized;
                tunnelType = c.type;
                return true;
            }
            return false;
        }

        void Apply()
        {
            string reason;
            Note = null;
            if (!Admit(out reason))
            {
                Built = false;
                Status = reason;
                floors.Clear();
                ClearSpan();
                TouchNetworks();
                return;
            }
            Vector3 pa, da, pb, db;
            TunnelType ta, tb;
            TryEnd(networkA, nodeA, out pa, out da, out ta);
            TryEnd(networkB, nodeB, out pb, out db, out tb);
            float L = Mathf.Max(0.5f, ta.segmentLength);
            Vector3 far = pa + da * L;
            if (!Application.isPlaying) AlignB(pb, db, far, da);
            if (tb != ta) Note = "Тип тоннеля у узла B («" + tb.Label + "») не такой, как у A («" + ta.Label + "»): на стыке с B сечения не совпадут.";
            BuildSpan(pa, far, ta);
            Built = true;
            Status = null;
            TouchNetworks();
        }

        /// <summary>Moves and turns graph B (about the vertical through its end) so its dead end sits at <paramref name="target"/>
        /// and its tunnel goes on along <paramref name="inward"/>.</summary>
        void AlignB(Vector3 endB, Vector3 outwardB, Vector3 target, Vector3 inward)
        {
            var tr = networkB.transform;
            Vector3 from = Vector3.ProjectOnPlane(-outwardB, Vector3.up);
            Vector3 to = Vector3.ProjectOnPlane(inward, Vector3.up);
            Quaternion q = Quaternion.identity;
            if (from.sqrMagnitude > 1e-6f && to.sqrMagnitude > 1e-6f)
                q = Quaternion.AngleAxis(Vector3.SignedAngle(from, to, Vector3.up), Vector3.up);
            Vector3 pos = target + q * (tr.position - endB);
            Quaternion rot = q * tr.rotation;
            if ((pos - tr.position).sqrMagnitude < 1e-10f && Quaternion.Angle(rot, tr.rotation) < 1e-4f) return;
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(tr, "Пристыковать граф мостом");
#endif
            tr.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Asks the two graphs to rebuild meshes. Does not write nodes, edges or shafts.</summary>
        void TouchNetworks()
        {
            if (Application.isPlaying) return;
            if (networkA != null) networkA.MarkDirty();
            if (networkB != null && networkB != networkA) networkB.MarkDirty();
        }

        bool Admit(out string reason)
        {
            reason = null;
            if (networkA == null || networkB == null) { reason = "Назначьте два графа."; return false; }
            if (networkA == networkB) { reason = "Мост соединяет два разных графа: граф B пристыковывается к графу A."; return false; }
            if (string.IsNullOrEmpty(nodeA) || string.IsNullOrEmpty(nodeB)) { reason = "Назначьте по узлу на каждом графе."; return false; }
            if (NodeOf(networkA, nodeA) == null) { reason = "Узел «" + nodeA + "» не найден в «" + networkA.name + "»."; return false; }
            if (NodeOf(networkB, nodeB) == null) { reason = "Узел «" + nodeB + "» не найден в «" + networkB.name + "»."; return false; }
            Vector3 p, d;
            TunnelType t;
            if (!TryEnd(networkA, nodeA, out p, out d, out t)) { reason = "Узел «" + nodeA + "» не тупик тоннеля. Мост ставится только на тупик."; return false; }
            if (!TryEnd(networkB, nodeB, out p, out d, out t)) { reason = "Узел «" + nodeB + "» не тупик тоннеля. Мост ставится только на тупик."; return false; }
            Accepted(this, out reason);
            return reason == null;
        }

        /// <summary>Bridges that are built, in hierarchy order. A graph is moved by one bridge at most, and bridges never close a ring
        /// of graphs (a ring cannot be aligned). When <paramref name="probe"/> is turned away, <paramref name="probeReason"/> says why.</summary>
        static List<TunnelBridge> Accepted(TunnelBridge probe, out string probeReason)
        {
            probeReason = null;
            var found = Object.FindObjectsByType<TunnelBridge>(FindObjectsInactive.Exclude);
            System.Array.Sort(found, CompareHierarchy);
            var ok = new List<TunnelBridge>();
            var counts = new Dictionary<int, int>();
            var used = new HashSet<string>();
            var moved = new HashSet<int>();
            var group = new Dictionary<int, int>();
            for (int i = 0; i < found.Length; i++)
            {
                var b = found[i];
                if (b == null || !b.ShapeReady()) continue;
                string why = null;
                int ia = Id(b.networkA), ib = Id(b.networkB);
                if (used.Contains(Key(b.networkA, b.nodeA)) || used.Contains(Key(b.networkB, b.nodeB)))
                    why = "Этот узел уже занят другим мостом.";
                else if (CountOf(counts, b.networkA) >= MaxPerNetwork)
                    why = "На «" + b.networkA.name + "» уже два моста. Третий не строится.";
                else if (CountOf(counts, b.networkB) >= MaxPerNetwork)
                    why = "На «" + b.networkB.name + "» уже два моста. Третий не строится.";
                else if (moved.Contains(ib))
                    why = "Граф «" + b.networkB.name + "» уже пристыкован другим мостом. Поменяйте графы A и B местами.";
                else if (Root(group, ia) == Root(group, ib))
                    why = "Графы уже связаны другими мостами: замкнутое кольцо выровнять нельзя.";
                if (why != null)
                {
                    if (b == probe) probeReason = why;
                    continue;
                }
                Bump(counts, b.networkA);
                Bump(counts, b.networkB);
                used.Add(Key(b.networkA, b.nodeA));
                used.Add(Key(b.networkB, b.nodeB));
                moved.Add(ib);
                group[Root(group, ib)] = Root(group, ia);
                ok.Add(b);
            }
            return ok;
        }

        static int Root(Dictionary<int, int> group, int id)
        {
            int up;
            while (group.TryGetValue(id, out up) && up != id) id = up;
            return id;
        }

        bool ShapeReady()
        {
            if (networkA == null || networkB == null || networkA == networkB) return false;
            if (string.IsNullOrEmpty(nodeA) || string.IsNullOrEmpty(nodeB)) return false;
            Vector3 p, d;
            TunnelType t;
            return TryEnd(networkA, nodeA, out p, out d, out t) && TryEnd(networkB, nodeB, out p, out d, out t);
        }

        static int Id(TunnelNetwork net) { return net.GetEntityId().GetHashCode(); }

        static int CountOf(Dictionary<int, int> counts, TunnelNetwork net)
        {
            int n;
            return net != null && counts.TryGetValue(Id(net), out n) ? n : 0;
        }

        static void Bump(Dictionary<int, int> counts, TunnelNetwork net)
        {
            int id = Id(net);
            int n;
            counts.TryGetValue(id, out n);
            counts[id] = n + 1;
        }

        static string Key(TunnelNetwork net, string node) { return Id(net) + ":" + node; }

        static bool Takes(TunnelBridge b, TunnelNetwork net, string node)
        {
            return (b.networkA == net && b.nodeA == node) || (b.networkB == net && b.nodeB == node);
        }

        static int CompareHierarchy(TunnelBridge a, TunnelBridge b)
        {
            var pa = Path(a.transform);
            var pb = Path(b.transform);
            int n = Mathf.Min(pa.Count, pb.Count);
            for (int i = 0; i < n; i++)
            {
                int c = pa[i].CompareTo(pb[i]);
                if (c != 0) return c;
            }
            return pa.Count.CompareTo(pb.Count);
        }

        static List<int> Path(Transform t)
        {
            var s = new List<int>();
            while (t != null)
            {
                s.Add(t.GetSiblingIndex());
                t = t.parent;
            }
            s.Reverse();
            return s;
        }

        static GraphNode NodeOf(TunnelNetwork net, string id) { return net == null ? null : net.Node(id); }

        void EnsureNav()
        {
            if (GetComponent<NpcNavigationSurface>() != null) return;
            var p = transform.parent;
            while (p != null)
            {
                if (p.GetComponent<NpcNavigationSurface>() != null) return;
                p = p.parent;
            }
            gameObject.AddComponent<NpcNavigationSurface>();
        }

        /// <summary>One slab of tunnel A's type, from A's end to the far end, square cuts, no rims, no caps.</summary>
        void BuildSpan(Vector3 worldA, Vector3 worldB, TunnelType t)
        {
            var path = EnsureSpan();
            var sec = t.section != null ? t.section.Clone() : new TunnelSection();
            path.section = sec;
            path.segmentLength = t.segmentLength;
            path.slabThickness = t.slabThickness;
            path.minJointGap = t.minJointGap;
            path.jointSink = t.jointSink;
            path.edgeBevel = t.edgeBevel;
            path.dirtLine = t.dirtLine;
            path.channelWater = t.channelWater;
            path.waterDepth = t.waterDepth;
            path.seamless = t.seamless;
            var mats = t.materials;
            if (mats == null && networkA.library != null) mats = networkA.library.materials;
            path.materials = mats;
            path.seed = 1;
            path.shafts = new List<ShaftSpec>();
            path.capStart = false;
            path.capEnd = false;
            path.startAtWall = true;
            path.endAtWall = true;
            path.straightEnds = false;
            SpanWidth = sec.HalfSpan * 2f;
            SpanHeight = sec.Height;

            path.drivenSamples = new List<Vector3>
            {
                path.transform.InverseTransformPoint(worldA),
                path.transform.InverseTransformPoint(worldB)
            };
            floors.Clear();
            floors.Add(worldA);
            floors.Add(worldB);
            path.Rebuild(true);
            MarkUnsaved(path.gameObject);
            var renderers = path.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].renderingLayerMask |= TunnelNetwork.TunnelRenderingLayer;
        }

        TunnelPath EnsureSpan()
        {
            Transform t = transform.Find("Span");
            if (t == null)
            {
                var go = new GameObject("Span") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                t = go.transform;
            }
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            var path = t.GetComponent<TunnelPath>();
            if (path == null) path = t.gameObject.AddComponent<TunnelPath>();
            return path;
        }

        void ClearSpan()
        {
            var t = transform.Find("Span");
            if (t == null) return;
            if (Application.isPlaying) Destroy(t.gameObject);
            else DestroyImmediate(t.gameObject);
        }

        static void MarkUnsaved(GameObject go)
        {
            var all = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                all[i].gameObject.hideFlags = HideFlags.DontSave;
        }

        void OnDrawGizmos()
        {
            Vector3 pa, da, pb, db;
            if (!Frames(out pa, out da, out pb, out db)) return;
            Gizmos.color = Built ? new Color(0.4f, 0.75f, 1f, 0.9f) : new Color(1f, 0.3f, 0.3f, 0.9f);
            Gizmos.DrawLine(pa, pb);
        }
    }
}
