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
        public static bool BuildCollision = true;

        /// <summary>Band above the floor, m. Lowered under low ceilings.</summary>
        public static float BandLow = 0.7f;
        public static float BandHigh = 1.9f;
        const int Rows = 4;
        static bool s_Collision;
        const float Step = 0.25f;           // spacing of probes along a wall, m
        const float MaxJump = 0.2f;         // depth change between neighbours that breaks the band (an opening, a column), m
        const float Inset = 0.6f;           // rooms and junctions: probes start this far inside the wall, m

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
                foreach (var s in path.slabs)
                {
                    int n = Mathf.Max(1, Mathf.CeilToInt(s.length / Step));
                    for (int i = 0; i <= n; i++)
                    {
                        if (line.Count > 0 && i == 0) continue;   // the next slab starts where this one ended
                        Vector3 floor = t.TransformPoint(s.Point(s.length * i / n, 0f, 0f));
                        Vector3 dir = t.TransformDirection(s.right * side).normalized;
                        line.Add(new Probe { floor = floor, dir = dir, start = 0f, maxDistance = 4f });
                    }
                }
                Strip(wall, line, false);
            }
            if (s_Collision) PathFloorAndCeiling(path, wall);
            return Finish(path.transform, wall);
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
            int rows = s_Collision ? 7 : Rows;
            var hit = new Vector3[n, rows];
            var depth = new float[n, rows];
            var ok = new bool[n, rows];
            for (int i = 0; i < n; i++)
            {
                var pr = line[i];
                if (!Band(wall, pr.floor, out float floorY, out float low, out float high)) continue;
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
                    if (!ok[i, r] || !ok[i - 1, r] || !ok[i + 1, r]) continue;
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
                    if (Mathf.Abs(depth[i, r] - depth[j, r]) > MaxJump || Mathf.Abs(depth[i, r + 1] - depth[j, r + 1]) > MaxJump) continue;
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
