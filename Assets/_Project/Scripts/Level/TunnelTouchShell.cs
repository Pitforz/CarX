using System.Collections.Generic;
using UnityEngine;

namespace CarX.Level
{
    /// <summary>
    /// Simplified invisible copies of every tunnel-graph piece, built in play mode by probing the real mesh with rays,
    /// so they lie exactly on the visible surface. Nothing is saved into scenes.
    /// - Touch band (<see cref="ShellName"/>, layer 31): a smooth band at hand height over the walls. The dark-orientation
    ///   hands feel it instead of the visible mesh, so slab joints, bevels and seams cannot catch them. Collides with nothing.
    /// - Collision shell (<see cref="CollisionName"/>, layer 30): smooth walls, floor and ceiling the player walks in
    ///   instead of the visible mesh (the player ignores the visible mesh where a piece has one). Everything else
    ///   (shots, the flashlight, dropped items, NPC) still uses the visible mesh.
    /// Doorways, side tunnels and shaft openings stay open in both.
    /// </summary>
    public static class TunnelTouchShell
    {
        public const string ShellName = "_TouchShell";
        public const string CollisionName = "_CollisionShell";
        /// <summary>Unnamed user layer; physics collisions with every layer are switched off at build time.</summary>
        public const int Layer = 31;
        /// <summary>Unnamed user layer of the collision shell; collides only with the player's layer.</summary>
        public const int CollisionLayer = 30;
        /// <summary>Switch off to keep the player on the visible mesh.</summary>
        public static bool BuildCollision => Config == null || Config.shellCollision;

        static CarX.DarkOrientation.DarkOrientationSettings Config => CarX.DarkOrientation.DarkOrientationSettings.Load();

        /// <summary>Band above the floor, m. Lowered under low ceilings.</summary>
        public static float BandLow => Config != null ? Config.shellBandLow : 0.7f;
        public static float BandHigh => Config != null ? Config.shellBandHigh : 1.9f;
        const int Rows = 4;
        static bool s_Collision;
        static float Step => Config != null ? Config.shellStep : 0.25f;           // spacing of probes along a wall, m
        static float MaxJump => Config != null ? Config.shellMaxJump : 0.2f;         // depth change between neighbours that breaks the band (an opening, a column), m
        static bool StraightWalls => Config == null || Config.collisionStraightWalls;
        static float WallProbeHeight => Config != null ? Config.collisionWallHeight : 1.2f;
        static float Inset => Config != null ? Config.shellInset : 0.6f;           // rooms and junctions: probes start this far inside the wall, m

        static readonly List<Vector3> s_Verts = new();
        static readonly List<int> s_Tris = new();

        public static bool IsShell(Collider c) => c != null && c.gameObject.layer == Layer && c.name == ShellName;

        public static bool IsCollisionShell(Collider c) => c != null && c.gameObject.layer == CollisionLayer && c.name == CollisionName;

        public static bool HasShell(Transform owner) => owner != null && owner.Find(ShellName) != null;

        public static bool HasCollision(Transform owner) => owner != null && owner.Find(CollisionName) != null;

        // ------------------------------------------------------------------ play-mode bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHooks() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            BuildAll();
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => BuildAll();

        /// <summary>Builds the band for every path, junction, room and column in the loaded scenes. Returns the count built.</summary>
        public static int BuildAll()
        {
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(Layer, i, true);
            int built = BuildPass(false);
            if (BuildCollision && Application.isPlaying)
            {
                int solid = BuildPass(true);
                int players = HookPlayers();
                Debug.Log("CarX TunnelTouchShell: touch bands " + built + ", collision shells " + solid + ", players " + players);
            }
            Physics.SyncTransforms();
            return built;
        }

        static int BuildPass(bool collision)
        {
            s_Collision = collision;
            int built = 0;
            foreach (var p in Object.FindObjectsByType<TunnelPath>(FindObjectsInactive.Exclude)) if (Build(p)) built++;
            foreach (var j in Object.FindObjectsByType<TunnelJunction>(FindObjectsInactive.Exclude)) if (Build(j)) built++;
            foreach (var r in Object.FindObjectsByType<TunnelRoom>(FindObjectsInactive.Exclude)) if (Build(r)) built++;
            foreach (var c in Object.FindObjectsByType<TunnelColumn>(FindObjectsInactive.Exclude)) if (Build(c)) built++;
            s_Collision = false;
            return built;
        }

        /// <summary>
        /// The player (tag Player with a CharacterController) walks in the collision shells and ignores the visible mesh
        /// of every piece that has one. The shell layer collides only with the player's layer.
        /// </summary>
        static int HookPlayers()
        {
            var players = new List<CharacterController>();
            foreach (var go in GameObject.FindGameObjectsWithTag("Player"))
                if (go.TryGetComponent(out CharacterController cc)) players.Add(cc);
            for (int i = 0; i < 32; i++)
            {
                bool playerLayer = false;
                foreach (var cc in players) if (cc.gameObject.layer == i) playerLayer = true;
                Physics.IgnoreLayerCollision(CollisionLayer, i, !playerLayer);
            }
            foreach (var cc in players)
            {
                foreach (var p in Object.FindObjectsByType<TunnelPath>(FindObjectsInactive.Exclude)) Ignore(cc, p.transform, WallCollider(p.transform));
                foreach (var j in Object.FindObjectsByType<TunnelJunction>(FindObjectsInactive.Exclude)) Ignore(cc, j.transform, WallCollider(j.transform));
                foreach (var r in Object.FindObjectsByType<TunnelRoom>(FindObjectsInactive.Exclude)) Ignore(cc, r.transform, WallCollider(r.transform));
                foreach (var c in Object.FindObjectsByType<TunnelColumn>(FindObjectsInactive.Exclude)) Ignore(cc, c.transform, c.GetComponent<MeshCollider>());
            }
            return players.Count;
        }

        static void Ignore(CharacterController cc, Transform owner, Collider visible)
        {
            if (visible == null) return;
            Physics.IgnoreCollision(cc, visible, HasCollision(owner));
        }

        /// <summary>Removes every built band (they are never saved, this is for edit-mode checks).</summary>
        public static int ClearAll()
        {
            // Bands are DontSave objects, which FindObjectsByType does not return: look under their owners.
            int n = 0;
            foreach (var p in Object.FindObjectsByType<TunnelPath>(FindObjectsInactive.Include)) n += ClearUnder(p.transform);
            foreach (var j in Object.FindObjectsByType<TunnelJunction>(FindObjectsInactive.Include)) n += ClearUnder(j.transform);
            foreach (var r in Object.FindObjectsByType<TunnelRoom>(FindObjectsInactive.Include)) n += ClearUnder(r.transform);
            foreach (var c in Object.FindObjectsByType<TunnelColumn>(FindObjectsInactive.Include)) n += ClearUnder(c.transform);
            return n;
        }

        static int ClearUnder(Transform owner)
        {
            int n = 0;
            foreach (var name in new[] { ShellName, CollisionName })
            {
                var shell = owner.Find(name);
                if (shell == null) continue;
                Kill(shell.gameObject);
                n++;
            }
            return n;
        }

