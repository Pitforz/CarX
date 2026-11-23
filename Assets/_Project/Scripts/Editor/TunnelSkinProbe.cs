using System.Collections.Generic;
using System.Text;
using UnityEngine;
using CarX.Level;

namespace CarX.EditorTools
{
    /// <summary>Diagnostic: finds places where the black light-tight skin of tunnel pieces can be seen from inside
    /// (nothing of the tunnel itself in front of it). call CarX.EditorTools.TunnelSkinProbe.Check</summary>
    public static class TunnelSkinProbe
    {
        /// <summary>The point is in the air of a tunnel (or a shaft): straight down it meets a floor from above,
        /// straight up a ceiling from below or open sky.</summary>
        const int Mask = ~((1 << 30) | (1 << 31));   // not the play-mode collision / touch shells

        static string why;
        static Transform Owner(Collider c)
        {
            var a = c.GetComponentInParent<TunnelPath>(); if (a != null) return a.transform;
            var b = c.GetComponentInParent<TunnelJunction>(); if (b != null) return b.transform;
            var r = c.GetComponentInParent<TunnelRoom>(); return r != null ? r.transform : null;
        }

        static bool InOpenSpace(Vector3 x, Transform self)
        {
            RaycastHit h;
            why = "";
            if (!Physics.Raycast(x + Vector3.up * 0.01f, Vector3.down, out h, 8f, Mask, QueryTriggerInteraction.Ignore)) return false;
            if (Vector3.Dot(h.normal, Vector3.down) >= -0.2f) return false;   // hit something from behind: we are inside concrete
            if (h.distance < 0.03f || !IsTunnel(h.collider)) return false;
            why = Owner(h.collider) == self ? "own" : "other:" + Owner(h.collider).name;
            if (!Physics.Raycast(x - Vector3.up * 0.01f, Vector3.up, out h, 30f, Mask, QueryTriggerInteraction.Ignore)) { why += "/sky"; return true; }
            return Vector3.Dot(h.normal, Vector3.up) < -0.2f && h.distance > 0.03f && IsTunnel(h.collider);
        }

        static bool IsTunnel(Collider c)
        {
            return c.GetComponentInParent<TunnelPath>() != null || c.GetComponentInParent<TunnelJunction>() != null || c.GetComponentInParent<TunnelRoom>() != null;
        }

        public static void At(int x100, int y100, int z100)
        {
            Debug.Log("CarX " + TunnelSkin.Probe(new Vector3(x100 / 100f, y100 / 100f, z100 / 100f)));
        }

        static Vector3 nearC; static float nearR = -1f;
        public static void CheckNear(int x100, int z100, int r100)
        {
            nearC = new Vector3(x100 / 100f, 0f, z100 / 100f); nearR = r100 / 100f;
            try { Check(); } finally { nearR = -1f; }
        }

        public static void Check()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            TunnelSkin.Flush();
            Debug.Log("CarX skin flush " + sw.ElapsedMilliseconds + " ms");
            Physics.SyncTransforms();
            bool back = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            var sb = new StringBuilder();
            int total = 0, bad = 0;
            var counts = new Dictionary<string, int>();
            try
            {
                foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude))
                {
                    string n = mf.gameObject.name;
                    if (n != "_ShadowShell" && n != "_Skin") continue;
                    var m = mf.sharedMesh;
                    if (m == null) continue;
                    var v = m.vertices;
                    var t = m.triangles;
                    var tr = mf.transform;
                    string owner = tr.parent != null ? tr.parent.name : n;
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        Vector3 a = tr.TransformPoint(v[t[i]]), b = tr.TransformPoint(v[t[i + 1]]), c = tr.TransformPoint(v[t[i + 2]]);
                        Vector3 nrm = Vector3.Cross(b - a, c - a);
                        if (nrm.sqrMagnitude < 1e-10f) continue;
                        nrm.Normalize();
                        float len = Mathf.Max((b - a).magnitude, (c - a).magnitude, (c - b).magnitude);
                        int steps = Mathf.Clamp(Mathf.CeilToInt(len / 0.2f), 1, 40);
                        for (int p = 0; p <= steps; p++)
                            for (int q = 0; q <= steps - p; q++)
                            {
                                float u = (p + 0.33f) / (steps + 1f), w = (q + 0.33f) / (steps + 1f);
                                if (u + w > 1f) continue;
                                Vector3 x = a + (b - a) * u + (c - a) * w;
                                total++;
                                if (nearR > 0f && new Vector2(x.x - nearC.x, x.z - nearC.z).magnitude > nearR) continue;
                                if (!InOpenSpace(x, tr.parent)) continue;
                                bad++;
                                string kind = nrm.y < -0.7f ? "top" : nrm.y > 0.7f ? "bottom" : "side";
                                string key = owner + " " + kind + " " + (why.StartsWith("other") ? "other" : why);
                                int k; counts.TryGetValue(key, out k); counts[key] = k + 1;
                                if (k < (nearR > 0f ? 6 : 1)) sb.Append("\n  " + key + " " + why + " at " + x.ToString("0.00") + " n " + nrm.ToString("0.0"));
                            }
                    }
                }
            }
            finally { Physics.queriesHitBackfaces = back; }
            var head = new StringBuilder("CarX skin probe: samples " + total + ", exposed " + bad + ", pieces " + counts.Count);
            foreach (var kv in counts) head.Append("\n  " + kv.Key + ": " + kv.Value);
            Debug.Log(head.ToString());
            Debug.Log("CarX skin probe examples:" + sb);
        }
    }
}
