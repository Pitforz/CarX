using System.Collections.Generic;
using CarX.NPC;
using UnityEngine;

namespace CarX.Level
{
    /// <summary>
    /// One tunnel span between a node of one graph and a node of another.
    /// Meshes live on this object. Nodes, edges and shafts of either TunnelNetwork are never written.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("CarX/Tunnel Bridge")]
    public class TunnelBridge : MonoBehaviour
    {
        public const int MaxPerNetwork = 2;

        [Tooltip("Первый граф. Сам граф не меняется")]
        public TunnelNetwork networkA;
        [Tooltip("Узел первого графа")]
        public string nodeA;
        [Tooltip("Второй граф. Сам граф не меняется")]
        public TunnelNetwork networkB;
        [Tooltip("Узел второго графа")]
        public string nodeB;
        [Tooltip("Сечение пролёта. Берётся только для меша этого объекта")]
        public TunnelType type;

        public bool Built { get; private set; }
        public string Status { get; private set; }
        public float SpanWidth { get; private set; }
        public float SpanHeight { get; private set; }
        public IReadOnlyList<Vector3> Floors => floors;

        static int revision;
        readonly List<Vector3> floors = new List<Vector3>();
        int seen = -1;
        bool hasLast;
        Vector3 lastA, lastB;

        public static void Touch() { revision++; }

        void OnEnable()
        {
            Apply();
            seen = revision;
            hasLast = TryWorldEnds(out lastA, out lastB);
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
            Vector3 a, b;
            bool moved = TryWorldEnds(out a, out b) && (!hasLast || a != lastA || b != lastB);
            if (seen == revision && !moved) return;
            Apply();
            seen = revision;
            hasLast = TryWorldEnds(out lastA, out lastB);
        }

        public void RebuildNow()
        {
            Apply();
            seen = revision;
            hasLast = TryWorldEnds(out lastA, out lastB);
        }

        /// <summary>This graph node is an end of an accepted bridge, so the build leaves the cap off.</summary>
        public static bool Opens(TunnelNetwork net, string nodeId)
        {
            if (net == null || string.IsNullOrEmpty(nodeId)) return false;
            var accepted = Accepted();
            for (int i = 0; i < accepted.Count; i++)
                if (Takes(accepted[i], net, nodeId)) return true;
            return false;
        }