        /// <summary>
        /// Builds the bands, checks them against the visible walls from every path's centre line and removes them again.
        /// Reports how many side rays at hand height met the band and how far it lies from the visible wall.
        /// </summary>
        public static string SelfTest()
        {
            int built = BuildAll();
            int rays = 0, onShell = 0, onWall = 0;
            float maxGap = 0f, sumGap = 0f;
            int mask = 1 << Layer;
            foreach (var p in Object.FindObjectsByType<TunnelPath>(FindObjectsInactive.Exclude))
            {
                var wall = WallCollider(p.transform);
                if (wall == null) continue;
                foreach (var s in p.slabs)
                    for (float d = 0.1f; d < s.length; d += 0.5f)
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Vector3 o = p.transform.TransformPoint(s.Point(d, 0f, 0f)) + Vector3.up * 1.3f;
                            Vector3 dir = p.transform.TransformDirection(s.right * side).normalized;
                            rays++;
                            bool w = wall.Raycast(new Ray(o, dir), out var wh, 4f);
                            if (w) onWall++;
                            if (Physics.Raycast(o, dir, out var sh, 4f, mask, QueryTriggerInteraction.Ignore))
                            {
                                onShell++;
                                if (w)
                                {
                                    float gap = Mathf.Abs(sh.distance - wh.distance);
                                    sumGap += gap;
                                    maxGap = Mathf.Max(maxGap, gap);
                                }
                            }
                        }
            }
            int cleared = ClearAll();
            return $"built {built}, cleared {cleared}; rays {rays}, visible wall {onWall}, band {onShell}, " +
                   $"mean gap {(onShell > 0 ? sumGap / onShell : 0f):0.0000} m, max gap {maxGap:0.000} m";
        }

        /// <summary>
        /// Looks for places where the band cuts in front of the visible wall (corners) or is missing:
        /// 72 rays at hand height from points along every path, junction and room. Lists the worst spots.
        /// </summary>
        public static string CornerTestCollision()
        {
            if (Application.isPlaying) return "run in edit mode";
            BuildPass(true);
            s_TestCollision = true;
            try { return CornerTest(); } finally { s_TestCollision = false; }
        }
        static bool s_TestCollision;

