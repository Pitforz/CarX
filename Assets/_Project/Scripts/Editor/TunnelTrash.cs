using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CarX.Level;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarX.EditorTools
{
    /// <summary>The "Мусор" tool's generator. The graph stores only a count and a seed per line / room / chamber
    /// (GraphEdge.trash, GraphNode.trash); the objects under the network's "Мусор" child are rebuilt from them, so they
    /// follow the tunnels and come out the same on every machine. Without the URP_WasteOvergrowth_SA pack nothing is touched.</summary>
    [InitializeOnLoad]
    public static class TunnelTrash
    {
        public const string RootName = "Мусор";
        public const string SettingsPath = TunnelGraphAssets.Dir + "/TrashSettings.asset";
        const string Pack = "Assets/URP_WasteOvergrowth_SA/Prefabs/";
        const int Version = 2;   // 2: no colliders, step zones

        static TunnelTrash()
        {
            var prev = TunnelNetwork.BuiltHook;
            TunnelNetwork.BuiltHook = net => { if (prev != null) prev(net); Sync(net); };
        }

        // ------------------------------------------------------------------ settings

        static TunnelTrashSettings cached;

        public static TunnelTrashSettings Settings
        {
            get
            {
                if (cached != null) return cached;
                cached = AssetDatabase.LoadAssetAtPath<TunnelTrashSettings>(SettingsPath);
                if (cached == null)
                {
                    cached = ScriptableObject.CreateInstance<TunnelTrashSettings>();
                    FillDefaults(cached);
                    TunnelAssets.EnsureFolder(TunnelGraphAssets.Dir);
                    AssetDatabase.CreateAsset(cached, SettingsPath);
                    AssetDatabase.SaveAssets();
                }
                else if (Count(cached.heaps) + Count(cached.piles) + Count(cached.litter) == 0 && cached.heaps.Count + cached.piles.Count + cached.litter.Count == 0 && PackInstalled)
                {
                    FillDefaults(cached);   // created on a machine without the pack: fill once the pack is there
                    EditorUtility.SetDirty(cached);
                    AssetDatabase.SaveAssets();
                }
                return cached;
            }
        }

        public static bool PackInstalled { get { return AssetDatabase.IsValidFolder("Assets/URP_WasteOvergrowth_SA"); } }

        /// <summary>True when at least one prefab of the settings is really there (the pack is installed).</summary>
        public static bool Ready
        {
            get { var s = Settings; return s != null && Count(s.heaps) + Count(s.piles) + Count(s.litter) > 0; }
        }

        static int Count(List<GameObject> l) { int n = 0; foreach (var g in l) if (g != null) n++; return n; }

        public static void FillDefaults(TunnelTrashSettings s)
        {
            s.heaps.Clear(); s.piles.Clear(); s.litter.Clear();
            foreach (int i in new[] { 7, 8, 9, 10, 11, 12, 14 }) Add(s.heaps, "TrashGroup/Prefab_TrashGroup_" + i + ".prefab");
            for (int i = 1; i <= 3; i++) Add(s.piles, "TrashPile/Prefab_Pile" + i + ".prefab");
            if (AssetDatabase.IsValidFolder(Pack + "Bottles/Crushed"))
                foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { Pack + "Bottles/Crushed" }))
                    s.litter.Add(AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)));
            foreach (var p in new[] { "Paper/Prefab_NewsPaper_flat", "Paper/Prefab_Newspaper_folded", "Paper/Prefab_Paper_flat",
                                      "Paper/Prefab_Paper_flat_2", "Paper/Prefab_Paper_folded", "FooDContainer/Prefab_BurgerBox_Cracked",
                                      "FooDContainer/Prefab_Pizzabox", "FooDContainer/Prefab_SaiceCup" })
                Add(s.litter, p + ".prefab");
        }

        static void Add(List<GameObject> l, string rel)
        {
            var g = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + rel);
            if (g != null) l.Add(g);
        }

        // ------------------------------------------------------------------ editing the counts (the tool)

        /// <summary>Spots on the line / room / chamber (0 if it cannot hold trash).</summary>
        public static int Spots(TunnelNetwork net, GraphEdge e) { return e != null ? e.trash : 0; }

        public static bool CanHold(TunnelNetwork net, GraphNode n)
        {
            return n != null && (net.rooms.ContainsKey(n.id) || net.chambers.ContainsKey(n.id));
        }

        public static int NewSeed() { return Random.Range(1, 99999); }

        /// <summary>"+ везде" adds this many spots to a line: one per settings step of length, at least one.</summary>
        public static int EverywhereStep(TunnelNetwork net, GraphEdge e, int perClick)
        {
            float len;
            if (!net.EdgeSpan(e, out len)) return 0;
            return perClick * Mathf.Max(1, Mathf.RoundToInt(len / 20f));
        }

        // ------------------------------------------------------------------ sync: objects from data

        /// <summary>Brings the objects under "Мусор" in line with the graph. Only changed lines / rooms are rebuilt.</summary>
        public static void Sync(TunnelNetwork net)
        {
            if (net == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool any = false;
            foreach (var e in net.edges) if (e.trash > 0) { any = true; break; }
            if (!any) foreach (var n in net.nodes) if (n.trash > 0) { any = true; break; }
            var root = net.transform.Find(RootName);
            if (!any && root == null) return;
            if (!Ready) return;   // no pack on this machine: keep whatever the scene has
            var s = Settings;
            net.EnsureLayout();
            if (root == null)
            {
                var go = new GameObject(RootName);
                go.transform.SetParent(net.transform, false);
                root = go.transform;
                if (TunnelNetworkEditor.Schematic) SceneVisibilityManager.instance.Hide(go, true);
            }
            string settingsKey = EditorJsonUtility.ToJson(s);
            var keep = new HashSet<Transform>();
            int rebuilt = 0;
            foreach (var e in net.edges)
            {
                if (e.trash <= 0) continue;
                float len;
                if (!net.EdgeSpan(e, out len)) continue;
                var g = Group(root, "Trash_" + e.id, keep);
                int sig = Hash(EdgeKey(net, e, len) + settingsKey);
                if (g.signature == sig && g.transform.childCount > 0) continue;
                Clear(g);
                g.owner = e.id;
                g.signature = sig;
                steps.Clear();
                g.items = FillEdge(net, e, len, s, g.transform);
                g.steps = new List<TrashStepBox>(steps);
                rebuilt++;
            }
            foreach (var n in net.nodes)
            {
                if (n.trash <= 0) continue;
                List<Vector2> poly; List<Door> doors; float yaw;
                if (!Polygon(net, n, out poly, out doors, out yaw)) continue;
                var g = Group(root, "Trash_" + n.id, keep);
                int sig = Hash(NodeKey(net, n, poly, doors) + settingsKey);
                if (g.signature == sig && g.transform.childCount > 0) continue;
                Clear(g);
                g.owner = n.id;
                g.signature = sig;
                steps.Clear();
                g.items = FillRoom(net, n, poly, doors, s, g.transform);
                g.steps = new List<TrashStepBox>(steps);
                rebuilt++;
            }
            var dead = new List<GameObject>();
            foreach (Transform ch in root) if (!keep.Contains(ch)) dead.Add(ch.gameObject);
            foreach (var d in dead) Object.DestroyImmediate(d);
            if (root.childCount == 0 && !any) { Object.DestroyImmediate(root.gameObject); root = null; }
            if ((rebuilt > 0 || dead.Count > 0) && net.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(net.gameObject.scene);
        }

        /// <summary>Total spots and objects of a network (for the panel).</summary>
        public static void Stats(TunnelNetwork net, out int spots, out int places, out int objects)
        {
            spots = places = objects = 0;
            foreach (var e in net.edges) if (e.trash > 0) { spots += e.trash; places++; }
            foreach (var n in net.nodes) if (n.trash > 0) { spots += n.trash; places++; }
            var root = net.transform.Find(RootName);
            if (root != null) foreach (var g in root.GetComponentsInChildren<TunnelTrashGroup>(true)) objects += g.items;
        }

        static TunnelTrashGroup Group(Transform root, string name, HashSet<Transform> keep)
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
            var g = t.GetComponent<TunnelTrashGroup>();
            if (g == null) g = t.gameObject.AddComponent<TunnelTrashGroup>();
            return g;
        }

        static void Clear(TunnelTrashGroup g)
        {
            for (int i = g.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(g.transform.GetChild(i).gameObject);
            g.items = 0;
        }

        // ------------------------------------------------------------------ keys (what the result depends on)

        static string F(float v) { return Mathf.RoundToInt(v * 1000f).ToString(CultureInfo.InvariantCulture); }
        static string F(Vector3 v) { return F(v.x) + "," + F(v.y) + "," + F(v.z); }

        static string EdgeKey(TunnelNetwork net, GraphEdge e, float len)
        {
            var sb = new StringBuilder();
            sb.Append(Version).Append('|').Append(e.id).Append('|').Append(e.trash).Append('|').Append(e.trashSeed).Append('|').Append(F(len));
            for (int k = 0; k <= 4; k++)
            {
                Vector3 axis, dir; TunnelSection sec;
                if (net.EdgeFrame(e, len * k / 4f, out axis, out dir, out sec)) sb.Append('|').Append(F(axis)).Append(':').Append(F(dir));
            }
            var t = net.TypeOf(e);
            if (t != null) sb.Append("|t").Append(t.section.Hash()).Append(t.channelWater ? 'w' : 'd');
            sb.Append(DeadEnd(net, e.a) ? "|A" : "|a").Append(DeadEnd(net, e.b) ? "B" : "b");
            return sb.ToString();
        }

        static string NodeKey(TunnelNetwork net, GraphNode n, List<Vector2> poly, List<Door> doors)
        {
            var sb = new StringBuilder();
            sb.Append(Version).Append('|').Append(n.id).Append('|').Append(n.trash).Append('|').Append(n.trashSeed).Append('|').Append(F(n.position));
            foreach (var v in poly) sb.Append('|').Append(F(v.x)).Append(',').Append(F(v.y));
            foreach (var d in doors) sb.Append("|d").Append(d.wall).Append(',').Append(F(d.along)).Append(',').Append(F(d.half));
            foreach (var c in n.columns) sb.Append("|c").Append(F(c.position.x)).Append(',').Append(F(c.position.y)).Append(',').Append(F(c.size));
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

        static System.Random Rng(string owner, int seed, int spot)
        {
            return new System.Random(Hash(owner + "#" + seed + "#" + spot));
        }

        static float R(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }
        static float R(System.Random r, Vector2 range) { return R(r, range.x, range.y); }
        static int Ri(System.Random r, Vector2Int range) { return r.Next(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y) + 1); }

        public static bool DeadEnd(TunnelNetwork net, string nodeId)
        {
            var n = net.Node(nodeId);
            return n != null && n.kind == GraphNodeKind.Auto && net.EdgesOf(nodeId).Count == 1 && !net.IsChamber(nodeId) && !net.IsRoom(nodeId);
        }

        // ------------------------------------------------------------------ items

        enum Kind { Heap, Pile, Litter }   // same order as TrashKind

        /// <summary>An instantiated prefab and its bounds in the placement frame (pivot at 0, yaw applied, scaled).</summary>
        class Item
        {
            public GameObject go;
            public Quaternion inner;   // prefab root rotation, kept under our yaw
            public Bounds b;
        }

        static GameObject Pick(List<GameObject> list, System.Random r)
        {
            if (list.Count == 0) return null;
            return list[r.Next(list.Count)];   // index over the whole list: same choice on every machine, a missing prefab is just skipped
        }

        static Item Spawn(GameObject prefab, Transform parent, float yaw, float scale)
        {
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (go == null) return null;
            var t = go.transform;
            var inner = t.localRotation;
            var s0 = t.localScale;
            t.localScale = s0 * scale;
            var yawQ = Quaternion.Euler(0f, yaw, 0f);
            // bounds with the pivot at the parent's origin, rotated as it will be in the frame
            t.SetPositionAndRotation(parent.position, parent.rotation * yawQ * inner);
            var b = new Bounds();
            bool has = false;
            foreach (var rend in go.GetComponentsInChildren<Renderer>())
            {
                if (rend is ParticleSystemRenderer || !rend.enabled) continue;
                if (!has) { b = rend.bounds; has = true; } else b.Encapsulate(rend.bounds);
            }
            // paper sheets are URP decals (no renderer): a thin box just above the floor, as wide as the sheet.
            // Next to meshes they do not count (a heap's loose sheets would make it look much wider than it is).
            if (!has) foreach (var dp in go.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>())
            {
                if (!dp.enabled) continue;
                float w = Mathf.Max(dp.size.x, dp.size.y) * dp.transform.lossyScale.x;
                var db = new Bounds(dp.transform.position + Vector3.up * 0.04f, new Vector3(w, 0.08f, w));
                if (!has) { b = db; has = true; } else b.Encapsulate(db);
            }
            if (!has) b = new Bounds(parent.position, Vector3.one * 0.3f);
            // world AABB -> parent space (the parent is the trash group: identity under the network, so only its rotation matters)
            var inv = Quaternion.Inverse(parent.rotation);
            var c = b.center - parent.position;
            var lb = new Bounds(inv * c, Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = c + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                lb.Encapsulate(inv * corner);
            }
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;   // walk-through; steps are caught by TrashSteps
            return new Item { go = go, inner = yawQ * inner, b = lb };
        }

        /// <summary>Puts an item so that its bounds centre is at (x, ?, z) of the frame and its bottom at floor - sink.
        /// The bounds were measured in the group's space with the group's axes; the frame rotates them only around up
        /// (tunnels) or not at all (rooms), and a tilted tunnel just tilts the whole item with the frame.</summary>
        static void Put(TunnelNetwork net, Item it, Vector3 origin, Quaternion frame, float x, float z, float floorY, float sink, TrashKind kind)
        {
            var pivot = new Vector3(x - it.b.center.x, floorY - sink - it.b.min.y, z - it.b.center.z);
            var local = origin + frame * pivot;
            it.go.transform.SetPositionAndRotation(net.World(local), net.transform.rotation * frame * it.inner);
            // step zone: the item's footprint, from just under the floor to a little above what sticks out
            float minSide = kind == TrashKind.Litter ? 0.4f : 0.3f;
            float top = Mathf.Max(0.5f, it.b.size.y - sink + 0.2f);
            steps.Add(new TrashStepBox
            {
                center = origin + frame * new Vector3(x, floorY + (top - 0.2f) * 0.5f, z),   // group space = network space
                rotation = frame,
                size = new Vector3(Mathf.Max(minSide, it.b.size.x), top + 0.2f, Mathf.Max(minSide, it.b.size.z)),
                kind = kind
            });
        }

        static readonly List<TrashStepBox> steps = new List<TrashStepBox>();

        // ------------------------------------------------------------------ tunnels

        struct Placed { public Vector2 p; public float r; }   // (across, along) centre and footprint radius

        static bool Clear(List<Placed> placed, Vector2 p, float r)
        {
            foreach (var q in placed) if (Vector2.Distance(q.p, p) < (q.r + r) * 0.8f) return false;
            return true;
        }

        static int FillEdge(TunnelNetwork net, GraphEdge e, float len, TunnelTrashSettings s, Transform parent)
        {
            var type = net.TypeOf(e);
            bool deadA = DeadEnd(net, e.a), deadB = DeadEnd(net, e.b);
            float m0 = deadA ? 0.1f : s.endMargin, m1 = deadB ? 0.1f : s.endMargin;
            // dead-end corners first, in an order the seed picks
            var corners = new List<int>();   // 0/1 = end A left/right, 2/3 = end B
            if (deadA) { corners.Add(0); corners.Add(1); }
            if (deadB) { corners.Add(2); corners.Add(3); }
            var ro = Rng(e.id, e.trashSeed, -1);
            for (int i = corners.Count - 1; i > 0; i--) { int j = ro.Next(i + 1); int t = corners[i]; corners[i] = corners[j]; corners[j] = t; }

            var placed = new List<Placed>();
            int count = 0;
            for (int spot = 0; spot < e.trash; spot++)
            {
                var r = Rng(e.id, e.trashSeed, spot);
                int corner = spot < corners.Count ? corners[spot] : -1;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    int made = EdgeSpot(net, e, len, type, s, parent, r, corner, m0, m1, placed, attempt == 5);
                    if (made >= 0) { count += made; break; }
                    corner = -1;   // a corner that does not fit (short line) becomes an ordinary spot
                }
            }
            return count;
        }

        /// <summary>One spot on a line; -1 when it did not fit (overlap or inside a room / chamber), the caller retries.</summary>
        static int EdgeSpot(TunnelNetwork net, GraphEdge e, float len, TunnelType type, TunnelTrashSettings s, Transform parent,
                            System.Random r, int corner, float m0, float m1, List<Placed> placed, bool force)
        {
            bool center = corner < 0 && r.NextDouble() < s.centerChance;
            float side = corner >= 0 ? (corner % 2 == 0 ? -1f : 1f) : (r.NextDouble() < 0.5 ? -1f : 1f);
            float u = (float)r.NextDouble();
            float pileP = corner >= 0 ? Mathf.Min(1f, s.pileChance * 2f) : s.pileChance;
            Kind kind = center ? Kind.Litter : (r.NextDouble() < pileP ? Kind.Pile : Kind.Heap);
            var prefab = Pick(kind == Kind.Heap ? s.heaps : kind == Kind.Pile ? s.piles : s.litter, r);
            if (kind != Kind.Litter && prefab == null) { kind = Kind.Litter; prefab = Pick(s.litter, r); }   // pack without that prefab
            float scale = R(r, s.scale);
            float yaw = (r.NextDouble() < 0.5 ? 0f : 180f) + R(r, -s.yawJitter, s.yawJitter);
            if (kind == Kind.Litter) yaw = R(r, 0f, 360f);
            float sinkK = R(r, s.pileSink), sinkM = R(r, s.heapSink);
            int litterN = center ? Ri(r, s.litterCenter) : Ri(r, s.litterAround);
            int litterSeed = r.Next();

            float usable = len - m0 - m1;
            if (len < 0.6f) return 0;
            float d = corner >= 0 ? (corner < 2 ? 0f : len) : (usable > 0.4f ? m0 + u * usable : len * (0.25f + 0.5f * u));
            Vector3 axis, dir; TunnelSection sec;
            if (!net.EdgeFrame(e, Mathf.Clamp(d, 0f, len), out axis, out dir, out sec)) return 0;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            right = right.sqrMagnitude < 1e-6f ? Vector3.right : right.normalized;
            Vector3 up = Vector3.Cross(dir, right).normalized;
            var frame = Quaternion.LookRotation(dir, up);
            float floorHalf = Mathf.Max(0.3f, Mathf.Min(sec.FloorHalfWidth, sec.WallXAt(sec.FloorHeightAt(0f) + 0.05f)));

            int made = 0;
            Vector2 centre;   // (across, along offset from d)
            if (kind == Kind.Litter && center)
            {
                float lim = floorHalf * (type != null && type.channelWater ? 0.55f : 0.45f);
                centre = new Vector2(R(r, -lim, lim), 0f);
                if (!force && !Clear(placed, new Vector2(centre.x, d), 0.6f)) return -1;
                if (Inside(net, axis + right * centre.x)) return -1;
                placed.Add(new Placed { p = new Vector2(centre.x, d), r = 0.6f });
            }
            else
            {
                var it = Spawn(prefab, parent, yaw, scale);
                if (it == null) return 0;
                float hx = it.b.extents.x, hz = it.b.extents.z, h = it.b.size.y;
                // too wide for the tunnel: shrink (a heap may take at most ~45% of the floor)
                float maxHalf = floorHalf * (kind == Kind.Pile ? 0.5f : 0.45f);
                if (hx > maxHalf)
                {
                    float k = maxHalf / hx;
                    Object.DestroyImmediate(it.go);
                    it = Spawn(prefab, parent, yaw, scale * k);
                    if (it == null) return 0;
                    hx = it.b.extents.x; hz = it.b.extents.z; h = it.b.size.y;
                }
                float sink = kind == Kind.Pile ? h * sinkK : sinkM;
                float wallY = sec.FloorHeightAt(side * floorHalf) + Mathf.Clamp((h - sink) * 0.5f, 0.05f, 0.6f);
                float wallX = sec.WallXAt(wallY);
                float x = side * Mathf.Max(0f, wallX - hx * (kind == Kind.Pile ? 0.6f : 1f) - s.wallGap);
                float z = 0f;
                if (corner >= 0)
                {
                    // into the corner of the end wall
                    float back = hz + Mathf.Max(0.02f, s.wallGap);
                    if (len < back * 2f + 0.2f) { Object.DestroyImmediate(it.go); return -1; }
                    d = corner < 2 ? back : len - back;
                    net.EdgeFrame(e, d, out axis, out dir, out sec);
                }
                float fy = Mathf.Max(sec.FloorHeightAt(x - hx * 0.5f), sec.FloorHeightAt(x), sec.FloorHeightAt(x + hx * 0.5f));
                if (kind == Kind.Pile) fy = sec.FloorHeightAt(x);
                float rad = Mathf.Max(hx, hz);
                if ((!force && !Clear(placed, new Vector2(x, d), rad)) || Inside(net, axis + right * x))
                {
                    Object.DestroyImmediate(it.go);
                    return -1;
                }
                placed.Add(new Placed { p = new Vector2(x, d), r = rad });
                Put(net, it, axis, frame, x, z, fy, sink, (TrashKind)(int)kind);
                made++;
                centre = new Vector2(x, 0f);
            }

            // litter around the spot
            var lr = new System.Random(litterSeed);
            float limX = floorHalf - 0.12f;
            for (int i = 0; i < litterN; i++)
            {
                var prefabL = Pick(s.litter, lr);
                float a = R(lr, 0f, Mathf.PI * 2f), dist = R(lr, 0.35f, 1.1f);
                float lx = Mathf.Clamp(centre.x + Mathf.Cos(a) * dist, -limX, limX);
                float ld = Mathf.Clamp(d + Mathf.Sin(a) * dist, 0.15f, len - 0.15f);
                float lyaw = R(lr, 0f, 360f), ls = R(lr, s.scale);
                if (prefabL == null) continue;
                Vector3 la, ldir; TunnelSection lsec;
                if (!net.EdgeFrame(e, ld, out la, out ldir, out lsec)) continue;
                Vector3 lright = Vector3.Cross(Vector3.up, ldir);
                lright = lright.sqrMagnitude < 1e-6f ? Vector3.right : lright.normalized;
                if (Inside(net, la + lright * lx)) continue;
                var it = Spawn(prefabL, parent, lyaw, ls);
                if (it == null) continue;
                var lf = Quaternion.LookRotation(ldir, Vector3.Cross(ldir, lright).normalized);
                Put(net, it, la, lf, lx, 0f, lsec.FloorHeightAt(lx), 0.005f, TrashKind.Litter);
                made++;
            }
            return made;
        }

        /// <summary>Inside a room or a chamber (their trash is their own, and lines reach into them).</summary>
        static bool Inside(TunnelNetwork net, Vector3 p)
        {
            return net.RoomAt(p, 0.3f) != null || net.ChamberAt(p, 0.3f) != null;
        }

        // ------------------------------------------------------------------ rooms and chambers

        struct Door { public int wall; public float along, half; }

        /// <summary>Floor outline (node-relative, counter-clockwise) of a room or a chamber, and its doorways.</summary>
        static bool Polygon(TunnelNetwork net, GraphNode n, out List<Vector2> poly, out List<Door> doors, out float yaw)
        {
            poly = null; doors = new List<Door>(); yaw = 0f;
            TunnelNetwork.RoomInfo room;
            if (net.rooms.TryGetValue(n.id, out room) && room.corners != null && room.corners.Count >= 3)
            {
                poly = new List<Vector2>(room.corners);
                foreach (var d in room.doors)
                    doors.Add(new Door { wall = d.wall, along = d.along, half = (d.chain != null && d.chain.type != null ? d.chain.type.section.HalfSpan : 1.5f) * d.stretch + 0.4f });
                return true;
            }
            TunnelNetwork.ChamberInfo ch;
            if (net.chambers.TryGetValue(n.id, out ch))
            {
                yaw = ch.yaw;
                var q = Quaternion.Euler(0f, ch.yaw, 0f);
                float hx = n.chamberSize.x * 0.5f, hz = n.chamberSize.y * 0.5f;
                poly = new List<Vector2>();
                foreach (var c in new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz) })
                {
                    var w = q * c;
                    poly.Add(new Vector2(w.x, w.z));
                }
                if (Area(poly) < 0f) poly.Reverse();
                // every wall may have a tunnel in its middle
                for (int i = 0; i < 4; i++)
                {
                    float l = Vector2.Distance(poly[i], poly[(i + 1) % 4]);
                    doors.Add(new Door { wall = i, along = l * 0.5f, half = 1.9f });
                }
                return true;
            }
            return false;
        }

        static float Area(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) { var u = p[i]; var v = p[(i + 1) % p.Count]; a += u.x * v.y - v.x * u.y; }
            return a * 0.5f;
        }

        static int FillRoom(TunnelNetwork net, GraphNode n, List<Vector2> poly, List<Door> doors, TunnelTrashSettings s, Transform parent)
        {
            var used = new HashSet<int>();
            var placed = new List<Placed>();
            int count = 0;
            for (int spot = 0; spot < n.trash; spot++)
            {
                var r = Rng(n.id, n.trashSeed, spot);
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    int made = RoomSpot(net, n, poly, doors, s, parent, r, used, placed, attempt == 7);
                    if (made >= 0) { count += made; break; }
                }
            }
            return count;
        }

        static int RoomSpot(TunnelNetwork net, GraphNode n, List<Vector2> poly, List<Door> doors, TunnelTrashSettings s, Transform parent,
                            System.Random r, HashSet<int> used, List<Placed> placed, bool force)
        {
            int k = poly.Count;
            double roll = r.NextDouble();
            bool wantCorner = used.Count < k && roll < 0.5;
            bool center = !wantCorner && roll > 1.0 - s.centerChance;
            int corner = -1;
            if (wantCorner) { corner = r.Next(k); while (used.Contains(corner)) corner = (corner + 1) % k; }
            int wall = r.Next(k);
            float u = (float)r.NextDouble(), v = (float)r.NextDouble();
            float pileP = corner >= 0 ? Mathf.Min(1f, s.pileChance * 2f) : s.pileChance;
            Kind kind = center ? Kind.Litter : (r.NextDouble() < pileP ? Kind.Pile : Kind.Heap);
            var prefab = Pick(kind == Kind.Heap ? s.heaps : kind == Kind.Pile ? s.piles : s.litter, r);
            if (kind != Kind.Litter && prefab == null) { kind = Kind.Litter; prefab = Pick(s.litter, r); }
            float scale = R(r, s.scale);
            float jitter = R(r, -s.yawJitter, s.yawJitter);
            float sinkK = R(r, s.pileSink), sinkM = R(r, s.heapSink);
            int litterN = center ? Ri(r, s.litterCenter) : Ri(r, s.litterAround);
            int litterSeed = r.Next();

            var origin = n.position;
            Vector2 c;   // centre on the floor, node-relative
            int made = 0;
            if (kind == Kind.Litter && center)
            {
                var min = poly[0]; var max = poly[0];
                foreach (var p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
                c = new Vector2(Mathf.Lerp(min.x, max.x, u), Mathf.Lerp(min.y, max.y, v));
                if (!TunnelNetwork.InPolygon(poly, c) || TunnelNetwork.PolyDistance(poly, c) < 1f || OnColumn(n, c, 0.6f)) return -1;
                if (!force && !Clear(placed, c, 0.6f)) return -1;
                placed.Add(new Placed { p = c, r = 0.6f });
            }
            else
            {
                // wall direction the heap lines up with: the corner's first wall or the chosen wall
                int w = corner >= 0 ? corner : wall;
                Vector2 a = poly[w], b = poly[(w + 1) % k];
                var t2 = (b - a).normalized;
                float wallYaw = Mathf.Atan2(t2.x, t2.y) * Mathf.Rad2Deg;
                var it = Spawn(prefab, parent, wallYaw + (r.NextDouble() < 0.5 ? 0f : 180f) + jitter, scale);
                if (it == null) return 0;
                float rad = Mathf.Max(it.b.extents.x, it.b.extents.z);
                float h = it.b.size.y;
                var inward = new Vector2(-t2.y, t2.x);   // counter-clockwise: inside is on the left
                if (corner >= 0)
                {
                    Vector2 prev = poly[(corner - 1 + k) % k], here = poly[corner], next = poly[(corner + 1) % k];
                    var bis = ((prev - here).normalized + (next - here).normalized);
                    if (bis.sqrMagnitude < 1e-4f) bis = inward; else bis.Normalize();
                    if (!TunnelNetwork.InPolygon(poly, here + bis * 0.05f)) { used.Add(corner); Object.DestroyImmediate(it.go); return -1; }   // concave corner
                    float half = Vector2.Angle(prev - here, next - here) * 0.5f * Mathf.Deg2Rad;
                    float push = (kind == Kind.Pile ? rad * 0.6f : rad) / Mathf.Max(0.3f, Mathf.Sin(half)) + s.wallGap;
                    c = here + bis * Mathf.Min(push, 3f);
                }
                else
                {
                    float l = Vector2.Distance(a, b);
                    if (l < rad * 2f + 0.4f) { Object.DestroyImmediate(it.go); return -1; }
                    float along = Mathf.Lerp(rad + 0.2f, l - rad - 0.2f, u);
                    foreach (var d in doors)
                        if (d.wall == w && Mathf.Abs(d.along - along) < d.half + rad) { Object.DestroyImmediate(it.go); return -1; }
                    c = a + t2 * along + inward * ((kind == Kind.Pile ? rad * 0.6f : rad) + s.wallGap);
                }
                if (!TunnelNetwork.InPolygon(poly, c) || OnColumn(n, c, rad) || NearDoor(poly, doors, c, rad) || (!force && !Clear(placed, c, rad)))
                {
                    Object.DestroyImmediate(it.go);
                    return -1;
                }
                if (corner >= 0) used.Add(corner);
                placed.Add(new Placed { p = c, r = rad });
                float sink = kind == Kind.Pile ? h * sinkK : sinkM;
                Put(net, it, origin, Quaternion.identity, c.x, c.y, 0f, sink, (TrashKind)(int)kind);
                made++;
            }

            var lr = new System.Random(litterSeed);
            for (int i = 0; i < litterN; i++)
            {
                var prefabL = Pick(s.litter, lr);
                float ang = R(lr, 0f, Mathf.PI * 2f), dist = R(lr, 0.35f, 1.1f);
                var p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                float lyaw = R(lr, 0f, 360f), ls = R(lr, s.scale);
                if (prefabL == null || !TunnelNetwork.InPolygon(poly, p) || TunnelNetwork.PolyDistance(poly, p) < 0.15f || OnColumn(n, p, 0.15f)) continue;
                var it = Spawn(prefabL, parent, lyaw, ls);
                if (it == null) continue;
                Put(net, it, origin, Quaternion.identity, p.x, p.y, 0f, 0.005f, TrashKind.Litter);
                made++;
            }
            return made;
        }

        static bool OnColumn(GraphNode n, Vector2 p, float r)
        {
            foreach (var col in n.columns) if (Vector2.Distance(col.position, p) < col.size * 0.75f + r) return true;
            return false;
        }

        static bool NearDoor(List<Vector2> poly, List<Door> doors, Vector2 p, float r)
        {
            int k = poly.Count;
            foreach (var d in doors)
            {
                Vector2 a = poly[d.wall % k], b = poly[(d.wall + 1) % k];
                var door = a + (b - a).normalized * d.along;
                if (Vector2.Distance(door, p) < d.half + r) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ automation

        /// <summary>Bridge: logs the size of every prefab in the settings.</summary>
        public static string Report()
        {
            var s = Settings;
            var sb = new StringBuilder("CarX trash prefabs:");
            var tmp = new GameObject("_trash_probe");
            try
            {
                foreach (var list in new[] { s.heaps, s.piles, s.litter })
                    foreach (var p in list)
                    {
                        if (p == null) { sb.Append("\n  (missing)"); continue; }
                        var it = Spawn(p, tmp.transform, 0f, 1f);
                        int rb = it.go.GetComponentsInChildren<Rigidbody>(true).Length, cols = it.go.GetComponentsInChildren<Collider>(true).Length;
                        int gos = it.go.GetComponentsInChildren<Transform>(true).Length;
                        sb.Append("\n  " + p.name + " size " + it.b.size.ToString("0.00") + " centre " + it.b.center.ToString("0.00") + " objects " + gos + " colliders " + cols + " rigidbodies " + rb);
                        Object.DestroyImmediate(it.go);
                    }
            }
            finally { Object.DestroyImmediate(tmp); }
            Debug.Log(sb.ToString());
            return "ok";
        }
    }
}