        /// <summary>True when another accepted bridge already uses this node.</summary>
        public static bool NodeTaken(TunnelNetwork net, string nodeId, TunnelBridge except)
        {
            if (net == null || string.IsNullOrEmpty(nodeId)) return false;
            var accepted = Accepted();
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

        void Apply()
        {
            string reason;
            if (!Admit(out reason))
            {
                Built = false;
                Status = reason;
                floors.Clear();
                ClearSpan();
                TouchNetworks();
                return;
            }
            Vector3 a, b;
            TryWorldEnds(out a, out b);
            BuildSpan(a, b);
            Built = true;
            Status = null;
            TouchNetworks();
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
            if (string.IsNullOrEmpty(nodeA) || string.IsNullOrEmpty(nodeB)) { reason = "Назначьте по узлу на каждом графе."; return false; }
            if (NodeOf(networkA, nodeA) == null) { reason = "Узел «" + nodeA + "» не найден в «" + networkA.name + "»."; return false; }
            if (NodeOf(networkB, nodeB) == null) { reason = "Узел «" + nodeB + "» не найден в «" + networkB.name + "»."; return false; }
            if (networkA == networkB && nodeA == nodeB) { reason = "Нужны два разных узла."; return false; }
            if (type == null) { reason = "Назначьте тип тоннеля для пролёта."; return false; }
            Vector3 a, b;
            if (!TryWorldEnds(out a, out b) || Vector3.Distance(a, b) < 0.5f) { reason = "Узлы слишком близко для пролёта."; return false; }

            var accepted = Accepted();
            int useA = 0, useB = 0;
            for (int i = 0; i < accepted.Count; i++)
            {
                var other = accepted[i];
                if (other == this) break;
                if (Uses(other, networkA)) useA++;
                if (networkB != networkA && Uses(other, networkB)) useB++;
                if (Takes(other, networkA, nodeA) || Takes(other, networkB, nodeB))
                {
                    reason = "Этот узел уже занят другим мостом.";
                    return false;
                }
            }
            if (useA >= MaxPerNetwork) { reason = "На «" + networkA.name + "» уже два моста. Третий не строится."; return false; }
            if (networkB != networkA && useB >= MaxPerNetwork) { reason = "На «" + networkB.name + "» уже два моста. Третий не строится."; return false; }
            return true;
        }

        static List<TunnelBridge> Accepted()
        {
            var found = Object.FindObjectsByType<TunnelBridge>(FindObjectsInactive.Exclude);
            System.Array.Sort(found, CompareHierarchy);
            var ok = new List<TunnelBridge>();
            var counts = new Dictionary<int, int>();
            var used = new HashSet<string>();
            for (int i = 0; i < found.Length; i++)
            {
                var b = found[i];
                if (b == null || !b.ShapeReady()) continue;
                if (CountOf(counts, b.networkA) >= MaxPerNetwork) continue;
                if (b.networkB != b.networkA && CountOf(counts, b.networkB) >= MaxPerNetwork) continue;
                if (used.Contains(Key(b.networkA, b.nodeA)) || used.Contains(Key(b.networkB, b.nodeB))) continue;
                Bump(counts, b.networkA);
                if (b.networkB != b.networkA) Bump(counts, b.networkB);
                used.Add(Key(b.networkA, b.nodeA));
                used.Add(Key(b.networkB, b.nodeB));
                ok.Add(b);
            }
            return ok;
        }

        bool ShapeReady()
        {
            if (networkA == null || networkB == null || type == null) return false;
            if (string.IsNullOrEmpty(nodeA) || string.IsNullOrEmpty(nodeB)) return false;
            if (NodeOf(networkA, nodeA) == null || NodeOf(networkB, nodeB) == null) return false;
            if (networkA == networkB && nodeA == nodeB) return false;
            Vector3 a, b;
            return TryWorldEnds(out a, out b) && Vector3.Distance(a, b) >= 0.5f;
        }

        static int CountOf(Dictionary<int, int> counts, TunnelNetwork net)
        {
            int n;
            return net != null && counts.TryGetValue(net.GetEntityId().GetHashCode(), out n) ? n : 0;
        }

        static void Bump(Dictionary<int, int> counts, TunnelNetwork net)
        {
            int id = net.GetEntityId().GetHashCode();
            int n;
            counts.TryGetValue(id, out n);
            counts[id] = n + 1;
        }

        static string Key(TunnelNetwork net, string node) { return net.GetEntityId().GetHashCode() + ":" + node; }

        static bool Uses(TunnelBridge b, TunnelNetwork net) { return b.networkA == net || b.networkB == net; }

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

        void BuildSpan(Vector3 worldA, Vector3 worldB)
        {
            var path = EnsureSpan();
            var sec = type.section != null ? type.section.Clone() : new TunnelSection();
            path.section = sec;
            path.segmentLength = type.segmentLength;
            path.slabThickness = type.slabThickness;
            path.minJointGap = type.minJointGap;
            path.jointSink = type.jointSink;
            path.edgeBevel = type.edgeBevel;
            path.dirtLine = type.dirtLine;
            path.channelWater = type.channelWater;
            path.waterDepth = type.waterDepth;
            path.seamless = type.seamless;
            var mats = type.materials;
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

            float step = Mathf.Max(0.5f, type.segmentLength);
            Vector3 localA = path.transform.InverseTransformPoint(worldA);
            Vector3 localB = path.transform.InverseTransformPoint(worldB);
            int count = Mathf.Clamp(Mathf.RoundToInt(Vector3.Distance(localA, localB) / step), 1, 500);
            var samples = new List<Vector3>(count + 1);
            floors.Clear();
            for (int k = 0; k <= count; k++)
            {
                float u = (float)k / count;
                samples.Add(Vector3.Lerp(localA, localB, u));
                floors.Add(Vector3.Lerp(worldA, worldB, u));
            }
            path.drivenSamples = samples;
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
            Vector3 a, b;
            if (!TryWorldEnds(out a, out b)) return;
            Gizmos.color = Built ? new Color(0.4f, 0.75f, 1f, 0.9f) : new Color(1f, 0.3f, 0.3f, 0.9f);
            Gizmos.DrawLine(a, b);
        }
    }
}