        public static string CornerTest()
        {
            bool playing = Application.isPlaying;
            if (!playing) BuildPass(false);
            Physics.SyncTransforms();
            int bandMask = s_TestCollision ? 1 << CollisionLayer : 1 << Layer;
            int wallMask = ~((1 << Layer) | (1 << CollisionLayer) | (1 << 2));
            var origins = new List<(string name, Vector3 p)>();
            foreach (var path in Object.FindObjectsByType<TunnelPath>(FindObjectsInactive.Exclude))
                foreach (var sl in path.slabs)
                    for (float d = 0f; d <= sl.length + 0.01f; d += 0.5f)
                        origins.Add((path.name, path.transform.TransformPoint(sl.Point(Mathf.Min(d, sl.length), 0f, 0f)) + Vector3.up * 1.3f));
            foreach (var j in Object.FindObjectsByType<TunnelJunction>(FindObjectsInactive.Exclude))
                for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++)
                    origins.Add((j.name, j.transform.TransformPoint(new Vector3(a * j.sizeX * 0.3f, 0f, b * j.sizeZ * 0.3f)) + Vector3.up * 1.3f));
            foreach (var r in Object.FindObjectsByType<TunnelRoom>(FindObjectsInactive.Exclude))
            {
                Vector2 c = Vector2.zero;
                foreach (var q in r.corners) c += q;
                c /= Mathf.Max(1, r.corners.Count);
                origins.Add((r.name, r.transform.TransformPoint(new Vector3(c.x, 0f, c.y)) + Vector3.up * 1.3f));
            }
            int rays = 0, cut = 0, missing = 0;
            var worst = new List<(float gap, string text)>();
            var hits = new RaycastHit[16];
            foreach (var (name, o) in origins)
                for (int k = 0; k < 72; k++)
                {
                    Vector3 dir = Quaternion.Euler(0f, k * 5f, 0f) * Vector3.forward;
                    int n = Physics.RaycastNonAlloc(o, dir, hits, 3f, wallMask, QueryTriggerInteraction.Ignore);
                    float wd = float.MaxValue;
                    Vector3 wp = default;
                    string wn = "";
                    for (int i = 0; i < n; i++)
                        if (!(hits[i].collider is CharacterController) && hits[i].distance < wd) { wd = hits[i].distance; wp = hits[i].point; wn = hits[i].collider.transform.parent != null ? hits[i].collider.transform.parent.name : hits[i].collider.name; }
                    if (wd > 3f) continue;
                    rays++;
                    bool band = Physics.Raycast(o, dir, out var bh, 3.5f, bandMask, QueryTriggerInteraction.Ignore);
                    float gap = band ? wd - bh.distance : -1f;
                    if (!band || bh.distance > wd + 0.3f) { missing++; continue; }
                    if (gap > 0.12f)
                    {
                        cut++;
                        worst.Add((gap, $"{name} wall {wp.x:0.0},{wp.y:0.0},{wp.z:0.0} of {wn} band {gap:0.00} m in front (band of {bh.collider.transform.parent.name}, from {o.x:0.0},{o.z:0.0} dir {k * 5})"));
                    }
                }
            worst.Sort((a, b) => b.gap.CompareTo(a.gap));
            var sb = new System.Text.StringBuilder();
            sb.Append($"origins {origins.Count}, rays {rays}, band in front >12cm {cut}, no band {missing}");
            for (int i = 0; i < worst.Count && i < 12; i++) sb.Append("\n  ").Append(worst[i].text);
            if (!playing) ClearAll();
            return sb.ToString();
        }

        public static string Slabs(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var path in Object.FindObjectsByType<TunnelPath>(FindObjectsInactive.Exclude))
            {
                if (path.name != name) continue;
                var t = path.transform;
                foreach (var sl in path.slabs)
                {
                    Vector3 a0 = t.TransformPoint(sl.a), f = t.TransformDirection(sl.fwd);
                    sb.Append($"\n  a {a0.x:0.00},{a0.z:0.00} fwd {f.x:0.00},{f.y:0.00},{f.z:0.00} len {sl.length:0.00} gap {sl.gapStart}/{sl.gapEnd}");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Edit mode: builds the collision shells, then puts the player's capsule along every path (centre and near both
        /// walls) and lists what it overlaps (the player ignores the visible mesh of pieces with a shell) and where the
        /// shell floor is missing under it.
        /// </summary>
        public static float BlockMargin = 0.12f;
        public static string PlayerBlockTestNear() { BlockMargin = 0.03f; try { return PlayerBlockTest(); } finally { BlockMargin = 0.12f; } }
        static CapsuleCollider s_Probe;
        static CapsuleCollider MakeProbe()
        {
            var go = new GameObject("_BlockProbe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
            go.transform.position = new Vector3(0f, -10000f, 0f);
            var c = go.AddComponent<CapsuleCollider>();
            c.isTrigger = true;
            return c;
        }

        public static string PlayerBlockTest()
        {
            if (Application.isPlaying) return "run in edit mode";
            BuildPass(false);
            BuildPass(true);
            Physics.SyncTransforms();
            float radius = 0.5f, height = 2f;
            foreach (var go in GameObject.FindGameObjectsWithTag("Player"))
                if (go.TryGetComponent(out CharacterController cc)) { radius = cc.radius * go.transform.lossyScale.x; height = cc.height * go.transform.lossyScale.y; break; }
            int mask = ~((1 << Layer) | (1 << 2));
            var cols = new Collider[16];
            var hits = new Dictionary<string, (int n, Vector3 at)>();
            var firstPen = new Dictionary<string, string>();
            int samples = 0, blocked = 0, noFloor = 0;
            Vector3 firstNoFloor = default;
            foreach (var path in Object.FindObjectsByType<TunnelPath>(FindObjectsInactive.Exclude))
            {
                var t = path.transform;
                bool shelled = HasCollision(t);
                float half = path.section.Width * 0.5f;
                foreach (var sl in path.slabs)
                    for (float d = 0.1f; d < sl.length; d += 0.25f)
                        for (int k = -1; k <= 1; k++)
                        {
                            float x = k * Mathf.Max(0f, half - radius - BlockMargin);
                            Vector3 c = t.TransformPoint(sl.Point(d, x, 0f));
                            Vector3 r = t.TransformDirection(sl.right).normalized;
                            if (!Physics.Raycast(c + Vector3.up * 1.2f, Vector3.down, out var fh, 2f, 1 << CollisionLayer, QueryTriggerInteraction.Ignore))
                            {
                                noFloor++;
                                if (noFloor == 1) firstNoFloor = c;
                                continue;
                            }
                            samples++;
                            Vector3 p0 = fh.point + Vector3.up * (radius + 0.04f), p1 = fh.point + Vector3.up * Mathf.Max(radius + 0.05f, height - radius);
                            int n = Physics.OverlapCapsuleNonAlloc(p0, p1, radius, cols, mask, QueryTriggerInteraction.Ignore);
                            bool any = false;
                            for (int i = 0; i < n; i++)
                            {
                                var col = cols[i];
                                if (col is CharacterController) continue;
                                var owner = col.transform.parent;
                                if (shelled && owner == t && col.name == TunnelPath.GeneratedName) continue;   // player ignores it
                                if (owner != null && col.name == TunnelPath.GeneratedName && HasCollision(owner)) continue;
                                if (IsCollisionShell(col) && col.transform.parent == t) {}
                                string pen = "";
                                if (Physics.ComputePenetration(s_Probe != null ? s_Probe : (s_Probe = MakeProbe()), (p0 + p1) * 0.5f, Quaternion.identity, col, col.transform.position, col.transform.rotation, out var pd0, out var _)
                                    && pd0.y < -0.85f) continue;   // the ceiling of a tunnel lower than the player: not what we look for
                                var probe = s_Probe != null ? s_Probe : (s_Probe = MakeProbe());
                                probe.radius = radius; probe.height = height;
                                Vector3 mid = (p0 + p1) * 0.5f;
                                if (Physics.ComputePenetration(probe, mid, Quaternion.identity, col, col.transform.position, col.transform.rotation, out var pd, out var pdist))
                                    pen = $" push {pd.x:0.00},{pd.y:0.00},{pd.z:0.00} {pdist:0.00} m, side {k}, W {path.section.Width:0.00} H {path.section.Height:0.00}";
                                string key = (owner != null ? owner.name + "/" : "") + col.name + " (layer " + col.gameObject.layer + ")" + (hits.ContainsKey((owner != null ? owner.name + "/" : "") + col.name + " (layer " + col.gameObject.layer + ")") ? "" : "");
                                if (!firstPen.ContainsKey(key)) firstPen[key] = pen;
                                hits.TryGetValue(key, out var v);
                                hits[key] = (v.n + 1, v.n == 0 ? c : v.at);
                                any = true;
                            }
                            if (any) blocked++;
                        }
            }
            ClearAll();
            var sb = new System.Text.StringBuilder();
            sb.Append($"capsule r {radius:0.00} h {height:0.00}; samples {samples}, blocked {blocked}, no shell floor {noFloor}");
            if (noFloor > 0) sb.Append($" (first at {firstNoFloor.x:0.0},{firstNoFloor.y:0.0},{firstNoFloor.z:0.0})");
            var list = new List<KeyValuePair<string, (int n, Vector3 at)>>(hits);
            list.Sort((a, b) => b.Value.n.CompareTo(a.Value.n));
            for (int i = 0; i < list.Count && i < 25; i++)
                sb.Append($"\n  {list[i].Value.n} x {list[i].Key} e.g. at {list[i].Value.at.x:0.0},{list[i].Value.at.y:0.0},{list[i].Value.at.z:0.0}{firstPen[list[i].Key]}");
            return sb.ToString();
        }

        /// <summary>
        /// Edit mode: the player's capsule on a 0.2 m grid over every path, junction and room. Lists spots where the
        /// collision shell blocks it but the visible tunnel mesh would not ("phantom" walls), clustered by metre.
        /// </summary>
        public static string PhantomTest()
        {
            if (Application.isPlaying) return "run in edit mode";
            BuildPass(false);
            BuildPass(true);
            Physics.SyncTransforms();
            float radius = 0.5f, height = 2f;
            foreach (var go in GameObject.FindGameObjectsWithTag("Player"))
                if (go.TryGetComponent(out CharacterController cc)) { radius = cc.radius * go.transform.lossyScale.x; height = cc.height * go.transform.lossyScale.y; break; }
            var probe = s_Probe != null ? s_Probe : (s_Probe = MakeProbe());
            probe.radius = radius; probe.height = height;
            int visMask = ~((1 << Layer) | (1 << CollisionLayer) | (1 << 2));
            var cols = new Collider[16];
            var pts = new List<(string owner, Vector3 p)>();
            foreach (var path in Object.FindObjectsByType<TunnelPath>(FindObjectsInactive.Exclude))
            {
                var t = path.transform;
                float half = path.section.Width * 0.5f;
                for (int si = 0; si < path.slabs.Count; si++)
                {
                    var sl = path.slabs[si];
                    for (float d = 0f; d <= sl.length; d += 0.2f)
                        for (float x = -half + 0.3f; x <= half - 0.3f; x += 0.2f)   // a capsule centred on a one-sided wall reads as free
                            pts.Add((path.name + $" slab {si}/{path.slabs.Count} d {d:0.0}/{sl.length:0.0} x {x:0.0}", t.TransformPoint(sl.Point(d, x, 0f))));
                }
            }
            foreach (var j in Object.FindObjectsByType<TunnelJunction>(FindObjectsInactive.Exclude))
                for (float x = -j.sizeX * 0.5f - 0.6f; x <= j.sizeX * 0.5f + 0.6f; x += 0.2f)
                    for (float z = -j.sizeZ * 0.5f - 0.6f; z <= j.sizeZ * 0.5f + 0.6f; z += 0.2f)
                        pts.Add((j.name, j.transform.TransformPoint(new Vector3(x, 0f, z))));
            foreach (var r in Object.FindObjectsByType<TunnelRoom>(FindObjectsInactive.Exclude))
            {
                var b = new Bounds(new Vector3(r.corners[0].x, 0f, r.corners[0].y), Vector3.zero);
                foreach (var c in r.corners) b.Encapsulate(new Vector3(c.x, 0f, c.y));
                for (float x = b.min.x - 0.6f; x <= b.max.x + 0.6f; x += 0.2f)
                    for (float z = b.min.z - 0.6f; z <= b.max.z + 0.6f; z += 0.2f)
                        pts.Add((r.name, r.transform.TransformPoint(new Vector3(x, 0f, z))));
            }
            int tested = 0, phantom = 0;
            var cluster = new Dictionary<Vector3Int, (int n, string owner, string by, Vector3 push)>();
            foreach (var (owner, p) in pts)
            {
                // floor of the visible mesh under the point
                if (!Physics.Raycast(p + Vector3.up * 1.0f, Vector3.down, out var fh, 2f, visMask, QueryTriggerInteraction.Ignore)) continue;
                // on a bench bevel: the straight collision walls keep the player off it anyway
                if (fh.point.y - p.y > 0.1f) continue;
                Vector3 mid = fh.point + Vector3.up * (height * 0.5f + 0.06f);
                Vector3 p0 = mid - Vector3.up * (height * 0.5f - radius), p1 = mid + Vector3.up * (height * 0.5f - radius);
                // only spots where the visible tunnel mesh leaves room for the player
                bool visBlocked = false;
                int n = Physics.OverlapCapsuleNonAlloc(p0, p1, radius, cols, visMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    if (cols[i] is CharacterController) continue;
                    if (Physics.ComputePenetration(probe, mid, Quaternion.identity, cols[i], cols[i].transform.position, cols[i].transform.rotation, out var vd, out var vdist) && vdist > 0.005f) { visBlocked = true; break; }
                }
                if (visBlocked) continue;
                tested++;
                n = Physics.OverlapCapsuleNonAlloc(p0, p1, radius, cols, 1 << CollisionLayer, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    if (!Physics.ComputePenetration(probe, mid, Quaternion.identity, cols[i], cols[i].transform.position, cols[i].transform.rotation, out var sd, out var sdist) || sdist < 0.02f) continue;
                    phantom++;
                    var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(fh.point.y), Mathf.RoundToInt(p.z));
                    cluster.TryGetValue(key, out var v);
                    cluster[key] = (v.n + 1, owner + $" ({p.x:0.0},{p.z:0.0})", cols[i].transform.parent != null ? cols[i].transform.parent.name : cols[i].name, sd * sdist);
                    break;
                }
            }
            ClearAll();
            var list = new List<KeyValuePair<Vector3Int, (int n, string owner, string by, Vector3 push)>>(cluster);
            list.Sort((a, b) => b.Value.n.CompareTo(a.Value.n));
            var sb = new System.Text.StringBuilder();
            sb.Append($"capsule r {radius:0.00} h {height:0.00}; free spots {tested}, phantom blocked {phantom}, clusters {list.Count}");
            for (int i = 0; i < list.Count && i < 20; i++)
            {
                var v = list[i].Value;
                sb.Append($"\n  {v.n} at {list[i].Key} in {v.owner} by shell of {v.by} push {v.push.x:0.00},{v.push.y:0.00},{v.push.z:0.00}");
            }
            return sb.ToString();
        }

        /// <summary>Edit mode: from (x, z) casts horizontal rays every 30 degrees at several heights at the collision shell
        /// and at the visible mesh, to see what part of the shell stands where.</summary>
        /// <summary>Edit mode: a CharacterController like the player's walks every path along three lanes, both ways,
        /// in the collision shells only, and reports where it gets stuck.</summary>
        public static string WalkTest()
        {
            if (Application.isPlaying) return "run in edit mode";
            BuildPass(true);
            CharacterController src = null;
            foreach (var go in GameObject.FindGameObjectsWithTag("Player")) if (go.TryGetComponent(out src)) break;
            var walker = new GameObject("_WalkTest") { hideFlags = HideFlags.DontSave };
            var cc = walker.AddComponent<CharacterController>();
            float radius = 0.5f, height = 2f;
            if (src != null)
            {
                walker.layer = src.gameObject.layer;
                radius = src.radius * src.transform.lossyScale.x; height = src.height * src.transform.lossyScale.y;
                cc.stepOffset = src.stepOffset; cc.slopeLimit = src.slopeLimit; cc.skinWidth = src.skinWidth; cc.minMoveDistance = 0f;
            }
            cc.radius = radius; cc.height = height; cc.center = Vector3.up * (height * 0.5f);
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(CollisionLayer, i, i != walker.layer);
            foreach (var p in Object.FindObjectsByType<TunnelPath>(FindObjectsInactive.Exclude)) Ignore(cc, p.transform, WallCollider(p.transform));
            foreach (var j in Object.FindObjectsByType<TunnelJunction>(FindObjectsInactive.Exclude)) Ignore(cc, j.transform, WallCollider(j.transform));
            foreach (var r in Object.FindObjectsByType<TunnelRoom>(FindObjectsInactive.Exclude)) Ignore(cc, r.transform, WallCollider(r.transform));
            foreach (var c in Object.FindObjectsByType<TunnelColumn>(FindObjectsInactive.Exclude)) Ignore(cc, c.transform, c.GetComponent<MeshCollider>());
            int walks = 0, stuck = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var path in Object.FindObjectsByType<TunnelPath>(FindObjectsInactive.Exclude))
            {
                if (!HasCollision(path.transform) || path.slabs.Count == 0) continue;
                var t = path.transform;
                float lane = Mathf.Max(0f, path.section.Width * 0.5f - radius - 0.08f);
                for (int k = -1; k <= 1; k++)
                    for (int dirSign = -1; dirSign <= 1; dirSign += 2)
                    {
                        var pts = new List<Vector3>();
                        foreach (var sl in path.slabs)
                            for (float d = 0f; d < sl.length; d += 0.2f) pts.Add(t.TransformPoint(sl.Point(d, k * lane, 0f)));
                        var last = path.slabs[path.slabs.Count - 1];
                        pts.Add(t.TransformPoint(last.Point(last.length, k * lane, 0f)));
                        if (dirSign < 0) pts.Reverse();
                        if (pts.Count < 6) continue;
                        walks++;
                        cc.enabled = false; walker.transform.position = pts[1] + Vector3.up * 0.1f; cc.enabled = true;
                        Physics.SyncTransforms();
                        int target = 3, idle = 0;
                        for (int step = 0; step < pts.Count * 6 && target < pts.Count - 2; step++)
                        {
                            Vector3 pos = walker.transform.position;
                            while (target < pts.Count - 2 && Vector3.ProjectOnPlane(pts[target] - pos, Vector3.up).magnitude < 0.4f) target++;
                            Vector3 want = Vector3.ProjectOnPlane(pts[target] - pos, Vector3.up).normalized * 0.08f;
                            cc.Move(want + Vector3.down * 0.06f);
                            float got = Vector3.ProjectOnPlane(walker.transform.position - pos, Vector3.up).magnitude;
                            idle = got < 0.02f ? idle + 1 : 0;
                            if (idle >= 6)
                            {
                                stuck++;
                                if (stuck <= 25)
                                {
                                    var hit = Physics.OverlapSphere(walker.transform.position + Vector3.up * 1f, radius + 0.1f, 1 << CollisionLayer);
                                    sb.Append($"\n  {path.name} lane {k} dir {dirSign} stuck at {pos.x:0.0},{pos.y:0.00},{pos.z:0.0} heading {want.normalized.x:0.00},{want.normalized.z:0.00} near {(hit.Length > 0 ? hit[0].transform.parent?.name : "-")}");
                                }
                                break;
                            }
                        }
                    }
            }
            Object.DestroyImmediate(walker);
            ClearAll();
            return $"walks {walks}, stuck {stuck}" + sb;
        }

        /// <summary>Edit mode: collision shell triangles that pass within 0.6 m of (x, 1 m, z).</summary>
        public static string TrisNear(string xs, string zs)
        {
            if (Application.isPlaying) return "run in edit mode";
            float x = float.Parse(xs, System.Globalization.CultureInfo.InvariantCulture), z = float.Parse(zs, System.Globalization.CultureInfo.InvariantCulture);
            BuildPass(true);
            var sb = new System.Text.StringBuilder();
            Vector3 c = new Vector3(x, 1f, z);
            Physics.SyncTransforms();
            foreach (var col in Physics.OverlapSphere(c, 1.5f, 1 << CollisionLayer))
            {
                if (col is not MeshCollider mc || mc.sharedMesh == null) continue;
                var v = mc.sharedMesh.vertices; var tr = mc.sharedMesh.triangles; var m = mc.transform.localToWorldMatrix;
                for (int i = 0; i + 2 < tr.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(v[tr[i]]), b = m.MultiplyPoint3x4(v[tr[i + 1]]), d = m.MultiplyPoint3x4(v[tr[i + 2]]);
                    var pl = new Plane(a, b, d);
                    if (Mathf.Abs(pl.GetDistanceToPoint(c)) > 0.6f) continue;
                    var b0 = new Bounds(a, Vector3.zero); b0.Encapsulate(b); b0.Encapsulate(d); b0.Expand(1.2f);
                    if (!b0.Contains(c)) continue;
                    sb.Append($"\n  {mc.transform.parent?.name} tri {i / 3}: {a.x:0.00},{a.y:0.00},{a.z:0.00} | {b.x:0.00},{b.y:0.00},{b.z:0.00} | {d.x:0.00},{d.y:0.00},{d.z:0.00} n {pl.normal.x:0.00},{pl.normal.y:0.00},{pl.normal.z:0.00}");
                }
            }
            int shells = 0; foreach (var mc in Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Exclude)) if (IsCollisionShell(mc)) shells++;
            var hits = Physics.OverlapSphere(c, 0.6f, 1 << CollisionLayer);
            sb.Insert(0, $"shells {shells}, overlap {hits.Length}" + (hits.Length > 0 ? $" ({hits[0].transform.parent?.name}/{hits[0].name} L{hits[0].gameObject.layer})" : ""));
            ClearAll();
            return sb.ToString();
        }

        public static string ProbeAt(string xs, string zs)
        {
            if (Application.isPlaying) return "run in edit mode";
            float x = float.Parse(xs, System.Globalization.CultureInfo.InvariantCulture), z = float.Parse(zs, System.Globalization.CultureInfo.InvariantCulture);
            BuildPass(true);
            Physics.SyncTransforms();
            int visMask = ~((1 << Layer) | (1 << CollisionLayer) | (1 << 2));
            var sb = new System.Text.StringBuilder();
            if (!Physics.Raycast(new Vector3(x, 50f, z), Vector3.down, out var fh, 100f, visMask, QueryTriggerInteraction.Ignore)) { ClearAll(); return "no floor"; }
            sb.Append($"floor {fh.point.y:0.00} ({fh.collider.transform.parent?.name})");
            if (Physics.Raycast(new Vector3(x, fh.point.y + 1.0f, z), Vector3.down, out var cf, 2f, 1 << CollisionLayer)) sb.Append($" shell floor {cf.point.y:0.00}");
            if (Physics.Raycast(new Vector3(x, fh.point.y + 0.3f, z), Vector3.up, out var cc, 4f, 1 << CollisionLayer)) sb.Append($" shell ceiling {cc.point.y:0.00}");
            if (Physics.Raycast(new Vector3(x, fh.point.y + 0.3f, z), Vector3.up, out var vc, 4f, visMask)) sb.Append($" visible ceiling {vc.point.y:0.00}");
            foreach (float h in new[] { 0.1f, 0.3f, 0.6f, 1.0f, 1.5f, 1.9f })
            {
                sb.Append($"\n  h {h:0.0}:");
                for (int a = 0; a < 360; a += 30)
                {
                    Vector3 o = new Vector3(x, fh.point.y + h, z), d = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                    float sd = Physics.Raycast(o, d, out var sh, 3f, 1 << CollisionLayer) ? sh.distance : 9f;
                    float vd = Physics.Raycast(o, d, out var vh, 3f, visMask) ? vh.distance : 9f;
                    sb.Append($" {a}:{sd:0.00}/{vd:0.00}");
                }
            }
            ClearAll();
            return sb.ToString();
        }

        static void Kill(GameObject go)
        {
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }

        // ------------------------------------------------------------------ owners

        public static bool Build(TunnelPath path)
        {
            var wall = WallCollider(path.transform);
            if (wall == null || path.slabs.Count == 0) return false;
            Begin();
            var t = path.transform;
            for (int side = -1; side <= 1; side += 2)
            {
                var line = new List<Probe>();
                var slabs = path.slabs;
                for (int si = 0; si < slabs.Count; si++)
                {
                    var s = slabs[si];
                    int n = Mathf.Max(1, Mathf.CeilToInt(s.length / Step));
                    // Turns: square probes of two slabs meet across the corner and cut it off (outer side) or cross
                    // the mitre and read the other slab's wall (inner side). Corner() lays the probes round the joint.
                    float cutStart = si > 0 ? InnerCut(path, slabs[si - 1], s, side) : 0f;
                    float cutEnd = si + 1 < slabs.Count ? InnerCut(path, s, slabs[si + 1], side) : 0f;
                    if (si > 0) Corner(path, wall, slabs[si - 1], s, side, line);
                    for (int i = 0; i <= n; i++)
                    {
                        if (si > 0 && i == 0) continue;   // the joint itself is probed by Corner or by the previous slab
                        float d = s.length * i / n;
                        if (d < cutStart - 1e-4f || d > s.length - cutEnd + 1e-4f) continue;
                        Vector3 floor = t.TransformPoint(s.Point(d, 0f, 0f));
                        Vector3 dir = t.TransformDirection(s.right * side).normalized;
                        // straight collision walls: the main wall plane is about half the width away; a ray that finds
                        // nothing there looks through a side opening (or hits its jamb) and leaves the opening free
                        // a round pipe is widest at mid height (HalfSpan), wider than its silt floor (Width): the ray must reach that far
                        float half = path.section.Width * 0.5f, span = Mathf.Max(half, path.section.HalfSpan);
                        bool straight = s_Collision && StraightWalls;
                        line.Add(new Probe { floor = floor, dir = dir, start = 0f, maxDistance = straight ? span + 0.35f : 4f, minDistance = straight ? half * 0.8f : 0f });
                    }
                }
                Strip(wall, line, false);
            }
            if (s_Collision) PathFloorAndCeiling(path, wall);
            PathEndWalls(path);
            return Finish(path.transform, wall);
        }

        const float CornerSweepStep = 5f;   // degrees between rays swept round the outer side of a turn

        /// <summary>
        /// At a joint that turns, rays from the joint swept from the old slab's side to the new one's follow both walls
        /// into the corner. Only on the outer side: on the inner side the old square probe already meets the inner corner.
        /// In collision mode the floor of the swept wedge is added too, or the player could step off the floor strips.
        /// </summary>
        static float Turn(TunnelPath path, TunnelPath.Slab a, TunnelPath.Slab b)
        {
            var t = path.transform;
            Vector3 fa = t.TransformDirection(a.fwd), fb = t.TransformDirection(b.fwd);
            return Vector3.SignedAngle(Vector3.ProjectOnPlane(fa, Vector3.up), Vector3.ProjectOnPlane(fb, Vector3.up), Vector3.up);
        }

        /// <summary>Inner side of a turn: how far from the joint square probes would cross the mitre into the other slab.</summary>
        static float InnerCut(TunnelPath path, TunnelPath.Slab a, TunnelPath.Slab b, int side)
        {
            float turn = Turn(path, a, b);
            if (Mathf.Abs(turn) < 3f || side * turn < 0f) return 0f;
            return path.section.Width * 0.5f * Mathf.Tan(Mathf.Abs(turn) * 0.5f * Mathf.Deg2Rad) + 0.05f;
        }

        static void Corner(TunnelPath path, MeshCollider wall, TunnelPath.Slab a, TunnelPath.Slab b, int side, List<Probe> line)
        {
            var t = path.transform;
            float turn = Turn(path, a, b);
            Vector3 da = t.TransformDirection(a.right * side).normalized, db = t.TransformDirection(b.right * side).normalized;
            Vector3 joint = t.TransformPoint(a.Point(a.length, 0f, 0f));
            if (Mathf.Abs(turn) < 3f) return;
            if (side * turn > 0f)
            {
                // inner side: one probe along the mitre meets the inner corner
                line.Add(new Probe { floor = joint, dir = (da + db).normalized, start = 0f, maxDistance = 4f, keep = true, minDistance = s_Collision && StraightWalls ? path.section.Width * 0.4f : 0f });
                return;
            }
            int m = Mathf.Max(1, Mathf.CeilToInt(Vector3.Angle(da, db) / CornerSweepStep));
            for (int k = 1; k <= m; k++)
                line.Add(new Probe { floor = joint, dir = Vector3.Slerp(da, db, k / (float)m).normalized, start = 0f, maxDistance = 4f, keep = true, minDistance = s_Collision && StraightWalls ? path.section.Width * 0.4f : 0f });
            if (!s_Collision) return;
            // floor wedge: a fan from the joint to the foot of the wall along each swept ray
            float floorY = joint.y;
            if (wall.Raycast(new Ray(joint + Vector3.up * 1.2f, Vector3.down), out var down, 3f)) floorY = down.point.y;
            Vector3 centre = new Vector3(joint.x, floorY, joint.z);
            Vector3 last = default;
            bool hasLast = false;
            for (int k = 0; k <= m; k++)
            {
                Vector3 dir = Vector3.Slerp(da, db, k / (float)m).normalized;
                if (!wall.Raycast(new Ray(centre + Vector3.up * (StraightWalls ? WallProbeHeight : 0.3f), dir), out var wh, 4f)) { hasLast = false; continue; }
                Vector3 foot = centre + dir * wh.distance;
                // straight walls: the fan stays flat (dropping onto the bench bevel made a ramp that lifted and held the player)
                if (!StraightWalls && wall.Raycast(new Ray(foot - dir * 0.05f + Vector3.up * 1.2f, Vector3.down), out var fd, 3f)) foot.y = fd.point.y;
                if (StraightWalls) foot = centre + dir * (wh.distance + 0.3f);
                if (hasLast) Tri(centre, last, foot, Vector3.up);
                last = foot;
                hasLast = true;
            }
        }

        /// <summary>Dead-end walls (and end walls with an opening) copied from the visible mesh:
        /// the player ignores the visible mesh, so without them he walks out through a dead end.</summary>
        static void PathEndWalls(TunnelPath path)
        {
            var tris = new List<Vector3>();
            path.EndWallTriangles(tris);
            for (int i = 0; i + 2 < tris.Count; i += 3)
                Tri(tris[i], tris[i + 1], tris[i + 2], Vector3.Cross(tris[i + 1] - tris[i], tris[i + 2] - tris[i]));
        }

        /// <summary>Floor and ceiling of a path as strips across the tunnel, probed straight down and up.</summary>
        static void PathFloorAndCeiling(TunnelPath path, MeshCollider wall)
        {
            var t = path.transform;
            float half = Mathf.Max(0.3f, path.section.Width * 0.5f - 0.04f);
            const int across = 7;
            var samples = new List<(Vector3 centre, Vector3 right)>();
            foreach (var s in path.slabs)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(s.length / Step));
                for (int i = 0; i <= n; i++)
                {
                    if (samples.Count > 0 && i == 0) continue;
                    samples.Add((t.TransformPoint(s.Point(s.length * i / n, 0f, 0f)), t.TransformDirection(s.right).normalized));
                }
            }
            int m = samples.Count;
            if (m < 2) return;
            var floor = new Vector3[m, across];
            var ceil = new Vector3[m, across];
            var okF = new bool[m, across];
            var okC = new bool[m, across];
            for (int i = 0; i < m; i++)
                for (int k = 0; k < across; k++)
                {
                    float x = Mathf.Lerp(-half, half, k / (float)(across - 1));
                    Vector3 o = samples[i].centre + samples[i].right * x + Vector3.up * 1.0f;
                    if (wall.Raycast(new Ray(o, Vector3.down), out var d, 2.5f)) { floor[i, k] = d.point; okF[i, k] = true; }
                    if (wall.Raycast(new Ray(o, Vector3.up), out var u, 6f)) { ceil[i, k] = u.point; okC[i, k] = true; }
                }
            if (StraightWalls)
            {
                // Straight walls stand on the main wall plane, so the floor runs flat across to them at the centre's
                // height (the bevels would lift it at the sides), and a little wider than the tunnel to meet the walls.
                int mid = across / 2;
                float wide = (half + 0.3f) / half;
                for (int i = 0; i < m; i++)
                {
                    if (!okF[i, mid]) { for (int k = 0; k < across; k++) okF[i, k] = false; continue; }
                    for (int k = 0; k < across; k++)
                    {
                        float x = Mathf.Lerp(-half, half, k / (float)(across - 1)) * wide;
                        Vector3 q = samples[i].centre + samples[i].right * x;
                        floor[i, k] = new Vector3(q.x, floor[i, mid].y, q.z);
                        okF[i, k] = true;
                    }
                }
                // and the ceiling flat at the centre's height: the top chamfer would lower it near the walls
                for (int i = 0; i < m; i++)
                {
                    if (!okC[i, mid]) { for (int k = 0; k < across; k++) okC[i, k] = false; continue; }
                    for (int k = 0; k < across; k++)
                    {
                        float x = Mathf.Lerp(-half, half, k / (float)(across - 1)) * wide;
                        Vector3 q = samples[i].centre + samples[i].right * x;
                        ceil[i, k] = new Vector3(q.x, ceil[i, mid].y, q.z);
                        okC[i, k] = true;
                    }
                }
            }
            for (int i = 0; i + 1 < m; i++)
                for (int k = 0; k + 1 < across; k++)
                {
                    if (okF[i, k] && okF[i + 1, k] && okF[i + 1, k + 1] && okF[i, k + 1] && Flat(floor[i, k], floor[i + 1, k], floor[i + 1, k + 1], floor[i, k + 1]))
                        Quad(floor[i, k], floor[i + 1, k], floor[i + 1, k + 1], floor[i, k + 1], Vector3.up);
                    if (okC[i, k] && okC[i + 1, k] && okC[i + 1, k + 1] && okC[i, k + 1] && Flat(ceil[i, k], ceil[i + 1, k], ceil[i + 1, k + 1], ceil[i, k + 1]))
                        Quad(ceil[i, k], ceil[i + 1, k], ceil[i + 1, k + 1], ceil[i, k + 1], Vector3.down);
                }
        }

        /// <summary>Four probe points on one surface: no height jump between them (a shaft opening, a curb edge).</summary>
        static bool Flat(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float lo = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y));
            float hi = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));
            return hi - lo < 0.35f;
        }

        /// <summary>Flat floor at local height 0 and flat ceiling at local height <paramref name="height"/> over a polygon.</summary>
        static void PolygonFloorAndCeiling(Transform t, List<Vector2> corners, float height)
        {
            var poly = new List<Vector2>(corners);
            if (TunnelRoom.Area(poly) < 0f) poly.Reverse();
            var tris = TunnelRoom.EarClip(poly);
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                Vector2 a = tris[i], b = tris[i + 1], c = tris[i + 2];
                Tri(t.TransformPoint(new Vector3(a.x, 0f, a.y)), t.TransformPoint(new Vector3(b.x, 0f, b.y)), t.TransformPoint(new Vector3(c.x, 0f, c.y)), t.up);
                Tri(t.TransformPoint(new Vector3(a.x, height, a.y)), t.TransformPoint(new Vector3(b.x, height, b.y)), t.TransformPoint(new Vector3(c.x, height, c.y)), -t.up);
            }
        }

        public static bool Build(TunnelJunction junction)
        {
            var wall = WallCollider(junction.transform);
            if (wall == null) return false;
            float hx = junction.sizeX * 0.5f, hz = junction.sizeZ * 0.5f;
            var corners = new List<Vector2> { new(-hx, -hz), new(hx, -hz), new(hx, hz), new(-hx, hz) };
            Begin();
            Polygon(wall, junction.transform, corners);
            if (s_Collision) PolygonFloorAndCeiling(junction.transform, corners, junction.height);
            return Finish(junction.transform, wall);
        }

        public static bool Build(TunnelRoom room)
        {
            var wall = WallCollider(room.transform);
            if (wall == null || room.corners.Count < 3) return false;
            Begin();
            Polygon(wall, room.transform, room.corners);
            if (room.columns != null)
                foreach (var c in room.columns)
                    if (c != null) Column(wall, room.transform, c.position, c.size, c.shape == ColumnShape.Square);
            if (s_Collision) PolygonFloorAndCeiling(room.transform, room.corners, room.height);
            return Finish(room.transform, wall);
        }

        public static bool Build(TunnelColumn column)
        {
            var wall = column.GetComponent<MeshCollider>();
            if (wall == null || wall.sharedMesh == null) return false;
            Begin();
            Column(wall, column.transform, Vector2.zero, column.size, column.shape == ColumnShape.Square);
            return Finish(column.transform, wall);
        }

        // ------------------------------------------------------------------ probing

        struct Probe
        {
            public Vector3 floor;      // a point on the floor the ray starts above
            public Vector3 dir;        // horizontal direction toward the wall
            public float start;        // the ray starts this far along dir from above the floor point
            public float maxDistance;
            public bool keep;          // a swept corner probe: not laid back by the seam smoothing (a corner is not a seam)
            public float minDistance;  // straight collision walls: a nearer hit is an opening's jamb, not the wall
        }

        static MeshCollider WallCollider(Transform owner)
        {
            var gen = owner.Find(TunnelPath.GeneratedName);
            var mc = gen != null ? gen.GetComponent<MeshCollider>() : null;
            return mc != null && mc.sharedMesh != null ? mc : null;
        }

        static void Polygon(MeshCollider wall, Transform t, List<Vector2> corners)
        {
            float area = 0f;
            for (int i = 0; i < corners.Count; i++)
            {
                Vector2 a = corners[i], b = corners[(i + 1) % corners.Count];
                area += a.x * b.y - b.x * a.y;
            }
            float inwardSign = area > 0f ? 1f : -1f;   // CCW (x, z): inside is to the left of each edge
            for (int i = 0; i < corners.Count; i++)
            {
                Vector2 a = corners[i], b = corners[(i + 1) % corners.Count];
                Vector2 e = b - a;
                float len = e.magnitude;
                if (len < 0.05f) continue;
                Vector2 inward2 = new Vector2(-e.y, e.x) / len * inwardSign;
                Vector3 outward = t.TransformDirection(new Vector3(-inward2.x, 0f, -inward2.y)).normalized;
                int n = Mathf.Max(1, Mathf.CeilToInt(len / Step));
                var line = new List<Probe>(n + 1);
                for (int k = 0; k <= n; k++)
                {
                    // Stay a little off the corners, where the neighbouring wall would answer the ray.
                    float u = Mathf.Lerp(0.02f, len - 0.02f, (float)k / n);
                    Vector2 p = a + e / len * u + inward2 * Inset;
                    line.Add(new Probe { floor = t.TransformPoint(new Vector3(p.x, 0f, p.y)), dir = outward, start = 0f, maxDistance = Inset * 2f });
                }
                Strip(wall, line, false);
            }
        }

        static void Column(MeshCollider wall, Transform t, Vector2 centre, float size, bool square)
        {
            float radius = size * (square ? 0.7072f : 0.5f);
            const int count = 28;
            var ring = new List<Probe>(count);
            for (int k = 0; k < count; k++)
            {
                float a = k * Mathf.PI * 2f / count;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 p = centre + d * (radius + Inset);
                ring.Add(new Probe { floor = t.TransformPoint(new Vector3(p.x, 0f, p.y)), dir = t.TransformDirection(new Vector3(-d.x, 0f, -d.y)).normalized, start = 0f, maxDistance = radius + Inset });
            }
            Strip(wall, ring, true);
        }

        /// <summary>Probes one wall line at all band rows and adds the quads whose four corners hit the same surface.</summary>
        static void Strip(MeshCollider wall, List<Probe> line, bool closed)
        {
            int n = line.Count;
            if (n < 2) return;
            bool straight = s_Collision && StraightWalls;
            int rows = straight ? 2 : s_Collision ? 7 : Rows;
            var hit = new Vector3[n, rows];
            var depth = new float[n, rows];
            var ok = new bool[n, rows];
            for (int i = 0; i < n; i++)
            {
                var pr = line[i];
                if (!Band(wall, pr.floor, out float floorY, out float low, out float high)) continue;
                if (straight)
                {
                    // One ray at the main wall plane (between the bottom bevel and the top chamfer), then a vertical wall
                    // from under the floor to over the ceiling: the bevels are inside the shell and do not hold the player.
                    float mid = Mathf.Clamp(WallProbeHeight, low + 0.3f, Mathf.Max(low + 0.3f, high - 0.3f));
                    Vector3 o = new Vector3(pr.floor.x, floorY + mid, pr.floor.z) + pr.dir * pr.start;
                    if (!wall.Raycast(new Ray(o, pr.dir), out var mh, pr.maxDistance) || mh.distance + pr.start < pr.minDistance) continue;
                    Vector3 p = mh.point;
                    hit[i, 0] = new Vector3(p.x, floorY - 0.3f, p.z);
                    hit[i, 1] = new Vector3(p.x, floorY + high + 0.3f, p.z);
                    depth[i, 0] = depth[i, 1] = mh.distance;
                    ok[i, 0] = ok[i, 1] = true;
                    continue;
                }
                for (int r = 0; r < rows; r++)
                {
                    float h = Mathf.Lerp(low, high, r / (float)(rows - 1));
                    Vector3 origin = new Vector3(pr.floor.x, floorY + h, pr.floor.z) + pr.dir * pr.start;
                    if (wall.Raycast(new Ray(origin, pr.dir), out var rh, pr.maxDistance))
                    {
                        hit[i, r] = rh.point;
                        depth[i, r] = rh.distance;
                        ok[i, r] = true;
                    }
                }
            }

            // A probe that fell into a seam reads a little deeper than both neighbours: lay it back onto the wall.
            for (int r = 0; r < rows; r++)
                for (int i = 1; i + 1 < n; i++)
                {
                    if (!ok[i, r] || !ok[i - 1, r] || !ok[i + 1, r] || line[i].keep) continue;
                    float d0 = depth[i - 1, r], d1 = depth[i, r], d2 = depth[i + 1, r];
                    if (Mathf.Abs(d0 - d2) < 0.03f && d1 - Mathf.Max(d0, d2) > 0.002f && d1 - Mathf.Max(d0, d2) < MaxJump)
                    {
                        hit[i, r] -= line[i].dir * (d1 - (d0 + d2) * 0.5f);
                        depth[i, r] = (d0 + d2) * 0.5f;
                    }
                }

            int segments = closed ? n : n - 1;
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % n;
                for (int r = 0; r + 1 < rows; r++)
                {
                    if (!ok[i, r] || !ok[j, r] || !ok[i, r + 1] || !ok[j, r + 1]) continue;
                    // corner probes fan out from one joint, so their depths differ more than along a straight wall
                    float jump = line[i].keep || line[j].keep ? Mathf.Max(MaxJump, 0.9f) : MaxJump;
                    if (Mathf.Abs(depth[i, r] - depth[j, r]) > jump || Mathf.Abs(depth[i, r + 1] - depth[j, r + 1]) > jump) continue;
                    if (Mathf.Abs(depth[i, r] - depth[i, r + 1]) > MaxJump * 2f || Mathf.Abs(depth[j, r] - depth[j, r + 1]) > MaxJump * 2f) continue;
                    Quad(hit[i, r], hit[j, r], hit[j, r + 1], hit[i, r + 1], -(line[i].dir + line[j].dir));
                }
            }
        }

        /// <summary>Floor height under a point and the band rows that fit under the ceiling there.</summary>
        static bool Band(MeshCollider wall, Vector3 at, out float floorY, out float low, out float high)
        {
            floorY = at.y;
            if (wall.Raycast(new Ray(at + Vector3.up * 1.2f, Vector3.down), out var down, 3f)) floorY = down.point.y;
            float ceiling = 4f;
            if (wall.Raycast(new Ray(new Vector3(at.x, floorY + 0.3f, at.z), Vector3.up), out var up, 6f)) ceiling = up.point.y - floorY;
            if (s_Collision)
            {
                // Walls from just above the floor to just under the ceiling; curbs and the round floor edge are walls too.
                low = 0.05f;
                high = Mathf.Max(low + 0.3f, ceiling - 0.05f);
                return true;
            }
            low = BandLow;
            high = BandHigh;
            high = Mathf.Min(high, ceiling - 0.12f);
            low = Mathf.Min(low, high - 0.4f);
            return low > 0.15f;
        }

        // ------------------------------------------------------------------ mesh

        static void Begin()
        {
            s_Verts.Clear();
            s_Tris.Clear();
        }

        /// <summary>Two triangles facing <paramref name="facing"/> (into the tunnel), so raycasts from inside hit their front.</summary>
        static void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing)
        {
            int i = s_Verts.Count;
            s_Verts.Add(a); s_Verts.Add(b); s_Verts.Add(c);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) >= 0f) { s_Tris.Add(i); s_Tris.Add(i + 1); s_Tris.Add(i + 2); }
            else { s_Tris.Add(i); s_Tris.Add(i + 2); s_Tris.Add(i + 1); }
        }

        static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            int i = s_Verts.Count;
            s_Verts.Add(a); s_Verts.Add(b); s_Verts.Add(c); s_Verts.Add(d);
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f;
            if (!flip) { s_Tris.Add(i); s_Tris.Add(i + 1); s_Tris.Add(i + 2); s_Tris.Add(i); s_Tris.Add(i + 2); s_Tris.Add(i + 3); }
            else { s_Tris.Add(i); s_Tris.Add(i + 2); s_Tris.Add(i + 1); s_Tris.Add(i); s_Tris.Add(i + 3); s_Tris.Add(i + 2); }
        }

        static bool Finish(Transform owner, Collider wall)
        {
            string name = s_Collision ? CollisionName : ShellName;
            var old = owner.Find(name);
            if (old != null) Kill(old.gameObject);
            if (s_Tris.Count == 0) return false;

            // World-space build, stored under the owner in its local space.
            var go = new GameObject(name) { layer = s_Collision ? CollisionLayer : Layer, hideFlags = HideFlags.DontSave };
            go.transform.SetParent(owner, false);
            var local = new Vector3[s_Verts.Count];
            for (int i = 0; i < local.Length; i++) local[i] = go.transform.InverseTransformPoint(s_Verts[i]);
            var mesh = new Mesh { name = owner.name + "_TouchShell", hideFlags = HideFlags.DontSave };
            if (local.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = local;
            mesh.triangles = s_Tris.ToArray();
            mesh.RecalculateBounds();
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return true;
        }
    }
}
