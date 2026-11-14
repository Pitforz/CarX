using System.Collections.Generic;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarX.EditorTools
{
    /// <summary>Random maze built as a tunnel graph: 5x5 chambers, random tunnel types, shafts, plus a long curved dead-end tunnel.</summary>
    public static class TunnelGraphDemo
    {
        public const string DemoPath = "Assets/_Project/Scenes/TunnelGraph_Demo.unity";
        public const string TestPath = "Assets/_Project/Scenes/Test/TunnelGraph_Test.unity";
        static string ScenePath = DemoPath;

        [MenuItem("CarX/Tunnels/Build Graph Maze Demo (граф)", priority = 19)]
        static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Debug.Log(Build(Random.Range(1, 99999), false));
        }

        /// <summary>For automation: builds into its own scene and closes it; open scenes are not touched.</summary>
        public static void BuildInBackground()
        {
            ScenePath = DemoPath;
            Debug.Log(Build(7, true));
        }

        /// <summary>For automation: same build into a throw-away test scene.</summary>
        public static void BuildTestScene()
        {
            TunnelAssets.EnsureFolder("Assets/_Project/Scenes/Test");
            ScenePath = TestPath;
            try { Debug.Log(Build(7, true)); }
            finally { ScenePath = DemoPath; }
        }

        /// <summary>Test: rays through every chamber doorway and every direct join must pass (no wall in the way).</summary>
        static string Probe(TunnelNetwork net)
        {
            Physics.SyncTransforms();
            int doorsOk = 0, doorsBad = 0, joinsOk = 0, joinsBad = 0, reducers = 0, wallsOk = 0, wallsBad = 0, flushOk = 0, flushBad = 0;
            foreach (var kv in net.chambers)
            {
                var info = kv.Value;
                for (int s = 0; s < 4; s++)
                {
                    if (info.sideChains[s] == null) continue;
                    Vector3 o;
                    var w = net.WallPoint(info, s, out o);
                    Vector3 from = net.World(info.node.position + Vector3.up * 1.2f);
                    Vector3 to = net.World(w + o * 1.0f + Vector3.up * 1.2f);
                    RaycastHit h2;
                    if (Blocked(net, from, to, out h2)) { doorsBad++; Debug.Log("CarX probe: doorway blocked at chamber " + info.node.id + " " + info.node.position + " side " + s + " by " + h2.collider.transform.parent.name); }
                    else doorsOk++;
                }
            }
            foreach (var kv in net.joins)
            {
                var J = kv.Value;
                var node = J.node;
                if (J.reducer)
                {
                    // straight through the opening in the end wall, from the bigger tunnel into the other one
                    float cy = 0f;
                    foreach (var v in J.hole) cy += v.y;
                    cy /= J.hole.Count;
                    Vector3 a = net.World(node.position - J.tangent * 1.5f + Vector3.up * cy);
                    Vector3 b = net.World(node.position + J.tangent * 1.5f + Vector3.up * cy);
                    RaycastHit rh;
                    if (Blocked(net, a, b, out rh)) { joinsBad++; Debug.Log("CarX probe: transition blocked by " + rh.collider.transform.parent.name + "/" + rh.collider.name + " at " + rh.point); }
                    else joinsOk++;
                    reducers++;
                    // and the end wall itself must be there next to the opening
                    var ms = J.mainType.section;
                    float wy = ms.Height * 0.5f, wx = ms.WallXAt(wy) - 0.1f;
                    if (!Inside(J.hole, new Vector2(wx, wy))) wy = ms.Height - 0.1f;
                    if (!Inside(J.hole, new Vector2(wx, wy)))
                    {
                        var side = Vector3.Cross(Vector3.up, J.tangent).normalized * wx;
                        Vector3 c0 = net.World(node.position - J.tangent * 1.5f + side + Vector3.up * wy);
                        Vector3 c1 = net.World(node.position + J.tangent * 1.5f + side + Vector3.up * wy);
                        if (Blocked(net, c0, c1, out rh)) wallsOk++; else { wallsBad++; Debug.Log("CarX probe: no end wall at transition " + node.id); }
                    }
                    continue;
                }
                foreach (var e in net.EdgesOf(node.id))
                {
                    if (e == J.e1 || e == J.e2) continue;
                    var bt = net.TypeOf(e);
                    float y0, x;
                    TunnelNetwork.BranchFit(J.mainType.section, bt.section, out y0, out x, node.joinLift);
                    var d = net.Node(TunnelNetwork.Other(e, node.id)).position - node.position;
                    var right = Vector3.Cross(Vector3.up, J.tangent).normalized;
                    var o = Vector3.Dot(d, right) >= 0f ? right : -right;
                    float yMid = y0 + bt.section.Height * 0.5f;
                    var basePos = node.position - J.tangent * J.back;
                    Vector3 from = net.World(basePos + Vector3.up * yMid);
                    Vector3 to = net.World(basePos + o * (x + 1.5f) + Vector3.up * yMid);
                    RaycastHit hit;
                    if (Blocked(net, from, to, out hit)) { joinsBad++; Debug.Log("CarX probe: join blocked by " + hit.collider.transform.parent.name + "/" + hit.collider.name + " tri " + hit.triangleIndex + " at " + hit.point); }
                    else joinsOk++;
                    // nothing of the branch sticks into the main tunnel: rays along the main wall, just inside it, pass the join
                    // (they follow the main tunnel's real path: on a bend it runs inside the node, not through it)
                    var ms = J.mainType.section;
                    float ahead = J.back > 0f ? J.back - 0.3f : 2.5f;
                    float span;
                    net.EdgeSpan(J.e1, out span);
                    float dNode = J.e1.a == node.id ? 0f : span;
                    Vector3 ax0, dir0;
                    TunnelSection sec0;
                    net.EdgeFrame(J.e1, dNode, out ax0, out dir0, out sec0);
                    float sg = Vector3.Dot(J.tangent, dir0) >= 0f ? 1f : -1f;
                    foreach (var fy in new[] { 0.12f, Mathf.Min(1.0f, ms.Height * 0.5f), ms.Height - 0.12f })
                    {
                        float fx = ms.WallXAt(fy) - 0.15f;
                        bool bad = false;
                        Vector3 prev = Vector3.zero;
                        for (int k = 0; k <= 12 && !bad; k++)
                        {
                            float sAlong = -J.back - 2.5f + (2.5f + ahead) * k / 12f;
                            Vector3 ax, dr;
                            TunnelSection sc;
                            net.EdgeFrame(J.e1, dNode + sg * sAlong, out ax, out dr, out sc);
                            var side = Vector3.Cross(Vector3.up, dr).normalized;
                            if (Vector3.Dot(side, o) < 0f) side = -side;
                            var p = net.World(ax + side * fx + Vector3.up * fy);
                            if (k > 0 && Blocked(net, prev, p, out hit))
                            {
                                bad = true;
                                Debug.Log("CarX probe: something sticks into the main tunnel at join " + node.id + " h " + fy + ": " + hit.collider.transform.parent.name + " at " + hit.point);
                            }
                            prev = p;
                        }
                        if (bad) flushBad++; else flushOk++;
                    }
                }
            }
            // rooms: every doorway open
            int roomOk = 0, roomBad = 0;
            foreach (var kv in net.rooms)
                foreach (var d in kv.Value.doors)
                {
                    float yy = d.chain.type.section.Height * 0.5f;
                    RaycastHit rh2;
                    if (Blocked(net, net.World(d.point - d.outward * 1.0f + Vector3.up * yy), net.World(d.point + d.outward * 1.5f + Vector3.up * yy), out rh2)) { roomBad++; Debug.Log("CarX probe: room door blocked by " + rh2.collider.transform.parent.name); }
                    else roomOk++;
                }
            // elbows: open around the inner corner, closed outside
            int elbOk = 0, elbBad = 0;
            foreach (var c in net.chains)
                foreach (var ep in c.elbows)
                {
                    int i = c.samples.FindIndex(p => (p - ep).sqrMagnitude < 1e-4f);
                    if (i <= 0 || i >= c.samples.Count - 1) continue;
                    Vector3 d1 = (c.samples[i] - c.samples[i - 1]).normalized, d2 = (c.samples[i + 1] - c.samples[i]).normalized;
                    float yy = c.type.section.Height * 0.5f;
                    RaycastHit eh;
                    bool open = !Blocked(net, net.World(ep - d1 * 0.5f + Vector3.up * yy), net.World(ep + d2 * 0.5f + Vector3.up * yy), out eh);
                    bool wall = Blocked(net, net.World(ep + Vector3.up * yy), net.World(ep + (d1 - d2).normalized * 3f + Vector3.up * yy), out eh);
                    if (open && wall) elbOk++; else { elbBad++; Debug.Log("CarX probe: elbow bad at " + ep + " open " + open + " wall " + wall); }
                }
            return "flush ok " + flushOk + ", bad " + flushBad + "; room doors ok " + roomOk + ", bad " + roomBad + "; elbows ok " + elbOk + ", bad " + elbBad + "; probe: chamber doorways open " + doorsOk + ", closed " + doorsBad + "; joins open " + joinsOk + ", closed " + joinsBad + " (transitions " + reducers + ", end walls ok " + wallsOk + ", missing " + wallsBad + ")";
        }

        // other open scenes share the physics world: only count hits in this graph's scene
        static bool Blocked(TunnelNetwork net, Vector3 from, Vector3 to, out RaycastHit first)
        {
            first = default(RaycastHit);
            var d = to - from;
            foreach (var h in Physics.RaycastAll(from, d.normalized, d.magnitude))
                if (h.collider.gameObject.scene == net.gameObject.scene) { first = h; return true; }
            return false;
        }

        /// <summary>Test: a seamless tunnel on an arc builds (mitred joints, no gaps between slab rings).</summary>
        public static void SeamlessCheck()
        {
            var go = new GameObject("_SeamlessCheck") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var p = go.AddComponent<TunnelPath>();
                p.section = TunnelGraphAssets.GetLibrary().types[0].section.Clone();
                p.seamless = true;
                var S = new List<Vector3>();
                for (int i = 0; i <= 12; i++) S.Add(Quaternion.Euler(0f, i * 2f, 0f) * new Vector3(0f, 0f, -57f) + new Vector3(0f, 0f, 57f));
                p.drivenSamples = S;
                p.Rebuild(true);
                float worst = 0f;
                for (int k = 1; k < p.slabs.Count; k++)
                {
                    var A = p.slabs[k - 1]; var B = p.slabs[k];
                    foreach (var v in new[] { new Vector2(-1.5f, 1f), new Vector2(1.5f, 1f), new Vector2(0f, 2.4f) })
                        worst = Mathf.Max(worst, Vector3.Distance(A.MPoint(A.length, v.x, v.y), B.MPoint(0f, v.x, v.y)));
                }
                Debug.Log("CarX seamless check: slabs " + p.slabs.Count + ", verts " + go.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount + ", worst joint gap " + worst.ToString("0.0000") + " m");
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Test aid: dumps the generated meshes (world space) and camera views of every join for offline inspection.</summary>
        public static void ExportGeo(TunnelNetwork net, string file)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            foreach (var mf in net.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh;
                if (m == null) continue;
                var tr = mf.transform;
                string nm = tr.parent != null ? tr.parent.name + "/" + tr.name : tr.name;
                sb.Append("o ").AppendLine(nm.Replace(' ', '_'));
                var v = m.vertices; var n = m.normals;
                for (int i = 0; i < v.Length; i++)
                {
                    var p = tr.TransformPoint(v[i]); var q = i < n.Length ? tr.TransformDirection(n[i]) : Vector3.up;
                    sb.AppendLine(string.Format(ci, "v {0:0.####} {1:0.####} {2:0.####} {3:0.###} {4:0.###} {5:0.###}", p.x, p.y, p.z, q.x, q.y, q.z));
                }
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    var t = m.GetTriangles(s);
                    for (int i = 0; i + 2 < t.Length; i += 3) sb.AppendLine("f " + t[i] + " " + t[i + 1] + " " + t[i + 2] + " " + s);
                }
            }
            System.Action<string, Vector3, Vector3> cam = (name, from, to) =>
                sb.AppendLine(string.Format(ci, "cam {0} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} {6:0.###}", name, net.World(from).x, net.World(from).y, net.World(from).z, net.World(to).x, net.World(to).y, net.World(to).z));
            int k = 0;
            foreach (var kv in net.joins)
            {
                var J = kv.Value;
                var node = J.node;
                if (J.reducer)
                {
                    cam("reducer" + k + "_fromBig", node.position - J.tangent * 4f + Vector3.up * 1.4f, node.position + J.tangent * 2f + Vector3.up * 0.9f);
                    cam("reducer" + k + "_fromSmall", node.position + J.tangent * 4f + Vector3.up * 1.0f, node.position - J.tangent * 2f + Vector3.up * 0.9f);
                    k++;
                    continue;
                }
                foreach (var e in net.EdgesOf(node.id))
                {
                    if (e == J.e1 || e == J.e2) continue;
                    var basePos = node.position - J.tangent * J.back;
                    var d = net.Node(TunnelNetwork.Other(e, node.id)).position - node.position;
                    var right = Vector3.Cross(Vector3.up, J.tangent).normalized;
                    var o = Vector3.Dot(d, right) >= 0f ? right : -right;
                    var t = new Vector3(J.tangent.x, 0f, J.tangent.z).normalized;
                    cam("join" + k + "_main", basePos - t * 4f - o * 0.6f + Vector3.up * 1.5f, basePos + o * 1.6f + Vector3.up * 0.9f);
                    cam("join" + k + "_mainAlong", basePos - t * 5f + o * 0.9f + Vector3.up * 1.2f, basePos + t * 3f + o * 0.9f + Vector3.up * 1.2f);
                    cam("join" + k + "_branch", basePos + o * 5f + Vector3.up * 1.2f, basePos - o * 1f + Vector3.up * 1.0f);
                    k++;
                }
            }
            int r = 0;
            foreach (var kv in net.rooms)
                foreach (var d in kv.Value.doors)
                {
                    cam("room" + r + "_in", kv.Value.node.position + Vector3.up * 1.6f, d.point + Vector3.up * 1.0f);
                    cam("room" + r + "_tunnel", d.point + d.outward * 4f + Vector3.up * 1.2f, d.point - d.outward * 2f + Vector3.up * 1.0f);
                    r++;
                }
            System.IO.File.WriteAllText(file, sb.ToString());
        }

        static int Elbows(TunnelNetwork net)
        {
            int n = 0;
            foreach (var c in net.chains) n += c.elbows.Count;
            return n;
        }

        static bool Inside(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }

        static void Branch(TunnelNetwork net, GraphNode from, Vector3 offset, TunnelType type, System.Random rnd)
        {
            var end = net.AddNode(from.position + offset);
            end.seed = rnd.Next(1, 99999);
            var e = net.AddEdge(from.id, end.id, type);
            e.seed = rnd.Next(1, 99999);
        }

        /// <summary>Joins that used to stick out or leave gaps: taller branches, a pipe into a box with curbs, a slanted branch (elbow next to
        /// the main wall), a room whose walls are too narrow for an oblique doorway, a doorway as tall as the room.</summary>
        static void AddJoinTests(TunnelNetwork net, TunnelTypeLibrary lib, System.Random rnd)
        {
            var T = lib.types;
            TunnelType oct = T[0], chan = T[1], narrow = T[2], wide = T[3], pipe = T[T.Count - 1];
            var o0 = new Vector3(-70f, 0f, 0f);
            var a0 = net.AddNode(o0); var a1 = net.AddNode(o0 + new Vector3(20f, 0f, 0f)); var a2 = net.AddNode(o0 + new Vector3(40f, 0f, 0f));
            net.AddEdge(a0.id, a1.id, wide).seed = rnd.Next(1, 99999);
            net.AddEdge(a1.id, a2.id, wide).seed = rnd.Next(1, 99999);
            Branch(net, a1, new Vector3(0f, 0f, 14f), narrow, rnd);      // taller than the main tunnel
            Branch(net, a1, new Vector3(8f, 0f, -10f), oct, rnd);        // taller, chamfers, leaves at an angle
            var b0 = net.AddNode(o0 + new Vector3(0f, 0f, 40f)); var b1 = net.AddNode(o0 + new Vector3(20f, 0f, 40f)); var b2 = net.AddNode(o0 + new Vector3(40f, 0f, 40f));
            net.AddEdge(b0.id, b1.id, chan).seed = rnd.Next(1, 99999);
            net.AddEdge(b1.id, b2.id, chan).seed = rnd.Next(1, 99999);
            Branch(net, b1, new Vector3(0f, 0f, 14f), pipe, rnd);        // pipe into a box with curbs
            Branch(net, b1, new Vector3(0f, 0f, -14f), narrow, rnd);
            var c0 = net.AddNode(o0 + new Vector3(0f, 0f, 80f)); var c1 = net.AddNode(o0 + new Vector3(20f, 0f, 80f)); var c2 = net.AddNode(o0 + new Vector3(40f, 0f, 80f));
            net.AddEdge(c0.id, c1.id, pipe).seed = rnd.Next(1, 99999);
            net.AddEdge(c1.id, c2.id, pipe).seed = rnd.Next(1, 99999);
            Branch(net, c1, new Vector3(0f, 0f, 14f), narrow, rnd);      // box into a pipe
            // the same box into a channel on a slope, and at a slight bend of the main tunnel
            var d0 = net.AddNode(o0 + new Vector3(0f, 0f, 120f)); var d1 = net.AddNode(o0 + new Vector3(20f, -0.3f, 120f)); var d2 = net.AddNode(o0 + new Vector3(40f, -0.6f, 120f));
            net.AddEdge(d0.id, d1.id, chan).seed = rnd.Next(1, 99999);
            net.AddEdge(d1.id, d2.id, chan).seed = rnd.Next(1, 99999);
            Branch(net, d1, new Vector3(0f, 0f, -14f), narrow, rnd);
            var g0 = net.AddNode(o0 + new Vector3(0f, 0f, 160f)); var g1 = net.AddNode(o0 + new Vector3(20f, 0f, 160f)); var g2 = net.AddNode(o0 + new Vector3(40f, 0f, 161.4f));
            net.AddEdge(g0.id, g1.id, chan).seed = rnd.Next(1, 99999);
            net.AddEdge(g1.id, g2.id, chan).seed = rnd.Next(1, 99999);
            Branch(net, g1, new Vector3(0f, 0f, -14f), narrow, rnd);
            var rm = net.AddNode(o0 + new Vector3(20f, 0f, -40f));
            rm.kind = GraphNodeKind.Room;
            rm.roomCorners = new List<Vector2>();
            foreach (var v in GraphNode.DefaultRoom()) rm.roomCorners.Add(v * 0.8f);   // walls 3.2 m: too narrow for most doorways
            rm.seed = rnd.Next(1, 99999);
            System.Func<float, Vector3> at = deg => new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad), 0f, Mathf.Sin(deg * Mathf.Deg2Rad)) * 15f;
            Branch(net, rm, at(20f), wide, rnd);       // 20° off a wall too narrow for it: a new wall is cut square to it
            Branch(net, rm, at(110f), chan, rnd);      // as tall as the room
            Branch(net, rm, at(200f), pipe, rnd);
            Branch(net, rm, at(290f), oct, rnd);
        }

        // ------------------------------------------------------------------ anchors

        class AnchorRig { public GraphNode n0, n1, n2, n3; public GraphEdge e01, e12, e23; }

        /// <summary>A dead end, a bend node, a chamber and a room, away from the other tests: props are anchored here.</summary>
        static AnchorRig AddAnchorTestGraph(TunnelNetwork net, TunnelTypeLibrary lib, System.Random rnd)
        {
            var T = lib.types;
            var o = new Vector3(-140f, 0f, 0f);
            var r = new AnchorRig();
            r.n0 = net.AddNode(o);
            r.n1 = net.AddNode(o + new Vector3(25f, 0f, 0f));
            r.n2 = net.AddNode(o + new Vector3(50f, 0f, 0f));
            r.n2.kind = GraphNodeKind.Chamber;
            r.n3 = net.AddNode(o + new Vector3(50f, 0f, 25f));
            r.n3.kind = GraphNodeKind.Room;
            r.n3.roomCorners = GraphNode.DefaultRoom();
            foreach (var n in new[] { r.n0, r.n1, r.n2, r.n3 }) n.seed = rnd.Next(1, 99999);
            r.e01 = net.AddEdge(r.n0.id, r.n1.id, T[0]);
            r.e12 = net.AddEdge(r.n1.id, r.n2.id, T[0]);
            r.e23 = net.AddEdge(r.n2.id, r.n3.id, T[0]);
            return r;
        }

        class Tracked { public TunnelAnchor a; public GraphEdge e; public AnchorSurface surf; public float d, span; public Vector3 local, world; }

        static TunnelAnchor Prop(Transform props, TunnelNetwork net, string name, Vector3 local, Quaternion rot)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());   // keeps the join probe's rays clear
            g.transform.SetParent(props, false);
            g.transform.localScale = new Vector3(0.4f, 0.4f, 0.05f);
            g.transform.SetPositionAndRotation(net.World(local), net.transform.rotation * rot);
            return g.AddComponent<TunnelAnchor>();
        }

        static bool Measure(TunnelNetwork net, Tracked t, out float d, out float x, out float y, out TunnelSection sec)
        {
            d = x = y = 0f;
            sec = null;
            var p = net.Local(t.a.transform.position);
            GraphEdge e;
            if (!net.ClosestOnTunnels(p, 0.5f, out e, out d) || e != t.e) return false;
            Vector3 axis, dir;
            net.EdgeFrame(e, d, out axis, out dir, out sec);
            var right = Vector3.Cross(Vector3.up, dir).normalized;
            var up = Vector3.Cross(dir, right);
            x = Vector3.Dot(p - axis, right);
            y = Vector3.Dot(p - axis, up);
            return true;
        }

        /// <summary>Props on the graph: moving nodes, changing a tunnel type, moving a chamber or a room keeps them in place.</summary>
        static string AnchorTest(TunnelNetwork net, AnchorRig r)
        {
            var props = new GameObject("Props").transform;
            props.SetParent(net.transform, false);
            var tracked = new List<Tracked>();
            Vector3 axis = Vector3.zero, dir = Vector3.forward;
            TunnelSection sec = null;
            System.Func<GraphEdge, float, Vector3> rightAt = (e, d) => { net.EdgeFrame(e, d, out axis, out dir, out sec); return Vector3.Cross(Vector3.up, dir).normalized; };

            var R = rightAt(r.e01, 8f);
            var floor = Prop(props, net, "Anchor_Floor", axis + R * 0.5f + Vector3.up * sec.FloorHeightAt(0.5f), Quaternion.LookRotation(dir));
            tracked.Add(new Tracked { a = floor, e = r.e01, surf = AnchorSurface.Floor });
            rightAt(r.e01, 18f);
            var ceil = Prop(props, net, "Anchor_Ceiling", axis + Vector3.up * sec.CeilingHeightAt(0f), Quaternion.LookRotation(dir, Vector3.down));
            tracked.Add(new Tracked { a = ceil, e = r.e01, surf = AnchorSurface.Ceiling });
            R = rightAt(r.e12, 6f);
            var wallP = Prop(props, net, "Anchor_Wall", axis + R * sec.WallXAt(1.2f) + Vector3.up * 1.2f, Quaternion.LookRotation(R));
            tracked.Add(new Tracked { a = wallP, e = r.e12, surf = AnchorSurface.Wall });
            var box = Prop(props, net, "Anchor_Chamber", r.n2.position + new Vector3(1f, 0f, 1f), Quaternion.Euler(0f, 30f, 0f));
            var room = net.rooms[r.n3.id];
            int w = 0;
            for (int i = 1; i < room.corners.Count; i++)
                if (room.corners[i].y + room.corners[(i + 1) % room.corners.Count].y > room.corners[w].y + room.corners[(w + 1) % room.corners.Count].y) w = i;
            Vector2 ca = room.corners[w], cb = room.corners[(w + 1) % room.corners.Count], cd = (cb - ca).normalized;
            var mid = (ca + cb) * 0.5f + new Vector2(-cd.y, cd.x) * 0.02f;
            var poster = Prop(props, net, "Anchor_RoomWall", r.n3.position + new Vector3(mid.x, 1.5f, mid.y), Quaternion.LookRotation(new Vector3(cd.y, 0f, -cd.x)));
            var crate = Prop(props, net, "Anchor_Room", r.n3.position + new Vector3(1f, 0f, 1f), Quaternion.identity);
            var far = Prop(props, net, "Anchor_Lost", r.n0.position + new Vector3(0f, 0f, -30f), Quaternion.identity);

            int ok = 0, bad = 0;
            var log = new System.Text.StringBuilder();
            System.Action<bool, string> check = (c, what) => { if (c) ok++; else { bad++; log.Append("\n  anchor bad: " + what); } };
            check(floor.Mode == AnchorMode.Tunnel && floor.BoundSurface == AnchorSurface.Floor, "floor bound as " + floor.Mode + "/" + floor.BoundSurface);
            check(ceil.Mode == AnchorMode.Tunnel && ceil.BoundSurface == AnchorSurface.Ceiling, "ceiling bound as " + ceil.Mode + "/" + ceil.BoundSurface);
            check(wallP.Mode == AnchorMode.Tunnel && wallP.BoundSurface == AnchorSurface.Wall, "wall bound as " + wallP.Mode + "/" + wallP.BoundSurface);
            check(box.Mode == AnchorMode.Chamber, "chamber bound as " + box.Mode);
            check(poster.Mode == AnchorMode.RoomWall, "room wall bound as " + poster.Mode);
            check(crate.Mode == AnchorMode.Room, "room bound as " + crate.Mode);
            check(far.Lost, "far object not lost");

            System.Func<TunnelNetwork.ChamberInfo, Vector3, Vector3> inChamber = (ch, p) => Quaternion.Inverse(Quaternion.Euler(0f, ch.yaw, 0f)) * (p - ch.node.position);
            System.Action<string, System.Action> step = (name, change) =>
            {
                var A = new Dictionary<string, Vector3>();
                foreach (var n in net.nodes) A[n.id] = n.position;
                foreach (var t in tracked)
                {
                    float x, y;
                    if (!Measure(net, t, out t.d, out x, out y, out sec)) { check(false, name + ": " + t.a.name + " not in its tunnel before"); continue; }
                    net.EdgeSpan(t.e, out t.span);
                }
                var boxLocal = inChamber(net.chambers[r.n2.id], net.Local(box.transform.position));
                Vector3 posterW = poster.transform.position, crateW = crate.transform.position, roomW = r.n3.position;
                change();
                net.MarkDirty();
                net.Layout();
                foreach (var t in tracked)
                {
                    float span, d, x, y;
                    net.EdgeSpan(t.e, out span);
                    bool aSame = (net.Node(t.e.a).position - A[t.e.a]).sqrMagnitude < 1e-8f, bSame = (net.Node(t.e.b).position - A[t.e.b]).sqrMagnitude < 1e-8f;
                    float expect = t.d;
                    if (Mathf.Abs(span - t.span) > 1e-5f)
                    {
                        if (aSame && bSame) expect = t.d <= t.span * 0.5f ? t.d : span - (t.span - t.d);
                        else if (bSame) expect = span - (t.span - t.d);
                        else if (!aSame) expect = t.d / t.span * span;
                    }
                    if (!Measure(net, t, out d, out x, out y, out sec)) { check(false, name + ": " + t.a.name + " left its tunnel"); continue; }
                    check(Mathf.Abs(d - expect) < 0.05f, name + ": " + t.a.name + " at " + d.ToString("0.000") + " m from node A, expected " + expect.ToString("0.000"));
                    float off = t.surf == AnchorSurface.Floor ? y - sec.FloorHeightAt(x) : t.surf == AnchorSurface.Ceiling ? sec.CeilingHeightAt(x) - y : sec.WallXAt(y) - Mathf.Abs(x);
                    check(Mathf.Abs(off) < 0.02f, name + ": " + t.a.name + " is " + off.ToString("0.000") + " m off its " + t.surf);
                }
                var boxNow = inChamber(net.chambers[r.n2.id], net.Local(box.transform.position));
                check((boxNow - boxLocal).magnitude < 0.01f, name + ": chamber prop moved inside the chamber by " + (boxNow - boxLocal).magnitude.ToString("0.000"));
                var shift = net.transform.TransformVector(r.n3.position - roomW);
                check((poster.transform.position - posterW - shift).magnitude < 0.01f, name + ": room wall prop off by " + (poster.transform.position - posterW - shift).magnitude.ToString("0.000"));
                check((crate.transform.position - crateW - shift).magnitude < 0.01f, name + ": room prop off by " + (crate.transform.position - crateW - shift).magnitude.ToString("0.000"));
            };
            step("dead end moved back and aside", () => r.n0.position += new Vector3(-6f, 0f, 3f));
            step("tunnel type changed", () => { r.e01.type = r.e12.type = net.library.types[3]; });
            step("chamber moved", () => r.n2.position += new Vector3(3f, 0f, 2f));
            step("room moved", () => r.n3.position += new Vector3(2f, 0f, 0f));
            step("bend node moved", () => r.n1.position += new Vector3(0f, 0f, -3f));
            // moved by hand: binds where it is put
            var before = floor.Distance;
            rightAt(r.e01, before);
            floor.transform.position += net.transform.TransformVector(dir * 3f);
            floor.Bind();
            check(floor.Mode == AnchorMode.Tunnel && Mathf.Abs(floor.Distance - before - 3f) < 0.05f, "moved by hand 3 m: now " + (floor.Distance - before).ToString("0.000") + " m further");
            return "anchors ok " + ok + ", bad " + bad + log;
        }

        static string Build(int seed, bool background)
        {
            var lib = TunnelGraphAssets.GetLibrary();
            Scene prevActive = SceneManager.GetActiveScene();
            // if the demo is already open, rebuild it in place (unless it has unsaved changes)
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool reuse = background && scene.IsValid() && scene.isLoaded;
            if (reuse)
            {
                if (scene.isDirty) return "CarX graph maze: demo scene is open with unsaved changes, not rebuilt";
                foreach (var r in scene.GetRootGameObjects()) Object.DestroyImmediate(r);
            }
            else scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, background ? NewSceneMode.Additive : NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);
            TunnelAssets.ApplyAtmosphere();
            var rnd = new System.Random(seed);
            const int N = 5;
            const float D = 24f;
            float[] levels = { 0f, -0.35f, -0.7f };

            var go = new GameObject("TunnelNetwork");
            var net = go.AddComponent<TunnelNetwork>();
            net.library = lib;

            var ids = new string[N, N];
            int shafts = 0;
            for (int x = 0; x < N; x++)
                for (int z = 0; z < N; z++)
                {
                    float y = (x == 0 && z == 0) ? 0f : levels[rnd.Next(levels.Length)];
                    var n = net.AddNode(new Vector3(x * D, y, z * D));
                    n.kind = GraphNodeKind.Chamber;
                    n.autoYaw = false;
                    n.yaw = 0f;
                    n.seed = rnd.Next(1, 99999);
                    if (!(x == 0 && z == 0) && rnd.NextDouble() < 0.22)
                    {
                        n.chamberShaft = true;
                        n.shaft = ShaftSpec.Preset(ShaftType.GrateSquare, 0f);
                        n.shaft.width = 1.3f;
                        n.shaft.height = 2.4f;
                        shafts++;
                    }
                    ids[x, z] = n.id;
                }

            // maze: depth-first spanning tree plus a few loops
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            var links = new List<int[]>();
            var seen = new bool[N, N];
            var stack = new Stack<Vector2Int>();
            stack.Push(Vector2Int.zero);
            seen[0, 0] = true;
            while (stack.Count > 0)
            {
                var c = stack.Peek();
                var options = new List<Vector2Int>();
                foreach (var d in dirs)
                {
                    var nb = c + d;
                    if (nb.x >= 0 && nb.x < N && nb.y >= 0 && nb.y < N && !seen[nb.x, nb.y]) options.Add(nb);
                }
                if (options.Count == 0) { stack.Pop(); continue; }
                var next = options[rnd.Next(options.Count)];
                seen[next.x, next.y] = true;
                links.Add(new[] { c.x, c.y, next.x, next.y });
                stack.Push(next);
            }
            int loops = 0, guard = 0;
            while (loops < 4 && guard++ < 300)
            {
                int x = rnd.Next(N), z = rnd.Next(N);
                var d = dirs[rnd.Next(4)];
                int x2 = x + d.x, z2 = z + d.y;
                if (x2 < 0 || x2 >= N || z2 < 0 || z2 >= N) continue;
                bool fresh = true;
                foreach (var l in links)
                    if ((l[0] == x && l[1] == z && l[2] == x2 && l[3] == z2) || (l[0] == x2 && l[1] == z2 && l[2] == x && l[3] == z)) { fresh = false; break; }
                if (!fresh) continue;
                links.Add(new[] { x, z, x2, z2 });
                loops++;
            }

            var shaftTypes = new[] { ShaftType.GrateLong, ShaftType.GrateLong, ShaftType.GrateSquare, ShaftType.Manhole, ShaftType.Manhole };
            Vector3 firstDir = Vector3.forward;
            for (int i = 0; i < links.Count; i++)
            {
                var l = links[i];
                var e = net.AddEdge(ids[l[0], l[1]], ids[l[2], l[3]], null);
                e.type = i == 0 ? lib.types[0] : lib.types[rnd.Next(lib.types.Count)];
                e.seed = rnd.Next(1, 99999);
                if (e.type.section.profile != TunnelProfile.Round && (i == 0 || rnd.NextDouble() < 0.42))
                {
                    var t = i == 0 ? ShaftType.GrateLong : shaftTypes[rnd.Next(shaftTypes.Length)];
                    var spec = ShaftSpec.Preset(t, i == 0 ? 9f : D * (float)(0.4 + rnd.NextDouble() * 0.2));
                    if (t == ShaftType.Manhole && rnd.NextDouble() < 0.5) spec.lateral = -10f;
                    e.shafts.Add(spec);
                    shafts++;
                }
                if (i == 0) firstDir = new Vector3(l[2] - l[0], 0f, l[3] - l[1]);
            }

            // long smooth tunnel from the south-east chamber: bends, a slope, two side tunnels joined directly, dead end
            var main = lib.types[0];
            string prev = ids[N - 1, 0];
            Vector3 p = net.Node(prev).position;
            float heading = 90f;
            float[] turns = { 0f, 0f, 20f, 0f, 20f, -10f };
            float[] lens = { 15f, 15f, 16f, 16f, 32f, 26f };
            float[] drops = { 0f, 0f, 0f, 0f, -1.5f, 0f };
            var mainNodes = new List<GraphNode>();
            for (int k = 0; k < turns.Length; k++)
            {
                heading += turns[k];
                var dir = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
                p += dir * lens[k];
                p.y += drops[k];
                var n = net.AddNode(p);
                n.seed = rnd.Next(1, 99999);
                var e = net.AddEdge(prev, n.id, main);
                e.seed = rnd.Next(1, 99999);
                if (k == 3) { e.shafts.Add(ShaftSpec.Preset(ShaftType.GrateLong, 8f)); shafts++; }
                prev = n.id;
                mainNodes.Add(n);
            }
            // side tunnels on straight parts: a round pipe from the left, a narrow box from the right
            Branch(net, mainNodes[0], Vector3.forward * 14f, lib.types[lib.types.Count - 1], rnd);
            var right = Quaternion.Euler(0f, 110f + 90f, 0f) * Vector3.forward;
            Branch(net, mainNodes[2], right * 16f, lib.types[2], rnd);

            // type changes without a chamber: octagon -> wide box (wall with an opening on both sides) -> round pipe out of the box end
            var hd = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            var r1 = net.AddNode(p + hd * 12f);
            r1.seed = rnd.Next(1, 99999);
            net.AddEdge(prev, r1.id, lib.types[3]).seed = rnd.Next(1, 99999);
            var r2 = net.AddNode(p + hd * 30f);
            r2.seed = rnd.Next(1, 99999);
            net.AddEdge(r1.id, r2.id, lib.types[lib.types.Count - 1]).seed = rnd.Next(1, 99999);
            // sharp 90° turn of the pipe (elbow), then another sharp turn into a narrow box (the pipe enters its side wall)
            var pipeDir = Quaternion.Euler(0f, 90f, 0f) * hd;
            var r3 = net.AddNode(r2.position + pipeDir * 15f);
            r3.seed = rnd.Next(1, 99999);
            net.AddEdge(r2.id, r3.id, lib.types[lib.types.Count - 1]).seed = rnd.Next(1, 99999);
            var boxDir = Quaternion.Euler(0f, 90f, 0f) * pipeDir;
            var r4 = net.AddNode(r3.position + boxDir * 14f);
            r4.seed = rnd.Next(1, 99999);
            net.AddEdge(r3.id, r4.id, lib.types[2]).seed = rnd.Next(1, 99999);
            // polygonal room at the end of the narrow box, with columns; a pipe and an octagon tunnel leave it
            var room = net.AddNode(r4.position + boxDir * 12f);
            room.seed = rnd.Next(1, 99999);
            room.kind = GraphNodeKind.Room;
            room.roomCorners = new List<Vector2> { new Vector2(-6f, -5f), new Vector2(4f, -6f), new Vector2(7f, -1f), new Vector2(6f, 5f), new Vector2(-1f, 7f), new Vector2(-7f, 3f) };
            room.columns.Add(new ColumnSpec { shape = ColumnShape.Square, size = 0.6f, position = new Vector2(-2f, 0f) });
            room.columns.Add(new ColumnSpec { shape = ColumnShape.Round, size = 0.7f, position = new Vector2(2f, 1f) });
            net.AddEdge(r4.id, room.id, lib.types[2]).seed = rnd.Next(1, 99999);
            var roomOut1 = net.AddNode(room.position + pipeDir * 16f);
            roomOut1.seed = rnd.Next(1, 99999);
            net.AddEdge(room.id, roomOut1.id, lib.types[lib.types.Count - 1]).seed = rnd.Next(1, 99999);
            var roomOut2 = net.AddNode(room.position + boxDir * 20f);
            roomOut2.seed = rnd.Next(1, 99999);
            net.AddEdge(room.id, roomOut2.id, lib.types[0]).seed = rnd.Next(1, 99999);

            AnchorRig rig = null;
            if (ScenePath == TestPath)
            {
                Branch(net, mainNodes[1], new Vector3(0f, 0f, 2.5f), lib.types[2], rnd);            // shorter than the lead-in
                Branch(net, net.Node(ids[2, 2]), new Vector3(0.3f, 0f, -1.2f), lib.types[0], rnd);   // inside the chamber
                var u = net.AddNode(roomOut2.position - boxDir * 3f + pipeDir * 0.5f);            // U-turn at the dead end
                net.AddEdge(roomOut2.id, u.id, lib.types[0]);
                AddJoinTests(net, lib, rnd);
                rig = AddAnchorTestGraph(net, lib, rnd);
            }
            var lw = System.Diagnostics.Stopwatch.StartNew();
            net.Layout();
            long layoutMs = lw.ElapsedMilliseconds;
            net.Build();

            var start = net.Node(ids[0, 0]).position;
            Vector3 spawn = start + Vector3.up * 0.05f - firstDir * 0.8f;
            TunnelMazeBuilder.SpawnPlayer(spawn, firstDir);

            EditorSceneManager.SaveScene(scene, ScenePath);
            string msg = "CarX graph maze: seed " + seed + ", nodes " + net.nodes.Count + ", lines " + net.edges.Count +
                         ", tunnels " + net.chains.Count + ", chambers " + net.chambers.Count + ", shafts " + shafts +
                         ", direct joins " + net.joins.Count + ", elbows " + Elbows(net) + ", issues " + net.issues.Count + ", layout " + layoutMs + " ms";
            foreach (var i in net.issues) msg += "\n  issue: " + i.message + " @ " + i.position;
            if (ScenePath == TestPath) { msg += "\n" + Probe(net); ExportGeo(net, "Logs/test_geo.txt"); msg += "\n" + AnchorTest(net, rig); }

            if (background)
            {
                if (prevActive.IsValid() && prevActive.isLoaded) SceneManager.SetActiveScene(prevActive);
                if (!reuse) EditorSceneManager.CloseScene(scene, true);
            }
            else
            {
                Selection.activeGameObject = go;
                var view = SceneView.lastActiveSceneView;
                if (view != null)
                {
                    view.pivot = spawn + firstDir * 7f + Vector3.up * 1.4f;
                    view.rotation = Quaternion.LookRotation(firstDir + Vector3.down * 0.05f);
                    view.size = 3f;
                    view.Repaint();
                }
            }
            return msg;
        }
    }
}
